import 'package:flutter/material.dart';

import '../../auth/auth_controller.dart';
import '../../components/buttons.dart';
import '../../components/containers.dart';
import '../../components/display.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';

/// Sign-in with FICTIONAL demo accounts (no passwords). Replaced by a real identity provider in a later phase.
class LoginScreen extends StatelessWidget {
  const LoginScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final auth = context.app.auth;
    final lang = context.app.locale.languageCode;
    final reason = auth.reason;
    return Scaffold(
      body: Column(
        children: [
          const SafeArea(bottom: false, child: DemoBanner()),
          Expanded(
            child: ScreenScaffold(
              title: context.t('auth.title'),
              subtitle: context.t('auth.subtitle'),
              children: [
                if (reason == SignedOutReason.expired) ...[
                  AlertCard(
                    tone: Tone.warning,
                    title: context.t('auth.expired.title'),
                    body: context.t('auth.expired.body'),
                  ),
                  const SizedBox(height: Space.s3),
                ],
                if (reason == SignedOutReason.disabled) ...[
                  AlertCard(
                    tone: Tone.danger,
                    title: context.t('auth.disabled.title'),
                    body: context.t('auth.disabled.body'),
                  ),
                  const SizedBox(height: Space.s3),
                ],
                if (reason == SignedOutReason.signedOut) ...[
                  AlertCard(
                    tone: Tone.success,
                    title: context.t('auth.signedOut'),
                  ),
                  const SizedBox(height: Space.s3),
                ],
                AlertCard(
                  tone: Tone.info,
                  icon: 'shield',
                  title: context.t('auth.demoEnv'),
                  body: context.t('auth.demoHow'),
                ),
                const SizedBox(height: Space.s5),
                Semantics(
                  header: true,
                  child: Text(
                    context.t('auth.chooseAccount'),
                    style: context.text.h3,
                  ),
                ),
                const SizedBox(height: Space.s3),
                for (final a in auth.demoAccounts) ...[
                  Semantics(
                    button: true,
                    label: context.t('auth.signInAs', {
                      'name': a.displayName[lang] ?? a.displayName['en']!,
                    }),
                    excludeSemantics: true,
                    child: AppCard(
                      onTap: () => auth.signIn(a.id),
                      title: a.displayName[lang] ?? a.displayName['en'],
                      subtitle: a.description[lang] ?? a.description['en'],
                      child: Wrap(
                        spacing: Space.s1,
                        runSpacing: Space.s1,
                        children: [
                          for (final r in a.roles)
                            AppBadge(
                              text: context.t('authRole.$r'),
                              tone: Tone.primary,
                            ),
                          if (a.disabled)
                            AppBadge(
                              text: context.t('auth.disabled.title'),
                              tone: Tone.danger,
                            ),
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(height: Space.s3),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// Neutral "no access" screen: professional roles use the web app, not this patient app.
class NoAccessScreen extends StatelessWidget {
  const NoAccessScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final auth = context.app.auth;
    return Scaffold(
      body: Column(
        children: [
          const SafeArea(bottom: false, child: DemoBanner()),
          Expanded(
            child: Center(
              child: EmptyState(
                icon: 'shield',
                title: context.t('auth.unauthorized.title'),
                body: context.t('auth.mobilePatientsOnly'),
                action: AppButton(
                  label: context.t('auth.signOut'),
                  variant: ButtonVariant.secondary,
                  onPressed: auth.signOut,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
