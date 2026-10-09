import 'package:flutter/material.dart';

import '../../api/records_client.dart';
import '../../components/buttons.dart';
import '../../components/display.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';
import 'common.dart';

const _careTone = {
  'PendingProvider': Tone.warning,
  'PendingPatient': Tone.warning,
  'Active': Tone.success,
  'Declined': Tone.neutral,
  'Ended': Tone.neutral,
};

/// Sharing and care: two-sided care relationships (either side can end them) and the person's consents.
class SharingScreen extends StatefulWidget {
  const SharingScreen({super.key});
  @override
  State<SharingScreen> createState() => _SharingScreenState();
}

class _SharingScreenState extends State<SharingScreen> {
  int _gen = 0;
  void _refresh() => setState(() => _gen++);

  @override
  Widget build(BuildContext context) {
    final api = context.app.records;
    return ScreenScaffold(
      title: context.t('rec.share.title'),
      subtitle: context.t('rec.share.sub'),
      children: [
        RecordsLoader<List<Json>>(
          key: ValueKey('care$_gen'),
          load: api.care,
          builder: (context, rows, _) => RecSection(
            title: context.t('rec.care.title'),
            subtitle: context.t('rec.care.twoSided'),
            child: rows.isEmpty
                ? Text(context.t('rec.care.empty'), style: context.text.bodySmall)
                : Column(
                    children: [
                      for (final r in rows) _CareRow(row: r, onChanged: _refresh),
                    ],
                  ),
          ),
        ),
        RecordsLoader<List<Json>>(
          key: ValueKey('consents$_gen'),
          load: api.consents,
          builder: (context, rows, _) => RecSection(
            title: context.t('rec.consent.title'),
            subtitle: context.t('rec.consent.sub'),
            child: rows.isEmpty
                ? Text(context.t('rec.consent.empty'), style: context.text.bodySmall)
                : Column(
                    children: [
                      for (final r in rows) _ConsentRow(row: r, onChanged: _refresh),
                    ],
                  ),
          ),
        ),
      ],
    );
  }
}

class _CareRow extends StatefulWidget {
  const _CareRow({required this.row, required this.onChanged});
  final Json row;
  final VoidCallback onChanged;
  @override
  State<_CareRow> createState() => _CareRowState();
}

class _CareRowState extends State<_CareRow> with ActionRunner {
  @override
  Widget build(BuildContext context) {
    final rel = widget.row['relationship'] as Json;
    final status = '${rel['status']}';
    final api = context.app.records;
    Widget btn(String label, String action, [ButtonVariant v = ButtonVariant.secondary]) =>
        AppButton(
          label: label,
          size: ButtonSize.sm,
          variant: v,
          onPressed: busy ? null : () => act(() => api.careAction('${rel['id']}', action), widget.onChanged),
        );
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        AppListTile(
          title: '${widget.row['providerName'] ?? context.t('rec.care.organization')}',
          meta: context.t('rec.care.kind.${rel['kind']}'),
          trailing: AppBadge(
            text: context.t('rec.care.status.$status'),
            tone: _careTone[status] ?? Tone.neutral,
          ),
        ),
        Padding(
          padding: const EdgeInsetsDirectional.only(start: Space.s4),
          child: Wrap(
            spacing: Space.s2,
            children: [
              if (status == 'PendingPatient') ...[
                btn(context.t('rec.care.accept'), 'accept', ButtonVariant.primary),
                btn(context.t('rec.care.decline'), 'decline'),
              ],
              if (status == 'Active') btn(context.t('rec.care.end'), 'end'),
              if (status == 'PendingProvider') btn(context.t('rec.care.withdraw'), 'end'),
            ],
          ),
        ),
        failureAlert(),
      ],
    );
  }
}

class _ConsentRow extends StatefulWidget {
  const _ConsentRow({required this.row, required this.onChanged});
  final Json row;
  final VoidCallback onChanged;
  @override
  State<_ConsentRow> createState() => _ConsentRowState();
}

class _ConsentRowState extends State<_ConsentRow> with ActionRunner {
  @override
  Widget build(BuildContext context) {
    final c = widget.row['consent'] as Json;
    final status = '${c['status']}';
    final until = DateTime.tryParse('${c['expiresAt']}');
    final who = widget.row['granteeName'];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        AppListTile(
          title: context.t('rec.purpose.${c['purpose']}') + (who == null ? '' : ' · $who'),
          meta: [
            (c['scope'] as List? ?? []).map((s) => context.t('rec.scope.$s')).join('، '),
            if (until != null)
              context.t('rec.consent.until', {'date': context.fmt.date(until)}),
          ].where((s) => s.isNotEmpty).join(' · '),
          trailing: AppBadge(
            text: context.t('rec.consent.status.$status'),
            tone: status == 'Active' ? Tone.success : Tone.neutral,
          ),
        ),
        if (status == 'Active')
          Padding(
            padding: const EdgeInsetsDirectional.only(start: Space.s4),
            child: AppButton(
              label: context.t('rec.consent.revoke'),
              size: ButtonSize.sm,
              variant: ButtonVariant.secondary,
              loading: busy,
              onPressed: () => act(
                () => context.app.records.revokeConsent('${c['id']}'),
                widget.onChanged,
              ),
            ),
          ),
        failureAlert(),
      ],
    );
  }
}
