import 'dart:convert';
import 'dart:math';

import 'package:http/http.dart' as http;

import 'api_session.dart';

enum RecordsErrorKind {
  notConnected,
  network,
  unauthorized,
  notFound,
  conflict,
  invalid,
  rateLimited,
  notAvailable,
  server,
}

/// UI-safe failure: a kind plus the server's machine code(s), which the UI shows only through a translation.
class RecordsException implements Exception {
  const RecordsException(this.kind, [this.code, this.errors = const []]);
  final RecordsErrorKind kind;
  final String? code;
  final List<String> errors;
  @override
  String toString() => 'RecordsException($kind)';
}

typedef Json = Map<String, dynamic>;

/// Client of the patient-layer endpoints (profile, medicines, batches, manufacturer reports, care and consents, messages).
/// It holds no secret and no business rule: the server decides who may see or change what. Without a token every call
/// fails with `notConnected` instead of pretending to work.
class RecordsClient {
  RecordsClient(this._session);
  final ApiSession? _session;

  String? get selfId => _session?.userId;

  Future<Object?> _call(String method, String path, [Object? body]) async {
    final s = _session;
    await s?.whenReady();
    if (s == null || !s.hasToken) {
      throw const RecordsException(RecordsErrorKind.notConnected);
    }
    final http.Response res;
    try {
      res = await s.send(method, path, body);
    } catch (_) {
      throw const RecordsException(RecordsErrorKind.network);
    }
    if (res.statusCode >= 200 && res.statusCode < 300) {
      return res.statusCode == 204 || res.body.isEmpty
          ? null
          : jsonDecode(utf8.decode(res.bodyBytes));
    }
    String? code;
    var errors = <String>[];
    try {
      final j = jsonDecode(res.body);
      if (j is Json) {
        code = j['code'] as String?;
        errors = [for (final e in (j['errors'] as List? ?? [])) '$e'];
      }
    } catch (_) {}
    throw switch (res.statusCode) {
      401 || 403 => const RecordsException(RecordsErrorKind.unauthorized),
      404 => RecordsException(RecordsErrorKind.notFound, code),
      409 => RecordsException(RecordsErrorKind.conflict, code, errors),
      400 => RecordsException(RecordsErrorKind.invalid, code, errors),
      429 => const RecordsException(RecordsErrorKind.rateLimited),
      501 => RecordsException(RecordsErrorKind.notAvailable, code),
      _ => const RecordsException(RecordsErrorKind.server),
    };
  }

  String _p(String rest) {
    // Without a token `_call` reports "not connected" before this path is ever used.
    return '/patients/${Uri.encodeComponent(selfId ?? '')}/$rest';
  }

  Future<Json> _obj(String m, String path, [Object? body]) async =>
      (await _call(m, path, body)) as Json;
  Future<List<Json>> _list(String path) async => [
    for (final e in ((await _call('GET', path)) as List? ?? [])) e as Json,
  ];

  // ---- record
  Future<Json> ensureOwn() => _obj('POST', '/patients/me');
  Future<Json> profile() => _obj('GET', _p('profile'));
  Future<List<Json>> freshness() => _list(_p('freshness'));
  Future<List<Json>> conditions() => _list(_p('conditions'));
  Future<Json> addCondition(String name, String status) =>
      _obj('POST', _p('conditions'), {
        'name': name,
        'onsetDate': null,
        'status': status,
        'note': null,
      });
  Future<void> removeCondition(String id) =>
      _call('DELETE', _p('conditions/$id'));
  Future<List<Json>> allergies() => _list(_p('allergies'));
  Future<Json> addAllergy(String substance, String severity) =>
      _obj('POST', _p('allergies'), {
        'kind': 'Other',
        'medicationId': null,
        'substance': substance,
        'severity': severity,
        'reaction': null,
      });
  Future<void> removeAllergy(String id) => _call('DELETE', _p('allergies/$id'));
  Future<List<Json>> symptoms() => _list(_p('symptoms?take=50'));
  Future<Json> addSymptom(String text, String severity) =>
      _obj('POST', _p('symptoms'), {
        'text': text,
        'severity': severity,
        'onsetAt': DateTime.now().toUtc().toIso8601String(),
        'resolvedAt': null,
        'patientMedicationId': null,
        'note': null,
      });

  // ---- medicines taken
  Future<List<Json>> medications() =>
      _list(_p('medications?includeStopped=false'));
  Future<Json> addMedication({
    required String name,
    String? dose,
    required String frequency,
    int? frequencyValue,
  }) => _obj('POST', _p('medications'), {
    'medicationId': null,
    'unregisteredName': name,
    'doseAmount': null,
    'doseUnit': null,
    'doseText': dose,
    'frequency': frequency,
    'frequencyValue': frequencyValue,
    'route': null,
    'startDate': _day(DateTime.now()),
    'endDate': null,
    'source': 'SelfReported',
    'prescriberNote': null,
  });
  Future<Json> stopMedication(String id, int version) =>
      _obj('POST', _p('medications/$id/stop'), {
        'reason': null,
        'endDate': _day(DateTime.now()),
        'expectedVersion': version,
      });
  Future<List<Json>> doses(DateTime day) => _list(_p('doses?date=${_day(day)}'));
  Future<void> logIntake(Json slot, String status) async {
    await _call('POST', _p('intake'), {
      'patientMedicationId': slot['patientMedicationId'],
      'scheduleEntryId': slot['scheduleEntryId'],
      'scheduledFor': slot['scheduledFor'],
      'status': status,
      'takenAt': null,
      'note': null,
    });
  }

  // ---- batches and manufacturer reports
  Future<List<Json>> products() => _list(_p('products'));
  Future<Json> addProduct({
    required String name,
    required String batch,
    required String expiry,
  }) => _obj('POST', _p('products'), {
    'patientMedicationId': null,
    'medicationId': null,
    'productName': name,
    'batchNumber': batch,
    'manufactureDate': null,
    'expiryDate': expiry,
    'gtin': null,
    'pharmacyNote': null,
    'receivedOn': _day(DateTime.now()),
    'method': 'Manual',
  });
  Future<List<Json>> reports() => _list(_p('manufacturer-reports'));
  Future<Json> createReport({
    required String productRecordId,
    required String issueType,
    required String severity,
    String? description,
  }) => _obj('POST', _p('manufacturer-reports'), {
    'productRecordId': productRecordId,
    'issueType': issueType,
    'severity': severity,
    'occurredOn': _day(DateTime.now()),
    'durationOfUseDays': null,
    'description': description,
    'includeConcomitantMedications': false,
    'clientRequestId': _uuid(),
  });
  Future<Json> submitReport(String id, int version) =>
      _obj('POST', _p('manufacturer-reports/$id/submit'), {
        'expectedVersion': version,
      });
  Future<Json> cancelReport(String id) =>
      _obj('POST', _p('manufacturer-reports/$id/cancel'));

  // ---- care and consents
  Future<List<Json>> care() => _list('/care-relationships');
  Future<void> careAction(String id, String action) => _call(
    'POST',
    '/care-relationships/$id/$action',
    action == 'end' ? {'reason': null} : null,
  );
  Future<List<Json>> consents() => _list('/consents');
  Future<void> revokeConsent(String id) => _call('DELETE', '/consents/$id');
  Future<void> grantConsent({
    required String purpose,
    required List<String> scope,
    required int days,
  }) => _call('POST', '/consents', {
    'granteeUserId': null,
    'purpose': purpose,
    'scope': scope,
    'expiresAt': DateTime.now()
        .toUtc()
        .add(Duration(days: days))
        .toIso8601String(),
    'version': 'v1',
  });

  // ---- messages
  Future<List<Json>> messages() => _list('/guidance/messages');
  Future<void> setMessageStatus(String id, String status) =>
      _call('POST', '/guidance/messages/$id/status', {'status': status});

  static String _uuid() {
    final r = Random.secure();
    String h(int n) => List.generate(n, (_) => r.nextInt(16).toRadixString(16)).join();
    return '${h(8)}-${h(4)}-4${h(3)}-${(8 + r.nextInt(4)).toRadixString(16)}${h(3)}-${h(12)}';
  }

  static String _day(DateTime d) =>
      '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';
}
