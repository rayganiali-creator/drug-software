import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:medsmarter_mobile/api/api_session.dart';
import 'package:medsmarter_mobile/api/medication_client.dart';

import 'helpers.dart';

Map<String, dynamic> loc(String en, String fa) => {'en': en, 'fa': fa};

Map<String, dynamic> summary({
  String id = '11111111-1111-1111-1111-111111111111',
  String validation = 'Demo',
  bool isDemo = true,
  bool brand = true,
  String lifecycle = 'Active',
}) => {
  'id': id,
  'name': loc('Nocturin', 'نوکتورین'),
  'brandName': brand ? loc('Nocturin', 'نوکتورین') : null,
  'dosageForm': loc('Tablet', 'قرص'),
  'strengthSummary': '5 mg',
  'ingredients': [loc('nocturamide (fictional)', 'نوکتورامید (فرضی)')],
  'lifecycle': lifecycle,
  'validation': validation,
  'isDemo': isDemo,
  'matchedOn': 'Nocturin',
  'score': 1060,
};

Map<String, dynamic> detail({
  String validation = 'Demo',
  bool isDemo = true,
}) => {
  'id': '11111111-1111-1111-1111-111111111111',
  'version': 3,
  'name': loc('Nocturin', 'نوکتورین'),
  'brand': {'id': 'b', 'name': loc('Nocturin', 'نوکتورین')},
  'manufacturer': {
    'id': 'm',
    'name': loc('DemoPharma A (fictional)', 'دموفارما الف (فرضی)'),
    'country': null,
  },
  'dosageForm': loc('Tablet', 'قرص'),
  'routes': [loc('Oral', 'خوراکی')],
  'strengthSummary': '5 mg',
  'ingredients': [
    {
      'ingredientId': 'i1',
      'name': loc('nocturamide (fictional)', 'نوکتورامید (فرضی)'),
      'strengthValue': 5,
      'strengthUnit': 'mg',
      'perUnit': null,
      'order': 0,
    },
  ],
  'statements': [
    {
      'id': 's1',
      'kind': 'Warning',
      'text': loc(
        'Fictional warning: may cause demo drowsiness.',
        'هشدار فرضی: خواب‌آلودگی نمایشی.',
      ),
      'validation': 'Demo',
      'sourceId': 'src',
    },
  ],
  'missingKinds': ['Indication', 'Contraindication', 'Precaution'],
  'interactions': [
    {
      'id': 'x',
      'otherIngredientName': loc('demoprilate (fictional)', 'دموپریلات (فرضی)'),
      'severity': 'Moderate',
      'mechanism': loc(
        'Fictional: dizziness may feel stronger.',
        'فرضی: سرگیجه بیشتر.',
      ),
      'management': loc('Pharmacist review suggested.', 'بازبینی داروساز.'),
      'validation': 'Demo',
    },
  ],
  'sources': [
    {
      'id': 'src',
      'name': 'DEMO seed (fictional)',
      'publisher': 'AI MedSmarter test fixtures',
      'version': 'demo-0.1',
      'licenseName': 'Fictional test data',
    },
  ],
  'identifiers': <Map<String, dynamic>>[],
  'lifecycle': 'Active',
  'validation': validation,
  'isDemo': isDemo,
  'updatedAt': '2026-10-09T08:00:00Z',
};

http.Response ok(Object body) => http.Response(
  jsonEncode(body),
  200,
  headers: {'content-type': 'application/json'},
);

const loginBody = {
  'tokens': {
    'accessToken': 'access-token-1',
    'refreshToken': 'refresh-token-1',
  },
};

/// A fake API: login always works; [handler] answers the medication routes. No test here can reach a network.
MockClient api(
  http.Response Function(http.Request r) handler, {
  List<String>? log,
  bool loginWorks = true,
}) => MockClient((r) async {
  log?.add(
    '${r.method} ${r.url.path}${r.url.hasQuery ? '?${r.url.query}' : ''}',
  );
  if (r.url.path == '/auth/login') {
    if (!loginWorks) throw http.ClientException('backend off');
    return ok(loginBody);
  }
  return handler(r);
});

Finder text(String s) => find.text(s);

void main() {
  group('ApiSession', () {
    test('keeps tokens in memory and sends them as a bearer header', () async {
      String? seen;
      final s = ApiSession(
        baseUrl: 'http://api.test',
        client: MockClient((r) async {
          if (r.url.path == '/auth/login') return ok(loginBody);
          seen = r.headers['Authorization'];
          return ok({});
        }),
      );
      expect(s.hasToken, isFalse);
      expect(await s.login('demo-patient'), isTrue);
      await s.get('/x');
      expect(seen, 'Bearer access-token-1');
      s.clear();
      expect(s.hasToken, isFalse);
    });

    test('refreshes once on 401 and retries', () async {
      var calls = 0;
      final s = ApiSession(
        baseUrl: 'http://api.test',
        client: MockClient((r) async {
          if (r.url.path == '/auth/login') return ok(loginBody);
          if (r.url.path == '/auth/refresh') {
            return ok({
              'tokens': {
                'accessToken': 'access-token-2',
                'refreshToken': 'refresh-token-2',
              },
            });
          }
          calls++;
          return r.headers['Authorization'] == 'Bearer access-token-2'
              ? ok({'fine': true})
              : http.Response('', 401);
        }),
      );
      await s.login('demo-patient');
      expect((await s.get('/x')).statusCode, 200);
      expect(calls, 2);
    });

    test('login failure and an unreachable server both return false', () async {
      expect(
        await ApiSession(
          baseUrl: 'http://api.test',
          client: MockClient((_) async => http.Response('{}', 401)),
        ).login('x'),
        isFalse,
      );
      expect(
        await ApiSession(
          baseUrl: 'http://api.test',
          client: MockClient((_) async => throw http.ClientException('off')),
        ).login('x'),
        isFalse,
      );
    });
  });

  group('MedicationClient', () {
    Future<MedicationClient> client(
      http.Response Function(http.Request) h, {
      List<String>? log,
    }) async {
      final s = ApiSession(
        baseUrl: 'http://api.test',
        client: api(h, log: log),
      );
      await s.login('demo-patient');
      return MedicationClient(s);
    }

    test('builds the search URL and parses the page', () async {
      final log = <String>[];
      final c = await client(
        (r) => ok({
          'items': [summary()],
          'total': 1,
          'limit': 20,
          'offset': 0,
        }),
        log: log,
      );
      final page = await c.search('  nocturin ', limit: 5, offset: 10);
      expect(page.total, 1);
      expect(page.items.single.name.of('fa'), 'نوکتورین');
      expect(page.items.single.validation, 'Demo');
      expect(log.last, 'GET /medications/search?q=nocturin&limit=5&offset=10');
      await c.search('   ');
      expect(log.last, 'GET /medications/search?limit=20&offset=0');
    });

    test(
      'parses the detail including missing kinds and interactions',
      () async {
        final c = await client((r) => ok(detail()));
        final d = await c.detail('x');
        expect(d.ingredients.single.strength, '5 mg');
        expect(d.missingKinds, contains('Contraindication'));
        expect(d.interactions.single.severity, 'Moderate');
        expect(d.sources.single.licence, 'Fictional test data');
      },
    );

    for (final (status, kind) in [
      (401, KnowledgeErrorKind.unauthorized),
      (403, KnowledgeErrorKind.unauthorized),
      (404, KnowledgeErrorKind.notFound),
      (429, KnowledgeErrorKind.rateLimited),
      (500, KnowledgeErrorKind.server),
    ]) {
      test('maps HTTP $status to $kind', () async {
        final c = await client((r) => http.Response('{}', status));
        await expectLater(
          c.search('abc'),
          throwsA(
            isA<KnowledgeException>().having((e) => e.kind, 'kind', kind),
          ),
        );
      });
    }

    test('keeps only the validation code from a 400', () async {
      final c = await client(
        (r) => http.Response(
          jsonEncode({'code': 'q.too_short', 'detail': 'internal stack'}),
          400,
        ),
      );
      await expectLater(
        c.search('a'),
        throwsA(
          isA<KnowledgeException>().having(
            (e) => e.code,
            'code',
            'q.too_short',
          ),
        ),
      );
    });

    test(
      'is "not connected" without a token and "network" on transport errors',
      () async {
        await expectLater(
          MedicationClient(
            ApiSession(baseUrl: 'http://api.test', client: api((r) => ok({}))),
          ).search('abc'),
          throwsA(
            isA<KnowledgeException>().having(
              (e) => e.kind,
              'kind',
              KnowledgeErrorKind.notConnected,
            ),
          ),
        );
        final s = ApiSession(
          baseUrl: 'http://api.test',
          client: MockClient(
            (r) async => r.url.path == '/auth/login'
                ? ok(loginBody)
                : throw http.ClientException('x'),
          ),
        );
        await s.login('a');
        await expectLater(
          MedicationClient(s).search('abc'),
          throwsA(
            isA<KnowledgeException>().having(
              (e) => e.kind,
              'kind',
              KnowledgeErrorKind.network,
            ),
          ),
        );
      },
    );
  });

  group('Drug reference screens', () {
    Future<void> openList(
      WidgetTester tester,
      http.Response Function(http.Request) h, {
      List<String>? log,
      Locale locale = const Locale('en'),
      Size size = const Size(390, 1800),
      double textScale = 1,
      bool loginWorks = true,
      String location = '/medications/reference',
    }) async {
      await pumpApp(
        tester,
        location: location,
        size: size,
        locale: locale,
        textScale: textScale,
        httpClient: api(h, log: log, loginWorks: loginWorks),
      );
      await tester.pumpAndSettle();
    }

    http.Response page(List<Map<String, dynamic>> items, [int? total]) => ok({
      'items': items,
      'total': total ?? items.length,
      'limit': 20,
      'offset': 0,
    });

    testWidgets('lists medicines with their validation labels', (tester) async {
      await openList(
        tester,
        (r) => page([
          summary(),
          summary(
            id: '2',
            validation: 'Validated',
            isDemo: false,
            brand: false,
          ),
        ]),
      );
      expect(text('Nocturin'), findsWidgets);
      expect(text('DEMO'), findsOneWidget);
      expect(text('Source-validated'), findsOneWidget);
      expect(find.textContaining('Generic product'), findsOneWidget);
      expect(find.textContaining('Showing 1–2 of 2'), findsOneWidget);
    });

    testWidgets('searches after a pause with the typed text', (tester) async {
      final log = <String>[];
      await openList(tester, (r) => page([summary()]), log: log);
      await tester.enterText(find.byType(TextField), 'noc');
      await tester.pump(const Duration(milliseconds: 100));
      expect(log.where((l) => l.contains('q=noc')), isEmpty); // debounced
      await tester.pump(const Duration(milliseconds: 400));
      await tester.pumpAndSettle();
      expect(log.where((l) => l.contains('q=noc')), hasLength(1));
    });

    testWidgets('does not call the API for a one-character query', (
      tester,
    ) async {
      final log = <String>[];
      await openList(tester, (r) => page([summary()]), log: log);
      final before = log.length;
      await tester.enterText(find.byType(TextField), 'n');
      await tester.pump(const Duration(milliseconds: 400));
      await tester.pumpAndSettle();
      expect(log.length, before);
      expect(text('Use between 2 and 64 characters.'), findsOneWidget);
    });

    testWidgets('shows the empty state', (tester) async {
      await openList(tester, (r) => page([]));
      expect(text('No medicines found'), findsOneWidget);
    });

    testWidgets('shows an error with a working retry', (tester) async {
      var n = 0;
      await openList(
        tester,
        (r) => n++ == 0 ? http.Response('{}', 500) : page([summary()]),
      );
      expect(text('The drug reference could not be loaded'), findsOneWidget);
      await tester.tap(text('Try again'));
      await tester.pumpAndSettle();
      expect(text('Nocturin'), findsWidgets);
    });

    testWidgets('says it is not connected when the backend is off', (
      tester,
    ) async {
      await openList(tester, (r) => page([summary()]), loginWorks: false);
      expect(text('The drug reference is not connected'), findsOneWidget);
    });

    for (final (status, title) in [
      (403, 'You do not have access to the drug reference'),
      (429, 'Too many searches'),
      (400, 'That search is not valid'),
    ]) {
      testWidgets('handles HTTP $status', (tester) async {
        await openList(tester, (r) => http.Response('{"code":"x"}', status));
        expect(text(title), findsOneWidget);
      });
    }

    testWidgets('shows more results', (tester) async {
      await openList(
        tester,
        (r) => r.url.queryParameters['offset'] == '0'
            ? ok({
                'items': [summary()],
                'total': 2,
                'limit': 20,
                'offset': 0,
              })
            : ok({
                'items': [
                  summary(id: '3')
                    ..['name'] = loc('Second page drug', 'دارو دوم'),
                ],
                'total': 2,
                'limit': 20,
                'offset': 1,
              }),
      );
      await tester.tap(text('Show more'));
      await tester.pumpAndSettle();
      expect(text('Second page drug'), findsOneWidget);
      expect(text('Show more'), findsNothing);
    });

    testWidgets('opens the detail page with sections, status and sources', (
      tester,
    ) async {
      await openList(
        tester,
        (r) => r.url.path.startsWith('/medications/search')
            ? page([summary()])
            : ok(detail()),
      );
      await tester.tap(text('Nocturin').first);
      await tester.pumpAndSettle();
      expect(find.textContaining('This is a fictional record'), findsOneWidget);
      expect(text('Warnings'), findsOneWidget);
      expect(find.textContaining('Fictional warning'), findsOneWidget);
      expect(
        find.textContaining('Licence: Fictional test data'),
        findsOneWidget,
      );
      expect(text('No official identifiers are recorded.'), findsOneWidget);
      expect(text('Information not available'), findsOneWidget);
      expect(find.textContaining('Contraindications'), findsWidgets);
    });

    testWidgets('detail of a missing medicine is handled', (tester) async {
      await openList(
        tester,
        (r) => http.Response('{}', 404),
        location: '/medications/reference/gone',
      );
      expect(text('This medicine is not available'), findsOneWidget);
    });

    testWidgets('notices differ for validated and unvalidated non-demo data', (
      tester,
    ) async {
      await openList(
        tester,
        (r) => ok(detail(validation: 'Validated', isDemo: false)),
        location: '/medications/reference/x',
      );
      expect(
        find.textContaining('Source-validated reference information'),
        findsOneWidget,
      );
    });

    testWidgets('unvalidated non-demo data is flagged as such', (tester) async {
      await openList(
        tester,
        (r) => ok(detail(validation: 'Unverified', isDemo: false)),
        location: '/medications/reference/x',
      );
      expect(find.textContaining('NOT VALIDATED'), findsOneWidget);
    });

    testWidgets('Persian is RTL with Persian names and labels', (tester) async {
      await openList(
        tester,
        (r) => page([summary()]),
        locale: const Locale('fa'),
      );
      expect(text('نوکتورین'), findsWidgets);
      expect(text('نمایشی'), findsOneWidget);
      expect(
        Directionality.of(tester.element(find.byType(Scaffold).first)),
        TextDirection.rtl,
      );
    });

    testWidgets('wide windows show list and detail side by side', (
      tester,
    ) async {
      await openList(
        tester,
        (r) => r.url.path.startsWith('/medications/search')
            ? page([summary()])
            : ok(detail()),
        size: const Size(1280, 900),
      );
      expect(text('Select a medicine to see its details.'), findsOneWidget);
      await tester.tap(text('Nocturin').first);
      await tester.pumpAndSettle();
      expect(text('Warnings'), findsOneWidget); // detail pane, same screen
      expect(find.byType(TextField), findsOneWidget); // list still visible
    });

    testWidgets('no overflow at 200% text on a phone', (tester) async {
      await openList(
        tester,
        (r) => page([
          summary(),
          summary(id: '2', validation: 'Validated', isDemo: false),
        ]),
        textScale: 2.0,
        size: const Size(360, 800),
      );
      expect(tester.takeException(), isNull);
    });

    testWidgets('the medications screen links to the reference', (
      tester,
    ) async {
      await openList(
        tester,
        (r) => page([summary()]),
        location: '/medications',
      );
      await tester.tap(text('Drug reference'));
      await tester.pumpAndSettle();
      expect(find.byType(TextField), findsOneWidget);
    });
  });
}
