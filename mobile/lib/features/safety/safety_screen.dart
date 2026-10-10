import 'package:flutter/material.dart';

import '../../api/safety_client.dart';
import '../../components/buttons.dart';
import '../../components/containers.dart';
import '../../components/display.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';

const _statusTone = {
  'CompletedWithFindings': Tone.warning,
  'CompletedNoMatches': null,
  'Incomplete': Tone.info,
  'NoApprovedCoverage': Tone.info,
  'Failed': Tone.danger,
};

const _severityTone = {
  'Informational': Tone.neutral,
  'Minor': Tone.info,
  'Moderate': Tone.warning,
  'Major': Tone.warning,
  'Critical': Tone.danger,
};

/// The patient's own medication safety check. Everything shown is decided by the server's deterministic rules: a status, a severity and whether a finding
/// is actionable. This screen runs nothing by itself and never states that anything is "safe".
class SafetyScreen extends StatefulWidget {
  const SafetyScreen({super.key});
  @override
  State<SafetyScreen> createState() => _SafetyScreenState();
}

enum _Phase { loading, empty, ready, error }

class _SafetyScreenState extends State<SafetyScreen> {
  _Phase _phase = _Phase.loading;
  Assessment? _a;
  SafetyErrorKind? _error;
  SafetyErrorKind? _runError;
  bool _running = false;
  bool _started = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_started) {
      _started = true;
      _load();
    }
  }

  Future<void> _load() async {
    final client = context.app.safety;
    final id = client.selfId;
    setState(() => _phase = _Phase.loading);
    try {
      if (id == null) throw const SafetyException(SafetyErrorKind.notConnected);
      final a = await client.latest(id);
      if (!mounted) return;
      setState(() {
        _a = a;
        _phase = _Phase.ready;
      });
    } on SafetyException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.kind;
        _phase = e.kind == SafetyErrorKind.notFound
            ? _Phase.empty
            : _Phase.error;
      });
    }
  }

  Future<void> _run() async {
    final client = context.app.safety;
    final id = client.selfId;
    final locale = context.app.locale.languageCode;
    setState(() {
      _running = true;
      _runError = null;
    });
    try {
      if (id == null) throw const SafetyException(SafetyErrorKind.notConnected);
      final a = await client.run(id, locale);
      if (!mounted) return;
      setState(() {
        _a = a;
        _phase = _Phase.ready;
        _running = false;
      });
    } on SafetyException catch (e) {
      if (!mounted) return;
      setState(() {
        _runError = e.kind;
        _running = false;
      });
    }
  }

  String _code(String prefix, String code) {
    final key = '$prefix.${code.replaceAll('.', '_')}';
    return context.app.strings.has(key) ? context.t(key) : code;
  }

  Widget _problem(SafetyErrorKind kind, VoidCallback onRetry) => switch (kind) {
    SafetyErrorKind.notConnected => EmptyState(
      icon: 'lock',
      title: context.t('sf.err.notConnected.title'),
      body: context.t('sf.err.notConnected.body'),
    ),
    SafetyErrorKind.unauthorized => EmptyState(
      icon: 'shield',
      title: context.t('sf.err.blocked.title'),
      body: context.t('sf.err.blocked.body'),
    ),
    SafetyErrorKind.rateLimited => EmptyState(
      icon: 'clock',
      title: context.t('sf.err.rate.title'),
      body: context.t('sf.err.rate.body'),
    ),
    SafetyErrorKind.invalid => ErrorState(
      title: context.t('sf.err.invalid.title'),
      body: context.t('sf.err.invalid.body'),
      retryLabel: context.t('common.retry'),
      onRetry: onRetry,
    ),
    _ => ErrorState(
      title: context.t('sf.err.unavailable.title'),
      body: context.t('sf.err.unavailable.body'),
      retryLabel: context.t('common.retry'),
      onRetry: onRetry,
    ),
  };

  @override
  Widget build(BuildContext context) {
    final a = _a;
    return ScreenScaffold(
      title: context.t('sf.title'),
      subtitle: context.t('sf.sub'),
      children: [
        AppCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(context.t('sf.intro'), style: context.text.body),
              const SizedBox(height: Space.s3),
              AppButton(
                label: a != null
                    ? context.t('sf.run')
                    : context.t('sf.runFirst'),
                iconStart: 'refresh',
                loading: _running,
                onPressed: _run,
              ),
              if (_runError != null) ...[
                const SizedBox(height: Space.s3),
                _problem(_runError!, _run),
              ],
            ],
          ),
        ),
        const SizedBox(height: Space.s4),
        if (_phase == _Phase.loading)
          LoadingState(label: context.t('common.loading'), rows: 3),
        if (_phase == _Phase.error)
          _problem(_error ?? SafetyErrorKind.server, _load),
        if (_phase == _Phase.empty)
          EmptyState(
            icon: 'clipboard',
            title: context.t('sf.none.title'),
            body: context.t('sf.none.body'),
          ),
        if (_phase == _Phase.ready && a != null)
          _AssessmentView(a: a, code: _code, onRerun: _run, busy: _running),
      ],
    );
  }
}

class _AssessmentView extends StatelessWidget {
  const _AssessmentView({
    required this.a,
    required this.code,
    required this.onRerun,
    required this.busy,
  });
  final Assessment a;
  final String Function(String prefix, String code) code;
  final VoidCallback onRerun;
  final bool busy;

  Widget _gap(Widget w) => Padding(
    padding: const EdgeInsets.only(bottom: Space.s3),
    child: w,
  );

  @override
  Widget build(BuildContext context) {
    final status = a.status;
    final actionable = [
      for (final f in a.findings)
        if (f['actionable'] == true) f,
    ];
    final others = [
      for (final f in a.findings)
        if (f['actionable'] != true) f,
    ];
    final cov = a.coverage;
    final children = <Widget>[
      if (context.app.locale.languageCode == 'fa')
        AlertCard(
          title: context.t('sf.fa.unreviewed.title'),
          body: context.t('sf.fa.unreviewed.body'),
          tone: Tone.info,
        ),
      AppCard(
        title: context.t('sf.status.$status'),
        tone: _statusTone[status],
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(context.t('sf.statusBody.$status'), style: context.text.body),
            const SizedBox(height: Space.s2),
            Wrap(
              spacing: Space.s2,
              runSpacing: Space.s2,
              children: [
                if (a.containsDemonstration)
                  AppBadge(
                    text: context.t('sf.demo.badge'),
                    tone: Tone.warning,
                    icon: 'alertTriangle',
                  ),
                if (!a.complete && status != 'NoApprovedCoverage')
                  AppBadge(
                    text: context.t('sf.incomplete'),
                    tone: Tone.info,
                    icon: 'info',
                  ),
                if (a.outdated)
                  AppBadge(
                    text: context.t('sf.outdated.badge'),
                    tone: Tone.warning,
                    icon: 'clock',
                  ),
              ],
            ),
            const SizedBox(height: Space.s2),
            Text(context.t('sf.notSafety'), style: context.text.label),
            if (a.containsDemonstration)
              Text(a.notice, style: context.text.caption),
          ],
        ),
      ),
      if (a.outdated)
        AlertCard(
          title: context.t('sf.outdated.title'),
          body:
              '${context.t('sf.outdated.body')}\n${a.outdatedReasons.map((r) => '• ${code('sf.outdated', r)}').join('\n')}',
          tone: Tone.warning,
          actions: [
            AppButton(
              label: context.t('sf.run'),
              variant: ButtonVariant.secondary,
              loading: busy,
              onPressed: onRerun,
            ),
          ],
        ),
      if (status == 'Failed')
        AlertCard(
          title: context.t('sf.failed.title'),
          body: context.t('sf.failed.body'),
          tone: Tone.danger,
        ),
      if (actionable.isNotEmpty)
        AppCard(
          title: context.t('sf.findings.title'),
          subtitle: context.t('sf.findings.sub'),
          child: Column(
            children: [
              for (final f in actionable) _FindingTile(f: f, a: a, code: code),
            ],
          ),
        ),
      if (others.isNotEmpty)
        AppCard(
          title: a.containsDemonstration
              ? context.t('sf.demoFindings.title')
              : context.t('sf.otherFindings.title'),
          subtitle: a.containsDemonstration
              ? context.t('sf.demoFindings.sub')
              : context.t('sf.otherFindings.sub'),
          child: Column(
            children: [
              for (final f in others) _FindingTile(f: f, a: a, code: code),
            ],
          ),
        ),
      if (a.findings.isEmpty && status != 'Failed')
        AppCard(
          title: context.t('sf.nothing.title'),
          child: Text(
            status == 'NoApprovedCoverage'
                ? context.t('sf.nothing.noCoverage')
                : status == 'Incomplete'
                ? context.t('sf.nothing.incomplete')
                : context.t('sf.nothing.noMatch'),
            style: context.text.body,
          ),
        ),
      if (a.noLongerMatching > 0)
        AlertCard(
          title: context.t('sf.noLonger.title'),
          body: context.t('sf.noLonger.body', {
            'n': context.fmt.number(a.noLongerMatching),
          }),
          tone: Tone.info,
        ),
      AppCard(
        title: context.t('sf.coverage.title'),
        subtitle: context.t('sf.coverage.sub'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${context.t('sf.coverage.active')}: ${context.fmt.number((cov['activeRules'] as num?) ?? 0)}',
              style: context.text.body,
            ),
            Text(
              '${context.t('sf.coverage.demo')}: ${context.fmt.number((cov['demonstrationRules'] as num?) ?? 0)}',
              style: context.text.body,
            ),
            Text(
              '${context.t('sf.coverage.inactive')}: ${context.fmt.number((cov['inactiveRules'] as num?) ?? 0)}',
              style: context.text.body,
            ),
            Text(
              '${context.t('sf.coverage.notEvaluable')}: ${context.fmt.number((cov['notEvaluable'] as num?) ?? 0)}',
              style: context.text.body,
            ),
            const SizedBox(height: Space.s2),
            Text(context.t('sf.coverage.covered'), style: context.text.label),
            if (((cov['domainsCovered'] as List?) ?? const []).isEmpty)
              Text(context.t('sf.coverage.none'), style: context.text.bodySmall)
            else
              for (final d in (cov['domainsCovered'] as List))
                Text(
                  '• ${context.t('sf.domain.$d')}',
                  style: context.text.bodySmall,
                ),
            const SizedBox(height: Space.s2),
            Text(
              context.t('sf.coverage.unsupported'),
              style: context.text.label,
            ),
            for (final d in ((cov['unsupportedDomains'] as List?) ?? const []))
              Text(
                '• ${code('sf.unsupported', '$d')}',
                style: context.text.bodySmall,
              ),
          ],
        ),
      ),
      AppCard(
        title: context.t('sf.rules.title'),
        subtitle: context.t('sf.rules.sub'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (a.evaluations.isEmpty)
              Text(context.t('sf.rules.none'), style: context.text.bodySmall),
            for (final e in a.evaluations)
              Padding(
                padding: const EdgeInsets.only(bottom: Space.s2),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Directionality(
                      textDirection: TextDirection.ltr,
                      child: Text(
                        '${e['ruleId']} v${e['version']}',
                        style: context.text.label,
                      ),
                    ),
                    Wrap(
                      spacing: Space.s2,
                      runSpacing: Space.s1,
                      children: [
                        AppBadge(
                          text: context.t('sf.outcome.${e['outcome']}'),
                          tone: e['outcome'] == 'Matched'
                              ? Tone.warning
                              : (e['outcome'] == 'NoMatch' ||
                                    e['outcome'] == 'NotApplicable' ||
                                    e['outcome'] == 'Unavailable')
                              ? Tone.neutral
                              : Tone.info,
                          icon: e['outcome'] == 'Matched'
                              ? 'alertTriangle'
                              : 'info',
                        ),
                        AppBadge(
                          text: context.t('sf.activation.${e['activation']}'),
                        ),
                        if (e['partial'] == true)
                          AppBadge(
                            text: context.t('sf.partial'),
                            tone: Tone.info,
                            icon: 'info',
                          ),
                      ],
                    ),
                    if (((e['reasons'] as List?) ?? const []).isNotEmpty)
                      Text(
                        [
                          for (final r in (e['reasons'] as List))
                            code('sf.reason', '$r'),
                        ].join(' · '),
                        style: context.text.caption,
                      ),
                  ],
                ),
              ),
          ],
        ),
      ),
      AppCard(
        title: context.t('sf.inputs.title'),
        subtitle: context.t('sf.inputs.sub'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            for (final i in a.inputs)
              Padding(
                padding: const EdgeInsets.only(bottom: Space.s2),
                child: Wrap(
                  spacing: Space.s2,
                  runSpacing: Space.s1,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    Text(
                      context.t('sf.input.${i['category']}'),
                      style: context.text.label,
                    ),
                    AppBadge(
                      text: context.t('sf.avail.${i['availability']}'),
                      tone: i['availability'] == 'Available'
                          ? Tone.success
                          : Tone.warning,
                      icon: i['availability'] == 'Available'
                          ? 'check'
                          : 'alertTriangle',
                    ),
                    if (i['isStale'] == true)
                      AppBadge(
                        text: context.t('sf.staleInput'),
                        tone: Tone.warning,
                        icon: 'clock',
                      ),
                  ],
                ),
              ),
          ],
        ),
      ),
      AppCard(
        title: context.t('sf.meta.title'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (a.evaluatedAt != null)
              Text(
                '${context.t('sf.meta.when')}: ${context.fmt.date(a.evaluatedAt!.toLocal())}',
                style: context.text.bodySmall,
              ),
            Directionality(
              textDirection: TextDirection.ltr,
              child: Text(
                '${context.t('sf.meta.engine')}: ${a.engineVersion}',
                style: context.text.bodySmall,
              ),
            ),
            Directionality(
              textDirection: TextDirection.ltr,
              child: Text(
                '${context.t('sf.meta.ruleSet')}: ${a.ruleSetVersion}',
                style: context.text.bodySmall,
              ),
            ),
            Text(
              '${context.t('sf.meta.guidance')}: ${context.t('sf.guidance.${a.guidanceState}')}',
              style: context.text.bodySmall,
            ),
            const SizedBox(height: Space.s2),
            for (final l in a.limitations)
              Text('• ${code('sf.limit', l)}', style: context.text.caption),
          ],
        ),
      ),
    ];
    return Semantics(
      liveRegion: true,
      container: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [for (final w in children) _gap(w)],
      ),
    );
  }
}

class _FindingTile extends StatefulWidget {
  const _FindingTile({required this.f, required this.a, required this.code});
  final Map<String, dynamic> f;
  final Assessment a;
  final String Function(String prefix, String code) code;

  @override
  State<_FindingTile> createState() => _FindingTileState();
}

class _FindingTileState extends State<_FindingTile> {
  bool _open = false;
  Map<String, dynamic> get f => widget.f;
  String Function(String prefix, String code) get code => widget.code;

  @override
  Widget build(BuildContext context) {
    final severity = '${f['severity']}';
    final domain = '${f['domain']}';
    final names = [
      for (final s in (f['subjects'] as List? ?? const []))
        '${(s as Map)['label']}',
    ].join(' · ');
    final evidence = [
      for (final e in (f['evidence'] as List? ?? const []))
        e as Map<String, dynamic>,
    ];
    final limits = [
      for (final l in (f['limitations'] as List? ?? const [])) '$l',
    ];
    final signs = f['emergencySigns'] as List?;
    final demo = f['isDemo'] == true;
    final actionable = f['actionable'] == true;
    return Padding(
      padding: const EdgeInsets.only(bottom: Space.s4),
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
              Text(context.t('sf.domain.$domain'), style: context.text.label),
              const SizedBox(height: Space.s1),
              Wrap(
                spacing: Space.s2,
                runSpacing: Space.s1,
                children: [
                  AppBadge(
                    text: context.t('sf.severity.$severity'),
                    tone: _severityTone[severity] ?? Tone.neutral,
                    icon: severity == 'Critical' || severity == 'Major'
                        ? 'alertTriangle'
                        : 'info',
                  ),
                  if (demo)
                    AppBadge(
                      text: context.t('sf.demo.badge'),
                      tone: Tone.warning,
                      icon: 'alertTriangle',
                    ),
                  if (!demo && !actionable)
                    AppBadge(text: context.t('sf.notActionable'), icon: 'info'),
                  if (f['evidenceConflict'] == true)
                    AppBadge(
                      text: context.t('sf.conflict'),
                      tone: Tone.warning,
                      icon: 'alertTriangle',
                    ),
                  if (f['inputsStale'] == true)
                    AppBadge(
                      text: context.t('sf.staleInput'),
                      tone: Tone.warning,
                      icon: 'clock',
                    ),
                ],
              ),
              const SizedBox(height: Space.s2),
              Text(
                '${context.t('sf.f.observed')}: $names',
                style: context.text.body,
              ),
              Text(
                '${context.t('sf.f.why')}: ${context.t('sf.why.$domain')}',
                style: context.text.bodySmall,
              ),
              Text(
                '${context.t('sf.f.next')}: ${context.t('sf.next.$domain')}',
                style: context.text.bodySmall,
              ),
              if (demo)
                Text(context.t('sf.demo.finding'), style: context.text.caption),
              if (signs != null && signs.isNotEmpty && actionable) ...[
                const SizedBox(height: Space.s2),
                Text(context.t('sf.f.signs'), style: context.text.label),
                for (final s in signs)
                  Text('• $s', style: context.text.bodySmall),
                Text(
                  context.t('sf.f.signsSourced'),
                  style: context.text.caption,
                ),
              ],
              AppButton(
                label: context.t('sf.f.details'),
                variant: ButtonVariant.ghost,
                size: ButtonSize.sm,
                iconEnd: _open ? 'chevronUp' : 'chevronDown',
                onPressed: () => setState(() => _open = !_open),
              ),
              if (_open)
                Align(
                  alignment: AlignmentDirectional.centerStart,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Directionality(
                        textDirection: TextDirection.ltr,
                        child: Text(
                          '${context.t('sf.f.rule')}: ${f['ruleId']} v${f['ruleVersion']}',
                          style: context.text.bodySmall,
                        ),
                      ),
                      Text(
                        '${context.t('sf.f.urgency')}: ${context.t('sf.urgency.${f['urgency']}')}',
                        style: context.text.bodySmall,
                      ),
                      if (f['referenceSeverity'] != null)
                        Text(
                          '${context.t('sf.f.refSeverity')}: ${f['referenceSeverity']}',
                          style: context.text.bodySmall,
                        ),
                      const SizedBox(height: Space.s2),
                      Text(
                        context.t('sf.f.evidence'),
                        style: context.text.label,
                      ),
                      for (final e in evidence) ...[
                        Text(
                          '${e['sourceName']} (${e['version']})',
                          style: context.text.bodySmall,
                        ),
                        Wrap(
                          spacing: Space.s2,
                          children: [
                            AppBadge(
                              text: context.t(
                                'sf.validation.${e['validation']}',
                              ),
                              tone: e['validation'] == 'Validated'
                                  ? Tone.success
                                  : e['validation'] == 'Demo'
                                  ? Tone.warning
                                  : Tone.danger,
                              icon: e['validation'] == 'Validated'
                                  ? 'check'
                                  : 'alertTriangle',
                            ),
                            Text(
                              e['publicationDate'] != null
                                  ? context.t('sf.f.published', {
                                      'date': '${e['publicationDate']}',
                                    })
                                  : context.t('sf.f.publishedUnknown'),
                              style: context.text.caption,
                            ),
                          ],
                        ),
                      ],
                      if (limits.isNotEmpty) ...[
                        const SizedBox(height: Space.s2),
                        Text(
                          context.t('sf.f.limits'),
                          style: context.text.label,
                        ),
                        for (final l in limits)
                          Text(
                            '• ${code('sf.limit', l)}',
                            style: context.text.caption,
                          ),
                      ],
                    ],
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}
