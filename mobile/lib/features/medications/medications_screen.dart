import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../components/buttons.dart';
import '../../components/containers.dart';
import '../../components/display.dart';
import '../../components/health.dart';
import '../../components/inputs.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../core/models.dart';
import '../../design/tokens.g.dart';

class MedicationsScreen extends StatefulWidget {
  const MedicationsScreen({super.key});
  @override
  State<MedicationsScreen> createState() => _MedicationsScreenState();
}

class _MedicationsScreenState extends State<MedicationsScreen> {
  String _filter = 'all';

  @override
  Widget build(BuildContext context) {
    final id = context.app.data.currentPatientId;
    return ScreenScaffold(
      title: context.t('nav.medications'),
      subtitle: context.t('med.pageSub'),
      children: [
        Align(
          alignment: AlignmentDirectional.centerStart,
          child: AppSegmented<String>(
            label: context.t('med.filter'),
            value: _filter,
            options: {
              'all': context.t('med.filterAll'),
              'today': context.t('med.filterToday'),
            },
            onChanged: (v) => setState(() => _filter = v),
          ),
        ),
        const SizedBox(height: Space.s3),
        AppButton(
          label: context.t('kn.title'),
          variant: ButtonVariant.tonal,
          iconStart: 'search',
          onPressed: () => context.push('/medications/reference'),
        ),
        const SizedBox(height: Space.s4),
        AsyncView<(List<PatientMedication>, List<Dose>)>(
          load: () async {
            final s = context.services;
            return (
              await s.medications.forPatient(id),
              await s.medications.todaysDoses(id),
            );
          },
          builder: (context, d, _) {
            final today = d.$2
                .where((x) => !x.tomorrow)
                .map((x) => x.drug.id)
                .toSet();
            final list = d.$1
                .where((m) => _filter == 'all' || today.contains(m.drug.id))
                .toList();
            if (list.isEmpty) {
              return EmptyState(
                icon: 'pill',
                title: context.t('med.emptyTitle'),
                body: context.t('med.emptyBody'),
              );
            }
            return ResponsiveColumns(
              minColumnWidth: 360,
              children: [
                for (final m in list)
                  MedicationCard(
                    med: m,
                    dose:
                        d.$2
                            .where(
                              (x) =>
                                  x.drug.id == m.drug.id &&
                                  (x.isNext || x.status != DoseStatus.taken),
                            )
                            .firstOrNull ??
                        d.$2.where((x) => x.drug.id == m.drug.id).firstOrNull,
                    onTap: () => context.push('/medications/${m.drug.id}'),
                  ),
              ],
            );
          },
        ),
        const SizedBox(height: Space.s6),
        Text(
          context.t('demo.footnote'),
          style: TextStyle(color: context.tone(Tone.neutral).fg, fontSize: 13),
        ),
      ],
    );
  }
}

class MedicationDetailScreen extends StatelessWidget {
  const MedicationDetailScreen({super.key, required this.drugId});
  final String drugId;

  @override
  Widget build(BuildContext context) {
    final id = context.app.data.currentPatientId;
    return Scaffold(
      body: SafeArea(
        bottom: false,
        child:
            AsyncView<(List<PatientMedication>, List<Dose>, List<Interaction>)>(
              load: () async {
                final s = context.services;
                return (
                  await s.medications.forPatient(id),
                  await s.medications.todaysDoses(id),
                  await s.prescriptions.interactionsForPatient(id),
                );
              },
              builder: (context, d, _) {
                final med = d.$1.where((m) => m.drug.id == drugId).firstOrNull;
                if (med == null) {
                  return ScreenScaffold(
                    title: context.t('med.notFound'),
                    leading: AppIconButton(
                      icon: 'chevronLeft',
                      label: context.t('common.back'),
                      onPressed: () => context.pop(),
                    ),
                    children: [
                      EmptyState(
                        icon: 'pill',
                        title: context.t('med.notFound'),
                      ),
                    ],
                  );
                }
                final dose =
                    d.$2
                        .where((x) => x.drug.id == drugId && x.isNext)
                        .firstOrNull ??
                    d.$2.where((x) => x.drug.id == drugId).firstOrNull;
                final related = d.$3
                    .where((x) => x.drugA.id == drugId || x.drugB.id == drugId)
                    .toList();
                return ScreenScaffold(
                  title: context.loc(med.drug.name),
                  subtitle: context.loc(med.drug.category),
                  leading: AppIconButton(
                    icon: 'chevronLeft',
                    label: context.t('common.back'),
                    onPressed: () => context.pop(),
                  ),
                  children: [
                    MedicationCard(med: med, dose: dose, detail: true),
                    const SizedBox(height: Space.s4),
                    AppButton(
                      label: context.t('med.askAbout'),
                      variant: ButtonVariant.ai,
                      iconStart: 'sparkles',
                      block: true,
                      onPressed: () => context.go('/assistant?drug=$drugId'),
                    ),
                    if (related.isNotEmpty) ...[
                      const SizedBox(height: Space.s4),
                      AlertCard(
                        tone: Tone.info,
                        title: context.t('med.pharmacistReview'),
                        body: context.t('med.pharmacistReviewBody'),
                      ),
                      for (final x in related)
                        Padding(
                          padding: const EdgeInsets.only(top: Space.s3),
                          child: InteractionCard(interaction: x),
                        ),
                    ],
                    const SizedBox(height: Space.s4),
                    AppCard(
                      title: context.t('med.sourceTitle'),
                      subtitle: context.t('med.sourceSub'),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          SourceChips(
                            sources: [for (final x in related) x.source],
                          ),
                          const SizedBox(height: Space.s2),
                          Text(
                            context.t('demo.footnote'),
                            style: TextStyle(
                              color: context.tone(Tone.neutral).fg,
                              fontSize: 13,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                );
              },
            ),
      ),
    );
  }
}
