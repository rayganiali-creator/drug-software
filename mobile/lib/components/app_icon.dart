import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../design/icons.g.dart';
import '../design/tokens.g.dart';

/// Same icon artwork as the web app (generated from design/icons.mjs).
/// Directional icons (chevrons, send, arrows) mirror automatically in RTL.
class AppIcon extends StatelessWidget {
  const AppIcon(
    this.name, {
    super.key,
    this.size = IconSizes.md,
    this.color,
    this.semanticLabel,
  });
  final String name;
  final double size;
  final Color? color;
  final String? semanticLabel;

  static String _svg(String inner) =>
      '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">$inner</svg>';

  @override
  Widget build(BuildContext context) {
    final inner = appIconPaths[name];
    assert(inner != null, 'Unknown icon "$name"');
    final c = color ?? IconTheme.of(context).color ?? const Color(0xFF000000);
    Widget w = SvgPicture.string(
      _svg(inner ?? ''),
      width: size,
      height: size,
      theme: SvgTheme(currentColor: c),
      semanticsLabel: semanticLabel,
    );
    if (directionalIcons.contains(name) &&
        Directionality.of(context) == TextDirection.rtl) {
      w = Transform.flip(flipX: true, child: w);
    }
    return ExcludeSemantics(
      excluding: semanticLabel == null,
      child: SizedBox(width: size, height: size, child: w),
    );
  }
}
