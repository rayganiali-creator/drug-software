import 'package:flutter/material.dart';

import '../../api/records_client.dart';
import '../../components/buttons.dart';
import '../../components/containers.dart';
import '../../components/display.dart';
import '../../components/health.dart';
import '../../components/inputs.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';
import 'common.dart';

/// "Medicines I take": today's doses with Taken/Skipped, the current list, and adding a medicine by typing its name.
/// A medicine that is not in the reference is kept as typed and clearly marked; it is never guessed or auto-matched.
class TakingScreen extends StatefulWidget {
  const TakingScreen({super.key});
  @override
  State<TakingScreen> createState() => _TakingScreenState();
}

class _TakingScreenState extends State<TakingScreen> {
  int _gen = 0;
  void _refresh() => setState(() => _gen++);

  @override
  Widget build(BuildContext context) {
    final api = context.app.records;
    return ScreenScaffold(
      title: context.t('rec.taking.title'),
      subtitle: context.t('rec.taking.sub'),
      children: [
        RecordsLoader<List<Json>>(
          key: ValueKey('doses$_gen'),
          load: () => api.doses(DateTime.now()),
          builder: (context, slots, _) => RecSection(
            title: context.t('rec.doses.title'),
            subtitle: context.t('rec.doses.sub'),
            child: slots.isEmpty
                ? EmptyState(
                    icon: 'clock',
                    title: context.t('rec.doses.empty'),
                    body: context.t('rec.doses.emptyBody'),
                  )
                : Column(
                    children: [
                      for (final s in slots) _DoseRow(slot: s, onDone: _refresh),
                    ],
                  ),
          ),
        ),
        RecordsLoader<List<Json>>(
          key: ValueKey('meds$_gen'),
          load: api.medications,
          builder: (context, meds, _) => RecSection(
            title: context.t('rec.meds.title'),
            child: meds.isEmpty
                ? EmptyState(
                    icon: 'pill',
                    title: context.t('rec.meds.empty'),
                    body: context.t('rec.meds.emptyBody'),
                  )
                : Column(
                    children: [
                      for (final m in meds) _MedRow(med: m, onChanged: _refresh),
                    ],
                  ),
          ),
        ),
        _AddMedicine(onAdded: _refresh),
      ],
    );
  }
}

class _DoseRow extends StatefulWidget {
  const _DoseRow({required this.slot, required this.onDone});
  final Json slot;
  final VoidCallback onDone;
  @override
  State<_DoseRow> createState() => _DoseRowState();
}

class _DoseRowState extends State<_DoseRow> with ActionRunner {
  @override
  Widget build(BuildContext context) {
    final s = widget.slot;
    final status = s['status'] as String?;
    final time = '${s['localTime']}'.padRight(5).substring(0, 5);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        AppListTile(
          title: '${s['medicationName']}',
          meta: context.fmt.time(time),
          trailing: status == null
              ? null
              : AppBadge(
                  text: context.t('rec.intake.$status'),
                  tone: status == 'Taken' ? Tone.success : Tone.neutral,
                  icon: status == 'Taken' ? 'check' : 'minus',
                ),
        ),
        Padding(
          padding: const EdgeInsetsDirectional.only(
            start: Space.s4,
            bottom: Space.s2,
          ),
          child: Wrap(
            spacing: Space.s2,
            children: [
              AppButton(
                label: context.t('rec.intake.markTaken'),
                size: ButtonSize.sm,
                variant: status == 'Taken'
                    ? ButtonVariant.tonal
                    : ButtonVariant.primary,
                loading: busy,
                onPressed: () =>
                    act(() => context.app.records.logIntake(s, 'Taken'), widget.onDone),
              ),
              AppButton(
                label: context.t('rec.intake.markSkipped'),
                size: ButtonSize.sm,
                variant: ButtonVariant.secondary,
                onPressed: busy
                    ? null
                    : () => act(
                        () => context.app.records.logIntake(s, 'Skipped'),
                        widget.onDone,
                      ),
              ),
            ],
          ),
        ),
        failureAlert(),
      ],
    );
  }
}

class _MedRow extends StatefulWidget {
  const _MedRow({required this.med, required this.onChanged});
  final Json med;
  final VoidCallback onChanged;
  @override
  State<_MedRow> createState() => _MedRowState();
}

class _MedRowState extends State<_MedRow> with ActionRunner {
  @override
  Widget build(BuildContext context) {
    final m = widget.med;
    final registered = m['isRegistered'] == true;
    final n = (m['frequencyValue'] as num?) ?? 0;
    final freq = context.t('rec.freq.${m['frequency']}', {
      'n': context.fmt.number(n),
    });
    final dose = (m['doseText'] as String?) ?? '';
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        AppListTile(
          title: '${m['displayName']}',
          meta: [if (dose.isNotEmpty) dose, freq].join(' · '),
          trailing: Wrap(
            spacing: Space.s2,
            children: [
              AppBadge(
                text: registered
                    ? context.t('rec.meds.registered')
                    : context.t('rec.meds.unregistered'),
                tone: registered ? Tone.info : Tone.warning,
                icon: registered ? 'check' : 'alertTriangle',
              ),
              if (m['referenceIsDemo'] == true) const DemoBadge(),
            ],
          ),
        ),
        if (!registered)
          Padding(
            padding: const EdgeInsetsDirectional.symmetric(horizontal: Space.s4),
            child: Text(
              context.t('rec.meds.unregisteredNote'),
              style: context.text.caption,
            ),
          ),
        Padding(
          padding: const EdgeInsetsDirectional.only(start: Space.s4),
          child: AppButton(
            label: context.t('rec.meds.stop'),
            size: ButtonSize.sm,
            variant: ButtonVariant.ghost,
            loading: busy,
            onPressed: () => act(
              () => context.app.records.stopMedication(
                '${m['id']}',
                (m['version'] as num).toInt(),
              ),
              widget.onChanged,
            ),
          ),
        ),
        failureAlert(),
      ],
    );
  }
}

class _AddMedicine extends StatefulWidget {
  const _AddMedicine({required this.onAdded});
  final VoidCallback onAdded;
  @override
  State<_AddMedicine> createState() => _AddMedicineState();
}

class _AddMedicineState extends State<_AddMedicine> with ActionRunner {
  final _name = TextEditingController();
  final _dose = TextEditingController();
  final _times = TextEditingController(text: '1');
  String _freq = 'TimesPerDay';

  @override
  void dispose() {
    _name.dispose();
    _dose.dispose();
    _times.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AppCard(
    title: context.t('rec.add.title'),
    subtitle: context.t('rec.add.sub'),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        AppTextField(
          label: context.t('rec.add.typedName'),
          hint: context.t('rec.add.typedHint'),
          controller: _name,
        ),
        const SizedBox(height: Space.s3),
        AppTextField(label: context.t('rec.add.dose'), controller: _dose),
        const SizedBox(height: Space.s3),
        AppSelect<String>(
          label: context.t('rec.add.frequency'),
          value: _freq,
          items: {
            for (final f in ['TimesPerDay', 'EveryNHours', 'AsNeeded', 'Other'])
              f: context.t('rec.freqName.$f'),
          },
          onChanged: (v) => setState(() => _freq = v),
        ),
        if (_freq == 'TimesPerDay' || _freq == 'EveryNHours') ...[
          const SizedBox(height: Space.s3),
          AppTextField(
            label: context.t('rec.add.times'),
            controller: _times,
            keyboardType: TextInputType.number,
          ),
        ],
        const SizedBox(height: Space.s3),
        AppButton(
          label: context.t('rec.add'),
          iconStart: 'plus',
          loading: busy,
          onPressed: () {
            final name = _name.text.trim();
            if (name.isEmpty) return;
            act(
              () => context.app.records.addMedication(
                name: name,
                dose: _dose.text.trim().isEmpty ? null : _dose.text.trim(),
                frequency: _freq,
                frequencyValue: int.tryParse(_times.text.trim()),
              ),
              () {
                _name.clear();
                _dose.clear();
                widget.onAdded();
              },
            );
          },
        ),
        failureAlert(),
      ],
    ),
  );
}
