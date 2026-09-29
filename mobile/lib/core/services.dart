import 'dart:async';

import 'l10n.dart';
import 'models.dart';

// Service contracts used by the UI. Phase 2 only ships Mock implementations; a real backend
// implementation replaces MockServices later without touching any screen.

abstract class MedicationService {
  Future<Drug> drug(String id);
  Future<List<PatientMedication>> forPatient(String patientId);
  Future<List<Dose>> todaysDoses(String patientId);
  Future<List<Dose>> setDose(String doseId, DoseStatus status);
}

abstract class PatientService {
  Future<Patient> current();
  Future<List<Patient>> all();
  Future<CheckInConfig> checkInConfig();
  Future<bool> submitCheckIn({
    required int mood,
    required List<String> symptomIds,
    required String note,
  });
  Future<bool> hasCheckedInToday();
}

abstract class PrescriptionService {
  Future<List<Prescription>> find({String? patientId});
  Future<List<Interaction>> interactionsForPatient(String patientId);
}

abstract class AIService {
  Future<List<Conversation>> conversations();
  Future<List<Localized>> quickQuestions();
  Future<AssistantAnswer> ask({
    required String text,
    required String locale,
    String? contextDrugId,
  });
  Future<void> escalate(String answerId);
}

abstract class ADRService {
  Future<List<AdverseReport>> reports({String? patientId});
}

abstract class NotificationService {
  Future<List<HealthAlert>> alerts(String patientId);
  Future<List<ActivityItem>> activity(String patientId);
}

abstract class AnalyticsService {
  Future<List<int>> adherenceSeries(String patientId);
}

class AppServices {
  const AppServices({
    required this.medications,
    required this.patients,
    required this.prescriptions,
    required this.ai,
    required this.adr,
    required this.notifications,
    required this.analytics,
  });
  final MedicationService medications;
  final PatientService patients;
  final PrescriptionService prescriptions;
  final AIService ai;
  final ADRService adr;
  final NotificationService notifications;
  final AnalyticsService analytics;
}

/// In-memory Mock implementation over the shared DEMO DATA. No network, no LLM.
class MockServices
    implements
        AppServices,
        MedicationService,
        PatientService,
        PrescriptionService,
        AIService,
        ADRService,
        NotificationService,
        AnalyticsService {
  MockServices(
    this.data, {
    this.latency = const Duration(milliseconds: 220),
    DateTime Function()? now,
  }) : _now = now ?? DateTime.now {
    _prescriptions = [
      for (final r in data.raw['prescriptions'] as List)
        Prescription(
          id: r['id'] as String,
          patient: data.patients[r['patientId']]!,
          prescriber: Localized.fromJson(r['prescriber']),
          issuedDaysAgo: r['issuedDaysAgo'] as int,
          status: r['status'] as String,
          refillsLeft: r['refillsLeft'] as int,
          items: [
            for (final i in r['items'] as List)
              PrescriptionItem(
                drug: data.drug(i['drugId'] as String),
                dose: Localized.fromJson(i['dose']),
                frequency: Localized.fromJson(i['frequency']),
              ),
          ],
        ),
    ];
    _rawRx = {
      for (final r in data.raw['prescriptions'] as List)
        r['id'] as String: r as Map<String, dynamic>,
    };
    _interactions = [
      for (final x in data.raw['interactions'] as List)
        Interaction(
          id: x['id'] as String,
          drugA: data.drug(x['drugAId'] as String),
          drugB: data.drug(x['drugBId'] as String),
          severity: x['severity'] as String,
          summary: Localized.fromJson(x['summary']),
          management: Localized.fromJson(x['management']),
          source: data.source(x['sourceId'] as String),
        ),
    ];
  }

  final DemoData data;
  final Duration latency;
  final DateTime Function() _now;
  late final List<Prescription> _prescriptions;
  late final Map<String, Map<String, dynamic>> _rawRx;
  late final List<Interaction> _interactions;
  final Set<String> _taken = {};
  bool _checkedIn = false;

  @override
  MedicationService get medications => this;
  @override
  PatientService get patients => this;
  @override
  PrescriptionService get prescriptions => this;
  @override
  AIService get ai => this;
  @override
  ADRService get adr => this;
  @override
  NotificationService get notifications => this;
  @override
  AnalyticsService get analytics => this;

  Future<T> _call<T>(T Function() fn) async {
    if (latency > Duration.zero) await Future<void>.delayed(latency);
    return fn();
  }

  static int _minutes(String t) =>
      int.parse(t.substring(0, 2)) * 60 + int.parse(t.substring(3, 5));
  static String _pad(int n) => n.toString().padLeft(2, '0');
  String _dayKey(DateTime d) => '${d.year}-${_pad(d.month)}-${_pad(d.day)}';

  // ---- medications ----
  @override
  Future<Drug> drug(String id) => _call(() => data.drug(id));

  List<PatientMedication> _medsFor(String patientId) => [
    for (final rx in _prescriptions.where(
      (p) => p.patient.id == patientId && p.status != 'pending-review',
    ))
      for (final raw in (_rawRx[rx.id]!['items'] as List))
        PatientMedication(
          drug: data.drug(raw['drugId'] as String),
          dose: Localized.fromJson(raw['dose']),
          frequency: Localized.fromJson(raw['frequency']),
          times: [for (final t in raw['times'] as List) t as String],
          prescriptionId: rx.id,
        ),
  ];

  @override
  Future<List<PatientMedication>> forPatient(String patientId) =>
      _call(() => _medsFor(patientId));

  List<Dose> _doses(String patientId) {
    final t = _now();
    final nowMin = t.hour * 60 + t.minute;
    final key = _dayKey(t);
    final seed =
        data.raw['patientHome']['missedSeedDose'] as Map<String, dynamic>;
    final seedKey = '${seed['prescriptionItemDrugId']}:${seed['time']}';
    final slots = <(Drug, String)>[
      for (final m in _medsFor(patientId))
        for (final time in m.times) (m.drug, time),
    ]..sort((a, b) => _minutes(a.$2).compareTo(_minutes(b.$2)));
    final doses = <Dose>[
      for (final s in slots)
        () {
          final id = '$key:${s.$1.id}:${s.$2}';
          var status = DoseStatus.upcoming;
          if (_taken.contains(id)) {
            status = DoseStatus.taken;
          } else if (_minutes(s.$2) <= nowMin) {
            status =
                ('${s.$1.id}:${s.$2}' == seedKey &&
                    patientId == data.currentPatientId)
                ? DoseStatus.missed
                : DoseStatus.taken;
          }
          return Dose(
            id: id,
            drug: s.$1,
            time: s.$2,
            status: status,
            isNext: false,
            tomorrow: false,
          );
        }(),
    ];
    final nextIndex = doses.indexWhere((d) => d.status == DoseStatus.upcoming);
    if (nextIndex >= 0) {
      doses[nextIndex] = doses[nextIndex].copyWith(isNext: true);
    } else if (doses.isNotEmpty) {
      final f = doses.first;
      doses.add(
        Dose(
          id: '${_dayKey(t.add(const Duration(days: 1)))}:${f.drug.id}:${f.time}',
          drug: f.drug,
          time: f.time,
          status: DoseStatus.upcoming,
          isNext: true,
          tomorrow: true,
        ),
      );
    }
    return doses;
  }

  @override
  Future<List<Dose>> todaysDoses(String patientId) =>
      _call(() => _doses(patientId));

  @override
  Future<List<Dose>> setDose(String doseId, DoseStatus status) => _call(() {
    if (status == DoseStatus.taken) {
      _taken.add(doseId);
    } else {
      _taken.remove(doseId);
    }
    return _doses(data.currentPatientId);
  });

  // ---- patients ----
  @override
  Future<Patient> current() =>
      _call(() => data.patients[data.currentPatientId]!);
  @override
  Future<List<Patient>> all() => _call(() => data.patients.values.toList());

  @override
  Future<CheckInConfig> checkInConfig() => _call(() {
    final c = data.raw['checkIn'] as Map<String, dynamic>;
    return CheckInConfig(
      moods: [
        for (final m in c['moods'] as List)
          MoodOption(m['value'] as int, Localized.fromJson(m['label'])),
      ],
      symptoms: [
        for (final s in c['symptoms'] as List)
          SymptomOption(
            s['id'] as String,
            Localized.fromJson(s['label']),
            s['urgent'] == true,
          ),
      ],
    );
  });

  @override
  Future<bool> submitCheckIn({
    required int mood,
    required List<String> symptomIds,
    required String note,
  }) => _call(() {
    _checkedIn = true;
    final symptoms = (data.raw['checkIn']['symptoms'] as List)
        .cast<Map<String, dynamic>>();
    return symptomIds.any(
      (id) => symptoms.firstWhere((s) => s['id'] == id)['urgent'] == true,
    );
  });

  @override
  Future<bool> hasCheckedInToday() => _call(() => _checkedIn);

  // ---- prescriptions ----
  @override
  Future<List<Prescription>> find({String? patientId}) => _call(
    () => [
      for (final p in _prescriptions)
        if (patientId == null || p.patient.id == patientId) p,
    ],
  );

  @override
  Future<List<Interaction>> interactionsForPatient(String patientId) =>
      _call(() {
        final ids = _medsFor(patientId).map((m) => m.drug.id).toSet();
        return [
          for (final x in _interactions)
            if (ids.contains(x.drugA.id) && ids.contains(x.drugB.id)) x,
        ];
      });

  // ---- AI: keyword rules over canned DEMO answers (no LLM) ----
  Map<String, dynamic> get _ai => data.raw['ai'] as Map<String, dynamic>;

  @override
  Future<List<Localized>> quickQuestions() => _call(
    () => [
      for (final q in _ai['quickQuestions'] as List)
        Localized.fromJson(q['text']),
    ],
  );

  @override
  Future<List<Conversation>> conversations() => _call(
    () => [
      for (final c in _ai['conversations'] as List)
        Conversation(
          id: c['id'] as String,
          title: Localized.fromJson(c['title']),
          updatedMinutesAgo: c['updatedMinutesAgo'] as int,
          messages: [
            for (final m in c['messages'] as List)
              if (m['role'] == 'user')
                ChatMessage.user(Localized.fromJson(m['text']))
              else
                ChatMessage.assistant(
                  data.answerFrom(
                    (_ai['answers'] as List).firstWhere(
                      (a) => a['id'] == m['answerId'],
                    ) as Map<String, dynamic>,
                  ),
                ),
          ],
        ),
    ],
  );

  bool _matchesAny(String text, Iterable<dynamic> words) =>
      words.any((w) => text.contains((w as String).toLowerCase()));

  @override
  Future<AssistantAnswer> ask({
    required String text,
    required String locale,
    String? contextDrugId,
  }) => _call(() {
    final ctx = contextDrugId == null
        ? ''
        : ' ${data.drug(contextDrugId).name.en} ${data.drug(contextDrugId).name.fa}';
    final lower = '$text$ctx'.toLowerCase();
    final rf = _ai['redFlag'] as Map<String, dynamic>;
    if (_matchesAny(lower, [
      ...rf['keywords']['en'] as List,
      ...rf['keywords']['fa'] as List,
    ])) {
      return AssistantAnswer(
        id: 'redflag-${DateTime.now().microsecondsSinceEpoch}',
        kind: AnswerKind.redFlag,
        title: Localized.fromJson(rf['title']),
        text: Localized.fromJson(rf['body']),
        confidence: Confidence.high,
        sources: const [],
        evidence: const [],
        followUps: const [],
        canEscalate: false,
      );
    }
    // Best keyword-hit count wins; ties go to the first rule.
    Map<String, dynamic>? best;
    var bestScore = 0;
    for (final a in (_ai['answers'] as List).cast<Map<String, dynamic>>()) {
      final words = [
        ...a['keywords']['en'] as List,
        ...a['keywords']['fa'] as List,
      ];
      final score = words
          .where((w) => lower.contains((w as String).toLowerCase()))
          .length;
      if (score > bestScore) {
        best = a;
        bestScore = score;
      }
    }
    return best != null
        ? data.answerFrom(best)
        : data.answerFrom(
            _ai['fallback'] as Map<String, dynamic>,
            kind: AnswerKind.refusal,
          );
  });

  @override
  Future<void> escalate(String answerId) => _call(() {});

  // ---- ADR / notifications / analytics ----
  @override
  Future<List<AdverseReport>> reports({String? patientId}) => _call(
    () => [
      for (final a in data.raw['adrReports'] as List)
        if (patientId == null || a['patientId'] == patientId)
          AdverseReport(
            id: a['id'] as String,
            patient: data.patients[a['patientId']]!,
            drug: data.drug(a['drugId'] as String),
            event: Localized.fromJson(a['event']),
            severity: a['severity'] as String,
            status: a['status'] as String,
            reportedDaysAgo: a['reportedDaysAgo'] as int,
            causality: a['causality'] as String,
          ),
    ],
  );

  @override
  Future<List<HealthAlert>> alerts(String patientId) => _call(
    () => [
      for (final a in data.raw['patientHome']['alerts'] as List)
        HealthAlert(
          id: a['id'] as String,
          severity: a['severity'] as String,
          title: Localized.fromJson(a['title']),
          body: Localized.fromJson(a['body']),
        ),
    ],
  );

  @override
  Future<List<ActivityItem>> activity(String patientId) => _call(
    () => [
      for (final a in data.raw['patientHome']['activity'] as List)
        ActivityItem(
          id: a['id'] as String,
          kind: a['kind'] as String,
          minutesAgo: a['minutesAgo'] as int,
          text: Localized.fromJson(a['text']),
        ),
    ],
  );

  @override
  Future<List<int>> adherenceSeries(String patientId) => _call(
    () => patientId == data.currentPatientId
        ? [
            for (final v
                in data.raw['patientHome']['adherenceSeries30'] as List)
              v as int,
          ]
        : data.patients[patientId]!.adherenceSeries,
  );
}
