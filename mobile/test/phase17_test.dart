import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'helpers.dart';

void main() {
  http.Response json(int status, Object body) => http.Response(
    jsonEncode(body),
    status,
    headers: {'content-type': 'application/json'},
  );

  MockClient backendOff() =>
      MockClient((_) async => throw http.ClientException('backend off (test)'));

  group('sample-data notice', () {
    for (final (path, label) in [
      ('/home', 'home'),
      ('/checkin', 'check-in'),
      ('/profile', 'profile'),
    ]) {
      testWidgets('$label says it runs on sample data and nothing is saved', (
        tester,
      ) async {
        await pumpApp(
          tester,
          location: path,
          size: const Size(390, 2400),
          httpClient: backendOff(),
        );
        expect(find.text('Sample data on this page'), findsOneWidget);
        expect(find.textContaining('nothing you do here is saved'), findsOneWidget);
      });
    }

    testWidgets('Persian: the same notice, no raw keys', (tester) async {
      await pumpApp(
        tester,
        location: '/checkin',
        locale: const Locale('fa'),
        size: const Size(390, 2400),
        httpClient: backendOff(),
      );
      expect(find.text('داده‌ی نمونه در این صفحه'), findsOneWidget);
      expect(find.textContaining('sample.'), findsNothing);
    });

    testWidgets('the button opens the screen with stored data', (tester) async {
      await pumpApp(
        tester,
        location: '/home',
        size: const Size(390, 2400),
        httpClient: MockClient((r) async {
          if (r.url.path == '/auth/login') {
            return json(200, {
              'tokens': {'accessToken': 'a', 'refreshToken': 'r'},
              'user': {'id': 'user-1'},
            });
          }
          return json(200, <Object>[]);
        }),
      );
      await tester.tap(find.text('Open the stored list: Medicines I take'));
      await tester.pumpAndSettle();
      expect(find.text('Sample data on this page'), findsNothing);
    });

    testWidgets('a screen with stored data does not carry the notice', (tester) async {
      await pumpApp(
        tester,
        location: '/profile/taking',
        size: const Size(390, 2400),
        httpClient: backendOff(),
      );
      expect(find.text('Sample data on this page'), findsNothing);
    });
  });
}

