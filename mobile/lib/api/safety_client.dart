import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'api_session.dart';
import 'records_client.dart' show Json;

enum SafetyErrorKind {
  notConnected,
  network,
  unauthorized,
  notFound,
  invalid,
  rateLimited,
  unavailable,
  server,
}

/// UI-safe failure: a kind only. Blocked-by-policy (`unauthorized`) and technical failure (`unavailable`/`server`) are different states.
class SafetyException implements Exception {
  const SafetyException(this.kind);
  final SafetyErrorKind kind;
  @override
  String toString() => 'SafetyException($kind)';
}

/// A stored or fresh assessment. Every status, severity and "actionable" flag comes from the server's deterministic engine; nothing is computed here.
class Assessment {
  Assessment(this.json);
  final Json json;

  Json get result => (json['result'] as Json?) ?? const {};
  String get status => (result['status'] as String?) ?? 'Failed';
  bool get complete => result['complete'] == true;
  bool get containsDemonstration => result['containsDemonstration'] == true;
  String get notice => (json['notice'] as String?) ?? '';
  String get guidanceState =>
      (json['guidanceState'] as String?) ?? 'NotApplicable';
  bool get outdated => json['outdated'] == true;
  List<String> get outdatedReasons => _strings(json['outdatedReasons']);
  int get noLongerMatching =>
      (json['openGuidanceNoLongerMatching'] as List? ?? const []).length;
  List<Json> get findings => _maps(result['findings']);
  List<Json> get evaluations => _maps(result['evaluations']);
  List<Json> get inputs => _maps(result['inputs']);
  Json get coverage => (result['coverage'] as Json?) ?? const {};
  List<String> get limitations => _strings(result['limitations']);
  String get engineVersion => (result['engineVersion'] as String?) ?? '';
  String get ruleSetVersion => (result['ruleSetVersion'] as String?) ?? '';
  DateTime? get evaluatedAt => DateTime.tryParse('${result['evaluatedAt']}');

  static List<String> _strings(Object? v) => [
    for (final e in (v as List? ?? const [])) '$e',
  ];
  static List<Json> _maps(Object? v) => [
    for (final e in (v as List? ?? const [])) e as Json,
  ];
}

/// Client of the clinical safety endpoints. It sends the route patient id only; the server decides what ownership, care relationship and consent allow.
class SafetyClient {
  SafetyClient(this._session, {this.timeout = const Duration(seconds: 30)});
  final ApiSession? _session;
  final Duration timeout;

  String? get selfId => _session?.userId;

  Future<Json> _call(String method, String path) async {
    final s = _session;
    await s?.whenReady();
    if (s == null || !s.hasToken) {
      throw const SafetyException(SafetyErrorKind.notConnected);
    }
    final http.Response res;
    try {
      res = await s.send(method, path, null, timeout);
    } on TimeoutException {
      throw const SafetyException(SafetyErrorKind.network);
    } catch (_) {
      throw const SafetyException(SafetyErrorKind.network);
    }
    if (res.statusCode >= 200 && res.statusCode < 300) {
      try {
        final j = jsonDecode(utf8.decode(res.bodyBytes));
        if (j is Json) return j;
      } catch (_) {}
      throw const SafetyException(SafetyErrorKind.server);
    }
    throw SafetyException(switch (res.statusCode) {
      401 || 403 => SafetyErrorKind.unauthorized,
      404 => SafetyErrorKind.notFound,
      400 => SafetyErrorKind.invalid,
      429 => SafetyErrorKind.rateLimited,
      503 => SafetyErrorKind.unavailable,
      _ => SafetyErrorKind.server,
    });
  }

  String _p(String id, String rest) =>
      '/patients/${Uri.encodeComponent(id)}/safety/$rest';

  Future<Assessment> latest(String id) async =>
      Assessment(await _call('GET', _p(id, 'assessments/latest')));

  Future<Assessment> run(String id, String locale) async =>
      Assessment(await _call('POST', _p(id, 'assessments?locale=$locale')));
}
