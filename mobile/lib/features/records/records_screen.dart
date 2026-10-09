import 'package:flutter/material.dart';

import '../../api/records_client.dart';
import '../../components/buttons.dart';
import '../../components/display.dart';
import '../../components/inputs.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';
import 'common.dart';

/// "My health record": what was last updated, the basic profile and the lists of conditions, allergies and symptoms.
/// Everything is optional and entered by the person; nothing is guessed.
class RecordsScreen extends StatefulWidget {
  const RecordsScreen({super.key});
  @override
  State<RecordsScreen> createState() => _RecordsScreenState();
}

class _RecordsScreenState extends State<RecordsScreen> {
  int _gen = 0;
  void _refresh() => setState(() => _gen++);

  @override
  Widget build(BuildContext context) {
    final api = context.app.records;
    return ScreenScaffold(
      title: context.t('rec.records.title'),
      subtitle: context.t('rec.records.sub'),
      children: [
        RecordsLoader<Json?>(
          key: ValueKey('profile$_gen'),
          load: () async {
            try {
              return await api.profile();
            } on RecordsException catch (e) {
              if (e.kind == RecordsErrorKind.notFound) return null;
              rethrow;
            }
          },
          builder: (context, profile, reload) => profile == null
              ? _NoRecord(onCreated: _refresh)
              : Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    _Freshness(key: ValueKey('fresh$_gen')),
                    _Profile(profile: profile),
                    _ListCard(
                      key: ValueKey('cond$_gen'),
                      title: context.t('rec.cond.title'),
                      empty: context.t('rec.cond.empty'),
                      addLabel: context.t('rec.cond.name'),
                      load: api.conditions,
                      name: (j) => '${j['name']}',
                      meta: (c, j) => c.t('rec.cond.status.${j['status']}'),
                      add: (text, sev) => api.addCondition(text, 'Active'),
                      withSeverity: false,
                      remove: (j) => api.removeCondition('${j['id']}'),
                      onChanged: _refresh,
                    ),
                    _ListCard(
                      key: ValueKey('allergy$_gen'),
                      title: context.t('rec.allergy.title'),
                      empty: context.t('rec.allergy.empty'),
                      addLabel: context.t('rec.allergy.substance'),
                      load: api.allergies,
                      name: (j) => '${j['substance']}',
                      meta: (c, j) => c.t('rec.severity.${j['severity']}'),
                      add: api.addAllergy,
                      remove: (j) => api.removeAllergy('${j['id']}'),
                      onChanged: _refresh,
                    ),
                    _ListCard(
                      key: ValueKey('symptom$_gen'),
                      title: context.t('rec.symptom.title'),
                      empty: context.t('rec.symptom.empty'),
                      addLabel: context.t('rec.symptom.text'),
                      load: api.symptoms,
                      name: (j) => '${j['text']}',
                      meta: (c, j) => c.t('rec.severity.${j['severity']}'),
                      add: api.addSymptom,
                      onChanged: _refresh,
                      footer: context.t('rec.symptom.advice'),
                    ),
                  ],
                ),
        ),
      ],
    );
  }
}

class _NoRecord extends StatefulWidget {
  const _NoRecord({required this.onCreated});
  final VoidCallback onCreated;
  @override
  State<_NoRecord> createState() => _NoRecordState();
}

class _NoRecordState extends State<_NoRecord> with ActionRunner {
  @override
  Widget build(BuildContext context) => Column(
    children: [
      EmptyState(
        icon: 'user',
        title: context.t('rec.noRecord.title'),
        body: context.t('rec.noRecord.body'),
        action: AppButton(
          label: context.t('rec.noRecord.create'),
          loading: busy,
          onPressed: () => act(context.app.records.ensureOwn, widget.onCreated),
        ),
      ),
      failureAlert(),
    ],
  );
}

class _Freshness extends StatelessWidget {
  const _Freshness({super.key});
  @override
  Widget build(BuildContext context) => RecordsLoader<List<Json>>(
    load: context.app.records.freshness,
    builder: (context, rows, _) => RecSection(
      title: context.t('rec.fresh.title'),
      subtitle: context.t('rec.fresh.sub'),
      child: Column(
        children: [
          for (final r in rows)
            AppListTile(
              title: context.t('rec.cat.${r['category']}'),
              meta: r['neverRecorded'] == true
                  ? null
                  : context.t('rec.fresh.count', {
                      'n': context.fmt.number((r['recordCount'] as num?) ?? 0),
                    }),
              trailing: r['neverRecorded'] == true
                  ? AppBadge(
                      text: context.t('rec.fresh.never'),
                      icon: 'clock',
                    )
                  : r['isStale'] == true
                  ? AppBadge(
                      text: context.t('rec.fresh.stale'),
                      tone: Tone.warning,
                      icon: 'alertTriangle',
                    )
                  : null,
            ),
        ],
      ),
    ),
  );
}

class _Profile extends StatelessWidget {
  const _Profile({required this.profile});
  final Json profile;
  @override
  Widget build(BuildContext context) {
    final sex = profile['sex'] as String?;
    final age = profile['ageYears'] as num?;
    return RecSection(
      title: context.t('rec.profile.title'),
      subtitle: context.t('rec.profile.minimal'),
      demo: profile['isDemo'] == true,
      child: Column(
        children: [
          AppListTile(
            title: context.t('rec.profile.year'),
            meta: profile['yearOfBirth'] == null
                ? context.t('rec.fresh.never')
                : context.fmt.number(profile['yearOfBirth'] as num),
          ),
          AppListTile(
            title: context.t('rec.ai.age'),
            meta: age == null ? context.t('rec.fresh.never') : context.fmt.number(age),
          ),
          AppListTile(
            title: context.t('rec.profile.sex'),
            meta: sex == null ? context.t('rec.fresh.never') : context.t('rec.sex.$sex'),
          ),
        ],
      ),
    );
  }
}

class _ListCard extends StatefulWidget {
  const _ListCard({
    super.key,
    required this.title,
    required this.empty,
    required this.addLabel,
    required this.load,
    required this.name,
    required this.meta,
    required this.add,
    required this.onChanged,
    this.remove,
    this.withSeverity = true,
    this.footer,
  });
  final String title, empty, addLabel;
  final Future<List<Json>> Function() load;
  final String Function(Json) name;
  final String Function(BuildContext, Json) meta;
  final Future<Object?> Function(String text, String severity) add;
  final Future<Object?> Function(Json)? remove;
  final VoidCallback onChanged;
  final bool withSeverity;
  final String? footer;
  @override
  State<_ListCard> createState() => _ListCardState();
}

class _ListCardState extends State<_ListCard> with ActionRunner {
  final _text = TextEditingController();
  String _severity = 'Mild';

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => RecordsLoader<List<Json>>(
    load: widget.load,
    builder: (context, rows, _) => RecSection(
      title: widget.title,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (rows.isEmpty)
            Text(widget.empty, style: context.text.bodySmall)
          else
            for (final j in rows)
              AppListTile(
                title: widget.name(j),
                meta: widget.meta(context, j),
                trailing: widget.remove == null
                    ? null
                    : AppIconButton(
                        icon: 'trash',
                        label: context.t('common.remove'),
                        onPressed: () => act(
                          () => widget.remove!(j),
                          widget.onChanged,
                        ),
                      ),
              ),
          const SizedBox(height: Space.s3),
          AppTextField(label: widget.addLabel, controller: _text),
          if (widget.withSeverity) ...[
            const SizedBox(height: Space.s2),
            AppSelect<String>(
              label: context.t('rec.allergy.severity'),
              value: _severity,
              items: {
                for (final s in ['Mild', 'Moderate', 'Severe'])
                  s: context.t('rec.severity.$s'),
              },
              onChanged: (v) => setState(() => _severity = v),
            ),
          ],
          const SizedBox(height: Space.s3),
          AppButton(
            label: context.t('rec.add'),
            iconStart: 'plus',
            loading: busy,
            onPressed: () {
              final text = _text.text.trim();
              if (text.isEmpty) return;
              act(() => widget.add(text, _severity), () {
                _text.clear();
                widget.onChanged();
              });
            },
          ),
          failureAlert(),
          if (widget.footer != null) ...[
            const SizedBox(height: Space.s2),
            Text(widget.footer!, style: context.text.caption),
          ],
        ],
      ),
    ),
  );
}
