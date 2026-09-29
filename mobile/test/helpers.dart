import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:medsmarter_mobile/app.dart';
import 'package:medsmarter_mobile/core/app_controller.dart';
import 'package:medsmarter_mobile/core/services.dart';

/// Fixed clock: Tuesday 29 Sep 2026, 10:00 (Jalali 7 Mehr 1405).
DateTime fixedNow() => DateTime(2026, 9, 29, 10, 0);

Future<AppController> makeController(
  WidgetTester tester, {
  Locale locale = const Locale('en'),
  DateTime Function()? now,
  String? account = 'demo-patient',
}) async {
  final clock = now ?? fixedNow;
  final base = await tester.runAsync(
    () => AppController.create(
      bundle: rootBundle,
      clock: clock,
      initialAccountId: account,
      servicesBuilder: (data) =>
          MockServices(data, latency: Duration.zero, now: clock),
    ),
  );
  final c = base!;
  c.setLocale(locale);
  return c;
}

/// Pumps the whole app at a given logical size.
Future<AppController> pumpApp(
  WidgetTester tester, {
  Locale locale = const Locale('en'),
  Size size = const Size(390, 844),
  String location = '/home',
  DateTime Function()? now,
  double textScale = 1,
  bool settle = true,
  String? account = 'demo-patient',
}) async {
  // Haptics go through the platform channel; there is no host in unit tests.
  tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
    SystemChannels.platform,
    (call) async => null,
  );
  addTearDown(
    () => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
      SystemChannels.platform,
      null,
    ),
  );
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  tester.platformDispatcher.textScaleFactorTestValue = textScale;
  addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
  final c = await makeController(
    tester,
    locale: locale,
    now: now,
    account: account,
  );
  await tester.pumpWidget(
    MedSmarterApp(controller: c, initialLocation: location),
  );
  // Screens with endless animations (skeleton shimmer) cannot "settle"; pump a fixed time instead.
  settle
      ? await tester.pumpAndSettle()
      : await tester.pump(const Duration(seconds: 1));
  return c;
}
