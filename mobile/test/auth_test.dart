import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:medsmarter_mobile/auth/access.g.dart';
import 'package:medsmarter_mobile/auth/auth_controller.dart';

import 'helpers.dart';

String location(WidgetTester tester) {
  final router = GoRouter.of(tester.element(find.byType(Scaffold).first));
  return router.routerDelegate.currentConfiguration.uri.path;
}

void main() {
  group('AuthController (demo accounts)', () {
    Future<AuthController> make(
      WidgetTester tester, {
      String? account,
      DateTime Function()? now,
    }) async {
      final c = await tester.runAsync(
        () => AuthController.create(
          clock: now ?? fixedNow,
          initialAccountId: account,
        ),
      );
      return c!;
    }

    testWidgets(
      'signs in with a catalog account and derives roles and permissions',
      (tester) async {
        final auth = await make(tester);
        expect(auth.isAuthenticated, isFalse);
        expect(auth.signIn('demo-patient'), isNull);
        expect(auth.user!.roles, ['Patient']);
        expect(auth.can(Permissions.patientCheckinsCreate), isTrue);
        expect(auth.can(Permissions.roleManage), isFalse);
        expect(auth.canUsePatientApp, isTrue);
      },
    );

    testWidgets('professional roles cannot use the patient app', (
      tester,
    ) async {
      for (final id in [
        'demo-physician',
        'demo-pharmacist',
        'demo-system-admin',
        'demo-industry',
      ]) {
        final auth = await make(tester);
        expect(auth.signIn(id), isNull, reason: id);
        expect(auth.canUsePatientApp, isFalse, reason: id);
      }
    });

    testWidgets('unknown, non-login and disabled accounts are refused', (
      tester,
    ) async {
      final auth = await make(tester);
      expect(auth.signIn('nobody'), SignInError.invalid);
      expect(auth.signIn('subject-pt-2'), SignInError.invalid);
      expect(auth.signIn('demo-disabled'), SignInError.disabled);
      expect(auth.reason, SignedOutReason.disabled);
      expect(auth.isAuthenticated, isFalse);
    });

    testWidgets('the session ends after 8 hours', (tester) async {
      var t = fixedNow();
      final auth = await make(tester, now: () => t);
      auth.signIn('demo-patient');
      expect(auth.isAuthenticated, isTrue);
      t = t.add(const Duration(hours: 8, seconds: 1));
      expect(auth.isAuthenticated, isFalse);
    });

    testWidgets('sign-out and simulated expiry record why', (tester) async {
      final auth = await make(tester, account: 'demo-patient');
      auth.signOut();
      expect(auth.reason, SignedOutReason.signedOut);
      auth.signIn('demo-patient');
      auth.simulateExpiry();
      expect(auth.reason, SignedOutReason.expired);
    });

    testWidgets('a patient sees only their own consents and can revoke them', (
      tester,
    ) async {
      final sara = await make(tester, account: 'demo-patient');
      expect(sara.consents, isNotEmpty);
      final first = sara.consents.first;
      expect(sara.consentStatus(first), ConsentStatus.active);
      sara.revokeConsent(first.id);
      expect(sara.consentStatus(first), ConsentStatus.revoked);
      final ali = await make(tester, account: 'demo-patient-2');
      expect(
        ali.consents.map((c) => c.id),
        isNot(contains(first.id)),
        reason: 'consents are per subject',
      );
      expect(
        ali.consents.any((c) => ali.consentStatus(c) == ConsentStatus.expired),
        isTrue,
      );
    });

    testWidgets('the demo catalog offers accounts without any credentials', (
      tester,
    ) async {
      final auth = await make(tester);
      expect(auth.demoAccounts.length, greaterThan(8));
      expect(auth.demoAccounts.where((a) => a.disabled), isNotEmpty);
    });
  });

  group('Guarded navigation', () {
    testWidgets('anonymous users are sent to the login screen', (tester) async {
      await pumpApp(tester, account: null);
      expect(find.text('Sign in'), findsWidgets);
      expect(find.textContaining('DEMO ENVIRONMENT'), findsOneWidget);
      expect(location(tester), '/login');
      expect(find.text('Good morning, Sara'), findsNothing);
    });

    testWidgets('deep links are also guarded', (tester) async {
      await pumpApp(
        tester,
        account: null,
        location: '/medications/drug-demopril',
      );
      expect(location(tester), '/login');
    });

    testWidgets('choosing the patient account opens the patient home', (
      tester,
    ) async {
      await pumpApp(tester, account: null, size: const Size(390, 2400));
      await tester.tap(
        find.bySemanticsLabel(RegExp('Sign in as DEMO Patient — Sara')).first,
      );
      await tester.pumpAndSettle();
      expect(location(tester), '/home');
      expect(find.text('Good morning, Sara'), findsOneWidget);
    });

    testWidgets('a physician account gets the neutral no-access screen', (
      tester,
    ) async {
      await pumpApp(tester, account: 'demo-physician');
      expect(location(tester), '/no-access');
      expect(find.text('You do not have access to this page'), findsOneWidget);
      expect(find.text('Good morning, Sara'), findsNothing);
    });

    testWidgets('signing out returns to login and shows the message', (
      tester,
    ) async {
      final c = await pumpApp(tester, size: const Size(390, 1600));
      c.auth.signOut();
      await tester.pumpAndSettle();
      expect(location(tester), '/login');
      expect(find.text('You have been signed out.'), findsOneWidget);
    });

    testWidgets('an expired session shows the session-ended message', (
      tester,
    ) async {
      final c = await pumpApp(tester);
      c.auth.simulateExpiry();
      await tester.pumpAndSettle();
      expect(find.text('Your session has ended'), findsOneWidget);
    });

    testWidgets('the disabled account explains why sign-in fails', (
      tester,
    ) async {
      final c = await pumpApp(
        tester,
        account: null,
        size: const Size(390, 3600),
      );
      c.auth.signIn('demo-disabled');
      await tester.pumpAndSettle();
      expect(find.text('This account is disabled'), findsWidgets);
      expect(location(tester), '/login');
    });

    testWidgets('login is RTL in Persian', (tester) async {
      await pumpApp(tester, account: null, locale: const Locale('fa'));
      expect(
        Directionality.of(tester.element(find.byType(Scaffold).first)),
        TextDirection.rtl,
      );
      expect(find.text('ورود'), findsWidgets);
    });

    testWidgets('login does not overflow at 200% text', (tester) async {
      await pumpApp(tester, account: null, textScale: 2.0, settle: false);
      expect(tester.takeException(), isNull);
    });
  });

  group('Profile account card', () {
    testWidgets('shows the roles, consents and revokes with immediate effect', (
      tester,
    ) async {
      await pumpApp(tester, location: '/profile', size: const Size(390, 3000));
      expect(find.text('Account & security'), findsWidgets);
      expect(find.text('Data sharing consents'), findsOneWidget);
      expect(find.text('Revoke'), findsWidgets);
      await tester.tap(find.text('Revoke').first);
      await tester.pumpAndSettle();
      expect(find.text('Revoked'), findsWidgets);
    });

    testWidgets('sign out button ends the session', (tester) async {
      await pumpApp(tester, location: '/profile', size: const Size(390, 3000));
      await tester.tap(find.text('Sign out'));
      await tester.pumpAndSettle();
      expect(location(tester), '/login');
    });
  });
}
