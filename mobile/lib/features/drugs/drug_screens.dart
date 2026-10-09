import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../api/medication_client.dart';
import '../../components/app_icon.dart';
import '../../components/buttons.dart';
import '../../components/containers.dart';
import '../../components/display.dart';
import '../../components/inputs.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../core/formatters.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';

const _statusTone = {
  'Demo': Tone.warning,
  'Validated': Tone.success,
  'Unverified': Tone.danger,
  'NeedsValidation': Tone.warning,
  'Rejected': Tone.danger,
};
const _statusKey = {
  'Demo': 'kn.status.demo',
  'Validated': 'kn.status.validated',
  'Unverified': 'kn.status.unverified',
  'NeedsValidation': 'kn.status.needs',
  'Rejected': 'kn.status.rejected',
};
const _sevTone = {
  'Unknown': Tone.neutral,
  'Minor': Tone.info,
  'Moderate': Tone.warning,
  'Major': Tone.danger,
  'Contraindicated': Tone.danger,
};

/// Validation state as text + icon (never colour alone). Demo and unverified data can never look "official".
class StatusBadge extends StatelessWidget {
  const StatusBadge(this.status, {super.key});
  final String status;
  @override
  Widget build(BuildContext context) => AppBadge(
    text: context.t(_statusKey[status] ?? 'kn.status.unverified'),
    tone: _statusTone[status] ?? Tone.danger,
    icon: status == 'Validated' ? 'check' : 'alertTriangle',
  );
}

class _ErrorPanel extends StatelessWidget {
  const _ErrorPanel({required this.error, required this.onRetry});
  final Object error;
  final VoidCallback onRetry;
  @override
  Widget build(BuildContext context) {
    final kind = error is KnowledgeException
        ? (error as KnowledgeException).kind
        : KnowledgeErrorKind.server;
    final retry = AppButton(
      label: context.t('common.retry'),
      variant: ButtonVariant.secondary,
      onPressed: onRetry,
    );
    return switch (kind) {
      KnowledgeErrorKind.notConnected => EmptyState(
        icon: 'lock',
        title: context.t('kn.notConnected.title'),
        body: context.t('kn.notConnected.bodyApp'),
        action: retry,
      ),
      KnowledgeErrorKind.unauthorized => EmptyState(
        icon: 'shield',
        title: context.t('kn.unauthorized.title'),
      ),
      KnowledgeErrorKind.rateLimited => EmptyState(
        icon: 'clock',
        title: context.t('kn.rate.title'),
        body: context.t('kn.rate.body'),
        action: retry,
      ),
      KnowledgeErrorKind.invalid => EmptyState(
        icon: 'search',
        title: context.t('kn.invalid.title'),
        body: context.t('kn.invalid.body'),
      ),
      KnowledgeErrorKind.notFound => EmptyState(
        icon: 'search',
        title: context.t('kn.notFound.title'),
        body: context.t('kn.notFound.body'),
      ),
      _ => ErrorState(
        title: context.t('kn.error.title'),
        body: context.t('kn.error.body'),
        retryLabel: context.t('common.retry'),
        onRetry: onRetry,
      ),
    };
  }
}

/// Drug reference search against the real API. Debounced; list + detail side by side on wide windows (tablet, desktop).
class DrugSearchScreen extends StatefulWidget {
  const DrugSearchScreen({
    super.key,
    this.debounce = const Duration(milliseconds: 300),
  });
  final Duration debounce;
  @override
  State<DrugSearchScreen> createState() => _DrugSearchScreenState();
}

class _DrugSearchScreenState extends State<DrugSearchScreen> {
  static const _page = 20;
  final _controller = TextEditingController();
  Timer? _timer;
  int _seq = 0;
  String _query = '';
  Object? _error;
  List<MedicationSummary>? _items;
  int _total = 0;
  bool _loading = true;
  bool _loadingMore = false;
  String? _selected;
  bool _started = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_started) {
      _started = true;
      _load();
    }
  }

  @override
  void dispose() {
    _timer?.cancel();
    _controller.dispose();
    super.dispose();
  }

  void _onChanged(String text) {
    _timer?.cancel();
    _timer = Timer(widget.debounce, () {
      final q = text.trim();
      if (q == _query) {
        return;
      }
      setState(() => _query = q);
      if (q.length == 1) {
        return; // too short: no request (the API would answer 400)
      }
      _load();
    });
  }

  Future<void> _load() async {
    final seq = ++_seq;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final r = await context.app.medications.search(_query, limit: _page);
      if (!mounted || seq != _seq) return;
      setState(() {
        _items = r.items;
        _total = r.total;
        _loading = false;
      });
    } catch (e) {
      if (!mounted || seq != _seq) return;
      setState(() {
        _error = e;
        _loading = false;
      });
    }
  }

  Future<void> _more() async {
    final seq = _seq;
    setState(() => _loadingMore = true);
    try {
      final r = await context.app.medications.search(
        _query,
        limit: _page,
        offset: _items?.length ?? 0,
      );
      if (!mounted || seq != _seq) return;
      setState(() {
        _items = [...?_items, ...r.items];
        _total = r.total;
        _loadingMore = false;
      });
    } catch (e) {
      if (!mounted || seq != _seq) return;
      setState(() {
        _error = e;
        _loadingMore = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final lang = context.app.locale.languageCode;
    final wide = MediaQuery.sizeOf(context).width >= Breakpoints.expanded;
    final tooShort = _query.length == 1;
    final items = _items;

    final list = <Widget>[
      AppSearchField(
        label: context.t('kn.search'),
        controller: _controller,
        onChanged: _onChanged,
      ),
      const SizedBox(height: Space.s3),
      if (tooShort)
        Text(context.t('kn.invalid.body'), style: context.text.bodySmall)
      else if (_loading)
        LoadingState(label: context.t('common.loading'), rows: 3)
      else if (_error != null)
        _ErrorPanel(error: _error!, onRetry: _load)
      else if (items != null && items.isEmpty)
        EmptyState(
          icon: 'search',
          title: context.t('kn.empty.title'),
          body: context.t('kn.empty.body'),
        )
      else if (items != null) ...[
        Semantics(
          liveRegion: true,
          child: Text(
            context.t('kn.showing', {
              'from': context.fmt.number(1),
              'to': context.fmt.number(items.length),
              'total': context.fmt.number(_total),
            }),
            style: context.text.bodySmall,
          ),
        ),
        const SizedBox(height: Space.s2),
        for (final m in items)
          Padding(
            padding: const EdgeInsets.only(bottom: Space.s2),
            child: _ResultCard(
              m: m,
              lang: lang,
              selected: wide && _selected == m.id,
              onTap: () => wide
                  ? setState(() => _selected = m.id)
                  : context.push('/medications/reference/${m.id}'),
            ),
          ),
        if (items.length < _total)
          AppButton(
            label: context.t('kn.more'),
            variant: ButtonVariant.secondary,
            loading: _loadingMore,
            onPressed: _loadingMore ? null : _more,
          ),
      ],
    ];

    if (!wide) {
      return ScreenScaffold(
        title: context.t('kn.title'),
        subtitle: context.t('kn.sub'),
        leading: AppIconButton(
          icon: 'chevronLeft',
          label: context.t('common.back'),
          onPressed: () =>
              context.canPop() ? context.pop() : context.go('/medications'),
        ),
        children: list,
      );
    }
    // Desktop / large tablet: two panes, not a stretched phone layout.
    return ScreenScaffold(
      title: context.t('kn.title'),
      subtitle: context.t('kn.sub'),
      children: [
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: 380,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: list,
              ),
            ),
            const SizedBox(width: Space.s5),
            Expanded(
              child: _selected == null
                  ? AppCard(
                      child: EmptyState(
                        icon: 'pill',
                        title: context.t('kn.pickOne'),
                      ),
                    )
                  : DrugDetailBody(key: ValueKey(_selected), id: _selected!),
            ),
          ],
        ),
      ],
    );
  }
}

class _ResultCard extends StatelessWidget {
  const _ResultCard({
    required this.m,
    required this.lang,
    required this.selected,
    required this.onTap,
  });
  final MedicationSummary m;
  final String lang;
  final bool selected;
  final VoidCallback onTap;
  @override
  Widget build(BuildContext context) {
    final meta = [
      m.brand?.of(lang) ?? context.t('kn.d.generic'),
      m.form.of(lang),
      if (m.strength.isNotEmpty) isolateLtr(m.strength),
    ].where((e) => e.isNotEmpty).join(' · ');
    return Semantics(
      button: true,
      selected: selected,
      label:
          '${m.name.of(lang)}, $meta, ${context.t(_statusKey[m.validation] ?? 'kn.status.unverified')}',
      excludeSemantics: true,
      child: AppCard(
        onTap: onTap,
        tone: selected ? Tone.primary : null,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              m.name.of(lang),
              style: context.text.body.copyWith(fontWeight: FontWeight.w700),
            ),
            Text(meta, style: context.text.bodySmall),
            Text(
              m.ingredients.map((i) => i.of(lang)).join(' + '),
              style: context.text.caption,
            ),
            const SizedBox(height: Space.s2),
            Wrap(
              spacing: Space.s1,
              runSpacing: Space.s1,
              children: [
                StatusBadge(m.validation),
                if (m.lifecycle != 'Active')
                  AppBadge(
                    text: context.t(
                      m.lifecycle == 'Inactive'
                          ? 'kn.life.inactive'
                          : 'kn.life.draft',
                    ),
                  ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// Phone/tablet route for one medicine.
class DrugDetailScreen extends StatelessWidget {
  const DrugDetailScreen({super.key, required this.id});
  final String id;
  @override
  Widget build(BuildContext context) => ScreenScaffold(
    title: context.t('kn.title'),
    leading: AppIconButton(
      icon: 'chevronLeft',
      label: context.t('kn.back'),
      onPressed: () => context.canPop()
          ? context.pop()
          : context.go('/medications/reference'),
    ),
    children: [DrugDetailBody(id: id)],
  );
}

class DrugDetailBody extends StatefulWidget {
  const DrugDetailBody({super.key, required this.id});
  final String id;
  @override
  State<DrugDetailBody> createState() => _DrugDetailBodyState();
}

class _DrugDetailBodyState extends State<DrugDetailBody> {
  Future<MedicationDetail>? _future;

  void _reload() =>
      setState(() => _future = context.app.medications.detail(widget.id));

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _future ??= context.app.medications.detail(widget.id);
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<MedicationDetail>(
    future: _future,
    builder: (context, snap) {
      if (snap.connectionState != ConnectionState.done) {
        return LoadingState(label: context.t('common.loading'), rows: 2);
      }
      if (snap.hasError) {
        return _ErrorPanel(error: snap.error!, onRetry: _reload);
      }
      return _DetailView(d: snap.data!);
    },
  );
}

class _DetailView extends StatelessWidget {
  const _DetailView({required this.d});
  final MedicationDetail d;

  @override
  Widget build(BuildContext context) {
    final lang = context.app.locale.languageCode;
    final noticeKey = d.isDemo
        ? 'kn.notice.demo'
        : d.validation == 'Validated'
        ? 'kn.notice.validated'
        : 'kn.notice.unverified';
    final kinds = {for (final s in d.statements) s.kind}.toList();
    String sourceName(String id) =>
        d.sources.where((s) => s.id == id).map((s) => s.name).firstOrNull ?? '';

    Widget section(String title, List<Widget> children) => Padding(
      padding: const EdgeInsets.only(bottom: Space.s4),
      child: AppCard(
        title: title,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: children,
        ),
      ),
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        AlertCard(
          tone: d.isDemo || d.validation != 'Validated'
              ? Tone.warning
              : Tone.info,
          title: context.t(noticeKey),
        ),
        const SizedBox(height: Space.s4),
        Semantics(
          header: true,
          child: Text(d.name.of(lang), style: context.text.h2),
        ),
        const SizedBox(height: Space.s2),
        Wrap(
          spacing: Space.s1,
          children: [
            StatusBadge(d.validation),
            if (d.lifecycle != 'Active')
              AppBadge(
                text: context.t(
                  d.lifecycle == 'Inactive'
                      ? 'kn.life.inactive'
                      : 'kn.life.draft',
                ),
              ),
          ],
        ),
        Text(
          context.t('kn.d.updated', {
            'date': d.updatedAt == null ? '—' : context.fmt.date(d.updatedAt!),
            'n': context.fmt.number(d.version),
          }),
          style: context.text.bodySmall,
        ),
        const SizedBox(height: Space.s4),
        section(context.t('kn.d.form'), [
          _Fact(
            context.t('kn.d.brand'),
            d.brand?.of(lang) ?? context.t('kn.d.generic'),
          ),
          _Fact(context.t('kn.d.form'), d.form.of(lang)),
          _Fact(
            context.t('kn.d.route'),
            d.routes.map((r) => r.of(lang)).join('، '),
          ),
          _Fact(context.t('kn.d.strength'), d.strength, ltr: true),
          if (d.manufacturer != null)
            _Fact(context.t('kn.d.manufacturer'), d.manufacturer!.of(lang)),
        ]),
        section(context.t('kn.d.ingredients'), [
          for (final i in d.ingredients)
            _Fact(i.name.of(lang), i.strength ?? '—', ltr: true),
        ]),
        section(context.t('kn.d.statements'), [
          for (final k in kinds) ...[
            Semantics(
              header: true,
              child: Text(context.t('kn.kind.$k'), style: context.text.h3),
            ),
            for (final s in d.statements.where((s) => s.kind == k))
              Padding(
                padding: const EdgeInsets.symmetric(vertical: Space.s1),
                child: Wrap(
                  spacing: Space.s2,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    Text(s.text.of(lang), style: context.text.body),
                    StatusBadge(s.validation),
                    Text(sourceName(s.sourceId), style: context.text.caption),
                  ],
                ),
              ),
            const SizedBox(height: Space.s2),
          ],
          if (d.missingKinds.isNotEmpty)
            Semantics(
              container: true,
              label: context.t('kn.d.missing'),
              child: Container(
                width: double.infinity,
                padding: const EdgeInsets.all(Space.s3),
                decoration: BoxDecoration(
                  border: Border.all(color: context.colors.borderControl),
                  borderRadius: BorderRadius.circular(Radii.lg),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        const AppIcon('info', size: IconSizes.sm),
                        const SizedBox(width: Space.s1),
                        Expanded(
                          child: Text(
                            context.t('kn.d.missing'),
                            style: context.text.h3,
                          ),
                        ),
                      ],
                    ),
                    Text(
                      context.t('kn.d.missingHint', {
                        'kinds': d.missingKinds
                            .map((k) => context.t('kn.kind.$k'))
                            .join('، '),
                      }),
                      style: context.text.bodySmall,
                    ),
                  ],
                ),
              ),
            ),
        ]),
        section(context.t('kn.d.interactions'), [
          if (d.interactions.isEmpty)
            Text(
              context.t('kn.d.noInteractions'),
              style: context.text.bodySmall,
            )
          else
            for (final i in d.interactions)
              Padding(
                padding: const EdgeInsets.only(bottom: Space.s3),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Wrap(
                      spacing: Space.s2,
                      children: [
                        AppBadge(
                          text: context.t('kn.sev.${i.severity}'),
                          tone: _sevTone[i.severity] ?? Tone.neutral,
                          icon: 'alertTriangle',
                        ),
                        Text(
                          context.t('kn.d.with', {'name': i.other.of(lang)}),
                          style: context.text.body.copyWith(
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ],
                    ),
                    Text(i.mechanism.of(lang), style: context.text.body),
                    Text(i.management.of(lang), style: context.text.bodySmall),
                    const SizedBox(height: Space.s1),
                    StatusBadge(i.validation),
                  ],
                ),
              ),
        ]),
        section(context.t('kn.d.identifiers'), [
          if (d.identifiers.isEmpty)
            Text(context.t('kn.d.noIdentifiers'), style: context.text.bodySmall)
          else
            for (final i in d.identifiers)
              Text(i, textDirection: TextDirection.ltr),
        ]),
        section(context.t('kn.d.sources'), [
          for (final s in d.sources)
            Padding(
              padding: const EdgeInsets.only(bottom: Space.s2),
              child: Text(
                '${s.name} · ${s.publisher} · ${context.t('kn.d.sourceVersion', {'v': s.version})} · ${context.t('kn.d.licence', {'name': s.licence})}',
                style: context.text.bodySmall,
              ),
            ),
        ]),
      ],
    );
  }
}

class _Fact extends StatelessWidget {
  const _Fact(this.label, this.value, {this.ltr = false});
  final String label;
  final String value;
  final bool ltr;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: Space.s1),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(flex: 2, child: Text(label, style: context.text.bodySmall)),
        Expanded(
          flex: 3,
          child: Text(
            value.isEmpty ? '—' : value,
            textDirection: ltr ? TextDirection.ltr : null,
            style: context.text.body.copyWith(fontWeight: FontWeight.w600),
          ),
        ),
      ],
    ),
  );
}
