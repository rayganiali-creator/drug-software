import 'dart:convert';

import 'package:http/http.dart' as http;

import 'api_session.dart';

enum KnowledgeErrorKind {
  notConnected,
  unauthorized,
  notFound,
  invalid,
  rateLimited,
  server,
  network,
}

/// UI-safe failure: a kind and (for validation) the server's machine-readable code. Never carries server internals.
class KnowledgeException implements Exception {
  const KnowledgeException(this.kind, [this.code]);
  final KnowledgeErrorKind kind;
  final String? code;
  @override
  String toString() => 'KnowledgeException($kind)';
}

class Loc {
  const Loc(this.en, this.fa);
  final String? en;
  final String? fa;
  static Loc from(Object? j) => j is Map<String, dynamic>
      ? Loc(j['en'] as String?, j['fa'] as String?)
      : const Loc(null, null);

  /// Text for [lang], falling back to the other language so a named record never shows an empty label.
  String of(String lang) => (lang == 'fa' ? (fa ?? en) : (en ?? fa)) ?? '';
}

class MedicationSummary {
  const MedicationSummary({
    required this.id,
    required this.name,
    required this.brand,
    required this.form,
    required this.strength,
    required this.ingredients,
    required this.lifecycle,
    required this.validation,
    required this.isDemo,
  });
  final String id;
  final Loc name;
  final Loc? brand;
  final Loc form;
  final String strength;
  final List<Loc> ingredients;
  final String lifecycle;
  final String validation;
  final bool isDemo;

  factory MedicationSummary.fromJson(Map<String, dynamic> j) =>
      MedicationSummary(
        id: j['id'] as String,
        name: Loc.from(j['name']),
        brand: j['brandName'] == null ? null : Loc.from(j['brandName']),
        form: Loc.from(j['dosageForm']),
        strength: (j['strengthSummary'] as String?) ?? '',
        ingredients: [
          for (final i in (j['ingredients'] as List? ?? [])) Loc.from(i),
        ],
        lifecycle: (j['lifecycle'] as String?) ?? 'Active',
        validation: (j['validation'] as String?) ?? 'Unverified',
        isDemo: j['isDemo'] == true,
      );
}

class MedicationPage {
  const MedicationPage(this.items, this.total);
  final List<MedicationSummary> items;
  final int total;
}

class MedStatement {
  const MedStatement(this.kind, this.text, this.validation, this.sourceId);
  final String kind;
  final Loc text;
  final String validation;
  final String sourceId;
}

class MedInteraction {
  const MedInteraction(
    this.other,
    this.severity,
    this.mechanism,
    this.management,
    this.validation,
  );
  final Loc other;
  final String severity;
  final Loc mechanism;
  final Loc management;
  final String validation;
}

class MedSource {
  const MedSource(
    this.id,
    this.name,
    this.publisher,
    this.version,
    this.licence,
  );
  final String id;
  final String name;
  final String publisher;
  final String version;
  final String licence;
}

class MedIngredient {
  const MedIngredient(this.name, this.strength);
  final Loc name;
  final String? strength;
}

class MedicationDetail {
  const MedicationDetail({
    required this.id,
    required this.version,
    required this.name,
    required this.brand,
    required this.manufacturer,
    required this.form,
    required this.routes,
    required this.strength,
    required this.ingredients,
    required this.statements,
    required this.missingKinds,
    required this.interactions,
    required this.sources,
    required this.identifiers,
    required this.lifecycle,
    required this.validation,
    required this.isDemo,
    required this.updatedAt,
  });
  final String id;
  final int version;
  final Loc name;
  final Loc? brand;
  final Loc? manufacturer;
  final Loc form;
  final List<Loc> routes;
  final String strength;
  final List<MedIngredient> ingredients;
  final List<MedStatement> statements;
  final List<String> missingKinds;
  final List<MedInteraction> interactions;
  final List<MedSource> sources;
  final List<String> identifiers;
  final String lifecycle;
  final String validation;
  final bool isDemo;
  final DateTime? updatedAt;

  factory MedicationDetail.fromJson(Map<String, dynamic> j) {
    List<Map<String, dynamic>> list(String k) => [
      for (final x in (j[k] as List? ?? [])) x as Map<String, dynamic>,
    ];
    return MedicationDetail(
      id: j['id'] as String,
      version: (j['version'] as num?)?.toInt() ?? 1,
      name: Loc.from(j['name']),
      brand: j['brand'] == null
          ? null
          : Loc.from((j['brand'] as Map<String, dynamic>)['name']),
      manufacturer: j['manufacturer'] == null
          ? null
          : Loc.from((j['manufacturer'] as Map<String, dynamic>)['name']),
      form: Loc.from(j['dosageForm']),
      routes: [for (final r in (j['routes'] as List? ?? [])) Loc.from(r)],
      strength: (j['strengthSummary'] as String?) ?? '',
      ingredients: [
        for (final i in list('ingredients'))
          MedIngredient(
            Loc.from(i['name']),
            i['strengthValue'] == null
                ? null
                : '${i['strengthValue']} ${i['strengthUnit'] ?? ''}${i['perUnit'] == null ? '' : ' / ${i['perUnit']}'}',
          ),
      ],
      statements: [
        for (final s in list('statements'))
          MedStatement(
            s['kind'] as String,
            Loc.from(s['text']),
            (s['validation'] as String?) ?? 'Unverified',
            (s['sourceId'] as String?) ?? '',
          ),
      ],
      missingKinds: [
        for (final k in (j['missingKinds'] as List? ?? [])) k as String,
      ],
      interactions: [
        for (final i in list('interactions'))
          MedInteraction(
            Loc.from(i['otherIngredientName']),
            (i['severity'] as String?) ?? 'Unknown',
            Loc.from(i['mechanism']),
            Loc.from(i['management']),
            (i['validation'] as String?) ?? 'Unverified',
          ),
      ],
      sources: [
        for (final s in list('sources'))
          MedSource(
            s['id'] as String,
            s['name'] as String,
            (s['publisher'] as String?) ?? '',
            (s['version'] as String?) ?? '',
            (s['licenseName'] as String?) ?? '',
          ),
      ],
      identifiers: [
        for (final i in list('identifiers')) '${i['scheme']}: ${i['value']}',
      ],
      lifecycle: (j['lifecycle'] as String?) ?? 'Active',
      validation: (j['validation'] as String?) ?? 'Unverified',
      isDemo: j['isDemo'] == true,
      updatedAt: DateTime.tryParse((j['updatedAt'] as String?) ?? ''),
    );
  }
}

/// Client of the medication reference endpoints. No secret: the token is inside [ApiSession].
class MedicationClient {
  MedicationClient(this._session);
  final ApiSession? _session;

  Future<MedicationPage> search(
    String q, {
    int limit = 20,
    int offset = 0,
  }) async {
    final qs = {
      if (q.trim().isNotEmpty) 'q': q.trim(),
      'limit': '$limit',
      'offset': '$offset',
    };
    final body = await _get(
      '/medications/search?${Uri(queryParameters: qs).query}',
    );
    return MedicationPage([
      for (final i in (body['items'] as List? ?? []))
        MedicationSummary.fromJson(i as Map<String, dynamic>),
    ], (body['total'] as num?)?.toInt() ?? 0);
  }

  Future<MedicationDetail> detail(String id) async => MedicationDetail.fromJson(
    await _get('/medications/${Uri.encodeComponent(id)}'),
  );

  Future<Map<String, dynamic>> _get(String path) async {
    final s = _session;
    await s?.whenReady();
    if (s == null || !s.hasToken) {
      throw const KnowledgeException(KnowledgeErrorKind.notConnected);
    }
    final http.Response res;
    try {
      res = await s.get(path);
    } catch (_) {
      throw const KnowledgeException(KnowledgeErrorKind.network);
    }
    switch (res.statusCode) {
      case 200:
        final j = jsonDecode(res.body);
        if (j is Map<String, dynamic>) return j;
        throw const KnowledgeException(KnowledgeErrorKind.server);
      case 401 || 403:
        throw const KnowledgeException(KnowledgeErrorKind.unauthorized);
      case 404:
        throw const KnowledgeException(KnowledgeErrorKind.notFound);
      case 429:
        throw const KnowledgeException(KnowledgeErrorKind.rateLimited);
      case 400:
        String? code;
        try {
          code =
              (jsonDecode(res.body) as Map<String, dynamic>)['code'] as String?;
        } catch (_) {}
        throw KnowledgeException(KnowledgeErrorKind.invalid, code);
      default:
        throw const KnowledgeException(KnowledgeErrorKind.server);
    }
  }
}
