import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../api/records_client.dart';
import '../../components/app_icon.dart';
import '../../components/buttons.dart';
import '../../components/containers.dart';
import '../../components/display.dart';
import '../../components/inputs.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';
import 'common.dart';

/// The patient's own adverse-event reports. These are INTERNAL records for a professional to review: not a submitted report, not proof that
/// a medicine caused anything. Dates left empty stay unknown; a medicine is a name as given, never matched or guessed.
class AdverseEventsScreen extends StatefulWidget {
  const AdverseEventsScreen({super.key});
  @override
  State<AdverseEventsScreen> createState() => _AdverseEventsScreenState();
}

class _AdverseEventsScreenState extends State<AdverseEventsScreen>
    with ActionRunner {
  final _what = TextEditingController();
  final _dose = TextEditingController();
  final _medicine = TextEditingController();
  final _medicines = <String>[];
  DateTime? _onset;
  String _requestId = RecordsClient.newRequestId();
  int _gen = 0;

  @override
  void dispose() {
    _what.dispose();
    _dose.dispose();
    _medicine.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final body = <String, dynamic>{
      'eventDescription': _what.text.trim(),
      'onsetAt': _onset == null
          ? null
          : DateTime(
              _onset!.year,
              _onset!.month,
              _onset!.day,
            ).toUtc().toIso8601String(),
      'resolvedAt': null,
      'suspectedMedications': [
        for (final m in _medicines)
          {'patientMedicationId': null, 'nameAsReported': m},
      ],
      'outcomeAsStated': null,
      'additionalDetails': null,
      'doseAndAdministrationAsReported': _dose.text.trim().isEmpty
          ? null
          : _dose.text.trim(),
      'concomitantMedications': <Object>[],
      'clientRequestId': _requestId,
    };
    final ok = await act(() => context.app.records.createAdverseEvent(body));
    if (ok && mounted) {
      setState(() {
        _what.clear();
        _dose.clear();
        _medicines.clear();
        _onset = null;
        _requestId = RecordsClient.newRequestId();
        _gen++;
      });
    }
  }

  @override
  Widget build(BuildContext context) => ScreenScaffold(
    title: context.t('adr.title.patient'),
    subtitle: context.t('adr.sub.patient'),
    children: [
      AlertCard(
        title: context.t('adr.signal'),
        body: context.t('adr.internal'),
        tone: Tone.info,
      ),
      if (context.app.locale.languageCode == 'fa')
        AlertCard(
          title: context.t('sf.fa.unreviewed.title'),
          body: context.t('sf.fa.unreviewed.body'),
          tone: Tone.info,
        ),
      const SizedBox(height: Space.s3),
      AlertCard(title: context.t('adr.emergency'), tone: Tone.warning),
      const SizedBox(height: Space.s3),
      RecordsLoader<List<Json>>(
        key: ValueKey('adr$_gen'),
        load: context.app.records.adverseEvents,
        builder: (context, rows, _) => rows.isEmpty
            ? EmptyState(
                icon: 'inbox',
                title: context.t('adr.empty'),
                body: context.t('adr.emptyBody'),
              )
            : Column(
                children: [
                  for (final r in rows)
                    Padding(
                      padding: const EdgeInsets.only(bottom: Space.s3),
                      child: AppCard(
                        title: '${r['excerpt']}',
                        actions: AppBadge(
                          text: context.t('adr.status.${r['status']}'),
                          tone: Tone.info,
                        ),
                        child: AppListTile(
                          title: context.t('adr.open'),
                          trailing: const AppIcon('chevronRight'),
                          onTap: () => context.push(
                            '/profile/adverse-events/${r['id']}',
                          ),
                        ),
                      ),
                    ),
                ],
              ),
      ),
      AppCard(
        title: context.t('adr.form.formTitle'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            AppTextField(
              label: context.t('adr.form.what'),
              controller: _what,
              minLines: 3,
              maxLines: 6,
              onChanged: (_) => setState(() {}),
            ),
            const SizedBox(height: Space.s3),
            AppButton(
              label: _onset == null
                  ? context.t('adr.form.when')
                  : '${_onset!.year}-${_onset!.month.toString().padLeft(2, '0')}-${_onset!.day.toString().padLeft(2, '0')}',
              variant: ButtonVariant.secondary,
              onPressed: () async {
                final now = DateTime.now();
                final d = await showDatePicker(
                  context: context,
                  initialDate: now,
                  firstDate: DateTime(now.year - 10),
                  lastDate: now,
                );
                if (d != null && mounted) setState(() => _onset = d);
              },
            ),
            Text(
              context.t('adr.form.unknownHint'),
              style: context.text.caption,
            ),
            const SizedBox(height: Space.s3),
            AppTextField(label: context.t('adr.form.dose'), controller: _dose),
            const SizedBox(height: Space.s3),
            Text(context.t('adr.form.meds'), style: context.text.label),
            Text(context.t('adr.form.medsHint'), style: context.text.caption),
            for (final m in _medicines)
              AppListTile(
                title: m,
                trailing: AppButton(
                  label: context.t('adr.form.remove'),
                  size: ButtonSize.sm,
                  variant: ButtonVariant.secondary,
                  onPressed: () => setState(() => _medicines.remove(m)),
                ),
              ),
            AppTextField(
              label: context.t('adr.form.medName'),
              controller: _medicine,
            ),
            const SizedBox(height: Space.s2),
            AppButton(
              label: context.t('adr.form.add'),
              size: ButtonSize.sm,
              variant: ButtonVariant.secondary,
              onPressed: () {
                final t = _medicine.text.trim();
                if (t.isEmpty) return;
                setState(() {
                  _medicines.add(t);
                  _medicine.clear();
                });
              },
            ),
            const SizedBox(height: Space.s4),
            AppButton(
              label: context.t('adr.form.submit'),
              loading: busy,
              onPressed: _what.text.trim().isEmpty || busy ? null : _submit,
            ),
            failureAlert(),
          ],
        ),
      ),
    ],
  );
}

/// One report: the original as written, what is not known, and what a professional asked the patient. Professional notes and the
/// category are never sent to a patient by the server, so nothing here can show them.
class AdverseEventDetailScreen extends StatefulWidget {
  const AdverseEventDetailScreen({super.key, required this.id});
  final String id;
  @override
  State<AdverseEventDetailScreen> createState() => _DetailState();
}

class _DetailState extends State<AdverseEventDetailScreen> with ActionRunner {
  final _text = TextEditingController();
  int _gen = 0;

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => ScreenScaffold(
    title: context.t('adr.title.detail'),
    children: [
      RecordsLoader<Json>(
        key: ValueKey('adrd$_gen'),
        load: () => context.app.records.adverseEvent(widget.id),
        builder: (context, d, _) {
          final e = d['event'] as Json;
          final o = e['original'] as Json;
          final missing = [
            for (final m in (e['missingInformation'] as List)) '$m',
          ];
          final entries = [for (final x in (d['entries'] as List)) x as Json];
          String none() => context.t('adr.f.notRecorded');
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              AlertCard(
                title: context.t('adr.signal'),
                body: context.t('adr.internal'),
                tone: Tone.info,
              ),
              AppBadge(
                text: context.t('adr.status.${e['status']}'),
                tone: Tone.info,
              ),
              const SizedBox(height: Space.s3),
              AppCard(
                title: context.t('adr.sec.original'),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      context.t('adr.f.description'),
                      style: context.text.label,
                    ),
                    Text('${o['eventDescription']}', style: context.text.body),
                    Text(context.t('adr.f.onset'), style: context.text.label),
                    Text('${o['onsetAt'] ?? none()}', style: context.text.body),
                    Text(context.t('adr.f.dose'), style: context.text.label),
                    Text(
                      '${o['doseAndAdministrationAsReported'] ?? none()}',
                      style: context.text.body,
                    ),
                    Text(
                      context.t('adr.f.external'),
                      style: context.text.label,
                    ),
                    Text(
                      context.t('adr.external.NotSubmitted'),
                      style: context.text.body,
                    ),
                  ],
                ),
              ),
              const SizedBox(height: Space.s3),
              AppCard(
                title: context.t('adr.sec.missing'),
                child: missing.isEmpty
                    ? Text(context.t('adr.miss.none'))
                    : Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          for (final m in missing)
                            Text(
                              '• ${context.t('adr.miss.${m.replaceAll('.', '_')}')}',
                            ),
                        ],
                      ),
              ),
              const SizedBox(height: Space.s3),
              AppCard(
                title: context.t('adr.sec.history'),
                child: entries.isEmpty
                    ? Text(context.t('adr.emptyBody'))
                    : Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          for (final x in entries)
                            Padding(
                              padding: const EdgeInsets.only(bottom: Space.s2),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    context.t('adr.kind.${x['kind']}'),
                                    style: context.text.label,
                                  ),
                                  if (x['text'] != null) Text('${x['text']}'),
                                ],
                              ),
                            ),
                        ],
                      ),
              ),
              const SizedBox(height: Space.s3),
              AppCard(
                title: context.t('adr.sec.follow'),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    AppTextField(
                      label: context.t('adr.act.followUp'),
                      hint: context.t('adr.act.followUpPh'),
                      controller: _text,
                      minLines: 2,
                      maxLines: 5,
                      onChanged: (_) => setState(() {}),
                    ),
                    const SizedBox(height: Space.s2),
                    AppButton(
                      label: context.t('adr.act.save'),
                      loading: busy,
                      onPressed: _text.text.trim().isEmpty || busy
                          ? null
                          : () => act(
                              () => context.app.records.addAdverseEventFollowUp(
                                widget.id,
                                _text.text.trim(),
                              ),
                              () => setState(() {
                                _text.clear();
                                _gen++;
                              }),
                            ),
                    ),
                    failureAlert(),
                  ],
                ),
              ),
            ],
          );
        },
      ),
    ],
  );
}
