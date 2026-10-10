import 'package:flutter/material.dart';

import '../../api/assistant_client.dart';
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

const _maxQuestion = 500;

const _statusTone = {
  'Answered': null,
  'NoEvidence': Tone.info,
  'Refused': Tone.info,
  'Escalated': Tone.danger,
  'Blocked': Tone.warning,
  'Unavailable': Tone.warning,
};

/// Source-based medication assistant, wired to the real API. All decisions (provider, consent, safety) are made by the server; this screen only
/// shows the structured answer: generated text apart from evidence, what is missing or limited, MOCK vs external, and the next step.
class GroundedAssistantScreen extends StatefulWidget {
  const GroundedAssistantScreen({super.key, this.initialQuestion});
  final String? initialQuestion;
  @override
  State<GroundedAssistantScreen> createState() => _GroundedAssistantScreenState();
}

enum _Phase { idle, loading, done, error }

class _GroundedAssistantScreenState extends State<GroundedAssistantScreen> {
  late final _input = TextEditingController(text: widget.initialQuestion ?? '');
  bool _useMine = false;
  _Phase _phase = _Phase.idle;
  GroundedAnswer? _answer;
  AssistantErrorKind? _error;

  @override
  void dispose() {
    _input.dispose();
    super.dispose();
  }

  void _onChanged(String v) {
    if (v.length > _maxQuestion) {
      _input.value = TextEditingValue(
        text: v.substring(0, _maxQuestion),
        selection: const TextSelection.collapsed(offset: _maxQuestion),
      );
    }
    setState(() {});
  }

  Future<void> _ask() async {
    final q = _input.text.trim();
    if (q.isEmpty || _phase == _Phase.loading) return;
    final locale = context.app.locale.languageCode;
    final client = context.app.assistant;
    setState(() {
      _phase = _Phase.loading;
      _error = null;
    });
    try {
      final a = await client.ask(
        question: q,
        locale: locale,
        includePatientContext: _useMine,
      );
      if (!mounted) return;
      setState(() {
        _answer = a;
        _phase = _Phase.done;
      });
    } on AssistantException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.kind;
        _phase = _Phase.error;
      });
    }
  }

  String _code(String prefix, String code) {
    final key = '$prefix.${code.replaceAll('.', '_')}';
    return context.app.strings.has(key) ? context.t(key) : code;
  }

  @override
  Widget build(BuildContext context) {
    final a = _answer;
    return ScreenScaffold(
      title: context.t('grounded.title'),
      subtitle: context.t('grounded.sub'),
      children: [
        AppCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              AppTextField(
                label: context.t('grounded.input'),
                hint: context.t('grounded.inputHint'),
                controller: _input,
                minLines: 3,
                maxLines: 6,
                onChanged: _onChanged,
              ),
              const SizedBox(height: Space.s1),
              Text(
                context.t('grounded.count', {
                  'n': context.fmt.number(_input.text.length),
                  'max': context.fmt.number(_maxQuestion),
                }),
                style: context.text.caption,
              ),
              const SizedBox(height: Space.s3),
              AppSwitch(
                label: context.t('grounded.useMine'),
                value: _useMine,
                onChanged: (v) => setState(() => _useMine = v),
              ),
              Text(context.t('grounded.useMineHint'), style: context.text.caption),
              const SizedBox(height: Space.s3),
              AppButton(
                label: context.t('grounded.ask'),
                iconStart: 'send',
                loading: _phase == _Phase.loading,
                onPressed: _input.text.trim().isEmpty ? null : _ask,
              ),
            ],
          ),
        ),
        const SizedBox(height: Space.s4),
        if (_phase == _Phase.loading)
          LoadingState(label: context.t('grounded.asking'), rows: 3),
        if (_phase == _Phase.error)
          AlertCard(
            title: context.t('grounded.err.title'),
            body: context.t(
              'grounded.err.${_error == AssistantErrorKind.notConnected ? 'network' : _error!.name}',
            ),
            tone: Tone.danger,
          ),
        if (_phase == _Phase.done && a != null) _AnswerView(answer: a, code: _code),
        if (_phase == _Phase.idle)
          Text(context.t('ai.disclaimerShort'), style: context.text.caption),
      ],
    );
  }
}

class _AnswerView extends StatelessWidget {
  const _AnswerView({required this.answer, required this.code});
  final GroundedAnswer answer;
  final String Function(String prefix, String code) code;

  @override
  Widget build(BuildContext context) {
    final a = answer;
    final tone = _statusTone[a.status];
    final gen = a.generation;
    final kind = gen?['kind'] as String?;
    final why = a.status != 'Answered' && a.status != 'Escalated' && a.reason != null
        ? code('grounded.reason', a.reason!)
        : null;
    final children = <Widget>[
      if (a.status == 'Escalated')
        AlertCard(
          title: context.t('grounded.status.Escalated'),
          body: a.text,
          tone: Tone.danger,
        )
      else
        AppCard(
          title: context.t('grounded.status.${a.status}'),
          tone: tone,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(a.text, style: context.text.body),
              if (why != null) ...[
                const SizedBox(height: Space.s2),
                Text('${context.t('grounded.reason.title')}: $why', style: context.text.bodySmall),
              ],
            ],
          ),
        ),
      Wrap(
        spacing: Space.s2,
        runSpacing: Space.s2,
        children: [
          if (a.isMock)
            AppBadge(text: context.t('grounded.gen.mock'), tone: Tone.warning, icon: 'alertTriangle')
          else
            AppBadge(
              text: kind != null && kind != 'Disabled' && kind != 'Mock'
                  ? context.t('grounded.gen.$kind')
                  : context.t('grounded.gen.none'),
              tone: (gen?['external'] == true) ? Tone.info : Tone.neutral,
              icon: 'sparkles',
            ),
          if (a.notice.contains('DEMO')) const DemoBadge(),
          if (a.patientContextUsed)
            AppBadge(text: context.t('grounded.contextUsed'), tone: Tone.info, icon: 'user'),
        ],
      ),
      if (a.patientContextNote != null && !a.patientContextUsed && a.patientContextNote != 'patient_context.used')
        Text(
          context.t('grounded.contextNotUsed', {'why': code('grounded.ctx', a.patientContextNote!)}),
          style: context.text.caption,
        ),
      if (a.nextStep != 'None')
        AlertCard(
          title: context.t('grounded.next.title'),
          body: context.t('grounded.next.${a.nextStep}'),
          tone: a.nextStep == 'EmergencyServices' ? Tone.danger : Tone.info,
        ),
      for (final c in a.conflicts)
        AlertCard(
          title: context.t('grounded.limit.evidence_conflict'),
          body: context.t('grounded.evidence.conflict', {
            'name': '${c['medicationName']}',
            'ids': [for (final i in (c['itemIds'] as List? ?? [])) '[$i]'].join(' '),
          }),
          tone: Tone.warning,
        ),
      if (a.items.isNotEmpty) _Quality(answer: a, code: code),
      if (a.status == 'Escalated' && a.limitations.isNotEmpty)
        Text(a.limitations.map((l) => code('grounded.limit', l)).join(' '), style: context.text.caption),
      if (a.items.isNotEmpty)
        AppCard(
          title: context.t('grounded.evidence.title'),
          subtitle: context.t('grounded.evidence.sub'),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [for (final e in a.items) _EvidenceTile(item: e, code: code)],
          ),
        ),
    ];
    return Semantics(
      liveRegion: true,
      container: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          for (final w in children)
            Padding(padding: const EdgeInsets.only(bottom: Space.s3), child: w),
        ],
      ),
    );
  }
}

class _Quality extends StatelessWidget {
  const _Quality({required this.answer, required this.code});
  final GroundedAnswer answer;
  final String Function(String prefix, String code) code;
  @override
  Widget build(BuildContext context) => AppCard(
    title: context.t('grounded.quality.title'),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        AppBadge(
          text: context.t('grounded.quality.${answer.quality}'),
          tone: answer.quality == 'Validated' ? Tone.success : Tone.warning,
          icon: answer.quality == 'Validated' ? 'check' : 'info',
        ),
        const SizedBox(height: Space.s2),
        Text(context.t('grounded.quality.explain'), style: context.text.caption),
        if (answer.limitations.isNotEmpty) ...[
          const SizedBox(height: Space.s3),
          Text(context.t('grounded.limits.title'), style: context.text.label),
          for (final l in answer.limitations) Text('• ${code('grounded.limit', l)}', style: context.text.bodySmall),
        ],
        if (answer.missing.isNotEmpty) ...[
          const SizedBox(height: Space.s3),
          Text(context.t('grounded.missing.title'), style: context.text.label),
          for (final m in answer.missing)
            Text('• ${code('grounded.kind', m.replaceFirst('kind.', ''))}', style: context.text.bodySmall),
        ],
      ],
    ),
  );
}

class _EvidenceTile extends StatefulWidget {
  const _EvidenceTile({required this.item, required this.code});
  final Map<String, dynamic> item;
  final String Function(String prefix, String code) code;
  @override
  State<_EvidenceTile> createState() => _EvidenceTileState();
}

class _EvidenceTileState extends State<_EvidenceTile> {
  bool _open = false;
  Map<String, dynamic> get item => widget.item;
  String Function(String prefix, String code) get code => widget.code;

  static const _statusKeys = {
    'Demo': 'demo',
    'Validated': 'validated',
    'Unverified': 'unverified',
    'NeedsValidation': 'needs',
    'Rejected': 'rejected',
  };

  @override
  Widget build(BuildContext context) {
    final src = (item['source'] as Map<String, dynamic>? ?? const {});
    final validation = '${item['validation']}';
    final received = DateTime.tryParse('${src['receivedAt']}');
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.s3),
      child: DecoratedBox(
        decoration: BoxDecoration(
          border: Border.all(color: context.colors.border),
          borderRadius: BorderRadius.circular(Radii.md),
        ),
        child: Padding(
          padding: const EdgeInsets.all(Space.s3),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Wrap(
                spacing: Space.s2,
                runSpacing: Space.s1,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  Directionality(
                    textDirection: TextDirection.ltr,
                    child: Text('[${item['id']}]', style: context.text.label),
                  ),
                  Text('${item['medicationName']}', style: context.text.label),
                  AppBadge(text: code('grounded.kind', '${item['kind']}')),
                  AppBadge(
                    text: context.t('kn.status.${_statusKeys[validation] ?? 'unverified'}'),
                    tone: validation == 'Validated' ? Tone.success : (validation == 'Demo' ? Tone.warning : Tone.danger),
                    icon: validation == 'Validated' ? 'check' : 'alertTriangle',
                  ),
                  if (item['stale'] == true)
                    AppBadge(text: context.t('grounded.evidence.stale'), tone: Tone.warning, icon: 'clock'),
                  if (item['sourceDateUnknown'] == true)
                    AppBadge(text: context.t('grounded.evidence.receivedUnknown'), icon: 'clock'),
                ],
              ),
              const SizedBox(height: Space.s2),
              Text('${item['text']}', style: context.text.body),
              if (item['qualifier'] != null)
                Text('${item['qualifier']}'.replaceFirst('|', ' · '), style: context.text.caption),
              const SizedBox(height: Space.s2),
              AppButton(
                label: context.t('grounded.evidence.show'),
                variant: ButtonVariant.ghost,
                size: ButtonSize.sm,
                iconEnd: _open ? 'chevronUp' : 'chevronDown',
                onPressed: () => setState(() => _open = !_open),
              ),
              if (_open) ...[
                _Fact(context.t('grounded.evidence.source'), '${src['name']}'),
                _Fact(context.t('grounded.evidence.version'), '${src['version']}'),
                _Fact(context.t('grounded.evidence.publisher'), '${src['publisher']}'),
                _Fact(
                  context.t('grounded.evidence.received'),
                  received == null ? context.t('grounded.evidence.receivedUnknown') : context.fmt.date(received),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _Fact extends StatelessWidget {
  const _Fact(this.label, this.value);
  final String label;
  final String value;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: Space.s1),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(width: 120, child: Text(label, style: context.text.caption)),
        Expanded(child: Text(value, style: context.text.bodySmall)),
      ],
    ),
  );
}
