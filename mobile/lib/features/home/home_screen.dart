import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

import '../../components/app_icon.dart';
import '../../components/buttons.dart';
import '../../components/containers.dart';
import '../../components/display.dart';
import '../../components/health.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../core/formatters.dart';
import '../../core/l10n.dart';
import '../../core/models.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';

class _HomeData {
  const _HomeData(
    this.patient,
    this.doses,
    this.alerts,
    this.activity,
    this.quick,
    this.series,
    this.checkedIn,
  );
  final Patient patient;
  final List<Dose> doses;
  final List<HealthAlert> alerts;
  final List<ActivityItem> activity;
  final List<Localized> quick;
  final List<int> series;
  final bool checkedIn;
}

Future<_HomeData> _load(BuildContext context) async {
  final s = context.services;
  final id = context.app.data.currentPatientId;
  final r = await Future.wait<Object>([
    s.patients.current(),
    s.medications.todaysDoses(id),
    s.notifications.alerts(id),
    s.notifications.activity(id),
    s.ai.quickQuestions(),
    s.analytics.adherenceSeries(id),
    s.patients.hasCheckedInToday(),
  ]);
  return _HomeData(
    r[0] as Patient,
    r[1] as List<Dose>,
    r[2] as List<HealthAlert>,
    r[3] as List<ActivityItem>,
    r[4] as List<Localized>,
    r[5] as List<int>,
    r[6] as bool,
  );
}

class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key});
  @override
  Widget build(BuildContext context) {
    return ScreenScaffold(
      title: context.t('nav.home'),
      visuallyHiddenTitle: true,
      children: [
        AsyncView<_HomeData>(
          load: () => _load(context),
          loadingRows: 3,
          builder: (context, d, update) => _HomeBody(
            data: d,
            onDosesChanged: (doses) => update(
              _HomeData(
                d.patient,
                doses,
                d.alerts,
                d.activity,
                d.quick,
                d.series,
                d.checkedIn,
              ),
            ),
          ),
        ),
      ],
    );
  }
}

class _HomeBody extends StatelessWidget {
  const _HomeBody({required this.data, required this.onDosesChanged});
  final _HomeData data;
  final ValueChanged<List<Dose>> onDosesChanged;

  Future<void> _take(BuildContext context, Dose d) async {
    final svc = context.services;
    HapticFeedback.lightImpact();
    onDosesChanged(await svc.medications.setDose(d.id, DoseStatus.taken));
    if (!context.mounted) return;
    showAppToast(
      context,
      context.t('home.doseRecorded', {'name': context.loc(d.drug.name)}),
      tone: Tone.success,
      actionLabel: context.t('common.undo'),
      onAction: () async => onDosesChanged(
        await svc.medications.setDose(d.id, DoseStatus.upcoming),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final fmt = context.fmt;
    final now = context.app.clock();
    final first = context.loc(data.patient.name).split(' ').first;
    final greetKey = now.hour < 12
        ? 'home.goodMorning'
        : now.hour < 18
        ? 'home.goodAfternoon'
        : 'home.goodEvening';

    final greeting = Row(
      children: [
        AppAvatar(name: context.loc(data.patient.name), size: 64),
        const SizedBox(width: Space.s4),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Semantics(
                header: true,
                child: Text(
                  context.t(greetKey, {'name': first}),
                  style: context.text.h2,
                ),
              ),
              Text(
                '${fmt.weekday(now)}، ${fmt.date(now)}',
                style: context.text.bodySmall.copyWith(
                  color: context.colors.textSecondary,
                ),
              ),
            ],
          ),
        ),
      ],
    );

    final alerts = data.alerts.take(2).toList();
    final timeline = [for (final d in data.doses.where((d) => !d.tomorrow)) d];

    final next = NextDoseHero(
      doses: data.doses,
      onTake: (d) => _take(context, d),
      onLater: () => showAppToast(context, context.t('home.laterToast')),
    );
    final alertsW = Column(
      children: [
        for (final a in alerts)
          Padding(
            padding: const EdgeInsets.only(bottom: Space.s3),
            child: AlertCard(
              tone: a.severity == 'warning' ? Tone.warning : Tone.info,
              title: context.loc(a.title),
              body: context.loc(a.body),
              actions: [
                AppButton(
                  label: context.t('home.viewMedications'),
                  onPressed: () => context.go('/medications'),
                  variant: ButtonVariant.secondary,
                  size: ButtonSize.sm,
                ),
              ],
            ),
          ),
        if (data.alerts.length > alerts.length)
          AppButton(
            label: context.t('home.allAlerts'),
            variant: ButtonVariant.ghost,
            size: ButtonSize.sm,
            onPressed: () => showAppBottomSheet<void>(
              context,
              title: context.t('home.alerts'),
              closeLabel: context.t('common.close'),
              body: Column(
                children: [
                  for (final a in data.alerts)
                    Padding(
                      padding: const EdgeInsets.only(bottom: Space.s3),
                      child: AlertCard(
                        tone: a.severity == 'warning'
                            ? Tone.warning
                            : Tone.info,
                        title: context.loc(a.title),
                        body: context.loc(a.body),
                      ),
                    ),
                ],
              ),
            ),
          ),
      ],
    );
    final ai = AppCard(
      ai: true,
      title: context.t('home.askTitle'),
      subtitle: context.t('home.askBody'),
      actions: AppIcon('sparkles', color: context.colors.accent),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: Space.s2,
            children: [
              for (final q in data.quick.take(3))
                AppChip(
                  label: context.loc(q),
                  ai: true,
                  onTap: () => context.go(
                    '/assistant?q=${Uri.encodeComponent(context.loc(q))}',
                  ),
                ),
            ],
          ),
          const SizedBox(height: Space.s3),
          AppButton(
            label: context.t('home.openAssistant'),
            variant: ButtonVariant.ai,
            iconStart: 'sparkles',
            onPressed: () => context.go('/assistant'),
          ),
        ],
      ),
    );
    final today = AppCard(
      title: context.t('home.today'),
      subtitle: context.t('home.todaySub', {'n': fmt.number(timeline.length)}),
      child: timeline.isEmpty
          ? Text(context.t('home.noDoses'), style: context.text.bodySmall)
          : AppTimeline(
              entries: [
                for (final d in timeline)
                  TimelineEntry(
                    title:
                        '${context.loc(d.drug.name)} ${isolateLtr(d.drug.strength)}',
                    meta:
                        '${fmt.time(d.time)} · ${d.status == DoseStatus.taken
                            ? context.t('dose.taken')
                            : d.status == DoseStatus.missed
                            ? context.t('dose.missed')
                            : context.t('dose.upcoming')}',
                    icon: d.status == DoseStatus.taken
                        ? 'check'
                        : d.status == DoseStatus.missed
                        ? 'alertTriangle'
                        : 'clock',
                    tone: d.status == DoseStatus.taken
                        ? Tone.success
                        : d.status == DoseStatus.missed
                        ? Tone.warning
                        : Tone.neutral,
                    current: d.isNext,
                    trailing: d.status == DoseStatus.missed
                        ? AppButton(
                            label: context.t('dose.markTaken'),
                            variant: ButtonVariant.ghost,
                            size: ButtonSize.sm,
                            onPressed: () => _take(context, d),
                          )
                        : null,
                  ),
              ],
            ),
    );
    final checkin = CheckInCard(
      done: data.checkedIn,
      onStart: () => context.go('/checkin'),
    );
    final adherence = AdherenceCard(
      value: data.patient.adherence,
      series: data.series.length > 14
          ? data.series.sublist(data.series.length - 14)
          : data.series,
    );
    final activity = AppCard(
      title: context.t('home.recent'),
      flush: true,
      child: Column(
        children: [
          for (final a in data.activity)
            AppListTile(
              title: context.loc(a.text),
              meta: fmt.relativeMinutes(
                a.minutesAgo,
                (k, p) => context.t(k, p),
              ),
              leading: Container(
                width: 36,
                height: 36,
                decoration: BoxDecoration(
                  color: context.colors.surfaceSunken,
                  borderRadius: BorderRadius.circular(Radii.md),
                ),
                child: Center(
                  child: AppIcon(
                    {
                          'dose': 'check',
                          'checkin': 'checkCircle',
                          'ai': 'sparkles',
                          'review': 'clipboard',
                        }[a.kind] ??
                        'info',
                    size: IconSizes.sm,
                    color: context.colors.textSecondary,
                  ),
                ),
              ),
            ),
        ],
      ),
    );

    return LayoutBuilder(
      builder: (context, box) {
        final twoCol = box.maxWidth >= 860;
        const gap = SizedBox(height: Space.s4);
        if (!twoCol) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              greeting,
              gap,
              next,
              gap,
              alertsW,
              if (alerts.isNotEmpty) gap,
              ai,
              gap,
              today,
              gap,
              checkin,
              gap,
              adherence,
              gap,
              activity,
            ],
          );
        }
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            greeting,
            gap,
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  flex: 13,
                  child: Column(
                    children: [
                      next,
                      gap,
                      alertsW,
                      if (alerts.isNotEmpty) gap,
                      today,
                    ],
                  ),
                ),
                const SizedBox(width: Space.s4),
                Expanded(
                  flex: 10,
                  child: Column(
                    children: [adherence, gap, ai, gap, checkin, gap, activity],
                  ),
                ),
              ],
            ),
          ],
        );
      },
    );
  }
}

class NextDoseHero extends StatelessWidget {
  const NextDoseHero({
    super.key,
    required this.doses,
    required this.onTake,
    required this.onLater,
  });
  final List<Dose> doses;
  final ValueChanged<Dose> onTake;
  final VoidCallback onLater;
  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final fmt = context.fmt;
    final next = doses.where((d) => d.isNext).firstOrNull;
    final decoration = BoxDecoration(
      borderRadius: BorderRadius.circular(Radii.lg),
      boxShadow: context.elevation(3),
      gradient: LinearGradient(
        begin: AlignmentDirectional.topStart,
        end: AlignmentDirectional.bottomEnd,
        colors: [
          next == null ? c.success : c.primary,
          Color.alphaBlend(
            (next == null ? c.primary : c.accent).withValues(alpha: 0.4),
            next == null ? c.success : c.primary,
          ),
        ],
      ),
    );
    final on = next == null ? c.onSuccess : c.onPrimary;
    if (next == null) {
      return Container(
        padding: const EdgeInsets.all(Space.s5),
        decoration: decoration,
        child: Row(
          children: [
            AppIcon('checkCircle', color: on),
            const SizedBox(width: Space.s3),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    context.t('home.allDone'),
                    style: context.text.label.copyWith(color: on),
                  ),
                  Text(
                    context.t('home.allDoneBody'),
                    style: context.text.h3.copyWith(color: on),
                  ),
                ],
              ),
            ),
          ],
        ),
      );
    }
    final now = context.app.clock();
    final mins =
        (int.parse(next.time.substring(0, 2)) * 60 +
            int.parse(next.time.substring(3)) +
            (next.tomorrow ? 1440 : 0)) -
        (now.hour * 60 + now.minute);
    final count = mins <= 0
        ? context.t('home.dueNow')
        : mins < 60
        ? context.t('home.inMinutes', {'n': fmt.number(mins)})
        : context.t('home.inHours', {
            'h': fmt.number(mins ~/ 60),
            'm': fmt.number(mins % 60),
          });
    return Semantics(
      container: true,
      label: context.t('med.nextDose'),
      child: Container(
        padding: const EdgeInsets.all(Space.s5),
        decoration: decoration,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                AppIcon('clock', size: IconSizes.sm, color: on),
                const SizedBox(width: Space.s2),
                Text(
                  '${context.t('med.nextDose')}${next.tomorrow ? ' · ${context.t('home.tomorrow')}' : ''}',
                  style: context.text.label.copyWith(color: on),
                ),
              ],
            ),
            const SizedBox(height: Space.s2),
            Text(
              '${context.loc(next.drug.name)} ${isolateLtr(next.drug.strength)}',
              style: context.text.h2.copyWith(
                color: on,
                fontWeight: FontWeight.w800,
              ),
            ),
            Text(
              '${context.loc(next.drug.form)} · ${context.loc(next.drug.instructions)}',
              style: context.text.bodySmall.copyWith(color: on),
            ),
            const SizedBox(height: Space.s3),
            Text(
              fmt.time(next.time),
              style: context.text.display.copyWith(
                color: on,
                fontWeight: FontWeight.w800,
              ),
              textDirection: TextDirection.ltr,
            ),
            Text(count, style: context.text.bodySmall.copyWith(color: on)),
            const SizedBox(height: Space.s5),
            Wrap(
              spacing: Space.s2,
              runSpacing: Space.s2,
              children: [
                Theme(
                  data: Theme.of(context),
                  child: Material(
                    color: c.surface,
                    borderRadius: BorderRadius.circular(Radii.lg),
                    child: InkWell(
                      borderRadius: BorderRadius.circular(Radii.lg),
                      onTap: next.tomorrow ? null : () => onTake(next),
                      child: Semantics(
                        button: true,
                        enabled: !next.tomorrow,
                        label: context.t('home.tookIt'),
                        excludeSemantics: true,
                        child: Container(
                          constraints: const BoxConstraints(
                            minHeight: ControlHeights.lg,
                            minWidth: 140,
                          ),
                          padding: const EdgeInsetsDirectional.symmetric(
                            horizontal: Space.s6,
                          ),
                          alignment: Alignment.center,
                          child: Row(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              AppIcon(
                                'check',
                                size: IconSizes.sm,
                                color: c.primary,
                              ),
                              const SizedBox(width: Space.s2),
                              Flexible(
                                child: Text(
                                  context.t('home.tookIt'),
                                  style: context.text.button.copyWith(
                                    color: c.primary,
                                  ),
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
                OutlinedButton(
                  onPressed: onLater,
                  style: OutlinedButton.styleFrom(
                    foregroundColor: on,
                    side: BorderSide(color: on.withValues(alpha: 0.6)),
                    minimumSize: const Size(0, ControlHeights.lg),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(Radii.lg),
                    ),
                    textStyle: context.text.button,
                  ),
                  child: Text(context.t('home.remindLater')),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
