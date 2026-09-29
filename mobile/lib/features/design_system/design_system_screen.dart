import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../components/app_icon.dart';
import '../../components/buttons.dart';
import '../../components/chat.dart';
import '../../components/containers.dart';
import '../../components/display.dart';
import '../../components/health.dart';
import '../../components/inputs.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../core/models.dart';
import '../../design/icons.g.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';

/// Developer gallery: every token and component in the current theme/language.
class DesignSystemScreen extends StatefulWidget {
  const DesignSystemScreen({super.key});
  @override
  State<DesignSystemScreen> createState() => _DesignSystemScreenState();
}

class _DesignSystemScreenState extends State<DesignSystemScreen> {
  String _seg = 'a';
  String _radio = 'x';
  bool _sw = true;
  bool _check = true;
  String _select = '1';
  final _search = TextEditingController();
  final _composer = TextEditingController();

  @override
  void dispose() {
    _search.dispose();
    _composer.dispose();
    super.dispose();
  }

  Widget _section(String title, List<Widget> children) => Padding(
    padding: const EdgeInsets.only(top: Space.s8),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Semantics(header: true, child: Text(title, style: context.text.h3)),
        const SizedBox(height: Space.s3),
        ...children.expand((w) => [w, const SizedBox(height: Space.s3)]),
      ],
    ),
  );

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final swatches = <String, Color>{
      'primary': c.primary,
      'secondary': c.secondary,
      'accent (AI)': c.accent,
      'success': c.success,
      'warning': c.warning,
      'danger': c.danger,
      'info': c.info,
      'background': c.background,
      'surface': c.surface,
      'surfaceSunken': c.surfaceSunken,
      'border': c.border,
      'textPrimary': c.textPrimary,
      'textSecondary': c.textSecondary,
      'textMuted': c.textMuted,
    };
    final type = {
      'display': context.text.display,
      'h1': context.text.h1,
      'h2': context.text.h2,
      'h3': context.text.h3,
      'h4': context.text.h4,
      'body': context.text.body,
      'bodySmall': context.text.bodySmall,
      'caption': context.text.caption,
      'label': context.text.label,
      'button': context.text.button,
    };
    return Scaffold(
      body: ScreenScaffold(
        title: context.t('nav.designSystem'),
        subtitle: context.t('ds.intro'),
        leading: AppIconButton(
          icon: 'chevronLeft',
          label: context.t('common.back'),
          onPressed: () => context.pop(),
        ),
        children: [
          _section(context.t('ds.colors'), [
            Wrap(
              spacing: Space.s3,
              runSpacing: Space.s3,
              children: [
                for (final e in swatches.entries)
                  SizedBox(
                    width: 104,
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Container(
                          height: 44,
                          decoration: BoxDecoration(
                            color: e.value,
                            borderRadius: BorderRadius.circular(Radii.md),
                            border: Border.all(color: c.border),
                          ),
                        ),
                        Text(
                          e.key,
                          style: context.text.caption.copyWith(
                            color: c.textPrimary,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ],
                    ),
                  ),
              ],
            ),
          ]),
          _section(context.t('ds.typography'), [
            AppCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  for (final e in type.entries)
                    Padding(
                      padding: const EdgeInsets.symmetric(vertical: 4),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.baseline,
                        textBaseline: TextBaseline.alphabetic,
                        children: [
                          SizedBox(
                            width: 84,
                            child: Text(e.key, style: context.text.caption),
                          ),
                          Expanded(
                            child: Text(
                              context.t('ds.sampleText'),
                              style: e.value,
                            ),
                          ),
                        ],
                      ),
                    ),
                ],
              ),
            ),
          ]),
          _section(context.t('ds.spacingRadius'), [
            Wrap(
              spacing: Space.s3,
              runSpacing: Space.s3,
              crossAxisAlignment: WrapCrossAlignment.end,
              children: [
                for (final s in [
                  Space.s1,
                  Space.s2,
                  Space.s3,
                  Space.s4,
                  Space.s6,
                  Space.s8,
                  Space.s12,
                ])
                  Column(
                    children: [
                      Container(width: s, height: s, color: c.primary),
                      Text('${s.round()}', style: context.text.caption),
                    ],
                  ),
                for (final r in [
                  Radii.xs,
                  Radii.sm,
                  Radii.md,
                  Radii.lg,
                  Radii.xl,
                ])
                  Container(
                    width: 48,
                    height: 48,
                    decoration: BoxDecoration(
                      color: c.primaryContainer,
                      border: Border.all(color: c.primary, width: 2),
                      borderRadius: BorderRadius.circular(r),
                    ),
                  ),
              ],
            ),
            Wrap(
              spacing: Space.s3,
              runSpacing: Space.s3,
              children: [
                for (final e in [1, 2, 3, 4])
                  Container(
                    width: 96,
                    height: 64,
                    decoration: BoxDecoration(
                      color: c.surfaceElevated,
                      borderRadius: BorderRadius.circular(Radii.lg),
                      boxShadow: context.elevation(e),
                    ),
                    alignment: Alignment.center,
                    child: Text('$e', style: context.text.caption),
                  ),
              ],
            ),
          ]),
          _section(context.t('ds.icons'), [
            Wrap(
              spacing: Space.s3,
              runSpacing: Space.s3,
              children: [
                for (final n in appIconPaths.keys.take(48))
                  Tooltip(
                    message: n,
                    child: Container(
                      width: 44,
                      height: 44,
                      decoration: BoxDecoration(
                        color: c.surface,
                        border: Border.all(color: c.border),
                        borderRadius: BorderRadius.circular(Radii.md),
                      ),
                      child: Center(child: AppIcon(n, color: c.textPrimary)),
                    ),
                  ),
              ],
            ),
          ]),
          _section(context.t('ds.actions'), [
            Wrap(
              spacing: Space.s2,
              runSpacing: Space.s2,
              children: [
                AppButton(label: context.t('ds.primary'), onPressed: () {}),
                AppButton(
                  label: context.t('ds.secondary'),
                  variant: ButtonVariant.secondary,
                  onPressed: () {},
                ),
                AppButton(
                  label: context.t('ds.tonal'),
                  variant: ButtonVariant.tonal,
                  onPressed: () {},
                ),
                AppButton(
                  label: context.t('ds.ai'),
                  variant: ButtonVariant.ai,
                  iconStart: 'sparkles',
                  onPressed: () {},
                ),
                AppButton(
                  label: context.t('ds.ghost'),
                  variant: ButtonVariant.ghost,
                  onPressed: () {},
                ),
                AppButton(
                  label: context.t('ds.danger'),
                  variant: ButtonVariant.danger,
                  onPressed: () {},
                ),
                AppButton(
                  label: context.t('common.loading'),
                  loading: true,
                  onPressed: () {},
                ),
                AppButton(label: context.t('ds.disabled'), onPressed: null),
              ],
            ),
            Wrap(
              spacing: Space.s2,
              runSpacing: Space.s2,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                AppButton(
                  label: context.t('ds.small'),
                  size: ButtonSize.sm,
                  onPressed: () {},
                ),
                AppButton(
                  label: context.t('ds.large'),
                  size: ButtonSize.lg,
                  onPressed: () {},
                ),
                AppIconButton(
                  icon: 'plus',
                  label: context.t('ds.add'),
                  onPressed: () {},
                ),
                AppIconButton(
                  icon: 'send',
                  label: context.t('ai.send'),
                  filled: true,
                  onPressed: () {},
                ),
                AppBadgeAnchor(
                  count: 3,
                  label: '3',
                  child: AppIconButton(
                    icon: 'bell',
                    label: context.t('notif.label'),
                    onPressed: () {},
                  ),
                ),
                Tooltip(
                  message: context.t('ds.tooltip'),
                  child: const AppIcon('info'),
                ),
              ],
            ),
          ]),
          _section(context.t('ds.inputs'), [
            AppTextField(
              label: context.t('ds.textField'),
              hint: context.t('ds.hint'),
            ),
            AppTextField(
              label: context.t('ds.textField'),
              error: context.t('ds.error'),
            ),
            AppSearchField(
              label: context.t('search.label'),
              controller: _search,
            ),
            AppSelect<String>(
              label: context.t('ds.select'),
              value: _select,
              items: {
                '1': context.t('ds.option', {'n': 1}),
                '2': context.t('ds.option', {'n': 2}),
              },
              onChanged: (v) => setState(() => _select = v),
            ),
            AppCheckbox(
              label: context.t('ds.checkbox'),
              value: _check,
              onChanged: (v) => setState(() => _check = v),
            ),
            AppRadioGroup<String>(
              legend: context.t('ds.radio'),
              value: _radio,
              options: {
                'x': context.t('ds.option', {'n': 1}),
                'y': context.t('ds.option', {'n': 2}),
              },
              onChanged: (v) => setState(() => _radio = v),
            ),
            AppSwitch(
              label: context.t('ds.switch'),
              value: _sw,
              onChanged: (v) => setState(() => _sw = v),
            ),
          ]),
          _section(context.t('ds.navigation'), [
            AppSegmented<String>(
              label: context.t('ds.segmented'),
              value: _seg,
              options: {
                'a': context.t('ds.option', {'n': 1}),
                'b': context.t('ds.option', {'n': 2}),
                'c': context.t('ds.option', {'n': 3}),
              },
              onChanged: (v) => setState(() => _seg = v),
            ),
            AppTabs(
              tabs: [
                (
                  context.t('ds.option', {'n': 1}),
                  Text(context.t('ds.sampleText')),
                ),
                (
                  context.t('ds.option', {'n': 2}),
                  Text('${context.t('ds.sampleText')} 2'),
                ),
              ],
            ),
          ]),
          _section(context.t('ds.feedback'), [
            AlertCard(
              tone: Tone.info,
              title: context.t('ds.alertInfo'),
              body: context.t('ds.sampleText'),
            ),
            AlertCard(
              tone: Tone.success,
              title: context.t('ds.alertSuccess'),
              body: context.t('ds.sampleText'),
            ),
            AlertCard(
              tone: Tone.warning,
              title: context.t('ds.alertWarning'),
              body: context.t('ds.sampleText'),
            ),
            AlertCard(
              tone: Tone.danger,
              title: context.t('ds.alertDanger'),
              body: context.t('ds.sampleText'),
            ),
            Wrap(
              spacing: Space.s2,
              runSpacing: Space.s2,
              children: [
                AppButton(
                  label: context.t('ds.showToast'),
                  variant: ButtonVariant.secondary,
                  onPressed: () => showAppToast(
                    context,
                    context.t('ds.toast'),
                    tone: Tone.success,
                  ),
                ),
                AppButton(
                  label: context.t('ds.showSnackbar'),
                  variant: ButtonVariant.secondary,
                  onPressed: () => showAppToast(
                    context,
                    context.t('ds.snackbar'),
                    actionLabel: context.t('common.undo'),
                    onAction: () {},
                  ),
                ),
                AppButton(
                  label: context.t('ds.modal'),
                  variant: ButtonVariant.secondary,
                  onPressed: () => showAppDialog<void>(
                    context,
                    title: context.t('ds.modal'),
                    closeLabel: context.t('common.close'),
                    body: Text(context.t('ds.sampleText')),
                  ),
                ),
                AppButton(
                  label: context.t('ds.bottomSheet'),
                  variant: ButtonVariant.secondary,
                  onPressed: () => showAppBottomSheet<void>(
                    context,
                    title: context.t('ds.bottomSheet'),
                    closeLabel: context.t('common.close'),
                    body: Text(context.t('ds.sampleText')),
                  ),
                ),
              ],
            ),
            Wrap(
              spacing: Space.s2,
              runSpacing: Space.s2,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                AppBadge(text: context.t('ds.badge'), tone: Tone.primary),
                AppBadge(
                  text: context.t('ds.badge'),
                  tone: Tone.success,
                  icon: 'check',
                ),
                AppBadge(
                  text: context.t('ds.badge'),
                  tone: Tone.danger,
                  solid: true,
                ),
                AppChip(label: context.t('ds.chip')),
                AppChip(
                  label: context.t('ds.chip'),
                  selected: true,
                  onTap: () {},
                ),
                AppChip(
                  label: context.t('ds.chip'),
                  ai: true,
                  icon: 'bookOpen',
                ),
                const AppAvatar(name: 'Sara Ahmadi'),
              ],
            ),
            AppProgress(value: 64, label: context.t('ds.progress')),
            Row(
              children: [
                ProgressRing(
                  value: 72,
                  label: context.t('ds.progress'),
                  size: 88,
                  child: Text(context.fmt.percent(72), style: context.text.h4),
                ),
                const SizedBox(width: Space.s4),
                Sparkline(
                  values: const [3, 6, 4, 8, 7, 9],
                  label: context.t('ds.sparkline'),
                  width: 120,
                  height: 40,
                ),
              ],
            ),
            const Row(
              children: [
                Expanded(child: Skeleton(height: 20)),
                SizedBox(width: Space.s3),
                Expanded(child: Skeleton(height: 20)),
              ],
            ),
            AppCard(
              child: EmptyState(
                icon: 'inbox',
                title: context.t('common.noResults'),
                body: context.t('common.noResultsBody'),
              ),
            ),
            AppCard(
              child: ErrorState(
                title: context.t('common.errorTitle'),
                body: context.t('common.errorBody'),
                retryLabel: context.t('common.retry'),
                onRetry: () {},
              ),
            ),
          ]),
          _section(context.t('ds.data'), [
            AppCard(
              title: context.t('ds.timeline'),
              child: AppTimeline(
                entries: [
                  TimelineEntry(
                    title: context.t('dose.taken'),
                    meta: context.fmt.time('08:00'),
                    icon: 'check',
                    tone: Tone.success,
                  ),
                  TimelineEntry(
                    title: context.t('med.nextDose'),
                    meta: context.fmt.time('20:00'),
                    icon: 'clock',
                    current: true,
                  ),
                  TimelineEntry(
                    title: context.t('dose.upcoming'),
                    meta: context.fmt.time('22:30'),
                    icon: 'clock',
                  ),
                ],
              ),
            ),
            StatCard(
              icon: 'activity',
              label: context.t('phys.statAdherence'),
              value: context.fmt.percent(84),
            ),
            ChartCard(
              title: context.t('ds.lineChart'),
              series: [
                ChartSeries('A', const [12, 18, 15, 24, 22, 30], c.chart1),
                ChartSeries('B', const [8, 10, 14, 12, 18, 16], c.chart2),
              ],
              child: LineChart(
                series: [
                  ChartSeries('A', const [12, 18, 15, 24, 22, 30], c.chart1),
                  ChartSeries('B', const [8, 10, 14, 12, 18, 16], c.chart2),
                ],
                xLabels: [
                  '1',
                  '2',
                  '3',
                  '4',
                  '5',
                  '6',
                ].map((e) => context.fmt.digits(e)).toList(),
                summary: context.t('ds.lineChart'),
              ),
            ),
          ]),
          _section(context.t('ds.chat'), [
            AppCard(
              child: Column(
                children: [
                  ChatBubble(
                    text: context.t('lp.sampleQuestion'),
                    isUser: true,
                  ),
                  const SizedBox(height: Space.s3),
                  ChatBubble(text: context.t('lp.sampleAnswer'), isUser: false),
                  const SizedBox(height: Space.s3),
                  MessageComposer(
                    controller: _composer,
                    onSend: () {},
                    onVoice: () {},
                    onImage: () {},
                    recording: false,
                    enabled: true,
                    labels: ComposerLabels(
                      input: context.t('ai.input'),
                      placeholder: context.t('ai.placeholder'),
                      send: context.t('ai.send'),
                      voice: context.t('ai.voice'),
                      stopVoice: context.t('ai.stopVoice'),
                      image: context.t('ai.image'),
                      removeAttachment: context.t('ai.removeAttachment'),
                      prototypeNote: context.t('ai.imageNote'),
                    ),
                  ),
                ],
              ),
            ),
          ]),
          _section(context.t('ds.healthcare'), [
            AsyncView<_Sample>(
              load: () async {
                final s = context.services;
                final id = context.app.data.currentPatientId;
                final meds = await s.medications.forPatient(id);
                final doses = await s.medications.todaysDoses(id);
                final rx = await s.prescriptions.find();
                final patients = await s.patients.all();
                final adr = await s.adr.reports();
                final ix = await s.prescriptions.interactionsForPatient(id);
                final convs = await s.ai.conversations();
                return _Sample(
                  meds,
                  doses,
                  rx,
                  patients,
                  adr,
                  ix,
                  convs.first.messages.where((m) => !m.isUser).first.answer!,
                );
              },
              builder: (context, d, _) => ResponsiveColumns(
                minColumnWidth: 340,
                children: [
                  MedicationCard(
                    med: d.meds.first,
                    dose: d.doses.where((x) => x.isNext).firstOrNull,
                  ),
                  DrugCard(drug: d.meds[1].drug),
                  PrescriptionCard(rx: d.rx.first, showPatient: true),
                  PatientCard(patient: d.patients[1]),
                  ADRCard(report: d.adr.first, showPatient: true),
                  RiskCard(
                    patient: d.patients[3],
                    reasons: [
                      context.t('phys.reasonAdherence', {
                        'n': context.fmt.percent(54),
                      }),
                      context.t('phys.reasonSymptom'),
                    ],
                  ),
                  if (d.ix.isNotEmpty) InteractionCard(interaction: d.ix.first),
                  AdherenceCard(
                    value: 86,
                    series: d.patients.first.adherenceSeries,
                  ),
                  CheckInCard(done: false, onStart: () {}),
                  AIInsightCard(
                    title: context.t('phys.aiSummary'),
                    text: context.loc(d.answer.text),
                    confidence: d.answer.confidence,
                    sources: d.answer.sources,
                  ),
                  EvidenceCard(evidence: d.answer.evidence.first, index: 1),
                ],
              ),
            ),
          ]),
        ],
      ),
    );
  }
}

class _Sample {
  const _Sample(
    this.meds,
    this.doses,
    this.rx,
    this.patients,
    this.adr,
    this.ix,
    this.answer,
  );
  final List<PatientMedication> meds;
  final List<Dose> doses;
  final List<Prescription> rx;
  final List<Patient> patients;
  final List<AdverseReport> adr;
  final List<Interaction> ix;
  final AssistantAnswer answer;
}
