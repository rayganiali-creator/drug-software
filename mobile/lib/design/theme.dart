import 'package:flutter/cupertino.dart' show CupertinoPageTransitionsBuilder;
import 'package:flutter/material.dart';

import 'tokens.g.dart';

/// Design tokens exposed to widgets through the theme (`context.colors`, `context.text`).
@immutable
class AppTokens extends ThemeExtension<AppTokens> {
  const AppTokens({required this.colors, required this.elevations});

  final AppColors colors;
  final Map<int, List<BoxShadow>> elevations;

  @override
  AppTokens copyWith({
    AppColors? colors,
    Map<int, List<BoxShadow>>? elevations,
  }) => AppTokens(
    colors: colors ?? this.colors,
    elevations: elevations ?? this.elevations,
  );

  @override
  AppTokens lerp(ThemeExtension<AppTokens>? other, double t) =>
      t < 0.5 ? this : (other as AppTokens? ?? this);
}

extension TokenContext on BuildContext {
  AppTokens get tokens => Theme.of(this).extension<AppTokens>()!;
  AppColors get colors => tokens.colors;
  List<BoxShadow> elevation(int level) => tokens.elevations[level] ?? const [];

  /// True on window widths below the `medium` breakpoint.
  bool get isCompact => MediaQuery.sizeOf(this).width < Breakpoints.medium;
  bool get reducedMotion => MediaQuery.disableAnimationsOf(this);
  bool get isRtl => Directionality.of(this) == TextDirection.rtl;
  AppText get text => AppText._(this);
}

/// Type scale from tokens; display/h1-h3 shrink on compact widths (same rule as the web CSS).
class AppText {
  const AppText._(this._context);
  final BuildContext _context;

  TextStyle _s(TypeToken t, {Color? color}) => TextStyle(
    fontFamily: TypeScale.fontFamily,
    fontSize: t.size,
    height: t.height,
    fontWeight: t.weight,
    color: color ?? _context.colors.textPrimary,
  );

  TypeToken _pick(TypeToken normal, TypeToken? compact) =>
      _context.isCompact && compact != null ? compact : normal;

  TextStyle get display =>
      _s(_pick(TypeScale.display, TypeScaleCompact.display));
  TextStyle get h1 => _s(_pick(TypeScale.h1, TypeScaleCompact.h1));
  TextStyle get h2 => _s(_pick(TypeScale.h2, TypeScaleCompact.h2));
  TextStyle get h3 => _s(_pick(TypeScale.h3, TypeScaleCompact.h3));
  TextStyle get h4 => _s(TypeScale.h4);
  TextStyle get body => _s(TypeScale.body);
  TextStyle get bodySmall => _s(TypeScale.bodySmall);
  TextStyle get caption =>
      _s(TypeScale.caption, color: _context.colors.textSecondary);
  TextStyle get label => _s(TypeScale.label);
  TextStyle get button => _s(TypeScale.button);
}

ThemeData buildTheme(Brightness brightness) {
  final c = brightness == Brightness.dark ? AppColors.dark : AppColors.light;
  final e = brightness == Brightness.dark ? Elevations.dark : Elevations.light;
  TextStyle t(TypeToken k, {Color? color}) => TextStyle(
    fontFamily: TypeScale.fontFamily,
    fontSize: k.size,
    height: k.height,
    fontWeight: k.weight,
    color: color ?? c.textPrimary,
  );
  final scheme = ColorScheme(
    brightness: brightness,
    primary: c.primary,
    onPrimary: c.onPrimary,
    primaryContainer: c.primaryContainer,
    onPrimaryContainer: c.onPrimaryContainer,
    secondary: c.secondary,
    onSecondary: c.onSecondary,
    secondaryContainer: c.secondaryContainer,
    onSecondaryContainer: c.onSecondaryContainer,
    tertiary: c.accent,
    onTertiary: c.onAccent,
    tertiaryContainer: c.accentContainer,
    onTertiaryContainer: c.onAccentContainer,
    error: c.danger,
    onError: c.onDanger,
    errorContainer: c.dangerContainer,
    onErrorContainer: c.onDangerContainer,
    surface: c.surface,
    onSurface: c.textPrimary,
    onSurfaceVariant: c.textSecondary,
    surfaceContainerHighest: c.surfaceSunken,
    outline: c.borderControl,
    outlineVariant: c.border,
    scrim: c.scrim,
  );
  return ThemeData(
    useMaterial3: true,
    brightness: brightness,
    colorScheme: scheme,
    scaffoldBackgroundColor: c.background,
    fontFamily: TypeScale.fontFamily,
    textTheme: TextTheme(
      displayLarge: t(TypeScale.display),
      headlineLarge: t(TypeScale.h1),
      headlineMedium: t(TypeScale.h2),
      headlineSmall: t(TypeScale.h3),
      titleLarge: t(TypeScale.h3),
      titleMedium: t(TypeScale.h4),
      titleSmall: t(TypeScale.label),
      bodyLarge: t(TypeScale.body),
      bodyMedium: t(TypeScale.bodySmall),
      bodySmall: t(TypeScale.caption, color: c.textSecondary),
      labelLarge: t(TypeScale.button),
      labelMedium: t(TypeScale.label),
      labelSmall: t(TypeScale.caption),
    ),
    dividerColor: c.border,
    focusColor: c.focusRing,
    splashFactory: InkRipple.splashFactory,
    // Native feel per platform: Cupertino swipe-back on iOS, predictive back on Android.
    pageTransitionsTheme: const PageTransitionsTheme(
      builders: {
        TargetPlatform.android: PredictiveBackPageTransitionsBuilder(),
        TargetPlatform.iOS: CupertinoPageTransitionsBuilder(),
        TargetPlatform.macOS: CupertinoPageTransitionsBuilder(),
      },
    ),
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      backgroundColor: c.textPrimary,
      contentTextStyle: t(TypeScale.bodySmall, color: c.background),
      actionTextColor: c.background,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(Radii.md),
      ),
    ),
    tooltipTheme: TooltipThemeData(
      decoration: BoxDecoration(
        color: c.textPrimary,
        borderRadius: BorderRadius.circular(Radii.sm),
      ),
      textStyle: t(TypeScale.caption, color: c.background),
    ),
    extensions: [AppTokens(colors: c, elevations: e)],
  );
}
