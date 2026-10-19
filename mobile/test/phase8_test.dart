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

Map<String, dynamic> adherence() => {
  'provenance': 'PatientReported',
  'intakeStale': false,
  'notice': 'This is what the patient recorded. It is not verified, it is not a measure of how well a treatment works, and a planned dose with no record is not counted as missed.',
  'medications': [
    {
      'patientMedicationId': 'm1',
      'medicationName': 'Demopril',
      'isRegistered': true,
      'recordingAllowed': true,
      'plannedDoses': 10,
      'taken': 6,
      'skipped': 1,
      'missed': 1,
      'notRecorded': 2,
      'instructionGaps': ['route.unknown'],
    },
    {
      'patientMedicationId': 'm2',
      'medicationName': 'Unknownol',
      'isRegistered': false,
      'recordingAllowed': false,
      'plannedDoses': 0,
      'taken': 0,
      'skipped': 0,
      'missed': 0,
      'notRecorded': 0,
      'instructionGaps': ['dose.unknown', 'schedule.none'],
    },
  ],
};

Map<String, dynamic> message(
  Map<String, dynamic>? delivery, {
  bool reviewed = false,
}) => {
  'id': 'g1',
  'level': 'FollowUp',
  'status': 'Sent',
  'isDemo': false,
  'provenance': 'AutomatedAssessment',
  'clinicianReviewed': reviewed,
  'delivery': delivery,
  'patient': {
    'observed': 'X is not in the medication reference.',
    'whyItMatters': 'w',
    'suggestedAction': 'a',
    'whenToConsult': 'c',
    'urgentSigns': null,
    'basisAndConfidence': 'b',
  },
};

Map<String, dynamic> delivery(
  String state, {
  String mode = 'Simulated',
  String channel = 'local-simulated',
  String? errorCode,
}) => {
  'state': state,
  'mode': mode,
  'channel': channel,
  'attempts': 0,
  'errorCode': errorCode,
};

Map<String, dynamic> monitoring({
  bool enrolled = false,
  String state = 'Withdrawn',
  bool worker = false,
  String? outcome,
  String? error,
}) => {
  'enrolled': enrolled,
  'state': state,
  'workerEnabled': worker,
  'lastAttemptAt': null,
  'lastSuccessAt': null,
  'lastOutcome': outcome,
  'lastErrorCode': error,
  'nextCheckAt': null,
  'consecutiveFailures': 0,
  'notice': 'n',
};

Map<String, dynamic> emptyAssessment() => {
  'id': 'a1',
  'trigger': 'scheduled',
  'guidanceState': 'NotApplicable',
  'outdated': false,
  'outdatedReasons': <String>[],
  'openGuidanceNoLongerMatching': <Object>[],
  'notice': 'x',
  'change': {
    'firstAssessment': false,
    'added': 1,
    'removed': 0,
    'changed': 2,
    'ruleSetChanged': false,
  },
  'result': {
    'status': 'CompletedNoMatches',
    'complete': true,
    'containsDemonstration': false,
    'engineVersion': 'engine-1.0.0',
    'ruleSetVersion': 'rs-1',
    'evaluatedAt': '2026-10-10T08:00:00Z',
    'inputs': <Object>[],
    'evaluations': <Object>[],
    'findings': <Object>[],
    'coverage': {
      'activeRules': 1,
      'demonstrationRules': 0,
      'inactiveRules': 0,
      'notEvaluable': 0,
      'domainsCovered': ['Interaction'],
      'unsupportedDomains': ['dose_and_route'],
    },
    'limitations': <String>[],
  },
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
    size: const Size(390, 3200),
    httpClient: MockClient((r) async {
      if (r.url.path == '/auth/login') return json(200, login);
      log?.add(
        '${r.method} ${r.url.path}${r.url.hasQuery ? '?${r.url.query}' : ''} ${r.body}',
      );
      return handler(r);
    }),
  );

  group('adherence on the taking screen', () {
    final past = DateTime.now()
        .subtract(const Duration(hours: 2))
        .toUtc()
        .toIso8601String();
    final future = DateTime.now()
        .add(const Duration(hours: 2))
        .toUtc()
        .toIso8601String();
    Map<String, dynamic> slot(String id, String at, {String? status}) => {
      'scheduleEntryId': id,
      'patientMedicationId': 'm1',
      'medicationName': 'Demopril',
      'localDate': '2026-10-14',
      'localTime': '08:00:00',
      'scheduledFor': at,
      'status': status,
    };

    testWidgets(
      'only a dose that has come can be reported missed; the report is sent as Missed with no taken time',
      (tester) async {
        final log = <String>[];
        await open(tester, '/profile/taking', (r) {
          if (r.url.path.endsWith('/doses')) {
            return json(200, [slot('e1', past), slot('e2', future)]);
          }
          if (r.url.path.endsWith('/adherence/summary')) {
            return json(200, adherence());
          }
          if (r.method == 'POST') return json(200, <String, dynamic>{});
          return json(200, <Object>[]);
        }, log: log);
        expect(find.text('I missed it'), findsOneWidget);
        await tester.tap(find.text('I missed it'));
        await tester.pumpAndSettle();
        final post = log.firstWhere(
          (l) => l.startsWith('POST') && l.contains('/intake'),
        );
        final body = jsonDecode(
          post.substring(post.indexOf('{')),
        ) as Map<String, dynamic>;
        expect(body['status'], 'Missed');
        expect(body['takenAt'], isNull);
        expect(body['scheduleEntryId'], 'e1');
      },
    );

    testWidgets(
      'a missed dose is shown with a calm note and never with advice to compensate',
      (tester) async {
        await open(tester, '/profile/taking', (r) {
          if (r.url.path.endsWith('/doses')) {
            return json(200, [slot('e1', past, status: 'Missed')]);
          }
          if (r.url.path.endsWith('/adherence/summary')) {
            return json(200, adherence());
          }
          return json(200, <Object>[]);
        });
        expect(find.text('Reported missed'), findsWidgets);
        expect(find.textContaining('do not take an extra dose'), findsWidgets);
        expect(find.textContaining('take two'), findsNothing);
      },
    );

    testWidgets(
      'the summary says patient-reported, lists unknown instructions and does not count silence as missed',
      (tester) async {
        await open(tester, '/profile/taking', (r) {
          if (r.url.path.endsWith('/adherence/summary')) {
            return json(200, adherence());
          }
          return json(200, <Object>[]);
        });
        expect(find.text('Adherence, as you reported it'), findsOneWidget);
        expect(find.text('Reported by the patient'), findsOneWidget);
        expect(find.textContaining('not counted as missed'), findsOneWidget);
        expect(
          find.text(
            '• The dose is not known, so planned doses cannot be recorded.',
          ),
          findsOneWidget,
        );
        expect(find.text('Cannot be recorded yet'), findsOneWidget);
        expect(find.textContaining(RegExp(r'\d\s?%')), findsNothing);
      },
    );
  });

  group('delivery labels on messages', () {
    Future<void> show(
      WidgetTester tester,
      Map<String, dynamic>? d, {
      bool reviewed = false,
      Locale locale = const Locale('en'),
    }) => open(
      tester,
      '/profile/messages',
      (r) => r.url.path.endsWith('/guidance/messages')
          ? json(200, [message(d, reviewed: reviewed)])
          : json(200, <Object>[]),
      locale: locale,
    );

    testWidgets('no delivery means in the app only', (tester) async {
      await show(tester, null);
      expect(
        find.text('In the app only. No notification was scheduled.'),
        findsOneWidget,
      );
      expect(find.text('Automated check'), findsOneWidget);
      expect(find.text('Not reviewed by a professional yet'), findsOneWidget);
    });

    testWidgets('scheduled is not sent', (tester) async {
      await show(tester, delivery('Scheduled'));
      expect(
        find.text('A notification is scheduled. It has not been sent yet.'),
        findsOneWidget,
      );
    });

    testWidgets('attempted is not delivered', (tester) async {
      await show(tester, delivery('Attempted'));
      expect(
        find.text(
          'A notification attempt started and its result is not known.',
        ),
        findsOneWidget,
      );
    });

    testWidgets('a simulated confirmation never reads as delivered', (
      tester,
    ) async {
      await show(tester, delivery('Confirmed'));
      expect(
        find.text(
          'Simulated notification: nothing real was sent to any device.',
        ),
        findsOneWidget,
      );
      expect(find.textContaining('Delivered through'), findsNothing);
    });

    testWidgets('only a real confirmation names its channel', (tester) async {
      await show(
        tester,
        delivery('Confirmed', mode: 'Real', channel: 'push-x'),
      );
      expect(find.text('Delivered through push-x.'), findsOneWidget);
    });

    const notSentEn = {
      'message.resolved': 'This message no longer needs to be sent.',
      'patient.missing': 'Not sent: patient information is unavailable.',
      'channel.unavailable': 'Not sent: the sending service is unavailable.',
      'message.missing': 'Not sent: the message is unavailable.',
    };
    const notSentFa = {
      'message.resolved': 'این پیام دیگر نیاز به ارسال ندارد.',
      'patient.missing': 'ارسال انجام نشد؛ اطلاعات بیمار در دسترس نیست.',
      'channel.unavailable': 'ارسال انجام نشد؛ سرویس ارسال در دسترس نیست.',
      'message.missing': 'ارسال انجام نشد؛ پیام موردنظر در دسترس نیست.',
    };
    for (final code in notSentEn.keys) {
      testWidgets(
        'nothing was attempted ($code): neutral wording, not a failure',
        (tester) async {
          await show(tester, delivery('Failed', errorCode: code));
          expect(find.text(notSentEn[code]!), findsOneWidget);
          expect(find.textContaining('could not be delivered'), findsNothing);
        },
      );

      testWidgets('approved Persian wording for $code', (tester) async {
        await show(
          tester,
          delivery('Failed', errorCode: code),
          locale: const Locale('fa'),
        );
        expect(find.text(notSentFa[code]!), findsOneWidget);
        expect(
          find.text('اعلان تحویل داده نشد. پیام همچنان اینجاست.'),
          findsNothing,
        );
      });
    }

    for (final code in [
      'channel.refused',
      'channel.transient_exhausted',
      'interrupted',
    ]) {
      testWidgets('a real failure ($code) keeps the failed wording', (
        tester,
      ) async {
        await show(tester, delivery('Failed', errorCode: code));
        expect(
          find.text(
            'The notification could not be delivered. The message is still here.',
          ),
          findsOneWidget,
        );
      });
    }

    testWidgets('a not-failed state ignores a stale not-sent code', (
      tester,
    ) async {
      await show(tester, delivery('Scheduled', errorCode: 'message.resolved'));
      expect(
        find.text('A notification is scheduled. It has not been sent yet.'),
        findsOneWidget,
      );
    });

    testWidgets(
      'failed keeps the message and a professional review is its own fact',
      (tester) async {
        await show(tester, delivery('Failed'), reviewed: true);
        expect(
          find.text(
            'The notification could not be delivered. The message is still here.',
          ),
          findsOneWidget,
        );
        expect(find.text('Reviewed by a professional'), findsOneWidget);
        expect(find.text('Automated check'), findsOneWidget);
      },
    );
  });

  group('scheduled checks card', () {
    http.Response Function(http.Request) safety(Map<String, dynamic> mon) =>
        (r) {
          if (r.url.path.contains('/safety/monitoring')) {
            return r.method == 'POST'
                ? json(200, monitoring(enrolled: true, state: 'Idle'))
                : json(200, mon);
          }
          if (r.url.path.endsWith('/assessments/latest')) {
            return json(200, emptyAssessment());
          }
          return json(404, <String, dynamic>{});
        };

    testWidgets(
      'off by default, says it is not continuous monitoring, and enrols only on a tap',
      (tester) async {
        final log = <String>[];
        await open(tester, '/profile/safety', safety(monitoring()), log: log);
        expect(find.text('Scheduled checks are off'), findsOneWidget);
        expect(
          find.textContaining('not continuous or real-time monitoring'),
          findsOneWidget,
        );
        expect(
          log.where((l) => l.startsWith('POST') && l.contains('monitoring')),
          isEmpty,
        );
        await tester.ensureVisible(find.text('Turn on scheduled checks'));
        await tester.tap(find.text('Turn on scheduled checks'));
        await tester.pumpAndSettle();
        expect(
          log.any(
            (l) => l.startsWith(
              'POST /patients/user-1/safety/monitoring?locale=en',
            ),
          ),
          isTrue,
        );
        expect(find.text('Scheduled checks are on'), findsOneWidget);
      },
    );

    testWidgets(
      'says plainly when the runner is off and when a job stopped, and shows how the last check was started',
      (tester) async {
        await open(
          tester,
          '/profile/safety',
          safety(
            monitoring(
              enrolled: true,
              state: 'Exhausted',
              outcome: 'failed.transient',
              error: 'guidance.incomplete',
            ),
          ),
        );
        expect(
          find.text(
            'The background runner is switched off on this system, so nothing runs by itself.',
          ),
          findsOneWidget,
        );
        expect(find.text('Stopped after repeated problems'), findsOneWidget);
        expect(find.textContaining('could not be confirmed'), findsOneWidget);
        expect(find.text('Start again'), findsOneWidget);
        expect(
          find.textContaining('Scheduled check (automatic)'),
          findsOneWidget,
        );
        expect(
          find.textContaining(
            'Since the previous check: 1 records added, 0 removed, 2 changed.',
          ),
          findsOneWidget,
        );
      },
    );

    testWidgets('Persian renders without raw keys', (tester) async {
      await open(
        tester,
        '/profile/safety',
        safety(monitoring()),
        locale: const Locale('fa'),
      );
      expect(find.text('بررسی زمان‌بندی‌شده خاموش است'), findsOneWidget);
      expect(find.textContaining(RegExp(r'\bsf\.mon\.')), findsNothing);
    });
  });
}
