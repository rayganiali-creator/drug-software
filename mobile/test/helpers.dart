import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:medsmarter_mobile/app.dart';
import 'package:medsmarter_mobile/core/app_controller.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:medsmarter_mobile/core/services.dart';

/// Fixed clock: Tuesday 29 Sep 2026, 10:00 (Jalali 7 Mehr 1405).
DateTime fixedNow() => DateTime(2026, 9, 29, 10, 0);

Future<AppController> makeController(
  WidgetTester tester, {
  Locale locale = const Locale('en'),
  DateTime Function()? now,
  String? account = 'demo-patient',
  http.Client? httpClient,
}) async {
  final clock = now ?? fixedNow;
  final base = await tester.runAsync(
    () => AppController.create(
      bundle: rootBundle,
      clock: clock,
      initialAccountId: account,
      // Tests never reach a network: by default every API call is refused as if the backend were off.
      httpClient:
          httpClient ??
          MockClient(
            (_) async => throw http.ClientException('backend off (test)'),
          ),
      servicesBuilder: (data) =>
          MockServices(data, latency: Duration.zero, now: clock),
    ),
  );
  final c = base!;
  c.setLocale(locale);
  // The background API login started at creation runs in the real-async zone: let it finish before the UI is pumped.
  await tester.runAsync(() => c.api.whenReady());
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
  http.Client? httpClient,
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
    httpClient: httpClient,
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
