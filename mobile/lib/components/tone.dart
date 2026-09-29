import 'package:flutter/material.dart';

import '../design/theme.dart';

enum Tone { neutral, primary, accent, success, warning, danger, info }

/// Container / foreground / solid colours for a semantic tone (mirrors the web `data-tone` CSS).
class ToneColors {
  const ToneColors(this.bg, this.fg, this.solid, this.onSolid);
  final Color bg;
  final Color fg;
  final Color solid;
  final Color onSolid;
}

extension ToneContext on BuildContext {
  ToneColors tone(Tone t) {
    final c = colors;
    switch (t) {
      case Tone.neutral:
        return ToneColors(
          c.surfaceSunken,
          c.textSecondary,
          c.textSecondary,
          c.surface,
        );
      case Tone.primary:
        return ToneColors(
          c.primaryContainer,
          c.onPrimaryContainer,
          c.primary,
          c.onPrimary,
        );
      case Tone.accent:
        return ToneColors(
          c.accentContainer,
          c.onAccentContainer,
          c.accent,
          c.onAccent,
        );
      case Tone.success:
        return ToneColors(
          c.successContainer,
          c.onSuccessContainer,
          c.success,
          c.onSuccess,
        );
      case Tone.warning:
        return ToneColors(
          c.warningContainer,
          c.onWarningContainer,
          c.warning,
          c.onWarning,
        );
      case Tone.danger:
        return ToneColors(
          c.dangerContainer,
          c.onDangerContainer,
          c.danger,
          c.onDanger,
        );
      case Tone.info:
        return ToneColors(c.infoContainer, c.onInfoContainer, c.info, c.onInfo);
    }
  }
}
