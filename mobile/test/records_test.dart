import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:medsmarter_mobile/api/api_session.dart';
import 'package:medsmarter_mobile/api/records_client.dart';

import 'helpers.dart';

http.Response json(int status, Object body) => http.Response(
  jsonEncode(body),
  status,
  headers: {'content-type': 'application/json'},
);

const login = {
  'tokens': {'accessToken': 'a1', 'refreshToken': 'r1'},
  'user': {'id': 'user-1'},
};

/// A fake API: login works and [handler] answers every other route. No test here can reach a network.
MockClient fake(
  http.Response Function(http.Request r) handler, {
  List<String>? log,
}) => MockClient((r) async {
  log?.add('${r.method} ${r.url.path}');
  if (r.url.path == '/auth/login') return json(200, login);
  return handler(r);
});

Future<RecordsClient> signedIn(MockClient c) async {
  final s = ApiSession(baseUrl: 'http://api.test', client: c);
  await s.login('demo-patient');
  return RecordsClient(s);
}

Map<String, dynamic> freshRow(String cat, {bool never = false, bool stale = false}) => {
  'category': cat,
  'lastUpdatedAt': never ? null : '2026-10-01T00:00:00Z',
  'recordCount': never ? 0 : 2,
  'neverRecorded': never,
  'isStale': stale,
  'staleAfterDays': 180,
};

const profile = {
  'subjectId': 'user-1',
  'yearOfBirth': 1985,
  'ageYears': 41,
  'sex': 'Female',
  'isDemo': true,
};

void main() {
  group('RecordsClient', () {
    test('is not connected without a token and never pretends', () async {
      final c = RecordsClient(null);
      await expectLater(
        c.freshness(),
        throwsA(
          isA<RecordsException>().having((e) => e.kind, 'kind', RecordsErrorKind.notConnected),
        ),
      );
    });

    test('uses the signed-in user id in the path and sends JSON', () async {
      final log = <String>[];
      String? body;
      final c = await signedIn(
        fake((r) {
          body = r.body;
          return json(200, {'id': 'c1'});
        }, log: log),
      );
      await c.addCondition('Asthma (DEMO)', 'Active');
      expect(log.last, 'POST /patients/user-1/conditions');
      expect(jsonDecode(body!)['name'], 'Asthma (DEMO)');
    });

    for (final (status, kind) in [
      (401, RecordsErrorKind.unauthorized),
      (403, RecordsErrorKind.unauthorized),
      (404, RecordsErrorKind.notFound),
      (409, RecordsErrorKind.conflict),
      (400, RecordsErrorKind.invalid),
      (429, RecordsErrorKind.rateLimited),
      (501, RecordsErrorKind.notAvailable),
      (500, RecordsErrorKind.server),
    ]) {
      test('maps HTTP $status to $kind', () async {
        final c = await signedIn(fake((r) => json(status, {'code': 'x'})));
        await expectLater(
          c.freshness(),
          throwsA(isA<RecordsException>().having((e) => e.kind, 'kind', kind)),
        );
      });
    }

    test('keeps the server validation codes', () async {
      final c = await signedIn(
        fake((r) => json(400, {'code': 'invalid', 'errors': ['batch.required']})),
      );
      try {
        await c.addProduct(name: 'x', batch: '', expiry: '2027-01-01');
        fail('expected an exception');
      } on RecordsException catch (e) {
        expect(e.errors, ['batch.required']);
      }
    });
  });

  group('record screens', () {
    Future<void> open(
      WidgetTester tester,
      String location,
      http.Response Function(http.Request r) handler, {
      Locale locale = const Locale('en'),
      Size size = const Size(390, 844),
    }) async {
      await pumpApp(
        tester,
        location: location,
        locale: locale,
        size: size,
        httpClient: fake(handler),
      );
    }

    testWidgets('shows freshness, including never recorded and stale', (tester) async {
      await open(tester, '/profile/records', (r) {
        final p = r.url.path;
        if (p.endsWith('/profile')) return json(200, profile);
        if (p.endsWith('/freshness')) {
          return json(200, [
            freshRow('Profile'),
            freshRow('Allergies', stale: true),
            freshRow('Symptoms', never: true),
          ]);
        }
        return json(200, <Object>[]);
      });
      expect(find.text('May be out of date'), findsOneWidget);
      expect(find.text('Not recorded yet'), findsWidgets);
    });

    testWidgets('offers to create a record when none exists', (tester) async {
      await open(tester, '/profile/records', (r) => json(404, {'code': 'patient.not_found'}));
      expect(find.text('Create my record'), findsOneWidget);
    });

    testWidgets('says plainly when the app is not connected', (tester) async {
      await pumpApp(tester, location: '/profile/records');
      expect(find.text('Not connected to the server'), findsWidgets);
    });

    testWidgets('marks a medicine outside the reference and never matches it silently', (tester) async {
      await open(tester, '/profile/taking', (r) {
        final p = r.url.path;
        if (p.endsWith('/medications')) {
          return json(200, [
            {
              'id': 'm1',
              'displayName': 'DEMO herbal tonic (unregistered)',
              'isRegistered': false,
              'referenceIsDemo': false,
              'doseText': '1 spoon',
              'frequency': 'AsNeeded',
              'version': 1,
            },
          ]);
        }
        return json(200, <Object>[]);
      });
      expect(find.text('Not in reference'), findsOneWidget);
      expect(find.textContaining('kept as you typed'), findsOneWidget);
    });

    testWidgets('lists a report with a plain status and the MOCK-delivery note', (tester) async {
      await open(tester, '/profile/batches', (r) {
        final p = r.url.path;
        if (p.endsWith('/manufacturer-reports')) {
          return json(200, [
            {
              'id': 'r1',
              'productName': 'Nocturin (DEMO)',
              'batchNumber': 'DEMO-B-1',
              'status': 'Acknowledged',
              'issueType': 'AbnormalAppearanceOrPackaging',
              'isMockDelivery': true,
              'version': 2,
            },
          ]);
        }
        return json(200, <Object>[]);
      }, size: const Size(1200, 2600));
      expect(find.text('Acknowledged'), findsOneWidget);
      expect(find.textContaining('MOCK'), findsWidgets);
    });

    testWidgets('shows an urgent message with its urgent-signs part', (tester) async {
      await open(tester, '/profile/messages', (r) {
        return json(200, [
          {
            'id': 'g1',
            'level': 'Urgent',
            'status': 'Sent',
            'isDemo': true,
            'patient': {
              'observed': 'obs',
              'whyItMatters': 'why',
              'suggestedAction': 'act',
              'whenToConsult': 'consult',
              'urgentSigns': 'signs text',
              'basisAndConfidence': 'basis',
            },
          },
        ]);
      });
      expect(find.text('Signs that need quick help'), findsOneWidget);
      expect(find.text('signs text'), findsOneWidget);
    });

    testWidgets('renders Persian (RTL) care and consent lists', (tester) async {
      await open(tester, '/profile/sharing', (r) {
        if (r.url.path == '/care-relationships') return json(200, <Object>[]);
        return json(200, <Object>[]);
      }, locale: const Locale('fa'));
      expect(find.text('روابط مراقبتی'), findsOneWidget);
      expect(find.text('رضایت‌های من'), findsOneWidget);
    });
  });
}
