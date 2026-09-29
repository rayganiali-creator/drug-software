import 'package:flutter/material.dart';

import '../core/app_scope.dart';
import '../core/formatters.dart';
import '../core/models.dart';
import '../design/theme.dart';
import '../design/tokens.g.dart';
import 'app_icon.dart';
import 'buttons.dart';
import 'containers.dart';
import 'display.dart';
import 'tone.dart';

/// Marks Mock records. Every healthcare card shows it.
class DemoBadge extends StatelessWidget {
  const DemoBadge({super.key});
  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    return Container(
      padding: const EdgeInsetsDirectional.symmetric(
        horizontal: Space.s2,
        vertical: 1,
      ),
      decoration: BoxDecoration(
        color: c.warningContainer,
        borderRadius: BorderRadius.circular(Radii.xs),
        border: Border.all(color: c.warning),
      ),
      child: Text(
        context.t('demo.badge'),
        style: context.text.caption.copyWith(
          color: c.onWarningContainer,
          fontWeight: FontWeight.w700,
          fontSize: 11,
        ),
      ),
    );
  }
}

Widget _tile(BuildContext context, String icon, Tone tone) {
  final tc = context.tone(tone);
  return Container(
    width: 48,
    height: 48,
    decoration: BoxDecoration(
      color: tc.bg,
      borderRadius: BorderRadius.circular(Radii.md),
    ),
    child: Center(child: AppIcon(icon, color: tc.fg)),
  );
}

Widget _head(
  BuildContext context, {
  required String icon,
  required Tone tone,
  required String title,
  String? sub,
  Widget? trailing,
}) => Row(
  children: [
    _tile(context, icon, tone),
    const SizedBox(width: Space.s3),
    Expanded(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Semantics(header: true, child: Text(title, style: context.text.h4)),
          if (sub != null) Text(sub, style: context.text.caption),
        ],
      ),
    ),
    if (trailing != null) ...[const SizedBox(width: Space.s2), trailing],
  ],
);

Tone riskTone(String risk) => risk == 'high'
    ? Tone.danger
    : risk == 'medium'
    ? Tone.warning
    : Tone.success;

class RiskBadge extends StatelessWidget {
  const RiskBadge({super.key, required this.risk});
  final String risk;
  @override
  Widget build(BuildContext context) => AppBadge(
    text: context.t('risk.$risk'),
    tone: riskTone(risk),
    icon: risk == 'low' ? 'checkCircle' : 'alertTriangle',
  );
}

// ---------- MedicationCard ----------
class MedicationCard extends StatelessWidget {
  const MedicationCard({
    super.key,
    required this.med,
    this.dose,
    this.onTap,
    this.detail = false,
  });
  final PatientMedication med;
  final Dose? dose;
  final VoidCallback? onTap;
  final bool detail;
  @override
  Widget build(BuildContext context) {
    final d = med.drug;
    final fmt = context.fmt;
    final nextTone = dose?.status == DoseStatus.missed
        ? Tone.warning
        : Tone.primary;
    final tc = context.tone(nextTone);
    return AppCard(
      onTap: onTap,
      semanticLabel: context.loc(d.name),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _head(
            context,
            icon: 'pill',
            tone: Tone.primary,
            title: '${context.loc(d.name)} ${isolateLtr(d.strength)}',
            sub:
                '${context.t('med.activeIngredient')}: ${context.loc(d.activeIngredient.name)}',
            trailing: const DemoBadge(),
          ),
          const SizedBox(height: Space.s3),
          Wrap(
            spacing: Space.s2,
            runSpacing: Space.s2,
            children: [
              AppChip(label: context.loc(d.form), icon: 'pill'),
              AppChip(label: context.loc(d.route), icon: 'activity'),
              AppChip(
                label: med.times.map(fmt.time).join(' · '),
                icon: 'clock',
              ),
            ],
          ),
          if (dose != null) ...[
            const SizedBox(height: Space.s3),
            Container(
              padding: const EdgeInsetsDirectional.symmetric(
                horizontal: Space.s3,
                vertical: Space.s2,
              ),
              decoration: BoxDecoration(
                color: tc.bg,
                borderRadius: BorderRadius.circular(Radii.md),
              ),
              child: Row(
                children: [
                  AppIcon('clock', size: IconSizes.sm, color: tc.fg),
                  const SizedBox(width: Space.s2),
                  Expanded(
                    child: Text(
                      '${dose!.status == DoseStatus.taken
                          ? context.t('dose.taken')
                          : dose!.status == DoseStatus.missed
                          ? context.t('dose.missed')
                          : context.t('med.nextDose')} · ${fmt.time(dose!.time)}',
                      style: context.text.bodySmall.copyWith(
                        color: tc.fg,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
          if (detail) ...[
            const SizedBox(height: Space.s4),
            Text(
              context.t('med.instructions'),
              style: context.text.label.copyWith(
                color: context.colors.textSecondary,
              ),
            ),
            Text(context.loc(d.instructions), style: context.text.body),
            const SizedBox(height: Space.s4),
            Text(
              context.t('med.schedule'),
              style: context.text.label.copyWith(
                color: context.colors.textSecondary,
              ),
            ),
            Text(
              '${context.loc(med.dose)} · ${context.loc(med.frequency)}',
              style: context.text.body,
            ),
            const SizedBox(height: Space.s4),
            for (final w in d.warnings)
              Padding(
                padding: const EdgeInsets.only(bottom: Space.s2),
                child: AlertCard(
                  tone: Tone.warning,
                  title: context.t('med.warning'),
                  body: context.loc(w),
                ),
              ),
          ],
        ],
      ),
    );
  }
}

// ---------- DrugCard ----------
class DrugCard extends StatelessWidget {
  const DrugCard({super.key, required this.drug, this.onTap});
  final Drug drug;
  final VoidCallback? onTap;
  @override
  Widget build(BuildContext context) => AppCard(
    onTap: onTap,
    semanticLabel: context.loc(drug.name),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _head(
          context,
          icon: 'flask',
          tone: Tone.accent,
          title: context.loc(drug.name),
          sub: context.loc(drug.activeIngredient.name),
          trailing: const DemoBadge(),
        ),
        const SizedBox(height: Space.s3),
        Wrap(
          spacing: Space.s2,
          runSpacing: Space.s2,
          children: [
            AppChip(label: isolateLtr(drug.strength)),
            AppChip(label: context.loc(drug.form)),
            AppChip(label: context.loc(drug.route)),
          ],
        ),
        const SizedBox(height: Space.s2),
        Text(
          '${context.t('drug.category')}: ${context.loc(drug.category)}',
          style: context.text.caption,
        ),
      ],
    ),
  );
}

// ---------- PrescriptionCard ----------
class PrescriptionCard extends StatelessWidget {
  const PrescriptionCard({
    super.key,
    required this.rx,
    this.showPatient = false,
  });
  final Prescription rx;
  final bool showPatient;
  @override
  Widget build(BuildContext context) {
    final tone =
        {
          'active': Tone.success,
          'pending-review': Tone.warning,
          'dispensed': Tone.info,
        }[rx.status] ??
        Tone.neutral;
    final issued = DateTime.now().subtract(Duration(days: rx.issuedDaysAgo));
    return AppCard(
      semanticLabel: '${context.t('rx.title')} ${rx.id}',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _head(
            context,
            icon: 'clipboard',
            tone: Tone.primary,
            title:
                '${rx.id.toUpperCase()}${showPatient ? ' · ${context.loc(rx.patient.name)}' : ''}',
            sub: '${context.loc(rx.prescriber)} · ${context.fmt.date(issued)}',
            trailing: AppBadge(
              text: context.t('rx.status.${rx.status}'),
              tone: tone,
            ),
          ),
          const SizedBox(height: Space.s3),
          for (final it in rx.items)
            Padding(
              padding: const EdgeInsets.only(bottom: Space.s2),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Padding(
                    padding: const EdgeInsets.only(top: 4),
                    child: AppIcon(
                      'pill',
                      size: IconSizes.sm,
                      color: context.colors.primary,
                    ),
                  ),
                  const SizedBox(width: Space.s2),
                  Expanded(
                    child: Text(
                      '${context.loc(it.drug.name)} ${isolateLtr(it.drug.strength)} · ${context.loc(it.dose)} · ${context.loc(it.frequency)}',
                      style: context.text.bodySmall,
                    ),
                  ),
                ],
              ),
            ),
          Row(
            children: [
              Expanded(
                child: Text(
                  context.t('rx.refills', {
                    'n': context.fmt.number(rx.refillsLeft),
                  }),
                  style: context.text.caption,
                ),
              ),
              const DemoBadge(),
            ],
          ),
        ],
      ),
    );
  }
}

// ---------- PatientCard / RiskCard ----------
class PatientCard extends StatelessWidget {
  const PatientCard({super.key, required this.patient, this.onTap});
  final Patient patient;
  final VoidCallback? onTap;
  @override
  Widget build(BuildContext context) {
    final fmt = context.fmt;
    return AppCard(
      onTap: onTap,
      semanticLabel: context.loc(patient.name),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              AppAvatar(
                name: context.loc(patient.name),
                size: 56,
                tone: riskTone(patient.risk),
              ),
              const SizedBox(width: Space.s3),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(context.loc(patient.name), style: context.text.h4),
                    Text(
                      context.t('patient.ageSex', {
                        'age': fmt.number(patient.age),
                        'sex': context.t('sex.${patient.sex}'),
                      }),
                      style: context.text.caption,
                    ),
                  ],
                ),
              ),
              RiskBadge(risk: patient.risk),
            ],
          ),
          const SizedBox(height: Space.s3),
          Wrap(
            spacing: Space.s2,
            runSpacing: Space.s2,
            children: [
              for (final cnd in patient.conditions)
                AppChip(label: context.loc(cnd)),
              for (final a in patient.allergies)
                AppBadge(
                  text: '${context.t('patient.allergy')}: ${context.loc(a)}',
                  tone: Tone.danger,
                  icon: 'alertTriangle',
                ),
            ],
          ),
          const SizedBox(height: Space.s3),
          Row(
            children: [
              Expanded(
                child: Text(
                  '${context.t('patient.adherence')}: ${fmt.percent(patient.adherence)}',
                  style: context.text.bodySmall,
                ),
              ),
              Sparkline(
                values: patient.adherenceSeries,
                label: context.t('patient.adherenceTrend'),
              ),
              const SizedBox(width: Space.s2),
              const DemoBadge(),
            ],
          ),
        ],
      ),
    );
  }
}

class RiskCard extends StatelessWidget {
  const RiskCard({
    super.key,
    required this.patient,
    required this.reasons,
    this.onTap,
  });
  final Patient patient;
  final List<String> reasons;
  final VoidCallback? onTap;
  @override
  Widget build(BuildContext context) => AppCard(
    onTap: onTap,
    semanticLabel: context.loc(patient.name),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            AppAvatar(
              name: context.loc(patient.name),
              tone: riskTone(patient.risk),
            ),
            const SizedBox(width: Space.s3),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(context.loc(patient.name), style: context.text.h4),
                  Text(
                    context.t('risk.needsAttention'),
                    style: context.text.caption,
                  ),
                ],
              ),
            ),
            RiskBadge(risk: patient.risk),
          ],
        ),
        const SizedBox(height: Space.s3),
        for (final r in reasons)
          Padding(
            padding: const EdgeInsets.only(bottom: 4),
            child: Row(
              children: [
                AppIcon(
                  'alertTriangle',
                  size: IconSizes.xs,
                  color: context.colors.warning,
                ),
                const SizedBox(width: Space.s2),
                Expanded(child: Text(r, style: context.text.bodySmall)),
              ],
            ),
          ),
      ],
    ),
  );
}

// ---------- ADRCard ----------
class ADRCard extends StatelessWidget {
  const ADRCard({super.key, required this.report, this.showPatient = false});
  final AdverseReport report;
  final bool showPatient;
  @override
  Widget build(BuildContext context) {
    final tone =
        {
          'mild': Tone.info,
          'moderate': Tone.warning,
          'serious': Tone.danger,
        }[report.severity] ??
        Tone.neutral;
    return AppCard(
      semanticLabel: context.loc(report.event),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _head(
            context,
            icon: 'alertTriangle',
            tone: tone,
            title: context.loc(report.event),
            sub:
                '${context.loc(report.drug.name)}${showPatient ? ' · ${context.loc(report.patient.name)}' : ''} · ${context.t('time.daysAgo', {'n': context.fmt.number(report.reportedDaysAgo)})}',
            trailing: AppBadge(
              text: context.t('severity.${report.severity}'),
              tone: tone,
            ),
          ),
          const SizedBox(height: Space.s3),
          Wrap(
            spacing: Space.s2,
            runSpacing: Space.s2,
            children: [
              AppChip(label: context.t('adr.status.${report.status}')),
              AppChip(
                label:
                    '${context.t('adr.causality')}: ${context.t('adr.causality.${report.causality}')}',
              ),
            ],
          ),
          const SizedBox(height: Space.s2),
          const Align(
            alignment: AlignmentDirectional.centerStart,
            child: DemoBadge(),
          ),
        ],
      ),
    );
  }
}

// ---------- InteractionCard ----------
class InteractionCard extends StatelessWidget {
  const InteractionCard({super.key, required this.interaction});
  final Interaction interaction;
  @override
  Widget build(BuildContext context) {
    final x = interaction;
    final tone =
        {
          'minor': Tone.info,
          'moderate': Tone.warning,
          'major': Tone.danger,
        }[x.severity] ??
        Tone.neutral;
    return AppCard(
      semanticLabel: context.t('ix.title'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: Space.s2,
            runSpacing: Space.s2,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              _pill(context, context.loc(x.drugA.name)),
              AppIcon(
                'flask',
                size: IconSizes.sm,
                color: context.colors.textSecondary,
              ),
              _pill(context, context.loc(x.drugB.name)),
              AppBadge(
                text: context.t('ix.severity.${x.severity}'),
                tone: tone,
                icon: 'alertTriangle',
              ),
              const DemoBadge(),
            ],
          ),
          const SizedBox(height: Space.s2),
          Text(context.loc(x.summary), style: context.text.body),
          Text(
            '${context.t('ix.management')}: ${context.loc(x.management)}',
            style: context.text.caption,
          ),
          const SizedBox(height: Space.s2),
          AppChip(
            label: context.loc(x.source.title),
            icon: 'bookOpen',
            ai: true,
          ),
        ],
      ),
    );
  }

  Widget _pill(BuildContext context, String text) => Container(
    padding: const EdgeInsetsDirectional.symmetric(
      horizontal: Space.s2,
      vertical: 2,
    ),
    decoration: BoxDecoration(
      color: context.colors.surfaceSunken,
      borderRadius: BorderRadius.circular(Radii.sm),
    ),
    child: Text(
      text,
      style: context.text.label.copyWith(fontWeight: FontWeight.w700),
    ),
  );
}

// ---------- AdherenceCard ----------
class AdherenceCard extends StatelessWidget {
  const AdherenceCard({super.key, required this.value, required this.series});
  final int value;
  final List<int> series;
  @override
  Widget build(BuildContext context) {
    final tone = value >= 85
        ? Tone.success
        : value >= 70
        ? Tone.warning
        : Tone.danger;
    final tc = context.tone(tone);
    return AppCard(
      title: context.t('adherence.title'),
      subtitle: context.t('adherence.period'),
      child: Row(
        children: [
          ProgressRing(
            value: value.toDouble(),
            tone: tone,
            label: context.t('adherence.title'),
            size: 104,
            child: Text(context.fmt.percent(value), style: context.text.h3),
          ),
          const SizedBox(width: Space.s5),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Sparkline(
                  values: series,
                  label: context.t('patient.adherenceTrend'),
                  width: 140,
                  height: 44,
                  color: tc.solid,
                ),
                const SizedBox(height: Space.s2),
                Text(
                  value >= 85
                      ? context.t('adherence.good')
                      : value >= 70
                      ? context.t('adherence.ok')
                      : context.t('adherence.low'),
                  style: context.text.caption,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

// ---------- CheckInCard ----------
class CheckInCard extends StatelessWidget {
  const CheckInCard({super.key, required this.done, required this.onStart});
  final bool done;
  final VoidCallback onStart;
  @override
  Widget build(BuildContext context) {
    final tone = done ? Tone.success : Tone.primary;
    final tc = context.tone(tone);
    return AppCard(
      tone: tone,
      child: Row(
        children: [
          _tile(context, done ? 'checkCircle' : 'heart', tone),
          const SizedBox(width: Space.s3),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  done
                      ? context.t('checkin.doneTitle')
                      : context.t('checkin.cardTitle'),
                  style: context.text.h4.copyWith(color: tc.fg),
                ),
                Text(
                  done
                      ? context.t('checkin.doneBody')
                      : context.t('checkin.cardBody'),
                  style: context.text.caption.copyWith(color: tc.fg),
                ),
              ],
            ),
          ),
          if (!done) ...[
            const SizedBox(width: Space.s2),
            AppButton(
              label: context.t('checkin.start'),
              onPressed: onStart,
              size: ButtonSize.sm,
              iconEnd: 'arrowRight',
            ),
          ],
        ],
      ),
    );
  }
}

// ---------- AI ----------
class ConfidenceMeter extends StatelessWidget {
  const ConfidenceMeter({super.key, required this.level});
  final Confidence level;
  @override
  Widget build(BuildContext context) {
    final tone = {
      Confidence.high: Tone.success,
      Confidence.medium: Tone.warning,
      Confidence.low: Tone.danger,
    }[level]!;
    final tc = context.tone(tone);
    final on = {
      Confidence.high: 3,
      Confidence.medium: 2,
      Confidence.low: 1,
    }[level]!;
    return Semantics(
      label:
          '${context.t('ai.confidence')}: ${context.t('ai.confidence.${level.name}')}. ${context.t('ai.confidence.explain')}',
      child: ExcludeSemantics(
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            for (var i = 1; i <= 3; i++)
              Container(
                width: 5,
                height: 4.0 + 4 * i,
                margin: const EdgeInsetsDirectional.only(end: 3),
                decoration: BoxDecoration(
                  color: i <= on ? tc.solid : context.colors.border,
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
            const SizedBox(width: Space.s1),
            Text(
              '${context.t('ai.confidence')}: ',
              style: context.text.caption,
            ),
            Text(
              context.t('ai.confidence.${level.name}'),
              style: context.text.caption.copyWith(
                color: tc.fg,
                fontWeight: FontWeight.w700,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class SourceChips extends StatelessWidget {
  const SourceChips({super.key, required this.sources, this.onOpen});
  final List<KnowledgeSource> sources;
  final VoidCallback? onOpen;
  @override
  Widget build(BuildContext context) {
    if (sources.isEmpty) {
      return Text(context.t('ai.noSources'), style: context.text.caption);
    }
    return Semantics(
      container: true,
      label: context.t('ai.sources'),
      child: Wrap(
        spacing: Space.s2,
        runSpacing: 0,
        children: [
          for (var i = 0; i < sources.length; i++)
            AppChip(
              label:
                  '[${context.fmt.number(i + 1)}] ${context.loc(sources[i].title)}',
              icon: 'bookOpen',
              ai: true,
              onTap: onOpen,
            ),
        ],
      ),
    );
  }
}

class AIInsightCard extends StatelessWidget {
  const AIInsightCard({
    super.key,
    required this.title,
    required this.text,
    required this.confidence,
    required this.sources,
    this.footer,
  });
  final String title;
  final String text;
  final Confidence confidence;
  final List<KnowledgeSource> sources;
  final Widget? footer;
  @override
  Widget build(BuildContext context) => AppCard(
    ai: true,
    semanticLabel: title,
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _head(
          context,
          icon: 'sparkles',
          tone: Tone.accent,
          title: title,
          sub: context.t('ai.draftNotice'),
          trailing: const DemoBadge(),
        ),
        const SizedBox(height: Space.s3),
        Text(text, style: context.text.body),
        const SizedBox(height: Space.s2),
        ConfidenceMeter(level: confidence),
        const SizedBox(height: Space.s2),
        SourceChips(sources: sources),
        if (footer != null) ...[const SizedBox(height: Space.s3), footer!],
      ],
    ),
  );
}

class EvidenceCard extends StatelessWidget {
  const EvidenceCard({super.key, required this.evidence, required this.index});
  final Evidence evidence;
  final int index;
  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    return AppCard(
      semanticLabel: context.loc(evidence.source.title),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _head(
            context,
            icon: 'bookOpen',
            tone: Tone.accent,
            title:
                '[${context.fmt.number(index)}] ${context.loc(evidence.source.title)}',
            sub:
                '${context.loc(evidence.source.type)} · ${context.t('ai.version')} ${evidence.source.version} · ${context.loc(evidence.section)}',
          ),
          const SizedBox(height: Space.s3),
          StripedBox(
            stripe: c.accent,
            fill: c.accentContainer,
            radius: BorderRadius.circular(Radii.sm),
            padding: const EdgeInsetsDirectional.symmetric(
              horizontal: Space.s3,
              vertical: Space.s2,
            ),
            child: Text(
              context.loc(evidence.excerpt),
              style: context.text.bodySmall.copyWith(
                color: c.onAccentContainer,
              ),
            ),
          ),
          const SizedBox(height: Space.s2),
          const DemoBadge(),
        ],
      ),
    );
  }
}
