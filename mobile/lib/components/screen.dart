import 'package:flutter/material.dart';

import '../core/app_scope.dart';
import '../design/theme.dart';
import '../design/tokens.g.dart';
import 'app_icon.dart';
import 'display.dart';

/// Always-visible reminder that all data is fictional.
class DemoBanner extends StatelessWidget {
  const DemoBanner({super.key});
  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    // Persistent chrome: cap text scaling so it cannot take over small screens.
    return MediaQuery.withClampedTextScaling(
      maxScaleFactor: 1.3,
      child: Semantics(
        container: true,
        child: Container(
          width: double.infinity,
          color: c.warningContainer,
          padding: const EdgeInsetsDirectional.symmetric(
            horizontal: Space.s3,
            vertical: Space.s1,
          ),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              AppIcon(
                'alertTriangle',
                size: IconSizes.xs,
                color: c.onWarningContainer,
              ),
              const SizedBox(width: Space.s2),
              Flexible(
                child: Text(
                  context.t('demo.banner'),
                  textAlign: TextAlign.center,
                  style: context.text.caption.copyWith(
                    color: c.onWarningContainer,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Page frame: title (a semantic header), optional actions, responsive gutters and max width.
class ScreenScaffold extends StatelessWidget {
  const ScreenScaffold({
    super.key,
    required this.title,
    this.subtitle,
    this.actions = const [],
    required this.children,
    this.leading,
    this.bottom,
    this.visuallyHiddenTitle = false,
  });
  final String title;
  final String? subtitle;
  final List<Widget> actions;
  final List<Widget> children;
  final Widget? leading;
  final Widget? bottom; // pinned below the scroll area (e.g. composer)
  final bool visuallyHiddenTitle;

  @override
  Widget build(BuildContext context) {
    final width = MediaQuery.sizeOf(context).width;
    final gutter = width >= Breakpoints.expanded
        ? LayoutTokens.gutterExpanded
        : width >= Breakpoints.medium
        ? LayoutTokens.gutterMedium
        : LayoutTokens.gutterCompact;
    final header = Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (leading != null) ...[leading!, const SizedBox(width: Space.s2)],
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Semantics(
                header: true,
                child: Text(title, style: context.text.h1),
              ),
              if (subtitle != null)
                Text(
                  subtitle!,
                  style: context.text.bodySmall.copyWith(
                    color: context.colors.textSecondary,
                  ),
                ),
            ],
          ),
        ),
        ...actions,
      ],
    );
    return SafeArea(
      bottom: false,
      child: Column(
        children: [
          Expanded(
            child: Align(
              alignment: Alignment.topCenter,
              child: ConstrainedBox(
                constraints: const BoxConstraints(
                  maxWidth: LayoutTokens.contentMaxWidth,
                ),
                child: ListView(
                  padding: EdgeInsetsDirectional.fromSTEB(
                    gutter,
                    Space.s4,
                    gutter,
                    Space.s8,
                  ),
                  children: [
                    if (visuallyHiddenTitle)
                      Semantics(
                        header: true,
                        child: SizedBox(
                          height: 0,
                          child: Text(
                            title,
                            style: const TextStyle(fontSize: 0),
                          ),
                        ),
                      )
                    else
                      header,
                    if (!visuallyHiddenTitle) const SizedBox(height: Space.s5),
                    ...children,
                  ],
                ),
              ),
            ),
          ),
          ?bottom,
        ],
      ),
    );
  }
}

/// Loads a future and shows loading / error / content. Supports reload and local updates.
class AsyncView<T> extends StatefulWidget {
  const AsyncView({
    super.key,
    required this.load,
    required this.builder,
    this.loadingRows = 2,
  });
  final Future<T> Function() load;
  final Widget Function(BuildContext context, T data, void Function(T) update)
  builder;
  final int loadingRows;
  @override
  State<AsyncView<T>> createState() => _AsyncViewState<T>();
}

class _AsyncViewState<T> extends State<AsyncView<T>> {
  late Future<T> _future = widget.load();
  T? _override;

  void _reload() => setState(() {
    _override = null;
    _future = widget.load();
  });

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<T>(
      future: _future,
      builder: (context, snap) {
        if (snap.hasError) {
          return ErrorState(
            title: context.t('common.errorTitle'),
            body: context.t('common.errorBody'),
            retryLabel: context.t('common.retry'),
            onRetry: _reload,
          );
        }
        if (snap.connectionState != ConnectionState.done) {
          return LoadingState(
            label: context.t('common.loading'),
            rows: widget.loadingRows,
          );
        }
        return widget.builder(
          context,
          _override ?? snap.requireData,
          (v) => setState(() => _override = v),
        );
      },
    );
  }
}

/// Lays children out in equal columns on wide screens, stacks them on compact.
class ResponsiveColumns extends StatelessWidget {
  const ResponsiveColumns({
    super.key,
    required this.children,
    this.minColumnWidth = 320,
    this.gap = Space.s4,
  });
  final List<Widget> children;
  final double minColumnWidth;
  final double gap;
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, box) {
      final cols = (box.maxWidth / minColumnWidth).floor().clamp(
        1,
        children.length,
      );
      if (cols <= 1) {
        return Column(
          children: [
            for (var i = 0; i < children.length; i++)
              Padding(
                padding: EdgeInsets.only(
                  bottom: i == children.length - 1 ? 0 : gap,
                ),
                child: children[i],
              ),
          ],
        );
      }
      final colWidth = (box.maxWidth - gap * (cols - 1)) / cols;
      return Wrap(
        spacing: gap,
        runSpacing: gap,
        children: [
          for (final c in children) SizedBox(width: colWidth, child: c),
        ],
      );
    },
  );
}
