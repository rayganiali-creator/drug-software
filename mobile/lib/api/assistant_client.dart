import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'api_session.dart';
import 'records_client.dart' show Json;

enum AssistantErrorKind {
  notConnected,
  network,
  timeout,
  unauthorized,
  rateLimited,
  invalid,
  server,
}

/// UI-safe failure: a kind only. Never carries server internals.
class AssistantException implements Exception {
  const AssistantException(this.kind);
  final AssistantErrorKind kind;
  @override
  String toString() => 'AssistantException($kind)';
}


/// The structured answer of `POST /ai/medication-assistant` (contract `ai-answer-1`). Generated text and evidence are separate; there is no
/// numeric confidence. Machine codes (`status`, `reason`, `limitations`...) are translated by the screen.
class GroundedAnswer {
  GroundedAnswer(this.json);
  final Json json;

  String get text => (json['text'] as String?) ?? '';
  String get status => (json['status'] as String?) ?? 'Unavailable';
  String? get reason => json['reason'] as String?;
  bool get isMock => json['isMock'] == true;
  String get notice => (json['notice'] as String?) ?? '';
  String get nextStep => (json['nextStep'] as String?) ?? 'None';
  String get quality => (json['evidenceQuality'] as String?) ?? 'None';
  bool get patientContextUsed => json['patientContextUsed'] == true;
  String? get patientContextNote => json['patientContextNote'] as String?;
  Json? get generation => json['generation'] as Json?;
  List<String> get limitations => _strings(json['limitations']);
  List<String> get missing => _strings(json['missingInformation']);
  Json? get _evidence => json['evidence'] as Json?;
  List<Json> get items => [
    for (final e in (_evidence?['items'] as List? ?? [])) e as Json,
  ];
  List<Json> get conflicts => [
    for (final e in (_evidence?['conflicts'] as List? ?? [])) e as Json,
  ];

  static List<String> _strings(Object? v) => [
    for (final e in (v as List? ?? [])) '$e',
  ];
}

class AssistantClient {
  AssistantClient(this._session, {this.timeout = const Duration(seconds: 30)});
  final ApiSession? _session;
  final Duration timeout;

  Future<GroundedAnswer> ask({
    required String question,
    required String locale,
    List<String>? medicationIds,
    bool includePatientContext = false,
  }) async {
    final s = _session;
    await s?.whenReady();
    if (s == null || !s.hasToken) {
      throw const AssistantException(AssistantErrorKind.notConnected);
    }
    final http.Response res;
    try {
      res = await s.send('POST', '/ai/medication-assistant', {
        'question': question,
        'locale': locale,
        'medicationIds': medicationIds,
        'includePatientContext': includePatientContext,
      }, timeout);
    } on TimeoutException {
      throw const AssistantException(AssistantErrorKind.timeout);
    } catch (_) {
      throw const AssistantException(AssistantErrorKind.network);
    }
    // 200 and 503 both carry the structured answer (a provider problem still shows the evidence).
    if (res.statusCode == 200 || res.statusCode == 503) {
      try {
        final j = jsonDecode(utf8.decode(res.bodyBytes));
        if (j is Json) return GroundedAnswer(j);
      } catch (_) {}
      throw const AssistantException(AssistantErrorKind.server);
    }
    throw AssistantException(switch (res.statusCode) {
      401 || 403 => AssistantErrorKind.unauthorized,
      429 => AssistantErrorKind.rateLimited,
      400 => AssistantErrorKind.invalid,
      _ => AssistantErrorKind.server,
    });
  }
}
