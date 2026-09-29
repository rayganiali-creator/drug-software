import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../components/app_icon.dart';
import '../../components/screen.dart';
import '../../core/app_scope.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';

class _Dest {
  const _Dest(this.icon, this.labelKey, {this.ai = false});
  final String icon;
  final String labelKey;
  final bool ai;
}

const _dests = [
  _Dest('home', 'nav.home'),
  _Dest('pill', 'nav.medications'),
  _Dest('sparkles', 'nav.assistant', ai: true),
  _Dest('checkCircle', 'nav.checkin'),
  _Dest('user', 'nav.profile'),
];

/// Adaptive frame: bottom bar on phones, navigation rail from the `medium` breakpoint.
class AppShell extends StatelessWidget {
  const AppShell({super.key, required this.shell});
  final StatefulNavigationShell shell;

  void _go(int i) =>
      shell.goBranch(i, initialLocation: i == shell.currentIndex);

  @override
  Widget build(BuildContext context) {
    final wide = !context.isCompact;
    return Scaffold(
      body: Column(
        children: [
          const SafeArea(bottom: false, child: DemoBanner()),
          Expanded(
            child: wide
                ? Row(
                    children: [
                      _Rail(index: shell.currentIndex, onSelect: _go),
                      VerticalDivider(width: 1, color: context.colors.border),
                      Expanded(child: shell),
                    ],
                  )
                : shell,
          ),
        ],
      ),
      bottomNavigationBar: wide
          ? null
          : _BottomBar(index: shell.currentIndex, onSelect: _go),
    );
  }
}

class _BottomBar extends StatelessWidget {
  const _BottomBar({required this.index, required this.onSelect});
  final int index;
  final ValueChanged<int> onSelect;
  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    return Semantics(
      key: const Key('bottom-bar'),
      container: true,
      label: context.t('nav.primary'),
      child: Container(
        decoration: BoxDecoration(
          color: c.surface,
          border: Border(top: BorderSide(color: c.border)),
        ),
        child: SafeArea(
          top: false,
          // Labels are tiny and the bar is fixed-height: cap text scaling here only.
          child: MediaQuery.withClampedTextScaling(
            maxScaleFactor: 1.2,
            child: SizedBox(
              height: LayoutTokens.bottomNavHeight,
              child: Row(
                children: [
                  for (var i = 0; i < _dests.length; i++)
                    Expanded(
                      child: Semantics(
                        button: true,
                        selected: i == index,
                        label: context.t(_dests[i].labelKey),
                        excludeSemantics: true,
                        onTap: () => onSelect(i),
                        child: InkWell(
                          onTap: () => onSelect(i),
                          child: Column(
                            mainAxisAlignment: MainAxisAlignment.center,
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              _dests[i].ai
                                  ? Transform.translate(
                                      offset: const Offset(0, -10),
                                      child: Container(
                                        width: 48,
                                        height: 48,
                                        decoration: BoxDecoration(
                                          color: c.accent,
                                          shape: BoxShape.circle,
                                          boxShadow: context.elevation(3),
                                          border: i == index
                                              ? Border.all(
                                                  color: c.accent.withValues(
                                                    alpha: 0.35,
                                                  ),
                                                  width: 3,
                                                )
                                              : null,
                                        ),
                                        child: Center(
                                          child: AppIcon(
                                            _dests[i].icon,
                                            color: c.onAccent,
                                          ),
                                        ),
                                      ),
                                    )
                                  : Container(
                                      width: 56,
                                      height: 30,
                                      decoration: BoxDecoration(
                                        color: i == index
                                            ? c.primaryContainer
                                            : Colors.transparent,
                                        borderRadius: BorderRadius.circular(15),
                                      ),
                                      child: Center(
                                        child: AppIcon(
                                          _dests[i].icon,
                                          color: i == index
                                              ? c.onPrimaryContainer
                                              : c.textSecondary,
                                        ),
                                      ),
                                    ),
                              Transform.translate(
                                offset: Offset(0, _dests[i].ai ? -10 : 0),
                                child: Text(
                                  context.t(_dests[i].labelKey),
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: context.text.caption.copyWith(
                                    fontSize: 11,
                                    fontWeight: FontWeight.w600,
                                    color: i == index
                                        ? (_dests[i].ai ? c.accent : c.primary)
                                        : c.textSecondary,
                                  ),
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _Rail extends StatelessWidget {
  const _Rail({required this.index, required this.onSelect});
  final int index;
  final ValueChanged<int> onSelect;
  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    return Semantics(
      key: const Key('nav-rail'),
      container: true,
      label: context.t('nav.primary'),
      child: Container(
        width: LayoutTokens.railWidth,
        color: c.surface,
        child: SafeArea(
          child: Column(
            children: [
              const SizedBox(height: Space.s4),
              for (var i = 0; i < _dests.length; i++)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 4),
                  child: Semantics(
                    button: true,
                    selected: i == index,
                    label: context.t(_dests[i].labelKey),
                    excludeSemantics: true,
                    onTap: () => onSelect(i),
                    child: Tooltip(
                      message: context.t(_dests[i].labelKey),
                      child: InkWell(
                        borderRadius: BorderRadius.circular(Radii.md),
                        onTap: () => onSelect(i),
                        child: Container(
                          width: 52,
                          height: 52,
                          decoration: BoxDecoration(
                            color: i == index
                                ? (_dests[i].ai
                                      ? c.accentContainer
                                      : c.primaryContainer)
                                : Colors.transparent,
                            borderRadius: BorderRadius.circular(Radii.md),
                          ),
                          child: Center(
                            child: AppIcon(
                              _dests[i].icon,
                              color: i == index
                                  ? (_dests[i].ai
                                        ? c.onAccentContainer
                                        : c.onPrimaryContainer)
                                  : c.textSecondary,
                            ),
                          ),
                        ),
                      ),
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
