import 'l10n.dart';

// Typed views over the shared DEMO DATA (design/mock/demo-data.mjs). Everything is fictional.

enum Confidence { high, medium, low }

Confidence _conf(String s) => Confidence.values.firstWhere((c) => c.name == s);

class ActiveIngredient {
  const ActiveIngredient(this.id, this.name);
  final String id;
  final Localized name;
}

class Drug {
  const Drug({
    required this.id,
    required this.name,
    required this.activeIngredient,
    required this.strength,
    required this.form,
    required this.route,
    required this.category,
    required this.instructions,
    required this.warnings,
  });
  final String id;
  final Localized name;
  final ActiveIngredient activeIngredient;
  final String strength;
  final Localized form;
  final Localized route;
  final Localized category;
  final Localized instructions;
  final List<Localized> warnings;
}

class KnowledgeSource {
  const KnowledgeSource({
    required this.id,
    required this.title,
    required this.type,
    required this.version,
  });
  final String id;
  final Localized title;
  final Localized type;
  final String version;
}

class PatientMedication {
  const PatientMedication({
    required this.drug,
    required this.dose,
    required this.frequency,
    required this.times,
    required this.prescriptionId,
  });
  final Drug drug;
  final Localized dose;
  final Localized frequency;
  final List<String> times;
  final String prescriptionId;
}

enum DoseStatus { taken, missed, upcoming }

class Dose {
  const Dose({
    required this.id,
    required this.drug,
    required this.time,
    required this.status,
    required this.isNext,
    required this.tomorrow,
  });
  final String id;
  final Drug drug;
  final String time;
  final DoseStatus status;
  final bool isNext;
  final bool tomorrow;
  Dose copyWith({DoseStatus? status, bool? isNext}) => Dose(
    id: id,
    drug: drug,
    time: time,
    status: status ?? this.status,
    isNext: isNext ?? this.isNext,
    tomorrow: tomorrow,
  );
}

class HealthAlert {
  const HealthAlert({
    required this.id,
    required this.severity,
    required this.title,
    required this.body,
  });
  final String id;
  final String severity; // info | warning | danger
  final Localized title;
  final Localized body;
}

class ActivityItem {
  const ActivityItem({
    required this.id,
    required this.kind,
    required this.minutesAgo,
    required this.text,
  });
  final String id;
  final String kind;
  final int minutesAgo;
  final Localized text;
}

class Interaction {
  const Interaction({
    required this.id,
    required this.drugA,
    required this.drugB,
    required this.severity,
    required this.summary,
    required this.management,
    required this.source,
  });
  final String id;
  final Drug drugA;
  final Drug drugB;
  final String severity; // minor | moderate | major
  final Localized summary;
  final Localized management;
  final KnowledgeSource source;
}

class Patient {
  const Patient({
    required this.id,
    required this.name,
    required this.age,
    required this.sex,
    required this.risk,
    required this.adherence,
    required this.conditions,
    required this.allergies,
    required this.adherenceSeries,
  });
  final String id;
  final Localized name;
  final int age;
  final String sex;
  final String risk; // low | medium | high
  final int adherence;
  final List<Localized> conditions;
  final List<Localized> allergies;
  final List<int> adherenceSeries;
}

class PrescriptionItem {
  const PrescriptionItem({
    required this.drug,
    required this.dose,
    required this.frequency,
  });
  final Drug drug;
  final Localized dose;
  final Localized frequency;
}

class Prescription {
  const Prescription({
    required this.id,
    required this.patient,
    required this.prescriber,
    required this.issuedDaysAgo,
    required this.status,
    required this.refillsLeft,
    required this.items,
  });
  final String id;
  final Patient patient;
  final Localized prescriber;
  final int issuedDaysAgo;
  final String status; // active | pending-review | dispensed
  final int refillsLeft;
  final List<PrescriptionItem> items;
}

class AdverseReport {
  const AdverseReport({
    required this.id,
    required this.patient,
    required this.drug,
    required this.event,
    required this.severity,
    required this.status,
    required this.reportedDaysAgo,
    required this.causality,
  });
  final String id;
  final Patient patient;
  final Drug drug;
  final Localized event;
  final String severity; // mild | moderate | serious
  final String status;
  final int reportedDaysAgo;
  final String causality;
}

class MoodOption {
  const MoodOption(this.value, this.label);
  final int value;
  final Localized label;
}

class SymptomOption {
  const SymptomOption(this.id, this.label, this.urgent);
  final String id;
  final Localized label;
  final bool urgent;
}

class CheckInConfig {
  const CheckInConfig({required this.moods, required this.symptoms});
  final List<MoodOption> moods;
  final List<SymptomOption> symptoms;
}

class Evidence {
  const Evidence({
    required this.source,
    required this.section,
    required this.excerpt,
  });
  final KnowledgeSource source;
  final Localized section;
  final Localized excerpt;
}

enum AnswerKind { answer, refusal, redFlag }

class AssistantAnswer {
  const AssistantAnswer({
    required this.id,
    required this.kind,
    required this.text,
    this.title,
    required this.confidence,
    required this.sources,
    required this.evidence,
    required this.followUps,
    required this.canEscalate,
  });
  final String id;
  final AnswerKind kind;
  final Localized text;
  final Localized? title;
  final Confidence confidence;
  final List<KnowledgeSource> sources;
  final List<Evidence> evidence;
  final List<Localized> followUps;
  final bool canEscalate;
}

class ChatMessage {
  const ChatMessage.user(this.userText, {this.attachment})
    : answer = null,
      id = null;
  const ChatMessage.assistant(this.answer, {this.id})
    : userText = null,
      attachment = null;
  final String? id;
  final Localized? userText;
  final String? attachment;
  final AssistantAnswer? answer;
  bool get isUser => answer == null;
}

class Conversation {
  const Conversation({
    required this.id,
    required this.title,
    required this.updatedMinutesAgo,
    required this.messages,
  });
  final String id;
  final Localized title;
  final int updatedMinutesAgo;
  final List<ChatMessage> messages;
}

/// Parses the shared JSON once into typed lookups.
class DemoData {
  DemoData(this.raw) {
    for (final a in raw['activeIngredients'] as List) {
      ingredients[a['id'] as String] = ActiveIngredient(
        a['id'] as String,
        Localized.fromJson(a['name']),
      );
    }
    for (final d in raw['drugs'] as List) {
      drugs[d['id'] as String] = Drug(
        id: d['id'] as String,
        name: Localized.fromJson(d['name']),
        activeIngredient: ingredients[d['activeIngredientId']]!,
        strength: d['strength'] as String,
        form: Localized.fromJson(d['form']),
        route: Localized.fromJson(d['route']),
        category: Localized.fromJson(d['category']),
        instructions: Localized.fromJson(d['instructions']),
        warnings: [
          for (final w in d['warnings'] as List) Localized.fromJson(w),
        ],
      );
    }
    for (final s in raw['knowledgeSources'] as List) {
      sources[s['id'] as String] = KnowledgeSource(
        id: s['id'] as String,
        title: Localized.fromJson(s['title']),
        type: Localized.fromJson(s['type']),
        version: s['version'] as String,
      );
    }
    for (final p in raw['patients'] as List) {
      patients[p['id'] as String] = Patient(
        id: p['id'] as String,
        name: Localized.fromJson(p['name']),
        age: p['age'] as int,
        sex: p['sex'] as String,
        risk: p['risk'] as String,
        adherence: p['adherence'] as int,
        conditions: [
          for (final c in p['conditions'] as List) Localized.fromJson(c),
        ],
        allergies: [
          for (final c in p['allergies'] as List) Localized.fromJson(c),
        ],
        adherenceSeries: [
          for (final v in p['adherenceSeries'] as List) v as int,
        ],
      );
    }
  }

  final Map<String, dynamic> raw;
  final Map<String, ActiveIngredient> ingredients = {};
  final Map<String, Drug> drugs = {};
  final Map<String, KnowledgeSource> sources = {};
  final Map<String, Patient> patients = {};

  Localized get demoLabel => Localized.fromJson(raw['meta']['label']);
  String get currentPatientId => raw['patientHome']['patientId'] as String;
  Drug drug(String id) => drugs[id]!;
  KnowledgeSource source(String id) => sources[id]!;

  AssistantAnswer answerFrom(
    Map<String, dynamic> a, {
    AnswerKind kind = AnswerKind.answer,
    String? idSuffix,
  }) => AssistantAnswer(
    id: '${a['id'] ?? 'fallback'}-${idSuffix ?? DateTime.now().microsecondsSinceEpoch}',
    kind: kind,
    text: Localized.fromJson(a['text']),
    confidence: _conf(a['confidence'] as String),
    sources: [for (final id in a['sourceIds'] as List) source(id as String)],
    evidence: [
      for (final e in a['evidence'] as List)
        Evidence(
          source: source(e['sourceId'] as String),
          section: Localized.fromJson(e['section']),
          excerpt: Localized.fromJson(e['excerpt']),
        ),
    ],
    followUps: [for (final f in a['followUps'] as List) Localized.fromJson(f)],
    canEscalate: a['escalate'] == true,
  );
}
