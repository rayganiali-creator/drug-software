import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

import '../../components/app_icon.dart';
import '../../components/buttons.dart';
import '../../components/containers.dart';
import '../../components/display.dart';
import '../../components/inputs.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../core/models.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';

const _moodIcons = ['moodBad', 'moodLow', 'moodOk', 'moodGood', 'moodGreat'];

class CheckInScreen extends StatefulWidget {
  const CheckInScreen({super.key});
  @override
  State<CheckInScreen> createState() => _CheckInScreenState();
}

class _CheckInScreenState extends State<CheckInScreen> {
  int _step = 0;
  int? _mood;
  final Set<String> _symptoms = {};
  final _note = TextEditingController();
  bool _busy = false;
  bool? _urgent; // non-null once submitted

  @override
  void dispose() {
    _note.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() => _busy = true);
    final urgent = await context.services.patients.submitCheckIn(
      mood: _mood ?? 3,
      symptomIds: _symptoms.toList(),
      note: _note.text,
    );
    if (!mounted) return;
    HapticFeedback.lightImpact();
    setState(() {
      _busy = false;
      _urgent = urgent;
    });
    showAppToast(context, context.t('checkin.saved'), tone: Tone.success);
  }

  @override
  Widget build(BuildContext context) {
    return ScreenScaffold(
      title: context.t('nav.checkin'),
      subtitle: context.t('checkin.sub'),
      children: [
        AsyncView<(CheckInConfig, bool)>(
          load: () async {
            final s = context.services;
            return (
              await s.patients.checkInConfig(),
              await s.patients.hasCheckedInToday(),
            );
          },
          builder: (context, d, _) {
            final cfg = d.$1;
            if (_urgent != null || d.$2) {
              return _done(context, urgent: _urgent ?? false);
            }
            return _form(context, cfg);
          },
        ),
      ],
    );
  }

  Widget _done(BuildContext context, {required bool urgent}) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      AppCard(
        tone: Tone.success,
        child: EmptyState(
          icon: 'checkCircle',
          title: context.t('checkin.doneTitle'),
          body: context.t('checkin.doneBody'),
          action: AppButton(
            label: context.t('checkin.backHome'),
            onPressed: () => context.go('/home'),
          ),
        ),
      ),
      if (urgent) ...[
        const SizedBox(height: Space.s4),
        AlertCard(
          tone: Tone.danger,
          title: context.t('checkin.urgentTitle'),
          body: context.t('checkin.urgentBody'),
          actions: [
            AppButton(
              label: context.t('checkin.urgentAction'),
              variant: ButtonVariant.danger,
              iconStart: 'phone',
              onPressed: () =>
                  showAppToast(context, context.t('pharm.callPrototype')),
            ),
          ],
        ),
      ],
    ],
  );

  Widget _form(BuildContext context, CheckInConfig cfg) {
    final steps = [
      context.t('checkin.stepMood'),
      context.t('checkin.stepSymptoms'),
      context.t('checkin.stepNote'),
    ];
    final urgentSelected = _symptoms.any(
      (id) => cfg.symptoms.firstWhere((s) => s.id == id).urgent,
    );
    return AppCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Semantics(
                  header: true,
                  child: Text(steps[_step], style: context.text.h4),
                ),
              ),
              Text(
                context.t('checkin.step', {
                  'n': context.fmt.number(_step + 1),
                  'total': context.fmt.number(steps.length),
                }),
                style: context.text.caption,
              ),
            ],
          ),
          const SizedBox(height: Space.s2),
          AppProgress(
            value: (_step + 1) / steps.length * 100,
            label: context.t('checkin.progress'),
          ),
          const SizedBox(height: Space.s5),
          if (_step == 0)
            Semantics(
              container: true,
              label: context.t('checkin.moodQuestion'),
              child: Row(
                children: [
                  for (final m in cfg.moods)
                    Expanded(
                      child: Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 3),
                        child: Semantics(
                          inMutuallyExclusiveGroup: true,
                          checked: _mood == m.value,
                          label: context.loc(m.label),
                          excludeSemantics: true,
                          onTap: () => setState(() => _mood = m.value),
                          child: InkWell(
                            borderRadius: BorderRadius.circular(Radii.lg),
                            onTap: () => setState(() => _mood = m.value),
                            child: Container(
                              constraints: const BoxConstraints(minHeight: 84),
                              padding: const EdgeInsets.symmetric(
                                vertical: Space.s3,
                                horizontal: 2,
                              ),
                              decoration: BoxDecoration(
                                color: _mood == m.value
                                    ? context.colors.primaryContainer
                                    : context.colors.surface,
                                border: Border.all(
                                  color: _mood == m.value
                                      ? context.colors.primary
                                      : context.colors.border,
                                  width: 2,
                                ),
                                borderRadius: BorderRadius.circular(Radii.lg),
                              ),
                              child: Column(
                                mainAxisAlignment: MainAxisAlignment.center,
                                children: [
                                  AppIcon(
                                    _moodIcons[m.value - 1],
                                    size: IconSizes.lg,
                                    color: _mood == m.value
                                        ? context.colors.onPrimaryContainer
                                        : context.colors.textSecondary,
                                  ),
                                  const SizedBox(height: 4),
                                  Text(
                                    context.loc(m.label),
                                    textAlign: TextAlign.center,
                                    style: context.text.caption.copyWith(
                                      color: _mood == m.value
                                          ? context.colors.onPrimaryContainer
                                          : context.colors.textSecondary,
                                      fontWeight: _mood == m.value
                                          ? FontWeight.w700
                                          : FontWeight.w400,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ),
                      ),
                    ),
                ],
              ),
            ),
          if (_step == 1) ...[
            Text(
              context.t('checkin.symptomQuestion'),
              style: context.text.body,
            ),
            const SizedBox(height: Space.s3),
            Wrap(
              spacing: Space.s2,
              children: [
                for (final s in cfg.symptoms)
                  AppChip(
                    label: context.loc(s.label),
                    selected: _symptoms.contains(s.id),
                    onTap: () => setState(
                      () => _symptoms.contains(s.id)
                          ? _symptoms.remove(s.id)
                          : _symptoms.add(s.id),
                    ),
                  ),
              ],
            ),
            if (urgentSelected) ...[
              const SizedBox(height: Space.s3),
              AlertCard(
                tone: Tone.danger,
                title: context.t('checkin.urgentTitle'),
                body: context.t('checkin.urgentBody'),
              ),
            ],
          ],
          if (_step == 2) ...[
            AppTextField(
              label: context.t('checkin.noteLabel'),
              hint: context.t('checkin.noteHint'),
              controller: _note,
              minLines: 3,
              maxLines: 5,
            ),
            const SizedBox(height: Space.s4),
            AppCard(
              tone: Tone.neutral,
              title: context.t('checkin.summary'),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    '${context.t('checkin.stepMood')} ${_mood == null ? '-' : context.loc(cfg.moods.firstWhere((m) => m.value == _mood).label)}',
                    style: context.text.bodySmall,
                  ),
                  Text(
                    '${context.t('checkin.stepSymptoms')} ${_symptoms.isEmpty ? context.t('checkin.none') : _symptoms.map((id) => context.loc(cfg.symptoms.firstWhere((s) => s.id == id).label)).join('، ')}',
                    style: context.text.bodySmall,
                  ),
                ],
              ),
            ),
            const SizedBox(height: Space.s2),
            Text(
              context.t('checkin.prototypeNote'),
              style: context.text.caption,
            ),
          ],
          const SizedBox(height: Space.s5),
          Wrap(
            alignment: WrapAlignment.spaceBetween,
            runSpacing: Space.s2,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              AppButton(
                label: context.t('common.back'),
                variant: ButtonVariant.ghost,
                iconStart: 'chevronLeft',
                onPressed: _step == 0 ? null : () => setState(() => _step--),
              ),
              if (_step < 2)
                AppButton(
                  label: context.t('common.next'),
                  iconEnd: 'chevronRight',
                  onPressed: _step == 0 && _mood == null
                      ? null
                      : () => setState(() => _step++),
                )
              else
                AppButton(
                  label: context.t('checkin.submit'),
                  iconStart: 'check',
                  loading: _busy,
                  onPressed: _submit,
                ),
            ],
          ),
        ],
      ),
    );
  }
}
