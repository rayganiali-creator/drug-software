import 'package:flutter/material.dart';

import '../../api/records_client.dart';
import '../../components/buttons.dart';
import '../../components/containers.dart';
import '../../components/display.dart';
import '../../components/health.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';
import 'common.dart';

const _levelTone = {
  'Information': Tone.info,
  'FollowUp': Tone.info,
  'ReviewSoon': Tone.warning,
  'Urgent': Tone.danger,
};

/// Calm, six-part messages about things worth a look (what we noticed, why it matters, what you can do, when to talk to
/// someone, signs that need quick help, basis and confidence). They are not diagnoses.
class MessagesScreen extends StatefulWidget {
  const MessagesScreen({super.key});
  @override
  State<MessagesScreen> createState() => _MessagesScreenState();
}

class _MessagesScreenState extends State<MessagesScreen> {
  int _gen = 0;
  @override
  Widget build(BuildContext context) => ScreenScaffold(
    title: context.t('rec.messages.title'),
    subtitle: context.t('rec.messages.sub'),
    children: [
      RecordsLoader<List<Json>>(
        key: ValueKey('msgs$_gen'),
        load: context.app.records.messages,
        builder: (context, rows, _) => rows.isEmpty
            ? EmptyState(
                icon: 'message',
                title: context.t('rec.messages.empty'),
                body: context.t('rec.messages.emptyBody'),
              )
            : Column(
                children: [
                  for (final m in rows)
                    _Message(m: m, onChanged: () => setState(() => _gen++)),
                ],
              ),
      ),
    ],
  );
}

class _Message extends StatefulWidget {
  const _Message({required this.m, required this.onChanged});
  final Json m;
  final VoidCallback onChanged;
  @override
  State<_Message> createState() => _MessageState();
}

class _MessageState extends State<_Message> with ActionRunner {
  @override
  Widget build(BuildContext context) {
    final m = widget.m;
    final level = '${m['level']}';
    final status = '${m['status']}';
    final p = m['patient'] as Json;
    Widget part(String key, Object? text) => Padding(
      padding: const EdgeInsets.only(bottom: Space.s3),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(context.t('rec.msg.$key'), style: context.text.label),
          Text('$text', style: context.text.body),
        ],
      ),
    );
    final api = context.app.records;
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.s4),
      child: AppCard(
        title: context.t('rec.level.$level'),
        actions: Wrap(
          spacing: Space.s2,
          children: [
            AppBadge(
              text: context.t('rec.msgStatus.$status'),
              tone: _levelTone[level] ?? Tone.neutral,
            ),
            if (m['isDemo'] == true) const DemoBadge(),
          ],
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            part('observed', p['observed']),
            part('why', p['whyItMatters']),
            part('action', p['suggestedAction']),
            part('consult', p['whenToConsult']),
            if (p['urgentSigns'] != null) part('urgent', p['urgentSigns']),
            part('basis', p['basisAndConfidence']),
            Wrap(
              spacing: Space.s2,
              children: [
                if (status == 'Sent')
                  AppButton(
                    label: context.t('rec.messages.seen'),
                    size: ButtonSize.sm,
                    variant: ButtonVariant.secondary,
                    loading: busy,
                    onPressed: () => act(
                      () => api.setMessageStatus('${m['id']}', 'Seen'),
                      widget.onChanged,
                    ),
                  ),
                if (status != 'Resolved')
                  AppButton(
                    label: context.t('rec.messages.resolve'),
                    size: ButtonSize.sm,
                    onPressed: busy
                        ? null
                        : () => act(
                            () => api.setMessageStatus('${m['id']}', 'Resolved'),
                            widget.onChanged,
                          ),
                  ),
              ],
            ),
            failureAlert(),
          ],
        ),
      ),
    );
  }
}
