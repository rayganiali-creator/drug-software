import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../api/health_client.dart';
import '../../auth/auth_controller.dart';
import '../../components/app_icon.dart';
import '../../components/buttons.dart';
import '../../components/containers.dart';
import '../../components/display.dart';
import '../../components/health.dart';
import '../../components/inputs.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../config.dart';
import '../../core/app_scope.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';

class ProfileScreen extends StatefulWidget {
  const ProfileScreen({super.key, this.healthClientFactory});
  final HealthClient Function()? healthClientFactory;
  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  bool _dose = true, _checkin = true, _pharmacist = true;

  @override
  Widget build(BuildContext context) {
    final app = context.app;
    return ScreenScaffold(
      title: context.t('nav.profile'),
      children: [
        AsyncView(
          load: () => context.services.patients.current(),
          builder: (context, p, _) => AppCard(
            child: Row(
              children: [
                AppAvatar(name: context.loc(p.name), size: 64),
                const SizedBox(width: Space.s4),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(context.loc(p.name), style: context.text.h3),
                      Text(
                        context.t('patient.ageSex', {
                          'age': context.fmt.number(p.age),
                          'sex': context.t('sex.${p.sex}'),
                        }),
                        style: context.text.bodySmall,
                      ),
                    ],
                  ),
                ),
                const DemoBadge(),
              ],
            ),
          ),
        ),
        const SizedBox(height: Space.s4),
        AppCard(
          title: context.t('nav.records'),
          flush: true,
          child: Column(
            children: [
              for (final (path, key, icon) in const [
                ('records', 'nav.records', 'clipboard'),
                ('taking', 'nav.taking', 'pill'),
                ('batches', 'nav.batches', 'box'),
                ('sharing', 'nav.sharing', 'shield'),
                ('messages', 'nav.messages', 'message'),
              ])
                AppListTile(
                  title: context.t(key),
                  leading: AppIcon(icon),
                  trailing: const AppIcon('chevronRight'),
                  onTap: () => context.push('/profile/$path'),
                ),
            ],
          ),
        ),
        const SizedBox(height: Space.s4),
        ListenableBuilder(
          listenable: app.auth,
          builder: (context, _) => _AccountCard(),
        ),
        const SizedBox(height: Space.s4),
        AppCard(
          title: context.t('profile.appearance'),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(context.t('profile.language'), style: context.text.label),
              const SizedBox(height: Space.s2),
              AppSegmented<String>(
                label: context.t('profile.language'),
                value: app.locale.languageCode,
                options: const {'fa': 'فارسی', 'en': 'English'},
                onChanged: (v) => app.setLocale(Locale(v)),
              ),
              const SizedBox(height: Space.s4),
              Text(context.t('theme.label'), style: context.text.label),
              const SizedBox(height: Space.s2),
              AppSegmented<ThemeMode>(
                label: context.t('theme.label'),
                value: app.themeMode,
                options: {
                  ThemeMode.system: context.t('theme.system'),
                  ThemeMode.light: context.t('theme.light'),
                  ThemeMode.dark: context.t('theme.dark'),
                },
                onChanged: app.setThemeMode,
              ),
            ],
          ),
        ),
        const SizedBox(height: Space.s4),
        AppCard(
          title: context.t('profile.notifications'),
          child: Column(
            children: [
              AppSwitch(
                label: context.t('profile.doseReminders'),
                value: _dose,
                onChanged: (v) => setState(() => _dose = v),
              ),
              AppSwitch(
                label: context.t('profile.checkinReminders'),
                value: _checkin,
                onChanged: (v) => setState(() => _checkin = v),
              ),
              AppSwitch(
                label: context.t('profile.pharmacistMessages'),
                value: _pharmacist,
                onChanged: (v) => setState(() => _pharmacist = v),
              ),
            ],
          ),
        ),
        const SizedBox(height: Space.s4),
        AppCard(
          title: context.t('profile.privacy'),
          subtitle: context.t('profile.privacySub'),
          flush: true,
          child: Column(
            children: [
              AppListTile(
                title: context.t('profile.viewer1'),
                meta: context.t('profile.viewer1Meta'),
                leading: const AppIcon('eye'),
              ),
              AppListTile(
                title: context.t('profile.viewer2'),
                meta: context.t('profile.viewer2Meta'),
                leading: const AppIcon('eye'),
              ),
              AppListTile(
                title: context.t('profile.consents'),
                meta: context.t('profile.consentsMeta'),
                leading: const AppIcon('lock'),
                trailing: AppBadge(
                  text: context.t('profile.active'),
                  tone: Tone.success,
                  icon: 'check',
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: Space.s4),
        AppCard(
          title: context.t('profile.about'),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                context.t('demo.footnote'),
                style: context.text.bodySmall.copyWith(
                  color: context.colors.textSecondary,
                ),
              ),
              const SizedBox(height: Space.s3),
              AppButton(
                label: context.t('nav.designSystem'),
                variant: ButtonVariant.secondary,
                size: ButtonSize.sm,
                iconStart: 'grid',
                onPressed: () => context.push('/profile/design-system'),
              ),
            ],
          ),
        ),
        const SizedBox(height: Space.s4),
        _ApiStatus(factory: widget.healthClientFactory),
      ],
    );
  }
}

class _ApiStatus extends StatefulWidget {
  const _ApiStatus({this.factory});
  final HealthClient Function()? factory;
  @override
  State<_ApiStatus> createState() => _ApiStatusState();
}

class _ApiStatusState extends State<_ApiStatus> {
  Future<HealthReport>? _future;

  @override
  Widget build(BuildContext context) {
    return AppCard(
      title: context.t('profile.devTitle'),
      subtitle: context.t('profile.devSub'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          AppButton(
            label: context.t('profile.checkApi'),
            variant: ButtonVariant.secondary,
            size: ButtonSize.sm,
            iconStart: 'refresh',
            onPressed: () => setState(
              () => _future =
                  (widget.factory?.call() ??
                          HealthClient(baseUrl: AppConfig.apiBaseUrl))
                      .fetchReadiness(),
            ),
          ),
          if (_future != null)
            FutureBuilder<HealthReport>(
              future: _future,
              builder: (context, snap) {
                if (snap.connectionState != ConnectionState.done) {
                  return const Padding(
                    padding: EdgeInsets.only(top: Space.s3),
                    child: LinearProgressIndicator(),
                  );
                }
                if (snap.hasError) {
                  return Padding(
                    padding: const EdgeInsets.only(top: Space.s3),
                    child: Text(
                      context.t('profile.apiDown'),
                      style: context.text.bodySmall,
                    ),
                  );
                }
                final r = snap.requireData;
                return Padding(
                  padding: const EdgeInsets.only(top: Space.s3),
                  child: Wrap(
                    spacing: Space.s2,
                    runSpacing: Space.s2,
                    children: [
                      AppBadge(
                        text: r.status,
                        tone: r.isHealthy ? Tone.success : Tone.warning,
                      ),
                      for (final e in r.checks.entries)
                        AppBadge(
                          text: '${e.key}: ${e.value}',
                          tone: e.value == 'Healthy'
                              ? Tone.success
                              : Tone.danger,
                        ),
                    ],
                  ),
                );
              },
            ),
        ],
      ),
    );
  }
}

/// Signed-in identity, sharing consents (revocable, effective at once) and sign-out. Demo controls are labelled DEMO DEV.
class _AccountCard extends StatelessWidget {
  // Deliberately not const: the ListenableBuilder above must be able to rebuild it when auth state changes.
  // ignore: prefer_const_constructors_in_immutables
  _AccountCard();

  @override
  Widget build(BuildContext context) {
    final auth = context.app.auth;
    final lang = context.app.locale.languageCode;
    final user = auth.user;
    if (user == null) return const SizedBox.shrink();
    final consents = auth.consents;
    return AppCard(
      title: context.t('acct.title'),
      subtitle: context.t('auth.signedInAs', {
        'name': user.displayName[lang] ?? user.displayName['en']!,
      }),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: Space.s1,
            children: [
              for (final r in user.roles)
                AppBadge(text: context.t('authRole.$r'), tone: Tone.primary),
            ],
          ),
          const SizedBox(height: Space.s4),
          Semantics(
            header: true,
            child: Text(context.t('acct.consents'), style: context.text.label),
          ),
          Text(
            context.t('acct.consentsSub'),
            style: context.text.bodySmall.copyWith(
              color: context.colors.textSecondary,
            ),
          ),
          const SizedBox(height: Space.s2),
          if (consents.isEmpty)
            Text(context.t('acct.consentNone'), style: context.text.bodySmall)
          else
            for (final c in consents) _ConsentRow(consent: c, lang: lang),
          const SizedBox(height: Space.s4),
          Wrap(
            spacing: Space.s2,
            runSpacing: Space.s2,
            children: [
              AppButton(
                label: context.t('auth.signOut'),
                variant: ButtonVariant.secondary,
                iconStart: 'logout',
                onPressed: auth.signOut,
              ),
              AppButton(
                label: context.t('auth.expireSession'),
                variant: ButtonVariant.ghost,
                size: ButtonSize.sm,
                onPressed: auth.simulateExpiry,
              ),
            ],
          ),
          const SizedBox(height: Space.s1),
          Text(
            context.t('auth.demoSwitchNote'),
            style: context.text.caption.copyWith(
              color: context.colors.textSecondary,
            ),
          ),
        ],
      ),
    );
  }
}

class _ConsentRow extends StatelessWidget {
  const _ConsentRow({required this.consent, required this.lang});
  final ConsentItem consent;
  final String lang;

  @override
  Widget build(BuildContext context) {
    final auth = context.app.auth;
    final status = auth.consentStatus(consent);
    final (label, tone) = switch (status) {
      ConsentStatus.active => ('acct.consentActive', Tone.success),
      ConsentStatus.expired => ('acct.consentExpired', Tone.warning),
      ConsentStatus.revoked => ('acct.consentRevokedStatus', Tone.neutral),
    };
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Space.s2),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            consent.grantee[lang] ?? consent.grantee['en']!,
            style: context.text.body.copyWith(fontWeight: FontWeight.w600),
          ),
          Text(
            '${context.t('purpose.${consent.purpose}')} · ${consent.scope.map((s) => context.t('scope.$s')).join('، ')}',
            style: context.text.bodySmall,
          ),
          const SizedBox(height: Space.s1),
          Wrap(
            spacing: Space.s2,
            runSpacing: Space.s1,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              AppBadge(text: context.t(label), tone: tone),
              if (status == ConsentStatus.active)
                AppButton(
                  label: context.t('acct.consentRevoke'),
                  variant: ButtonVariant.secondary,
                  size: ButtonSize.sm,
                  onPressed: () => auth.revokeConsent(consent.id),
                ),
            ],
          ),
        ],
      ),
    );
  }
}
