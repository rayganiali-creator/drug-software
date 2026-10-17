import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:medsmarter_mobile/api/api_session.dart';
import 'package:medsmarter_mobile/api/records_client.dart';
import 'package:medsmarter_mobile/core/offline_queue.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'helpers.dart';

http.Response json(int status, Object body) =>
    http.Response(jsonEncode(body), status, headers: {'content-type': 'application/json'});

const login = {
  'tokens': {'accessToken': 'tok-access-1', 'refreshToken': 'tok-refresh-1'},
  'user': {'id': 'user-1'},
};

final slot = <String, dynamic>{
  'patientMedicationId': 'm1',
  'scheduleEntryId': 'e1',
  'scheduledFor': '2026-10-17T05:00:00Z',
  'medicationName': 'Paracetamol (must never be stored)',
};

/// A server that can be switched off, and records what it was sent.
class FakeServer {
  bool online = true;
  int status = 200;
  Map<String, dynamic> body = {};
  final received = <Map<String, dynamic>>[];
  Future<http.Response> handle(http.Request r) async {
    if (r.url.path == '/auth/login') return json(200, login);
    if (!online) throw http.ClientException('offline');
    received.add(jsonDecode(r.body) as Map<String, dynamic>);
    return json(status, body);
  }
}

Future<RecordsClient> client(FakeServer s) async {
  final session = ApiSession(baseUrl: 'http://api.invalid', client: MockClient(s.handle));
  await session.login('demo-patient');
  return RecordsClient(session);
}

void main() {
  late SharedPreferences prefs;
  late FakeServer server;
  late RecordsClient api;
  var now = DateTime.utc(2026, 10, 17, 6);

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    prefs = await SharedPreferences.getInstance();
    server = FakeServer();
    api = await client(server);
  });

  OfflineQueue make({int max = 50, int attempts = 5}) =>
      OfflineQueue(prefs: prefs, now: () => now, maxItems: max, maxAttempts: attempts)..bind('user-1');

  test('online: sent at once, nothing queued, client time travels with the report', () async {
    final q = make();
    expect(await q.submitIntake(api, slot, 'Taken'), SubmitResult.synced);
    expect(q.ops, isEmpty);
    expect(server.received.single['clientRecordedAt'], '2026-10-17T06:00:00.000Z');
  });

  test('offline: the report is queued as pending and stored without names or tokens', () async {
    final q = make();
    server.online = false;
    expect(await q.submitIntake(api, slot, 'Taken'), SubmitResult.queued);
    expect(q.pendingCount, 1);
    final stored = prefs.getString('offlineQueue.v1.user-1')!;
    expect(stored, isNot(contains('Paracetamol')));
    expect(stored, isNot(contains('tok-')));
    expect(stored, contains('"state":"pending"'));
  });

  test('restart recovery: a new queue instance reloads pending work; another account sees nothing', () async {
    server.online = false;
    await make().submitIntake(api, slot, 'Taken');
    final again = OfflineQueue(prefs: prefs, now: () => now)..bind('user-1');
    expect(again.pendingCount, 1);
    expect(OfflineQueue(prefs: prefs)..bind('user-2'), isA<OfflineQueue>().having((q) => q.ops, 'ops', isEmpty));
  });

  test('sync: reachable server accepts it once; a second sync sends nothing (no duplicates)', () async {
    final q = make();
    server.online = false;
    await q.submitIntake(api, slot, 'Taken');
    server.online = true;
    server.received.clear();
    await Future.wait([q.sync(api), q.sync(api)]); // overlapping calls
    expect(server.received, hasLength(1));
    expect(q.ops.single.state, QueueState.synced);
    await q.sync(api);
    expect(server.received, hasLength(1));
    q.pruneSynced();
    expect(q.ops, isEmpty);
    expect(prefs.getString('offlineQueue.v1.user-1'), isNull);
  });

  test('the newest local decision for a dose replaces the older queued one', () async {
    final q = make();
    server.online = false;
    await q.submitIntake(api, slot, 'Taken');
    await q.submitIntake(api, slot, 'Skipped');
    expect(q.ops, hasLength(1));
    expect(q.ops.single.status, 'Skipped');
  });

  test('conflict: the server holds a newer record; the report is never applied and waits for the person', () async {
    final q = make();
    server.online = false;
    await q.submitIntake(api, slot, 'Taken');
    server.online = true;
    server.status = 409;
    server.body = {'code': 'intake.newer_record_exists', 'errors': ['intake.newer_record_exists']};
    await q.sync(api);
    expect(q.ops.single.state, QueueState.conflict);
    expect(q.ops.single.code, 'intake.newer_record_exists');
    server.received.clear();
    await q.sync(api); // a conflict is not retried automatically
    expect(server.received, isEmpty);
    q.discard(q.ops.single.id);
    expect(q.ops, isEmpty);
  });

  test('retries are bounded: unreachable server leaves it pending until the limit, then failed; retry resets', () async {
    final q = make(attempts: 3);
    server.online = false;
    await q.submitIntake(api, slot, 'Taken');
    await q.sync(api);
    await q.sync(api);
    expect(q.ops.single.state, QueueState.pending);
    await q.sync(api);
    expect(q.ops.single.state, QueueState.failed);
    expect(q.ops.single.code, 'retries_exhausted');
    final calls = server.received.length;
    await q.sync(api);
    expect(server.received.length, calls);
    q.retry(q.ops.single.id);
    expect(q.ops.single.state, QueueState.pending);
    expect(q.ops.single.attempts, 0);
  });

  test('a rejected report (invalid) becomes failed at once and is not retried', () async {
    final q = make();
    server.online = false;
    await q.submitIntake(api, slot, 'Taken');
    server.online = true;
    server.status = 400;
    server.body = {'code': 'scheduled_for.not_a_planned_dose'};
    await q.sync(api);
    expect(q.ops.single.state, QueueState.failed);
    expect(q.ops.single.code, 'scheduled_for.not_a_planned_dose');
  });

  test('a server answer while online is shown to the person, not queued', () async {
    final q = make();
    server.status = 400;
    server.body = {'code': 'instructions.dose_unknown'};
    await expectLater(q.submitIntake(api, slot, 'Taken'), throwsA(isA<RecordsException>()));
    expect(q.ops, isEmpty);
  });

  test('the queue is bounded', () async {
    final q = make(max: 2);
    server.online = false;
    for (final i in [1, 2]) {
      await q.submitIntake(api, {...slot, 'scheduleEntryId': 'e$i'}, 'Taken');
    }
    await expectLater(
      q.submitIntake(api, {...slot, 'scheduleEntryId': 'e3'}, 'Taken'),
      throwsA(isA<RecordsException>().having((e) => e.code, 'code', 'offline.queue_full')),
    );
    await q.submitIntake(api, {...slot, 'scheduleEntryId': 'e1'}, 'Skipped'); // replacing is allowed
    expect(q.ops, hasLength(2));
  });

  test('sign-out clears the queue and its stored copy', () async {
    final q = make();
    server.online = false;
    await q.submitIntake(api, slot, 'Taken');
    q.bind(null);
    expect(q.ops, isEmpty);
    expect(prefs.getString('offlineQueue.v1.user-1'), isNull);
  });

  test('a damaged stored queue is dropped, not guessed at', () async {
    await prefs.setString('offlineQueue.v1.user-1', '[{"id":1},"x",{"id":"a","slotKey":"k","payload":{},"queuedAt":"bad","state":"pending"}]');
    expect(make().ops, isEmpty);
    await prefs.setString('offlineQueue.v1.user-1', 'not json');
    expect(OfflineQueue(prefs: prefs)..bind('user-1'), isA<OfflineQueue>().having((q) => q.ops, 'ops', isEmpty));
  });

  group('taking screen', () {
    testWidgets('an unreachable server leaves the dose visibly "not sent", never "saved", and offers to send it later', (tester) async {
      var online = false;
      final posts = <String>[];
      await pumpApp(
        tester,
        location: '/profile/taking',
        size: const Size(390, 2400),
        httpClient: MockClient((r) async {
          if (r.url.path == '/auth/login') return json(200, login);
          if (r.method == 'POST' && r.url.path.endsWith('/intake')) {
            if (!online) throw http.ClientException('offline');
            posts.add(r.body);
            return json(200, <String, dynamic>{});
          }
          if (r.url.path.endsWith('/doses')) {
            return json(200, [
              {...slot, 'localTime': '08:00:00', 'status': null},
            ]);
          }
          return json(200, <Object>[]);
        }),
      );
      expect(find.text('Taken'), findsOneWidget); // the button
      await tester.tap(find.text('Taken'));
      await tester.pumpAndSettle();
      expect(find.text('Not sent yet'), findsWidgets);
      expect(find.textContaining('Saved on this phone only'), findsOneWidget);
      expect(find.text('Send now'), findsOneWidget);
      expect(find.textContaining('Sent and saved'), findsNothing);

      online = true;
      await tester.tap(find.text('Send now'));
      await tester.pumpAndSettle();
      expect(posts, hasLength(1));
      expect(jsonDecode(posts.single)['status'], 'Taken');
      expect(find.text('Send now'), findsNothing);
    });
  });
}
