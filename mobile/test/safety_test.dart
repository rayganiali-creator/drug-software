import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:medsmarter_mobile/api/api_session.dart';
import 'package:medsmarter_mobile/api/safety_client.dart';

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

const evidence = {
  'sourceId': 'test-fixture',
  'sourceName': 'TEST FIXTURE (not a real source)',
  'version': '1',
  'publicationDate': null,
  'validation': 'Validated',
  'reviewStatus': 'fixture',
  'locator': null,
};

Map<String, dynamic> finding({
  bool actionable = true,
  bool demo = false,
  String severity = 'Major',
  String ruleId = 'T-INT',
}) => {
  'key': 'f-1',
  'ruleId': ruleId,
  'ruleVersion': 1,
  'domain': 'Interaction',
  'severity': severity,
  'urgency': 'Soon',
  'actionable': actionable,
  'isDemo': demo,
  'guidanceTemplateKey': 'safety.interaction',
  'subjects': [
    {'kind': 'medication', 'recordId': 'm1', 'label': 'Alpha'},
    {'kind': 'medication', 'recordId': 'm2', 'label': 'Beta'},
  ],
  'evidence': [evidence],
  'referenceSeverity': 'Major',
  'evidenceConflict': false,
  'inputsStale': false,
  'limitations': ['reference.not_validated'],
  'emergencySigns': null,
};

Map<String, dynamic> assessment({
  String status = 'CompletedWithFindings',
  bool complete = true,
  bool demo = false,
  List<Map<String, dynamic>>? findings,
  List<Map<String, dynamic>>? evaluations,
  bool outdated = false,
  List<String> outdatedReasons = const [],
}) => {
  'id': 'a1',
  'subjectId': 'user-1',
  'trigger': 'manual',
  'requestedByPatient': true,
  'guidanceState': 'Created',
  'guidanceLinks': <Object>[],
  'openGuidanceNoLongerMatching': <Object>[],
  'outdated': outdated,
  'outdatedReasons': outdatedReasons,
  'notice': demo
      ? 'DEMO: includes demonstration rules that are NOT clinically validated. This is not medical advice.'
      : 'Based on the rules listed.',
  'result': {
    'status': status,
    'complete': complete,
    'containsDemonstration': demo,
    'safetyClaimAllowed': false,
    'engineVersion': 'engine-1.0.0',
    'ruleSetVersion': 'rs-abc123',
    'evaluatedAt': '2026-10-10T08:00:00Z',
    'inputs': [
      {
        'category': 'medications',
        'availability': 'Available',
        'lastUpdatedAt': '2026-10-01T00:00:00Z',
        'recordCount': 2,
        'isStale': false,
        'staleAfterDays': 180,
        'source': 'patient-record',
      },
      {
        'category': 'allergies',
        'availability': 'NeverRecorded',
        'lastUpdatedAt': null,
        'recordCount': 0,
        'isStale': false,
        'staleAfterDays': 365,
        'source': 'patient-record',
      },
    ],
    'evaluations':
        evaluations ??
        [
          {
            'ruleId': 'T-INT',
            'version': 1,
            'domain': 'Interaction',
            'outcome': 'Matched',
            'reasons': <String>[],
            'activation': 'Active',
            'isDemo': false,
            'partial': false,
            'findingCount': 1,
          },
        ],
    'findings': findings ?? [finding()],
    'coverage': {
      'activeRules': demo ? 0 : 1,
      'demonstrationRules': demo ? 5 : 0,
      'inactiveRules': 0,
      'notEvaluable': 0,
      'domainsCovered': demo ? <String>[] : ['Interaction'],
      'unsupportedDomains': ['dose_and_route', 'adherence'],
    },
    'limitations': [
      'engine.not_a_diagnosis',
      'engine.not_triage',
      'engine.clean_result_is_not_safety',
    ],
  },
};

MockClient fake(
  http.Response Function(http.Request r) handler, {
  List<String>? calls,
}) => MockClient((r) async {
  if (r.url.path == '/auth/login') return json(200, login);
  calls?.add(
    '${r.method} ${r.url.path}${r.url.hasQuery ? '?${r.url.query}' : ''}',
  );
  return handler(r);
});

http.Response latest(Map<String, dynamic> a) => json(200, a);

Future<SafetyClient> signedIn(MockClient c) async {
  final s = ApiSession(baseUrl: 'http://api.test', client: c);
  await s.login('demo-patient');
  return SafetyClient(s);
}

void main() {
  group('SafetyClient', () {
    test('is not connected without a token and never pretends', () async {
      await expectLater(
        SafetyClient(null).latest('x'),
        throwsA(
          isA<SafetyException>().having(
            (e) => e.kind,
            'kind',
            SafetyErrorKind.notConnected,
          ),
        ),
      );
    });

    test('runs with the route patient id and the locale only, and reads the structured result', () async {
      final calls = <String>[];
      final c = await signedIn(
        fake((r) => json(201, assessment()), calls: calls),
      );
      final a = await c.run('p 1', 'fa');
      expect(a.status, 'CompletedWithFindings');
      expect(a.findings, hasLength(1));
      expect(calls.single, 'POST /patients/p%201/safety/assessments?locale=fa');
    });

    for (final (status, kind) in [
      (401, SafetyErrorKind.unauthorized),
      (403, SafetyErrorKind.unauthorized),
      (404, SafetyErrorKind.notFound),
      (400, SafetyErrorKind.invalid),
      (429, SafetyErrorKind.rateLimited),
      (503, SafetyErrorKind.unavailable),
      (500, SafetyErrorKind.server),
    ]) {
      test('maps HTTP $status to $kind', () async {
        final c = await signedIn(fake((r) => json(status, {})));
        await expectLater(
          c.latest('p'),
          throwsA(isA<SafetyException>().having((e) => e.kind, 'kind', kind)),
        );
      });
    }

    test('a lost connection is its own kind', () async {
      final broken = MockClient((r) async {
        if (r.url.path == '/auth/login') return json(200, login);
        throw http.ClientException('offline');
      });
      await expectLater(
        (await signedIn(broken)).latest('p'),
        throwsA(
          isA<SafetyException>().having(
            (e) => e.kind,
            'kind',
            SafetyErrorKind.network,
          ),
        ),
      );
    });
  });

  group('safety screen', () {
    Future<void> open(
      WidgetTester tester,
      http.Response Function(http.Request r) handler, {
      Locale locale = const Locale('en'),
      List<String>? calls,
    }) async {
      await pumpApp(
        tester,
        location: '/profile/safety',
        locale: locale,
        size: const Size(390, 2600),
        httpClient: fake(handler, calls: calls),
      );
    }

    testWidgets(
      'completed with findings: severity as a word, sources and rule version in the details, never a safety claim',
      (tester) async {
        await open(tester, (r) => latest(assessment()));
        expect(
          find.text('The check found something to look at'),
          findsOneWidget,
        );
        expect(find.text('Major'), findsWidgets);
        expect(find.textContaining('Alpha · Beta'), findsOneWidget);
        expect(
          find.textContaining('Do not change anything on your own'),
          findsOneWidget,
        );
        expect(
          find.text(
            'A result without findings is not a statement that anything is safe.',
          ),
          findsOneWidget,
        );
        await tester.ensureVisible(find.text('Basis, rule and sources'));
        await tester.tap(find.text('Basis, rule and sources'));
        await tester.pumpAndSettle();
        expect(find.text('Rule: T-INT v1'), findsOneWidget);
        expect(
          find.textContaining('TEST FIXTURE (not a real source)'),
          findsOneWidget,
        );
        expect(find.text('Publication date not recorded'), findsOneWidget);
        expect(find.textContaining(RegExp(r'\d\s?%')), findsNothing);
      },
    );

    testWidgets('does not run anything by itself', (tester) async {
      final calls = <String>[];
      await open(tester, (r) => latest(assessment()), calls: calls);
      expect(calls.where((c) => c.startsWith('POST')), isEmpty);
    });

    testWidgets(
      'no approved rules: says so and labels demonstration findings as not advice',
      (tester) async {
        await open(
          tester,
          (r) => latest(
            assessment(
              status: 'NoApprovedCoverage',
              complete: false,
              demo: true,
              findings: [
                finding(
                  actionable: false,
                  demo: true,
                  severity: 'Moderate',
                  ruleId: 'DEMO-INT-001',
                ),
              ],
              evaluations: [
                {
                  'ruleId': 'DEMO-INT-001',
                  'version': 1,
                  'domain': 'Interaction',
                  'outcome': 'Matched',
                  'reasons': <String>[],
                  'activation': 'DemonstrationOnly',
                  'isDemo': true,
                  'partial': false,
                  'findingCount': 1,
                },
              ],
            ),
          ),
        );
        expect(find.text('No approved rules are in force'), findsOneWidget);
        expect(find.text('Demonstration findings'), findsOneWidget);
        expect(find.text('DEMO - not clinically validated'), findsWidgets);
        expect(
          find.text(
            'This comes from a demonstration rule or demonstration data. It is not approved advice.',
          ),
          findsOneWidget,
        );
        expect(find.text('What was found'), findsNothing);
      },
    );

    testWidgets('completed with no matches states its coverage limits', (
      tester,
    ) async {
      await open(
        tester,
        (r) => latest(
          assessment(
            status: 'CompletedNoMatches',
            findings: [],
            evaluations: [],
          ),
        ),
      );
      expect(find.text('The approved rules found nothing'), findsOneWidget);
      expect(find.textContaining('not the same as being safe'), findsOneWidget);
      expect(find.text('Not checked at all'), findsOneWidget);
      expect(find.text('• Doses and how a medicine is taken'), findsOneWidget);
    });

    testWidgets(
      'incomplete: missing, withheld and out-of-date information is named',
      (tester) async {
        await open(
          tester,
          (r) => latest(
            assessment(
              status: 'Incomplete',
              complete: false,
              findings: [],
              evaluations: [
                {
                  'ruleId': 'T-INT',
                  'version': 1,
                  'domain': 'Interaction',
                  'outcome': 'MissingData',
                  'reasons': [
                    'input.allergies.never_recorded',
                    'input.symptoms.not_authorized',
                  ],
                  'activation': 'Active',
                  'isDemo': false,
                  'partial': false,
                  'findingCount': 0,
                },
              ],
            ),
          ),
        );
        expect(find.text('The check is incomplete'), findsOneWidget);
        expect(
          find.textContaining('never treated as nothing found'),
          findsOneWidget,
        );
        expect(
          find.textContaining('No allergies have been recorded.'),
          findsOneWidget,
        );
        expect(
          find.textContaining('Symptoms may not be read by this account.'),
          findsOneWidget,
        );
        expect(find.text('Never recorded'), findsOneWidget);
      },
    );

    testWidgets('an outdated result says why', (tester) async {
      await open(
        tester,
        (r) => latest(
          assessment(outdated: true, outdatedReasons: ['rules.changed']),
        ),
      );
      expect(find.text('This result may be out of date'), findsOneWidget);
      expect(
        find.textContaining('The set of rules in force changed.'),
        findsOneWidget,
      );
    });

    testWidgets(
      'blocked by authorization and a technical failure are different states',
      (tester) async {
        await open(tester, (r) => json(403, {'title': 'Access denied'}));
        expect(find.text('This is not available to you'), findsOneWidget);
        expect(find.text('The check is unavailable right now'), findsNothing);
      },
    );

    testWidgets(
      'a technical failure says there is no result and that it is not "nothing found"',
      (tester) async {
        await open(
          tester,
          (r) => json(503, {'code': 'assessment.unavailable'}),
        );
        expect(find.text('The check is unavailable right now'), findsOneWidget);
        expect(
          find.textContaining('This does not mean nothing was found'),
          findsOneWidget,
        );
      },
    );

    testWidgets('no check yet: offers to run one and shows the new result', (
      tester,
    ) async {
      final calls = <String>[];
      await open(
        tester,
        (r) => r.method == 'POST'
            ? json(201, assessment())
            : json(404, {'code': 'assessment.none'}),
        calls: calls,
      );
      expect(find.text('No check has been run yet'), findsOneWidget);
      await tester.tap(find.text('Run the check'));
      await tester.pumpAndSettle();
      expect(find.text('The check found something to look at'), findsOneWidget);
      expect(
        calls.any(
          (c) => c == 'POST /patients/user-1/safety/assessments?locale=en',
        ),
        isTrue,
      );
    });

    testWidgets(
      'Persian: right-to-left, the not-yet-reviewed notice, no raw keys',
      (tester) async {
        await open(
          tester,
          (r) => latest(assessment()),
          locale: const Locale('fa'),
        );
        expect(find.text('متن فارسی هنوز بازبینی نشده است'), findsOneWidget);
        expect(find.text('بررسی مواردی برای توجه پیدا کرد'), findsOneWidget);
        expect(find.textContaining(RegExp(r'\bsf\.[a-z]')), findsNothing);
        expect(
          Directionality.of(tester.element(find.byType(Scaffold).first)),
          TextDirection.rtl,
        );
      },
    );
  });
}
