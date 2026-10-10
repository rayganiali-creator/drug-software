import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'helpers.dart';

Finder inBottomBar(String text) => find.descendant(
  of: find.byKey(const Key('bottom-bar')),
  matching: find.text(text),
);

Future<void> ask(WidgetTester tester, String q) async {
  await tester.enterText(find.byType(TextField), q);
  await tester.pump();
  await tester.tap(find.byTooltip('Send message'));
  await tester.pumpAndSettle();
}

void main() {
  group('Home', () {
    testWidgets('shows the demo banner, greeting and next dose', (
      tester,
    ) async {
      await pumpApp(tester);
      expect(find.textContaining('NOT FOR CLINICAL USE'), findsWidgets);
      expect(find.text('Good morning, Sara'), findsOneWidget);
      expect(find.text('Next dose'), findsWidgets);
      expect(find.textContaining('Solvita'), findsWidgets);
      expect(find.text('In 3 h 0 min'), findsOneWidget);
      expect(find.text('I took it'), findsOneWidget);
    });

    testWidgets(
      'marking the dose taken confirms with an undo action and advances',
      (tester) async {
        await pumpApp(tester);
        await tester.tap(find.text('I took it'));
        await tester.pumpAndSettle();
        expect(find.text('Solvita recorded as taken'), findsOneWidget);
        expect(find.text('Undo'), findsOneWidget);
        expect(
          find.text('In 10 h 0 min'),
          findsOneWidget,
        ); // now the 20:00 dose
        await tester.tap(find.text('Undo'));
        await tester.pumpAndSettle();
        expect(find.text('In 3 h 0 min'), findsOneWidget);
      },
    );

    testWidgets('rolls over to tomorrow after the last dose of the day', (
      tester,
    ) async {
      await pumpApp(tester, now: () => DateTime(2026, 9, 29, 23, 30));
      expect(find.textContaining('Tomorrow'), findsWidgets);
    });

    testWidgets('Persian is RTL with Persian digits and Jalali date', (
      tester,
    ) async {
      await pumpApp(tester, locale: const Locale('fa'));
      final dir = Directionality.of(
        tester.element(find.byType(Scaffold).first),
      );
      expect(dir, TextDirection.rtl);
      expect(find.text('صبح بخیر، سارا'), findsOneWidget);
      expect(find.textContaining('۷ مهر ۱۴۰۵'), findsOneWidget);
      expect(find.text('۳ ساعت و ۰ دقیقه دیگر'), findsOneWidget);
    });
  });

  group('Navigation', () {
    testWidgets('bottom bar switches between the five tabs on phones', (
      tester,
    ) async {
      await pumpApp(tester);
      expect(find.byKey(const Key('bottom-bar')), findsOneWidget);
      expect(find.byKey(const Key('nav-rail')), findsNothing);
      await tester.tap(inBottomBar('Medications'));
      await tester.pumpAndSettle();
      expect(
        find.text('Everything about the medicines in your plan.'),
        findsOneWidget,
      );
      await tester.tap(inBottomBar('AI Assistant'));
      await tester.pumpAndSettle();
      expect(find.text('Ask about your medicines'), findsOneWidget);
      await tester.tap(inBottomBar('Check-in'));
      await tester.pumpAndSettle();
      expect(find.text('Three quick steps.'), findsOneWidget);
      await tester.tap(inBottomBar('Profile'));
      await tester.pumpAndSettle();
      expect(find.text('Account & security'), findsWidgets);
      await tester.tap(inBottomBar('Home'));
      await tester.pumpAndSettle();
      expect(find.text('Good morning, Sara'), findsOneWidget);
    });

    testWidgets(
      'wide screens use a navigation rail instead of the bottom bar',
      (tester) async {
        await pumpApp(tester, size: const Size(1200, 800));
        expect(find.byKey(const Key('nav-rail')), findsOneWidget);
        expect(find.byKey(const Key('bottom-bar')), findsNothing);
      },
    );

    testWidgets('navigation items are exposed to assistive technology', (
      tester,
    ) async {
      final handle = tester.ensureSemantics();
      await pumpApp(tester);
      for (final label in [
        'Home',
        'Medications',
        'AI Assistant',
        'Check-in',
        'Profile',
      ]) {
        expect(find.bySemanticsLabel(label), findsWidgets, reason: label);
      }
      handle.dispose();
    });
  });

  group('Medications', () {
    testWidgets('list shows fictional drugs with DEMO DATA markers', (
      tester,
    ) async {
      await pumpApp(
        tester,
        size: const Size(390, 2600),
        location: '/medications',
      );
      expect(find.textContaining('Demopril'), findsWidgets);
      expect(find.text('DEMO DATA'), findsWidgets);
    });

    testWidgets(
      'detail separates instructions from warnings and shows the interaction',
      (tester) async {
        await pumpApp(
          tester,
          size: const Size(390, 2600),
          location: '/medications/drug-demopril',
        );
        expect(find.text('Instructions'), findsOneWidget);
        expect(find.text('Warning (fictional)'), findsOneWidget);
        expect(
          find.text('A pharmacist is reviewing this combination'),
          findsOneWidget,
        );
        expect(find.text('Ask the assistant about this'), findsOneWidget);
      },
    );
  });

  group('AI assistant', () {
    testWidgets('answers with sources, confidence and a safety notice', (
      tester,
    ) async {
      await pumpApp(
        tester,
        size: const Size(390, 1400),
        location: '/assistant',
      );
      await tester.tap(find.text('What should I do if I miss a dose?'));
      await tester.pumpAndSettle();
      expect(
        find.textContaining('missed dose should be taken'),
        findsOneWidget,
      );
      expect(find.textContaining('Source match'), findsOneWidget);
      expect(find.textContaining('Informational only'), findsOneWidget);
      expect(
        find.textContaining('[1] Demo Guide: Missed Doses'),
        findsOneWidget,
      );
    });

    testWidgets('opens the evidence sheet', (tester) async {
      await pumpApp(
        tester,
        size: const Size(390, 1400),
        location: '/assistant',
      );
      await tester.tap(find.text('What should I do if I miss a dose?'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('View evidence'));
      await tester.pumpAndSettle();
      expect(find.text('Evidence'), findsOneWidget);
      expect(find.textContaining('Section 2: Missed doses'), findsOneWidget);
    });

    testWidgets('red flags show fixed emergency guidance, not an AI answer', (
      tester,
    ) async {
      await pumpApp(
        tester,
        size: const Size(390, 1400),
        location: '/assistant',
      );
      await ask(tester, 'I have chest pain');
      expect(find.text('This may be urgent'), findsOneWidget);
      expect(
        find.textContaining('fixed text, not generated by AI'),
        findsOneWidget,
      );
      expect(find.textContaining('Source match'), findsNothing);
    });

    testWidgets('unknown questions are refused instead of guessed', (
      tester,
    ) async {
      await pumpApp(
        tester,
        size: const Size(390, 1400),
        location: '/assistant',
      );
      await ask(tester, 'Explain quantum tunnelling');
      expect(find.textContaining('will not guess'), findsOneWidget);
      expect(find.text('Send to pharmacist'), findsOneWidget);
    });

    testWidgets('human escalation asks for confirmation and confirms', (
      tester,
    ) async {
      await pumpApp(
        tester,
        size: const Size(390, 1400),
        location: '/assistant',
      );
      await ask(tester, 'Can Demopril and Nocturin be taken together?');
      await tester.ensureVisible(find.text('Send to pharmacist')); // the prototype banner above the chat takes some height
      await tester.pumpAndSettle();
      await tester.tap(find.text('Send to pharmacist'));
      await tester.pumpAndSettle();
      expect(find.text('Send this question to a pharmacist?'), findsOneWidget);
      expect(find.text('What will be sent'), findsOneWidget);
      await tester.tap(find.text('Send').last);
      await tester.pumpAndSettle();
      expect(find.text('Sent to your pharmacist (demo).'), findsOneWidget);
      expect(find.text('Sent to pharmacist'), findsOneWidget);
    });

    testWidgets('reduced motion shows the full answer without waiting', (
      tester,
    ) async {
      tester.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(
        tester.platformDispatcher.clearAccessibilityFeaturesTestValue,
      );
      await pumpApp(
        tester,
        size: const Size(390, 1400),
        location: '/assistant',
      );
      await tester.tap(find.text('What should I do if I miss a dose?'));
      await tester.pump();
      await tester.pump();
      await tester.pump();
      expect(find.textContaining('Source match'), findsOneWidget);
    });

    testWidgets(
      'voice input is a prototype: no microphone, inserts sample text',
      (tester) async {
        await pumpApp(
          tester,
          size: const Size(390, 1400),
          location: '/assistant',
        );
        await tester.tap(find.byTooltip('Voice input'));
        await tester.pump();
        expect(
          find.textContaining('the microphone is not used'),
          findsOneWidget,
        );
        await tester.pump(const Duration(milliseconds: 1700));
        expect(find.text('What should I do if I miss a dose?'), findsWidgets);
      },
    );
  });

  group('Check-in', () {
    testWidgets('urgent symptoms show a non-diagnostic escalation message', (
      tester,
    ) async {
      await pumpApp(tester, size: const Size(390, 1400), location: '/checkin');
      expect(find.text('Next').first, findsOneWidget);
      await tester.tap(find.text('Good'));
      await tester.pump();
      await tester.tap(find.text('Next'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Chest pain'));
      await tester.pump();
      expect(find.textContaining('cannot assess emergencies'), findsOneWidget);
    });

    testWidgets('completes all steps and shows the done state', (tester) async {
      await pumpApp(tester, size: const Size(390, 1400), location: '/checkin');
      await tester.tap(find.text('Great'));
      await tester.pump();
      await tester.tap(find.text('Next'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Headache'));
      await tester.tap(find.text('Next'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Submit check-in'));
      await tester.pumpAndSettle();
      expect(find.text('Check-in complete'), findsOneWidget);
    });
  });

  group('Profile', () {
    testWidgets('language switch flips direction and text', (tester) async {
      await pumpApp(
        tester,
        locale: const Locale('fa'),
        size: const Size(390, 2000),
        location: '/profile',
      );
      expect(
        Directionality.of(tester.element(find.byType(Scaffold).first)),
        TextDirection.rtl,
      );
      await tester.tap(find.text('English'));
      await tester.pumpAndSettle();
      expect(
        Directionality.of(tester.element(find.byType(Scaffold).first)),
        TextDirection.ltr,
      );
      expect(find.text('Appearance & language'), findsOneWidget);
    });
  });

  group('Design system gallery', () {
    testWidgets('renders tokens and components without errors', (tester) async {
      await pumpApp(
        tester,
        size: const Size(390, 12000),
        location: '/profile/design-system',
        settle: false,
      );
      expect(find.text('Colours'), findsOneWidget);
      expect(find.text('Typography'), findsOneWidget);
      expect(find.text('Healthcare components'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });
  });

  group('Accessibility', () {
    for (final route in [
      '/home',
      '/medications',
      '/assistant',
      '/checkin',
      '/profile',
    ]) {
      testWidgets('200% text scale does not overflow on $route', (
        tester,
      ) async {
        await pumpApp(tester, location: route, textScale: 2.0);
        expect(tester.takeException(), isNull);
      });
    }

    testWidgets(
      'tap targets are at least 44 logical pixels on the home screen',
      (tester) async {
        await pumpApp(tester);
        for (final label in ['I took it', 'Remind me later']) {
          final size = tester.getSize(
            find
                .ancestor(of: find.text(label), matching: find.byType(InkWell))
                .first,
          );
          expect(size.height, greaterThanOrEqualTo(44), reason: label);
        }
      },
    );
  });
}
