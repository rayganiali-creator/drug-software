import 'package:flutter/material.dart';

import '../design/theme.dart';
import '../design/tokens.g.dart';
import 'app_icon.dart';
import 'buttons.dart';
import 'tone.dart';

class AppCard extends StatelessWidget {
  const AppCard({
    super.key,
    this.child,
    this.title,
    this.subtitle,
    this.actions,
    this.onTap,
    this.tone,
    this.ai = false,
    this.flush = false,
    this.padding,
    this.semanticLabel,
  });
  final Widget? child;
  final String? title;
  final String? subtitle;
  final Widget? actions;
  final VoidCallback? onTap;
  final Tone? tone; // tonal card when set
  final bool ai; // Iris gradient card (AI surfaces)
  final bool flush;
  final EdgeInsetsGeometry? padding;
  final String? semanticLabel;

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final tc = tone == null ? null : context.tone(tone!);
    final decoration = BoxDecoration(
      color: tc?.bg ?? (ai ? null : c.surface),
      gradient: ai
          ? LinearGradient(
              begin: AlignmentDirectional.topStart,
              end: AlignmentDirectional.bottomEnd,
              colors: [c.accentContainer, c.surface],
            )
          : null,
      borderRadius: BorderRadius.circular(Radii.lg),
      border: tc != null
          ? null
          : Border.all(
              color: ai
                  ? Color.alphaBlend(c.accent.withValues(alpha: 0.3), c.border)
                  : c.border,
            ),
      boxShadow: tc != null ? null : context.elevation(1),
    );
    final body = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        if (title != null || actions != null)
          Padding(
            padding: flush
                ? const EdgeInsetsDirectional.fromSTEB(
                    Space.s4,
                    Space.s4,
                    Space.s4,
                    Space.s2,
                  )
                : const EdgeInsetsDirectional.only(bottom: Space.s4),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      if (title != null)
                        Semantics(
                          header: true,
                          child: Text(
                            title!,
                            style: context.text.h4.copyWith(color: tc?.fg),
                          ),
                        ),
                      if (subtitle != null)
                        Text(
                          subtitle!,
                          style: context.text.caption.copyWith(color: tc?.fg),
                        ),
                    ],
                  ),
                ),
                ?actions,
              ],
            ),
          ),
        ?child,
      ],
    );
    final padded = Padding(
      padding: flush
          ? EdgeInsets.zero
          : (padding ?? const EdgeInsets.all(Space.s5)),
      child: body,
    );
    return Semantics(
      container: true,
      label: semanticLabel,
      child: DecoratedBox(
        decoration: decoration,
        child: ClipRRect(
          borderRadius: BorderRadius.circular(Radii.lg),
          child: onTap == null
              ? padded
              : Material(
                  type: MaterialType.transparency,
                  child: InkWell(onTap: onTap, child: padded),
                ),
        ),
      ),
    );
  }
}

/// Centered dialog (tablet/desktop) with focus handling from the framework.
Future<T?> showAppDialog<T>(
  BuildContext context, {
  required String title,
  required Widget body,
  List<Widget> actions = const [],
  required String closeLabel,
}) {
  return showDialog<T>(
    context: context,
    builder: (ctx) => Dialog(
      backgroundColor: ctx.colors.surfaceElevated,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(Radii.xl),
      ),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 480),
        child: Padding(
          padding: const EdgeInsets.all(Space.s6),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Semantics(
                      header: true,
                      child: Text(title, style: ctx.text.h3),
                    ),
                  ),
                  AppIconButton(
                    icon: 'x',
                    label: closeLabel,
                    onPressed: () => Navigator.of(ctx).pop(),
                  ),
                ],
              ),
              const SizedBox(height: Space.s3),
              Flexible(child: SingleChildScrollView(child: body)),
              if (actions.isNotEmpty) ...[
                const SizedBox(height: Space.s5),
                Wrap(
                  alignment: WrapAlignment.end,
                  spacing: Space.s2,
                  runSpacing: Space.s2,
                  children: actions,
                ),
              ],
            ],
          ),
        ),
      ),
    ),
  );
}

/// Modal bottom sheet: the primary overlay on phones.
Future<T?> showAppBottomSheet<T>(
  BuildContext context, {
  required String title,
  required Widget body,
  List<Widget> actions = const [],
  required String closeLabel,
}) {
  return showModalBottomSheet<T>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    showDragHandle: true,
    backgroundColor: context.colors.surfaceElevated,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(Radii.xl)),
    ),
    constraints: const BoxConstraints(maxWidth: 640),
    builder: (ctx) => Padding(
      padding: EdgeInsetsDirectional.fromSTEB(
        Space.s5,
        0,
        Space.s5,
        Space.s5 + MediaQuery.viewInsetsOf(ctx).bottom,
      ),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Semantics(
                    header: true,
                    child: Text(title, style: ctx.text.h3),
                  ),
                ),
                AppIconButton(
                  icon: 'x',
                  label: closeLabel,
                  onPressed: () => Navigator.of(ctx).pop(),
                ),
              ],
            ),
            const SizedBox(height: Space.s3),
            body,
            if (actions.isNotEmpty) ...[
              const SizedBox(height: Space.s5),
              Wrap(
                alignment: WrapAlignment.end,
                spacing: Space.s2,
                runSpacing: Space.s2,
                children: actions,
              ),
            ],
          ],
        ),
      ),
    ),
  );
}

/// Toast / Snackbar. With [actionLabel] it behaves as a snackbar with an undo-style action.
void showAppToast(
  BuildContext context,
  String message, {
  Tone tone = Tone.neutral,
  String? actionLabel,
  VoidCallback? onAction,
}) {
  final messenger = ScaffoldMessenger.of(context);
  messenger.hideCurrentSnackBar();
  messenger.showSnackBar(
    SnackBar(
      duration: Duration(seconds: actionLabel == null ? 4 : 7),
      content: Row(
        children: [
          AppIcon(
            tone == Tone.success
                ? 'checkCircle'
                : tone == Tone.danger
                ? 'alertTriangle'
                : 'info',
            size: IconSizes.sm,
            color: context.colors.background,
          ),
          const SizedBox(width: Space.s3),
          Expanded(child: Text(message)),
        ],
      ),
      action: actionLabel == null
          ? null
          : SnackBarAction(label: actionLabel, onPressed: onAction ?? () {}),
    ),
  );
}

/// Rounded box with a coloured stripe on the reading-start edge (AI answers, quotes).
/// Flutter cannot combine a border radius with a non-uniform border, so the stripe is drawn separately.
class StripedBox extends StatelessWidget {
  const StripedBox({
    super.key,
    required this.child,
    required this.stripe,
    required this.fill,
    this.borderColor,
    this.radius = const BorderRadiusDirectional.all(Radius.circular(Radii.lg)),
    this.padding = const EdgeInsetsDirectional.symmetric(
      horizontal: Space.s4,
      vertical: Space.s3,
    ),
  });
  final Widget child;
  final Color stripe;
  final Color fill;
  final Color? borderColor;
  final BorderRadiusGeometry radius;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: BoxDecoration(
        color: fill,
        borderRadius: radius,
        border: borderColor == null ? null : Border.all(color: borderColor!),
      ),
      child: ClipRRect(
        borderRadius: radius,
        child: IntrinsicHeight(
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Container(width: 3, color: stripe),
              Expanded(
                child: Padding(padding: padding, child: child),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
