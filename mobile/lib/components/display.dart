import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../design/theme.dart';
import '../design/tokens.g.dart';
import 'app_icon.dart';
import 'buttons.dart';
import 'containers.dart';
import 'tone.dart';

class AppAvatar extends StatelessWidget {
  const AppAvatar({
    super.key,
    required this.name,
    this.size = 44,
    this.tone = Tone.primary,
  });
  final String name;
  final double size;
  final Tone tone;
  @override
  Widget build(BuildContext context) {
    final tc = context.tone(tone);
    final initials = name
        .trim()
        .split(RegExp(r'\s+'))
        .take(2)
        .map((p) => p.characters.first)
        .join();
    return Semantics(
      label: name,
      image: true,
      child: ExcludeSemantics(
        child: Container(
          width: size,
          height: size,
          alignment: Alignment.center,
          decoration: BoxDecoration(color: tc.bg, shape: BoxShape.circle),
          child: Text(
            initials,
            style: context.text.label.copyWith(
              color: tc.fg,
              fontWeight: FontWeight.w700,
              fontSize: size * 0.36,
            ),
          ),
        ),
      ),
    );
  }
}

class AppBadge extends StatelessWidget {
  const AppBadge({
    super.key,
    required this.text,
    this.tone = Tone.neutral,
    this.icon,
    this.solid = false,
  });
  final String text;
  final Tone tone;
  final String? icon;
  final bool solid;
  @override
  Widget build(BuildContext context) {
    final tc = context.tone(tone);
    final fg = solid ? tc.onSolid : tc.fg;
    return Container(
      padding: const EdgeInsetsDirectional.symmetric(
        horizontal: Space.s2,
        vertical: 2,
      ),
      decoration: BoxDecoration(
        color: solid ? tc.solid : tc.bg,
        borderRadius: BorderRadius.circular(Radii.pill),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            AppIcon(icon!, size: IconSizes.xs, color: fg),
            const SizedBox(width: Space.s1),
          ],
          // Flexible: a long label (translated text can be long) wraps instead of overflowing a narrow screen.
          Flexible(
            child: Text(
              text,
              style: context.text.caption.copyWith(
                color: fg,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Numeric badge anchored to a child (e.g. notification count).
class AppBadgeAnchor extends StatelessWidget {
  const AppBadgeAnchor({
    super.key,
    required this.count,
    required this.label,
    required this.child,
  });
  final int count;
  final String label;
  final Widget child;
  @override
  Widget build(BuildContext context) => Stack(
    clipBehavior: Clip.none,
    children: [
      child,
      if (count > 0)
        PositionedDirectional(
          top: -2,
          end: -2,
          child: Semantics(
            label: label,
            child: ExcludeSemantics(
              child: AppBadge(text: '$count', tone: Tone.danger, solid: true),
            ),
          ),
        ),
    ],
  );
}

class AppChip extends StatelessWidget {
  const AppChip({
    super.key,
    required this.label,
    this.icon,
    this.selected,
    this.onTap,
    this.ai = false,
    this.onRemove,
    this.removeLabel,
  });
  final String label;
  final String? icon;
  final bool? selected;
  final VoidCallback? onTap;
  final bool ai;
  final VoidCallback? onRemove;
  final String? removeLabel;
  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final on = selected == true;
    final bg = ai
        ? c.accentContainer
        : on
        ? c.primaryContainer
        : c.surface;
    final fg = ai
        ? c.onAccentContainer
        : on
        ? c.onPrimaryContainer
        : c.textPrimary;
    final border = ai
        ? Color.alphaBlend(c.accent.withValues(alpha: 0.45), c.surface)
        : on
        ? c.primary
        : c.borderControl;
    final content = Container(
      constraints: const BoxConstraints(minHeight: ControlHeights.sm),
      padding: const EdgeInsetsDirectional.symmetric(horizontal: Space.s3),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(Radii.pill),
        border: Border.all(color: border),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            AppIcon(icon!, size: IconSizes.xs, color: fg),
            const SizedBox(width: Space.s1),
          ],
          Flexible(
            child: Text(label, style: context.text.label.copyWith(color: fg)),
          ),
          if (onRemove != null) ...[
            const SizedBox(width: Space.s1),
            Semantics(
              button: true,
              label: removeLabel,
              child: GestureDetector(
                onTap: onRemove,
                child: AppIcon('x', size: IconSizes.xs, color: fg),
              ),
            ),
          ],
        ],
      ),
    );
    if (onTap == null) return content;
    return Semantics(
      button: true,
      selected: selected,
      label: label,
      excludeSemantics: true,
      onTap: onTap,
      child: GestureDetector(
        onTap: onTap,
        behavior: HitTestBehavior.opaque,
        child: ConstrainedBox(
          constraints: const BoxConstraints(
            minHeight: ControlHeights.touchTargetMin,
          ),
          child: Center(widthFactor: 1, child: content),
        ),
      ),
    );
  }
}

class AppListTile extends StatelessWidget {
  const AppListTile({
    super.key,
    required this.title,
    this.meta,
    this.leading,
    this.trailing,
    this.onTap,
  });
  final String title;
  final String? meta;
  final Widget? leading;
  final Widget? trailing;
  final VoidCallback? onTap;
  @override
  Widget build(BuildContext context) {
    final row = Padding(
      padding: const EdgeInsetsDirectional.symmetric(
        horizontal: Space.s4,
        vertical: Space.s3,
      ),
      child: Row(
        children: [
          if (leading != null) ...[leading!, const SizedBox(width: Space.s3)],
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: context.text.body.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
                if (meta != null) Text(meta!, style: context.text.caption),
              ],
            ),
          ),
          ?trailing,
        ],
      ),
    );
    return MergeSemantics(
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: ControlHeights.lg),
        child: onTap == null ? row : InkWell(onTap: onTap, child: row),
      ),
    );
  }
}

class EmptyState extends StatelessWidget {
  const EmptyState({
    super.key,
    this.icon = 'inbox',
    required this.title,
    this.body,
    this.action,
  });
  final String icon;
  final String title;
  final String? body;
  final Widget? action;
  @override
  Widget build(BuildContext context) => _StateBody(
    icon: icon,
    title: title,
    body: body,
    action: action,
    tone: Tone.neutral,
  );
}

class ErrorState extends StatelessWidget {
  const ErrorState({
    super.key,
    required this.title,
    this.body,
    required this.retryLabel,
    this.onRetry,
  });
  final String title;
  final String? body;
  final String retryLabel;
  final VoidCallback? onRetry;
  @override
  Widget build(BuildContext context) => _StateBody(
    icon: 'alertTriangle',
    title: title,
    body: body,
    tone: Tone.danger,
    isAlert: true,
    action: onRetry == null
        ? null
        : AppButton(
            label: retryLabel,
            onPressed: onRetry,
            variant: ButtonVariant.secondary,
            iconStart: 'refresh',
          ),
  );
}

class _StateBody extends StatelessWidget {
  const _StateBody({
    required this.icon,
    required this.title,
    this.body,
    this.action,
    required this.tone,
    this.isAlert = false,
  });
  final String icon;
  final String title;
  final String? body;
  final Widget? action;
  final Tone tone;
  final bool isAlert;
  @override
  Widget build(BuildContext context) {
    final tc = context.tone(tone);
    return Semantics(
      liveRegion: isAlert,
      child: Padding(
        padding: const EdgeInsets.symmetric(
          vertical: Space.s10,
          horizontal: Space.s6,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 64,
              height: 64,
              decoration: BoxDecoration(color: tc.bg, shape: BoxShape.circle),
              child: Center(
                child: AppIcon(icon, size: IconSizes.lg, color: tc.fg),
              ),
            ),
            const SizedBox(height: Space.s3),
            Text(title, style: context.text.h4, textAlign: TextAlign.center),
            if (body != null) ...[
              const SizedBox(height: Space.s2),
              Text(
                body!,
                style: context.text.bodySmall.copyWith(
                  color: context.colors.textSecondary,
                ),
                textAlign: TextAlign.center,
              ),
            ],
            if (action != null) ...[const SizedBox(height: Space.s4), action!],
          ],
        ),
      ),
    );
  }
}

/// Shimmer placeholder. Static when the user prefers reduced motion.
class Skeleton extends StatefulWidget {
  const Skeleton({
    super.key,
    this.width,
    this.height = 16,
    this.radius = Radii.sm,
  });
  final double? width;
  final double height;
  final double radius;
  @override
  State<Skeleton> createState() => _SkeletonState();
}

class _SkeletonState extends State<Skeleton>
    with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1400),
  );
  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (context.reducedMotion) {
      _c.stop();
    } else if (!_c.isAnimating) {
      _c.repeat();
    }
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    return ExcludeSemantics(
      child: AnimatedBuilder(
        animation: _c,
        builder: (context, _) => Container(
          width: widget.width,
          height: widget.height,
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(widget.radius),
            gradient: LinearGradient(
              begin: AlignmentDirectional(-1 + 3 * _c.value, 0),
              end: AlignmentDirectional(1 + 3 * _c.value, 0),
              colors: [
                c.surfaceSunken,
                Color.alphaBlend(
                  c.surface.withValues(alpha: 0.5),
                  c.surfaceSunken,
                ),
                c.surfaceSunken,
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class LoadingState extends StatelessWidget {
  const LoadingState({super.key, required this.label, this.rows = 3});
  final String label;
  final int rows;
  @override
  Widget build(BuildContext context) => Semantics(
    label: label,
    liveRegion: true,
    child: Column(
      children: [
        for (var i = 0; i < rows; i++)
          const Padding(
            padding: EdgeInsets.only(bottom: Space.s4),
            child: AppCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Skeleton(width: 140, height: 18),
                  SizedBox(height: Space.s3),
                  Skeleton(),
                  SizedBox(height: Space.s2),
                  Skeleton(width: 220, height: 14),
                ],
              ),
            ),
          ),
      ],
    ),
  );
}

class AppProgress extends StatelessWidget {
  const AppProgress({
    super.key,
    required this.value,
    required this.label,
    this.tone = Tone.primary,
  });
  final double value; // 0..100
  final String label;
  final Tone tone;
  @override
  Widget build(BuildContext context) {
    final v = value.clamp(0, 100).toDouble();
    return Semantics(
      label: label,
      value: '${v.round()}%',
      child: ExcludeSemantics(
        child: ClipRRect(
          borderRadius: BorderRadius.circular(8),
          child: LinearProgressIndicator(
            value: v / 100,
            minHeight: 8,
            color: context.tone(tone).solid,
            backgroundColor: context.colors.surfaceSunken,
          ),
        ),
      ),
    );
  }
}

/// "Care Ring": circular progress, the product's signature motif.
class ProgressRing extends StatelessWidget {
  const ProgressRing({
    super.key,
    required this.value,
    required this.label,
    this.size = 120,
    this.stroke = 10,
    this.tone = Tone.primary,
    this.child,
  });
  final double value;
  final String label;
  final double size;
  final double stroke;
  final Tone tone;
  final Widget? child;
  @override
  Widget build(BuildContext context) {
    final v = value.clamp(0, 100).toDouble();
    final tc = context.tone(tone);
    return Semantics(
      label: label,
      value: '${v.round()}%',
      child: SizedBox(
        width: size,
        height: size,
        child: TweenAnimationBuilder<double>(
          tween: Tween(begin: 0, end: v / 100),
          duration: context.reducedMotion ? Duration.zero : Motion.slow,
          curve: Motion.decelerate,
          builder: (context, t, _) => CustomPaint(
            painter: _RingPainter(
              t,
              tc.solid,
              context.colors.surfaceSunken,
              stroke,
              Directionality.of(context) == TextDirection.rtl,
            ),
            child: Center(
              child: child == null ? null : ExcludeSemantics(child: child),
            ),
          ),
        ),
      ),
    );
  }
}

class _RingPainter extends CustomPainter {
  _RingPainter(this.t, this.color, this.track, this.stroke, this.rtl);
  final double t;
  final Color color;
  final Color track;
  final double stroke;
  final bool rtl;
  @override
  void paint(Canvas canvas, Size size) {
    final rect = Rect.fromLTWH(
      stroke / 2,
      stroke / 2,
      size.width - stroke,
      size.height - stroke,
    );
    final base = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = stroke
      ..color = track;
    canvas.drawArc(rect, 0, math.pi * 2, false, base);
    final arc = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = stroke
      ..strokeCap = StrokeCap.round
      ..color = color;
    // Sweep starts at 12 o'clock and follows reading direction.
    canvas.drawArc(
      rect,
      -math.pi / 2,
      (rtl ? -1 : 1) * math.pi * 2 * t,
      false,
      arc,
    );
  }

  @override
  bool shouldRepaint(_RingPainter old) =>
      old.t != t || old.color != color || old.track != track;
}

class TimelineEntry {
  const TimelineEntry({
    required this.title,
    this.meta,
    this.icon,
    this.tone = Tone.neutral,
    this.current = false,
    this.trailing,
  });
  final String title;
  final String? meta;
  final String? icon;
  final Tone tone;
  final bool current;
  final Widget? trailing;
}

class AppTimeline extends StatelessWidget {
  const AppTimeline({super.key, required this.entries});
  final List<TimelineEntry> entries;
  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    return Column(
      children: [
        for (var i = 0; i < entries.length; i++)
          Semantics(
            container: true,
            selected: entries[i].current,
            child: IntrinsicHeight(
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Column(
                    children: [
                      Builder(
                        builder: (context) {
                          final e = entries[i];
                          final tc = context.tone(e.tone);
                          return Container(
                            width: 28,
                            height: 28,
                            decoration: BoxDecoration(
                              shape: BoxShape.circle,
                              color: e.current ? c.primary : tc.bg,
                              border: Border.all(color: c.surface, width: 2),
                              boxShadow: [
                                BoxShadow(
                                  color: e.current
                                      ? c.primary.withValues(alpha: 0.25)
                                      : c.border,
                                  spreadRadius: e.current ? 4 : 1,
                                ),
                              ],
                            ),
                            child: e.icon == null
                                ? null
                                : Center(
                                    child: AppIcon(
                                      e.icon!,
                                      size: IconSizes.xs,
                                      color: e.current ? c.onPrimary : tc.fg,
                                    ),
                                  ),
                          );
                        },
                      ),
                      if (i != entries.length - 1)
                        Expanded(
                          child: Container(
                            width: 2,
                            margin: const EdgeInsets.only(top: 4),
                            color: c.border,
                          ),
                        ),
                    ],
                  ),
                  const SizedBox(width: Space.s3),
                  Expanded(
                    child: Padding(
                      padding: EdgeInsets.only(
                        bottom: i == entries.length - 1 ? 0 : Space.s4,
                      ),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  entries[i].title,
                                  style: context.text.body.copyWith(
                                    fontWeight: FontWeight.w600,
                                  ),
                                ),
                                if (entries[i].meta != null)
                                  Text(
                                    entries[i].meta!,
                                    style: context.text.caption,
                                  ),
                              ],
                            ),
                          ),
                          if (entries[i].trailing != null)
                            Flexible(child: entries[i].trailing!),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
      ],
    );
  }
}

class StatCard extends StatelessWidget {
  const StatCard({
    super.key,
    required this.label,
    required this.value,
    this.icon,
    this.hint,
  });
  final String label;
  final String value;
  final String? icon;
  final String? hint;
  @override
  Widget build(BuildContext context) => AppCard(
    child: MergeSemantics(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              if (icon != null) ...[
                AppIcon(
                  icon!,
                  size: IconSizes.sm,
                  color: context.colors.textSecondary,
                ),
                const SizedBox(width: Space.s2),
              ],
              Expanded(
                child: Text(
                  label,
                  style: context.text.label.copyWith(
                    color: context.colors.textSecondary,
                  ),
                ),
              ),
            ],
          ),
          Text(value, style: context.text.h1),
          if (hint != null) Text(hint!, style: context.text.caption),
        ],
      ),
    ),
  );
}

class AlertCard extends StatelessWidget {
  const AlertCard({
    super.key,
    required this.title,
    this.body,
    this.tone = Tone.info,
    this.icon,
    this.actions,
  });
  final String title;
  final String? body;
  final Tone tone;
  final String? icon;
  final List<Widget>? actions;
  @override
  Widget build(BuildContext context) {
    final tc = context.tone(tone);
    final iconName =
        icon ??
        (tone == Tone.warning || tone == Tone.danger
            ? 'alertTriangle'
            : tone == Tone.success
            ? 'checkCircle'
            : tone == Tone.accent
            ? 'sparkles'
            : 'info');
    return Semantics(
      container: true,
      liveRegion: tone == Tone.danger,
      child: Container(
        padding: const EdgeInsets.all(Space.s4),
        decoration: BoxDecoration(
          color: tc.bg,
          borderRadius: BorderRadius.circular(Radii.lg),
          border: Border.all(color: tc.solid.withValues(alpha: 0.3)),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Padding(
              padding: const EdgeInsets.only(top: 2),
              child: AppIcon(
                iconName,
                color: tone == Tone.warning ? tc.fg : tc.solid,
              ),
            ),
            const SizedBox(width: Space.s3),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    title,
                    style: context.text.body.copyWith(
                      color: tc.fg,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  if (body != null)
                    Text(
                      body!,
                      style: context.text.bodySmall.copyWith(color: tc.fg),
                    ),
                  if (actions != null) ...[
                    const SizedBox(height: Space.s3),
                    Wrap(
                      spacing: Space.s2,
                      runSpacing: Space.s2,
                      children: actions!,
                    ),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

// ---------- charts ----------
class ChartSeries {
  const ChartSeries(this.label, this.values, this.color);
  final String label;
  final List<num> values;
  final Color color;
}

class LineChart extends StatelessWidget {
  const LineChart({
    super.key,
    required this.series,
    required this.xLabels,
    required this.summary,
    this.yMax,
    this.height = 180,
  });
  final List<ChartSeries> series;
  final List<String> xLabels;
  final String summary;
  final double? yMax;
  final double height;
  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    return Semantics(
      label: summary,
      image: true,
      child: ExcludeSemantics(
        child: SizedBox(
          height: height,
          width: double.infinity,
          child: CustomPaint(
            painter: _LinePainter(
              series,
              xLabels,
              yMax,
              c.border,
              c.textSecondary,
              Directionality.of(context) == TextDirection.rtl,
              context.text.caption,
            ),
          ),
        ),
      ),
    );
  }
}

class _LinePainter extends CustomPainter {
  _LinePainter(
    this.series,
    this.labels,
    this.yMax,
    this.grid,
    this.text,
    this.rtl,
    this.style,
  );
  final List<ChartSeries> series;
  final List<String> labels;
  final double? yMax;
  final Color grid;
  final Color text;
  final bool rtl;
  final TextStyle style;
  @override
  void paint(Canvas canvas, Size size) {
    const l = 30.0, r = 8.0, t = 8.0, b = 22.0;
    final maxV =
        yMax ??
        series
            .expand((s) => s.values)
            .fold<num>(1, (a, b) => math.max(a, b))
            .toDouble();
    final top = (maxV / 5).ceil() * 5.0;
    final w = size.width - l - r, h = size.height - t - b;
    double x(int i, int n) =>
        l + (n <= 1 ? 0.5 : (rtl ? 1 - i / (n - 1) : i / (n - 1))) * w;
    double y(num v) => t + h - (v / top) * h;
    final gridPaint = Paint()
      ..color = grid
      ..strokeWidth = 1;
    for (final tick in [0.0, 0.5, 1.0]) {
      canvas.drawLine(
        Offset(l, y(top * tick)),
        Offset(size.width - r, y(top * tick)),
        gridPaint,
      );
      final tp = TextPainter(
        text: TextSpan(
          text: (top * tick).round().toString(),
          style: style.copyWith(fontSize: 10, color: text),
        ),
        textDirection: TextDirection.ltr,
      )..layout();
      tp.paint(
        canvas,
        Offset(rtl ? size.width - r - 0 : 0, y(top * tick) - tp.height / 2),
      );
    }
    final step = math.max(1, (labels.length / 6).ceil());
    for (var i = 0; i < labels.length; i += step) {
      final tp = TextPainter(
        text: TextSpan(
          text: labels[i],
          style: style.copyWith(fontSize: 10, color: text),
        ),
        textDirection: TextDirection.ltr,
      )..layout();
      tp.paint(
        canvas,
        Offset(x(i, labels.length) - tp.width / 2, size.height - tp.height),
      );
    }
    for (final s in series) {
      final path = Path();
      for (var i = 0; i < s.values.length; i++) {
        final p = Offset(x(i, s.values.length), y(s.values[i]));
        i == 0 ? path.moveTo(p.dx, p.dy) : path.lineTo(p.dx, p.dy);
      }
      canvas.drawPath(
        path,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2.5
          ..strokeJoin = StrokeJoin.round
          ..strokeCap = StrokeCap.round
          ..color = s.color,
      );
    }
  }

  @override
  bool shouldRepaint(_LinePainter old) => true;
}

class Sparkline extends StatelessWidget {
  const Sparkline({
    super.key,
    required this.values,
    required this.label,
    this.color,
    this.width = 96,
    this.height = 28,
  });
  final List<num> values;
  final String label;
  final Color? color;
  final double width;
  final double height;
  @override
  Widget build(BuildContext context) => Semantics(
    label: label,
    image: true,
    child: ExcludeSemantics(
      child: CustomPaint(
        size: Size(width, height),
        painter: _SparkPainter(
          values,
          color ?? context.colors.chart1,
          Directionality.of(context) == TextDirection.rtl,
        ),
      ),
    ),
  );
}

class _SparkPainter extends CustomPainter {
  _SparkPainter(this.values, this.color, this.rtl);
  final List<num> values;
  final Color color;
  final bool rtl;
  @override
  void paint(Canvas canvas, Size size) {
    if (values.isEmpty) return;
    final minV = values.map((v) => v.toDouble()).reduce(math.min);
    final span = math.max(
      1.0,
      values.map((v) => v.toDouble()).reduce(math.max) - minV,
    );
    final path = Path();
    for (var i = 0; i < values.length; i++) {
      final t = values.length <= 1 ? 0.5 : i / (values.length - 1);
      final p = Offset(
        (rtl ? 1 - t : t) * (size.width - 4) + 2,
        size.height - 3 - ((values[i] - minV) / span) * (size.height - 6),
      );
      i == 0 ? path.moveTo(p.dx, p.dy) : path.lineTo(p.dx, p.dy);
    }
    canvas.drawPath(
      path,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2
        ..strokeCap = StrokeCap.round
        ..strokeJoin = StrokeJoin.round
        ..color = color,
    );
  }

  @override
  bool shouldRepaint(_SparkPainter old) =>
      old.values != values || old.color != color || old.rtl != rtl;
}

class ChartCard extends StatelessWidget {
  const ChartCard({
    super.key,
    required this.title,
    this.subtitle,
    required this.series,
    required this.child,
    this.footer,
  });
  final String title;
  final String? subtitle;
  final List<ChartSeries> series;
  final Widget child;
  final String? footer;
  @override
  Widget build(BuildContext context) => AppCard(
    title: title,
    subtitle: subtitle,
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Wrap(
          spacing: Space.s3,
          children: [
            for (final s in series)
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Container(
                    width: 10,
                    height: 10,
                    decoration: BoxDecoration(
                      color: s.color,
                      borderRadius: BorderRadius.circular(3),
                    ),
                  ),
                  const SizedBox(width: Space.s1),
                  Text(s.label, style: context.text.caption),
                ],
              ),
          ],
        ),
        const SizedBox(height: Space.s3),
        child,
        if (footer != null) ...[
          const SizedBox(height: Space.s2),
          Text(footer!, style: context.text.caption),
        ],
      ],
    ),
  );
}
