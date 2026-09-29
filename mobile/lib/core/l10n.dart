import 'dart:convert';

import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';

/// A value available in both supported languages (same shape as the web `Localized`).
@immutable
class Localized {
  const Localized(this.en, this.fa);
  factory Localized.fromJson(Object? json) {
    final m = json as Map<String, dynamic>;
    return Localized(m['en'] as String, m['fa'] as String);
  }
  final String en;
  final String fa;
  String of(Locale locale) => locale.languageCode == 'fa' ? fa : en;
}

/// UI strings from the shared `design/i18n` source (identical files to the web app).
class AppStrings {
  AppStrings._(this._maps);
  final Map<String, Map<String, String>> _maps;

  static Future<AppStrings> load([AssetBundle? bundle]) async {
    final b = bundle ?? rootBundle;
    Future<Map<String, String>> read(String lang) async => (jsonDecode(
      await b.loadString('assets/shared/$lang.json'),
    ) as Map<String, dynamic>).cast<String, String>();
    return AppStrings._({'en': await read('en'), 'fa': await read('fa')});
  }

  static final RegExp _param = RegExp(r'\{(\w+)\}');

  String t(Locale locale, String key, [Map<String, Object>? params]) {
    final raw = _maps[locale.languageCode]?[key] ?? _maps['en']![key] ?? key;
    if (params == null) return raw;
    return raw.replaceAllMapped(
      _param,
      (m) => '${params[m.group(1)] ?? m.group(0)}',
    );
  }

  bool has(String key) =>
      _maps['en']!.containsKey(key) && _maps['fa']!.containsKey(key);
  Iterable<String> get keys => _maps['en']!.keys;
  Set<String> keysFor(String lang) => _maps[lang]!.keys.toSet();
}
