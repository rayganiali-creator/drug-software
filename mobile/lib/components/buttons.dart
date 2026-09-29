import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../design/theme.dart';
import '../design/tokens.g.dart';
import 'app_icon.dart';

enum ButtonVariant { primary, secondary, tonal, ai, ghost, danger }

enum ButtonSize { sm, md, lg }

class AppButton extends StatelessWidget {
  const AppButton({
    super.key,
    required this.label,
    required this.onPressed,
    this.variant = ButtonVariant.primary,
    this.size = ButtonSize.md,
    this.iconStart,
    this.iconEnd,
    this.loading = false,
    this.block = false,
    this.haptic = false,
  });

  final String label;
  final VoidCallback? onPressed;
  final ButtonVariant variant;
  final ButtonSize size;
  final String? iconStart;
  final String? iconEnd;
  final bool loading;
  final bool block;
  final bool haptic;

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final (bg, fg, border) = switch (variant) {
      ButtonVariant.primary => (c.primary, c.onPrimary, Colors.transparent),
      ButtonVariant.secondary => (c.surface, c.textPrimary, c.borderControl),
      ButtonVariant.tonal => (
        c.primaryContainer,
        c.onPrimaryContainer,
        Colors.transparent,
      ),
      ButtonVariant.ai => (c.accent, c.onAccent, Colors.transparent),
      ButtonVariant.ghost => (
        Colors.transparent,
        c.primary,
        Colors.transparent,
      ),
      ButtonVariant.danger => (c.danger, c.onDanger, Colors.transparent),
    };
    final height = switch (size) {
      ButtonSize.sm => ControlHeights.sm,
      ButtonSize.md => ControlHeights.md,
      ButtonSize.lg => ControlHeights.lg,
    };
    final enabled = onPressed != null && !loading;
    final style = context.text.button.copyWith(
      color: fg,
      fontSize: size == ButtonSize.sm ? 13 : null,
    );
    final child = Row(
      mainAxisSize: block ? MainAxisSize.max : MainAxisSize.min,
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        if (loading)
          SizedBox(
            width: 18,
            height: 18,
            child: CircularProgressIndicator(strokeWidth: 2, color: fg),
          )
        else if (iconStart != null)
          AppIcon(iconStart!, size: IconSizes.sm, color: fg),
        if (loading || iconStart != null) const SizedBox(width: Space.s2),
        Flexible(
          child: Text(label, style: style, overflow: TextOverflow.ellipsis),
        ),
        if (iconEnd != null && !loading) ...[
          const SizedBox(width: Space.s2),
          AppIcon(iconEnd!, size: IconSizes.sm, color: fg),
        ],
      ],
    );
    return Semantics(
      button: true,
      enabled: enabled,
      label: label,
      excludeSemantics: true,
      onTap: enabled ? onPressed : null,
      child: Opacity(
        opacity: onPressed == null ? 0.5 : 1,
        child: Material(
          color: bg,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(
              size == ButtonSize.lg ? Radii.lg : Radii.md,
            ),
            side: BorderSide(color: border),
          ),
          child: InkWell(
            onTap: enabled
                ? () {
                    if (haptic) HapticFeedback.lightImpact();
                    onPressed!();
                  }
                : null,
            borderRadius: BorderRadius.circular(
              size == ButtonSize.lg ? Radii.lg : Radii.md,
            ),
            child: ConstrainedBox(
              constraints: BoxConstraints(
                minHeight: height < ControlHeights.touchTargetMin
                    ? ControlHeights.touchTargetMin
                    : height,
                minWidth: ControlHeights.touchTargetMin,
              ),
              child: Padding(
                padding: EdgeInsetsDirectional.symmetric(
                  horizontal: size == ButtonSize.sm
                      ? Space.s3
                      : size == ButtonSize.lg
                      ? Space.s6
                      : Space.s5,
                ),
                child: child,
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class AppIconButton extends StatelessWidget {
  const AppIconButton({
    super.key,
    required this.icon,
    required this.label,
    required this.onPressed,
    this.filled = false,
    this.pressed = false,
    this.color,
  });
  final String icon;
  final String label;
  final VoidCallback? onPressed;
  final bool filled;
  final bool pressed;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final bg = filled
        ? c.primary
        : pressed
        ? c.primaryContainer
        : Colors.transparent;
    final fg =
        color ??
        (filled
            ? c.onPrimary
            : pressed
            ? c.onPrimaryContainer
            : c.textSecondary);
    return Tooltip(
      message: label,
      child: Semantics(
        button: true,
        enabled: onPressed != null,
        label: label,
        toggled: pressed ? true : null,
        excludeSemantics: true,
        onTap: onPressed,
        child: Opacity(
          opacity: onPressed == null ? 0.5 : 1,
          child: Material(
            color: bg,
            borderRadius: BorderRadius.circular(Radii.md),
            child: InkWell(
              onTap: onPressed,
              borderRadius: BorderRadius.circular(Radii.md),
              child: SizedBox(
                width: ControlHeights.md,
                height: ControlHeights.md,
                child: Center(child: AppIcon(icon, color: fg)),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
