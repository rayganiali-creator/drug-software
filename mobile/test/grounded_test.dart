import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:medsmarter_mobile/api/api_session.dart';
import 'package:medsmarter_mobile/api/assistant_client.dart';

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

Map<String, dynamic> item({String id = 'E1', bool stale = false}) => {
  'id': id,
  'medicationName': 'Nocturin',
  'kind': 'Warning',
  'text': 'Fictional warning: may cause demo drowsiness.',
  'qualifier': null,
  'source': {
    'sourceId': 's1',
    'name': 'DEMO seed (fictional)',
    'version': 'demo-0.1',
    'publisher': 'AI MedSmarter test fixtures',
    'receivedAt': '2026-09-01T00:00:00Z',
    'validation': 'Demo',
  },
  'validation': 'Demo',
  'isDemo': true,
  'stale': stale,
  'sourceDateUnknown': false,
};

Map<String, dynamic> answer({
  String status = 'Answered',
  String? reason = 'answered',
  String text = '[MOCK AI] No language model was used. Nocturin: Warning ([E1]).',
  bool mock = true,
  List<Map<String, dynamic>>? items,
  List<String> limitations = const ['evidence.demo_data'],
  String nextStep = 'ConsultProfessional',
  String quality = 'DemoOnly',
  Map<String, dynamic>? generation,
}) => {
  'text': text,
  'provider': mock ? 'mock' : 'none',
  'isMock': mock,
  'answered': status == 'Answered',
  'notice': 'DEMO DATA - NOT FOR CLINICAL USE',
  'error': 'None',
  'patientContextUsed': false,
  'status': status,
  'reason': reason,
  'evidence': {
    'items': items ?? [item()],
    'conflicts': <Object>[],
    'missingInformation': <String>[],
    'limitations': limitations,
    'quality': quality,
  },
  'evidenceQuality': quality,
  'limitations': limitations,
  'missingInformation': <String>[],
  'nextStep': nextStep,
  'generation':
      generation ??
      (mock
          ? {'provider': 'mock', 'kind': 'Mock', 'isMock': true, 'external': false}
          : null),
  'contractVersion': 'ai-answer-1',
};

MockClient fake(http.Response Function(http.Request r) handler, {List<String>? bodies}) =>
    MockClient((r) async {
      if (r.url.path == '/auth/login') return json(200, login);
      bodies?.add(r.body);
      return handler(r);
    });

Future<AssistantClient> signedIn(MockClient c, {Duration? timeout}) async {
  final s = ApiSession(baseUrl: 'http://api.test', client: c);
  await s.login('demo-patient');
  return AssistantClient(s, timeout: timeout ?? const Duration(seconds: 30));
}

void main() {
  group('AssistantClient', () {
    test('is not connected without a token and never pretends', () async {
      await expectLater(
        AssistantClient(null).ask(question: 'q', locale: 'en'),
        throwsA(isA<AssistantException>().having((e) => e.kind, 'kind', AssistantErrorKind.notConnected)),
      );
    });

    test('posts the question and reads the structured answer, also from a 503', () async {
      final bodies = <String>[];
      final c = await signedIn(fake((r) => json(200, answer()), bodies: bodies));
      final a = await c.ask(question: 'Nocturin', locale: 'en');
      expect(a.status, 'Answered');
      expect(a.isMock, isTrue);
      expect(a.items, hasLength(1));
      expect(jsonDecode(bodies.single), {
        'question': 'Nocturin',
        'locale': 'en',
        'medicationIds': null,
        'includePatientContext': false,
      });
      final c2 = await signedIn(fake((r) => json(503, answer(status: 'Unavailable', reason: 'provider.timeout', mock: false))));
      expect((await c2.ask(question: 'q', locale: 'en')).status, 'Unavailable');
    });

    for (final (status, kind) in [
      (401, AssistantErrorKind.unauthorized),
      (403, AssistantErrorKind.unauthorized),
      (429, AssistantErrorKind.rateLimited),
      (400, AssistantErrorKind.invalid),
      (500, AssistantErrorKind.server),
    ]) {
      test('maps HTTP $status to $kind', () async {
        final c = await signedIn(fake((r) => json(status, {})));
        await expectLater(
          c.ask(question: 'q', locale: 'en'),
          throwsA(isA<AssistantException>().having((e) => e.kind, 'kind', kind)),
        );
      });
    }

    test('reports a slow answer as a timeout and a broken connection as network', () async {
      final slow = MockClient((r) async {
        if (r.url.path == '/auth/login') return json(200, login);
        await Future<void>.delayed(const Duration(milliseconds: 300));
        return json(200, answer());
      });
      final c = await signedIn(slow, timeout: const Duration(milliseconds: 30));
      await expectLater(
        c.ask(question: 'q', locale: 'en'),
        throwsA(isA<AssistantException>().having((e) => e.kind, 'kind', AssistantErrorKind.timeout)),
      );
      final broken = MockClient((r) async {
        if (r.url.path == '/auth/login') return json(200, login);
        throw http.ClientException('offline');
      });
      await expectLater(
        (await signedIn(broken)).ask(question: 'q', locale: 'en'),
        throwsA(isA<AssistantException>().having((e) => e.kind, 'kind', AssistantErrorKind.network)),
      );
    });
  });

  group('grounded assistant screen', () {
    Future<void> open(
      WidgetTester tester,
      http.Response Function(http.Request r) handler, {
      Locale locale = const Locale('en'),
      Size size = const Size(390, 1600),
    }) async {
      await pumpApp(tester, location: '/assistant/live', locale: locale, size: size, httpClient: fake(handler));
    }

    Future<void> ask(WidgetTester tester, String text) async {
      await tester.enterText(find.byType(TextField).first, text);
      await tester.pump();
      await tester.tap(find.text(tester.element(find.byType(Scaffold).first).mounted ? (text.contains(RegExp('[؀-ۿ]')) ? 'پرسیدن' : 'Ask') : 'Ask'));
      await tester.pumpAndSettle();
    }

    testWidgets('shows a MOCK answer with its evidence apart from the text, and no numeric confidence', (tester) async {
      await open(tester, (r) => json(200, answer()));
      await ask(tester, 'Tell me about Nocturin');
      expect(find.text('Answer'), findsOneWidget);
      expect(find.text('MOCK: no language model was used'), findsOneWidget);
      expect(find.text('Fictional demonstration data only'), findsOneWidget);
      expect(find.text('Fictional warning: may cause demo drowsiness.'), findsOneWidget);
      expect(find.text('[E1]'), findsOneWidget);
      expect(find.text('DEMO seed (fictional)'), findsNothing); // source details are collapsed until asked for
      await tester.ensureVisible(find.text('Show details'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Show details'));
      await tester.pumpAndSettle();
      expect(find.text('DEMO seed (fictional)'), findsOneWidget);
      expect(find.text('demo-0.1'), findsOneWidget);
      expect(find.textContaining('not a probability'), findsOneWidget);
      expect(find.text('Talk to a pharmacist or doctor about what this means for you.'), findsOneWidget);
      expect(find.textContaining(RegExp(r'\d\s?%')), findsNothing);
      expect(find.textContaining('confidence'), findsNothing);
    });

    testWidgets('an escalation stays urgent and says what the check is', (tester) async {
      await open(
        tester,
        (r) => json(
          200,
          answer(
            status: 'Escalated',
            reason: 'emergency_signs',
            mock: false,
            text: 'What you describe can be a sign of something that needs help right away. Please call your local emergency number now.',
            items: <Map<String, dynamic>>[],
            limitations: ['screen.keyword_based'],
            nextStep: 'EmergencyServices',
            quality: 'None',
          ),
        ),
      );
      await ask(tester, "I can't breathe");
      expect(find.text('This may need help right away'), findsOneWidget);
      expect(find.textContaining('call your local emergency number now'), findsOneWidget);
      expect(find.text('Call your local emergency number now or go to the nearest emergency service.'), findsOneWidget);
      expect(find.textContaining('works on keywords'), findsOneWidget);
    });

    testWidgets('a refusal and an empty-evidence answer are calm and name the reason', (tester) async {
      await open(
        tester,
        (r) => json(200, answer(status: 'Refused', reason: 'medication_change', mock: false, text: 'Whether to take a medicine is a decision for you and your doctor.', items: <Map<String, dynamic>>[], limitations: [], nextStep: 'ConsultPrescriber', quality: 'None')),
      );
      await ask(tester, 'Should I stop?');
      expect(find.text("I can't help with that here"), findsOneWidget);
      expect(find.textContaining('Starting, stopping or changing a medicine is for you and your prescriber'), findsOneWidget);
    });

    testWidgets('a withheld or unavailable answer says so and keeps the sources', (tester) async {
      await open(tester, (r) => json(200, answer(status: 'Blocked', reason: 'answer.blocked', mock: false, text: 'The generated answer did not pass our safety check, so it is not shown.')));
      await ask(tester, 'Nocturin');
      expect(find.text('The generated answer is not shown'), findsOneWidget);
      expect(find.text('Fictional warning: may cause demo drowsiness.'), findsOneWidget);
    });

    testWidgets('a blocked external call says nothing was sent', (tester) async {
      await open(tester, (r) => json(200, answer(status: 'Unavailable', reason: 'external.consent_required', mock: false, text: 'The conditions for using an outside service are not met, so nothing was sent.')));
      await ask(tester, 'Nocturin');
      expect(find.textContaining('You have not agreed to use an outside service, so nothing was sent.'), findsOneWidget);
    });

    testWidgets('an HTTP error is explained in words', (tester) async {
      await open(tester, (r) => json(429, {}));
      await ask(tester, 'Nocturin');
      expect(find.text('Too many questions in a short time. Wait a moment.'), findsOneWidget);
    });

    testWidgets('renders Persian (RTL) labels', (tester) async {
      await open(tester, (r) => json(200, answer()), locale: const Locale('fa'));
      await ask(tester, 'نوکتورین');
      expect(find.text('پاسخ'), findsOneWidget);
      expect(find.text('آزمایشی (MOCK): مدل زبانی استفاده نشد'), findsOneWidget);
    });

    testWidgets('the prototype conversation is labelled as a prototype and links to the real assistant', (tester) async {
      await pumpApp(tester, location: '/assistant');
      expect(find.text('Prototype: scripted sample answers'), findsOneWidget);
      expect(find.text('Open source-based assistant'), findsOneWidget);
    });
  });
}
