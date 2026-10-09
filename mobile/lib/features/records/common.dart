import 'package:flutter/material.dart';

import '../../api/records_client.dart';
import '../../components/buttons.dart';
import '../../components/containers.dart';
import '../../components/display.dart';
import '../../components/health.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../design/tokens.g.dart';

/// What went wrong, in words a person can act on. Server codes are machine codes and are shown only through a translation.
String recordsErrorText(BuildContext context, Object error) {
  if (error is! RecordsException) return context.t('rec.err.server');
  for (final code in [error.code, ...error.errors]) {
    if (code == null) continue;
    final key = 'rec.err.${code.replaceAll('.', '_')}';
    if (context.app.strings.has(key)) return context.t(key);
  }
  return context.t(switch (error.kind) {
    RecordsErrorKind.conflict => 'rec.err.conflict',
    RecordsErrorKind.invalid => 'rec.err.invalid',
    RecordsErrorKind.unauthorized => 'rec.err.unauthorized',
    RecordsErrorKind.network => 'rec.err.network',
    RecordsErrorKind.rateLimited => 'rec.err.rate',
    RecordsErrorKind.notFound => 'rec.err.notFound',
    RecordsErrorKind.notAvailable => 'rec.err.notAvailable',
    _ => 'rec.err.server',
  });
}

/// Loads data with explicit Loading / Error / not-connected states and hands the builder a `reload` callback.
class RecordsLoader<T> extends StatefulWidget {
  const RecordsLoader({
    super.key,
    required this.load,
    required this.builder,
    this.rows = 2,
  });
  final Future<T> Function() load;
  final Widget Function(BuildContext context, T data, VoidCallback reload)
  builder;
  final int rows;
  @override
  State<RecordsLoader<T>> createState() => _RecordsLoaderState<T>();
}

class _RecordsLoaderState<T> extends State<RecordsLoader<T>> {
  late Future<T> _future = widget.load();
  void _reload() => setState(() => _future = widget.load());

  @override
  Widget build(BuildContext context) => FutureBuilder<T>(
    future: _future,
    builder: (context, snap) {
      if (snap.hasError) return RecordsErrorPanel(snap.error!, _reload);
      if (snap.connectionState != ConnectionState.done) {
        return LoadingState(label: context.t('common.loading'), rows: widget.rows);
      }
      return widget.builder(context, snap.data as T, _reload);
    },
  );
}

class RecordsErrorPanel extends StatelessWidget {
  const RecordsErrorPanel(this.error, this.onRetry, {super.key});
  final Object error;
  final VoidCallback onRetry;
  @override
  Widget build(BuildContext context) {
    final kind = error is RecordsException
        ? (error as RecordsException).kind
        : RecordsErrorKind.server;
    final retry = AppButton(
      label: context.t('common.retry'),
      variant: ButtonVariant.secondary,
      onPressed: onRetry,
    );
    return switch (kind) {
      RecordsErrorKind.notConnected => EmptyState(
        icon: 'lock',
        title: context.t('rec.notConnected.title'),
        body: context.t('rec.notConnected.body'),
        action: retry,
      ),
      RecordsErrorKind.unauthorized => EmptyState(
        icon: 'shield',
        title: context.t('rec.unauthorized.title'),
        body: context.t('rec.unauthorized.body'),
      ),
      _ => ErrorState(
        title: context.t('rec.error.title'),
        body: context.t('rec.error.body'),
        retryLabel: context.t('common.retry'),
        onRetry: onRetry,
      ),
    };
  }
}

/// Runs a write with a busy flag, shows a failure in an alert and calls [then] after success.
mixin ActionRunner<W extends StatefulWidget> on State<W> {
  bool busy = false;
  String? failure;

  Future<bool> act(Future<Object?> Function() fn, [VoidCallback? then]) async {
    setState(() {
      busy = true;
      failure = null;
    });
    try {
      await fn();
      if (!mounted) return true;
      setState(() => busy = false);
      then?.call();
      return true;
    } catch (e) {
      if (!mounted) return false;
      setState(() {
        busy = false;
        failure = recordsErrorText(context, e);
      });
      return false;
    }
  }

  Widget failureAlert() => failure == null
      ? const SizedBox.shrink()
      : Padding(
          padding: const EdgeInsets.only(top: Space.s2),
          child: AlertCard(
            title: context.t('rec.err.title'),
            body: failure,
            tone: Tone.danger,
          ),
        );
}

/// Section heading + optional DEMO flag.
class RecSection extends StatelessWidget {
  const RecSection({
    super.key,
    required this.title,
    this.subtitle,
    required this.child,
    this.demo = false,
  });
  final String title;
  final String? subtitle;
  final Widget child;
  final bool demo;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: Space.s4),
    child: AppCard(
      title: title,
      subtitle: subtitle,
      actions: demo ? const DemoBadge() : null,
      child: child,
    ),
  );
}

Tone severityTone(String s) => switch (s) {
  'Severe' => Tone.danger,
  'Moderate' => Tone.warning,
  'Mild' => Tone.info,
  _ => Tone.neutral,
};
