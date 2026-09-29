// ignore_for_file: prefer_initializing_formals
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'formatters.dart';
import 'l10n.dart';
import 'models.dart';
import 'services.dart';

/// App-wide state: language, theme mode, strings and services. Persisted preferences are
/// best-effort (a storage failure never blocks the UI).
class AppController extends ChangeNotifier {
  AppController({
    required this.strings,
    required this.data,
    required this.services,
    Locale? locale,
    ThemeMode? themeMode,
    SharedPreferences? prefs,
    DateTime Function()? clock,
  }) : clock = clock ?? DateTime.now,
       _locale = locale ?? const Locale('fa'),
       _themeMode = themeMode ?? ThemeMode.system,
       _prefs = prefs;

  final AppStrings strings;
  final DemoData data;
  final AppServices services;
  final SharedPreferences? _prefs;

  /// Injectable clock so "next dose" and greetings are testable.
  final DateTime Function() clock;
  Locale _locale;
  ThemeMode _themeMode;

  Locale get locale => _locale;
  ThemeMode get themeMode => _themeMode;
  Formatters get fmt => Formatters(_locale);
  TextDirection get textDirection =>
      _locale.languageCode == 'fa' ? TextDirection.rtl : TextDirection.ltr;

  String t(String key, [Map<String, Object>? params]) =>
      strings.t(_locale, key, params);
  String loc(Localized v) => v.of(_locale);

  void setLocale(Locale l) {
    _locale = l;
    _prefs?.setString('locale', l.languageCode);
    notifyListeners();
  }

  void setThemeMode(ThemeMode m) {
    _themeMode = m;
    _prefs?.setString('theme', m.name);
    notifyListeners();
  }

  static Future<AppController> create({
    AssetBundle? bundle,
    SharedPreferences? prefs,
    AppServices Function(DemoData)? servicesBuilder,
    DateTime Function()? clock,
  }) async {
    final b = bundle ?? rootBundle;
    final strings = await AppStrings.load(b);
    final raw = await b.loadString('assets/shared/demo-data.json');
    final data = DemoData(jsonDecode(raw) as Map<String, dynamic>);
    final lang = prefs?.getString('locale');
    final theme = prefs?.getString('theme');
    return AppController(
      strings: strings,
      data: data,
      services: servicesBuilder?.call(data) ?? MockServices(data, now: clock),
      clock: clock,
      locale: lang == 'en' ? const Locale('en') : const Locale('fa'),
      themeMode:
          ThemeMode.values.where((m) => m.name == theme).firstOrNull ??
          ThemeMode.system,
      prefs: prefs,
    );
  }
}
