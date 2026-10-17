import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

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

Map<String, dynamic> summary({String status = 'Submitted'}) => {
  'id': 'e1',
  'subjectId': 'user-1',
  'reporterRole': 'Patient',
  'origin': 'PatientReported',
  'reportedAt': '2026-10-10T08:00:00Z',
  'category': null,
  'status': status,
  'lastActivityAt': '2026-10-10T08:00:00Z',
  'excerpt': 'Itchy rash on both arms',
};

Map<String, dynamic> detail({List<Map<String, dynamic>> entries = const []}) =>
    {
      'event': {
        'id': 'e1',
        'status': 'InformationRequested',
        'category': null,
        'original': {
          'eventDescription': 'Itchy rash on both arms',
          'onsetAt': null,
          'doseAndAdministrationAsReported': null,
        },
        'missingInformation': ['onset.unknown', 'dose.not_stated'],
      },
      'entries': entries,
    };

void main() {
  Future<void> open(
    WidgetTester tester,
    String location,
    http.Response Function(http.Request r) handler, {
    List<String>? log,
    Locale locale = const Locale('en'),
  }) => pumpApp(
    tester,
    location: location,
    locale: locale,
    size: const Size(390, 3600),
    httpClient: MockClient((r) async {
      if (r.url.path == '/auth/login') return json(200, login);
      log?.add('${r.method} ${r.url.path} ${r.body}');
      return handler(r);
    }),
  );

  group('adverse-event reports (patient)', () {
    testWidgets(
      'lists the patient\'s own reports with the signal and internal-record notices, no category, and says it sends no urgent alerts',
      (tester) async {
        await open(
          tester,
          '/profile/adverse-events',
          (r) => r.url.path.endsWith('/adverse-events')
              ? json(200, [summary()])
              : json(404, <String, dynamic>{}),
        );
        expect(
          find.text(
            'This record is a signal for human review. It is not proof that a medicine caused anything.',
          ),
          findsOneWidget,
        );
        expect(find.textContaining('has not been submitted'), findsOneWidget);
        expect(
          find.textContaining('does not send urgent alerts'),
          findsOneWidget,
        );
        expect(find.text('Itchy rash on both arms'), findsOneWidget);
        expect(find.text('Submitted'), findsOneWidget);
        expect(find.text('Not classified'), findsNothing);
      },
    );

    testWidgets('an empty list says so calmly', (tester) async {
      await open(
        tester,
        '/profile/adverse-events',
        (r) => json(200, <Object>[]),
      );
      expect(find.text('No records yet'), findsOneWidget);
    });

    testWidgets(
      'a failed load shows a retry state and a network failure is explained in words',
      (tester) async {
        await open(
          tester,
          '/profile/adverse-events',
          (r) => json(500, <String, dynamic>{}),
        );
        expect(find.text('Try again'), findsWidgets);
        expect(find.textContaining('adr.'), findsNothing);
      },
    );

    testWidgets(
      'the form sends nulls for unknown values and a request id, and only after something is written',
      (tester) async {
        final log = <String>[];
        await open(tester, '/profile/adverse-events', (r) {
          if (r.method == 'POST') return json(201, detail());
          return json(200, <Object>[]);
        }, log: log);
        final save = find.text('Save this record');
        await tester.ensureVisible(save);
        await tester.tap(save); // nothing written yet: the button is disabled
        await tester.pump();
        expect(log.where((l) => l.startsWith('POST')), isEmpty);
        await tester.enterText(
          find.byType(TextField).first,
          'Dizzy after lunch',
        );
        await tester.pump();
        await tester.tap(save);
        await tester.pumpAndSettle();
        final post = log.firstWhere((l) => l.startsWith('POST'));
        final body = jsonDecode(
          post.substring(post.indexOf('{')),
        ) as Map<String, dynamic>;
        expect(body['eventDescription'], 'Dizzy after lunch');
        expect(body['onsetAt'], isNull);
        expect(body['resolvedAt'], isNull);
        expect(body['outcomeAsStated'], isNull);
        expect(body['doseAndAdministrationAsReported'], isNull);
        expect(body['suspectedMedications'], isEmpty);
        expect((body['clientRequestId'] as String).length, greaterThan(8));
      },
    );

    testWidgets(
      'the detail shows the original, what is not known and the question a professional asked, and offers a follow-up',
      (tester) async {
        await open(
          tester,
          '/profile/adverse-events/e1',
          (r) => json(
            200,
            detail(
              entries: [
                {
                  'id': 'n1',
                  'kind': 'InformationRequest',
                  'text': 'When did the rash start?',
                },
              ],
            ),
          ),
        );
        expect(find.text('Itchy rash on both arms'), findsOneWidget);
        expect(find.textContaining('The start is not known'), findsOneWidget);
        expect(
          find.textContaining('dose and how it was taken were not stated'),
          findsOneWidget,
        );
        expect(find.text('When did the rash start?'), findsOneWidget);
        expect(find.text('Not submitted anywhere'), findsOneWidget);
        expect(find.text('Add information'), findsWidgets);
      },
    );

    testWidgets(
      'Persian: right-to-left, the not-yet-reviewed notice, no raw keys',
      (tester) async {
        await open(
          tester,
          '/profile/adverse-events',
          (r) => json(200, <Object>[]),
          locale: const Locale('fa'),
        );
        expect(find.text('متن فارسی هنوز بازبینی نشده است'), findsOneWidget);
        expect(find.textContaining(RegExp(r'\badr\.')), findsNothing);
      },
    );
  });
}
