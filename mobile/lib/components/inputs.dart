import 'package:flutter/material.dart';

import '../design/theme.dart';
import '../design/tokens.g.dart';
import 'app_icon.dart';

class AppTextField extends StatelessWidget {
  const AppTextField({
    super.key,
    this.label,
    this.hint,
    this.error,
    this.controller,
    this.icon,
    this.onChanged,
    this.minLines = 1,
    this.maxLines = 1,
    this.keyboardType,
    this.textInputAction,
  });
  final String? label;
  final String? hint;
  final String? error;
  final TextEditingController? controller;
  final String? icon;
  final ValueChanged<String>? onChanged;
  final int minLines;
  final int maxLines;
  final TextInputType? keyboardType;
  final TextInputAction? textInputAction;

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    OutlineInputBorder border(Color color, [double w = 1]) =>
        OutlineInputBorder(
          borderRadius: BorderRadius.circular(Radii.md),
          borderSide: BorderSide(color: color, width: w),
        );
    return TextField(
      controller: controller,
      onChanged: onChanged,
      minLines: minLines,
      maxLines: maxLines,
      keyboardType: keyboardType,
      textInputAction: textInputAction,
      style: context.text.body,
      cursorColor: c.primary,
      decoration: InputDecoration(
        labelText: label,
        hintText: hint,
        helperText: error == null ? hint : null,
        errorText: error,
        labelStyle: context.text.label.copyWith(color: c.textSecondary),
        hintStyle: context.text.body.copyWith(color: c.textMuted),
        floatingLabelBehavior: FloatingLabelBehavior.always,
        prefixIcon: icon == null
            ? null
            : Padding(
                padding: const EdgeInsetsDirectional.only(
                  start: Space.s3,
                  end: Space.s2,
                ),
                child: AppIcon(icon!, size: IconSizes.sm, color: c.textMuted),
              ),
        prefixIconConstraints: const BoxConstraints(minWidth: 0, minHeight: 0),
        filled: true,
        fillColor: c.surface,
        contentPadding: const EdgeInsetsDirectional.symmetric(
          horizontal: Space.s3,
          vertical: Space.s3,
        ),
        border: border(c.borderControl),
        enabledBorder: border(c.borderControl),
        focusedBorder: border(c.focusRing, 2),
        errorBorder: border(c.danger),
        focusedErrorBorder: border(c.danger, 2),
      ),
    );
  }
}

class AppSearchField extends StatelessWidget {
  const AppSearchField({
    super.key,
    required this.label,
    required this.controller,
    this.onChanged,
  });
  final String label;
  final TextEditingController controller;
  final ValueChanged<String>? onChanged;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      textField: true,
      label: label,
      child: AppTextField(
        controller: controller,
        hint: label,
        icon: 'search',
        onChanged: onChanged,
        textInputAction: TextInputAction.search,
      ),
    );
  }
}

class AppSelect<T> extends StatelessWidget {
  const AppSelect({
    super.key,
    required this.label,
    required this.value,
    required this.items,
    required this.onChanged,
  });
  final String label;
  final T value;
  final Map<T, String> items;
  final ValueChanged<T> onChanged;

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    return InputDecorator(
      decoration: InputDecoration(
        labelText: label,
        labelStyle: context.text.label.copyWith(color: c.textSecondary),
        floatingLabelBehavior: FloatingLabelBehavior.always,
        filled: true,
        fillColor: c.surface,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(Radii.md),
          borderSide: BorderSide(color: c.borderControl),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(Radii.md),
          borderSide: BorderSide(color: c.borderControl),
        ),
        contentPadding: const EdgeInsetsDirectional.symmetric(
          horizontal: Space.s3,
          vertical: Space.s1,
        ),
      ),
      child: DropdownButtonHideUnderline(
        child: DropdownButton<T>(
          value: value,
          isExpanded: true,
          dropdownColor: c.surfaceElevated,
          borderRadius: BorderRadius.circular(Radii.md),
          icon: AppIcon('chevronDown', size: IconSizes.sm, color: c.textMuted),
          style: context.text.body,
          items: [
            for (final e in items.entries)
              DropdownMenuItem<T>(value: e.key, child: Text(e.value)),
          ],
          onChanged: (v) {
            if (v != null) onChanged(v);
          },
        ),
      ),
    );
  }
}

class AppCheckbox extends StatelessWidget {
  const AppCheckbox({
    super.key,
    required this.label,
    required this.value,
    required this.onChanged,
  });
  final String label;
  final bool value;
  final ValueChanged<bool>? onChanged;
  @override
  Widget build(BuildContext context) => MergeSemantics(
    child: ConstrainedBox(
      constraints: const BoxConstraints(
        minHeight: ControlHeights.touchTargetMin,
      ),
      child: CheckboxListTile(
        value: value,
        onChanged: onChanged == null ? null : (v) => onChanged!(v ?? false),
        title: Text(label, style: context.text.body),
        controlAffinity: ListTileControlAffinity.leading,
        contentPadding: EdgeInsets.zero,
        activeColor: context.colors.primary,
        checkColor: context.colors.onPrimary,
        side: BorderSide(color: context.colors.borderControl, width: 2),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(Radii.xs),
        ),
      ),
    ),
  );
}

class AppRadioGroup<T> extends StatelessWidget {
  const AppRadioGroup({
    super.key,
    required this.legend,
    required this.value,
    required this.options,
    required this.onChanged,
  });
  final String legend;
  final T value;
  final Map<T, String> options;
  final ValueChanged<T> onChanged;
  @override
  Widget build(BuildContext context) => Semantics(
    container: true,
    label: legend,
    child: RadioGroup<T>(
      groupValue: value,
      onChanged: (v) {
        if (v != null) onChanged(v);
      },
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(legend, style: context.text.label),
          for (final e in options.entries)
            ConstrainedBox(
              constraints: const BoxConstraints(
                minHeight: ControlHeights.touchTargetMin,
              ),
              child: RadioListTile<T>(
                value: e.key,
                title: Text(e.value, style: context.text.body),
                contentPadding: EdgeInsets.zero,
                activeColor: context.colors.primary,
              ),
            ),
        ],
      ),
    ),
  );
}

/// Uses the native look on each platform (Cupertino switch on iOS, Material on Android).
class AppSwitch extends StatelessWidget {
  const AppSwitch({
    super.key,
    required this.label,
    required this.value,
    required this.onChanged,
  });
  final String label;
  final bool value;
  final ValueChanged<bool> onChanged;
  @override
  Widget build(BuildContext context) => MergeSemantics(
    child: ConstrainedBox(
      constraints: const BoxConstraints(
        minHeight: ControlHeights.touchTargetMin,
      ),
      child: Row(
        children: [
          Expanded(child: Text(label, style: context.text.body)),
          Switch.adaptive(
            value: value,
            onChanged: onChanged,
            activeTrackColor: context.colors.primary,
            activeThumbColor: context.colors.onPrimary,
          ),
        ],
      ),
    ),
  );
}

class AppSegmented<T> extends StatelessWidget {
  const AppSegmented({
    super.key,
    required this.label,
    required this.value,
    required this.options,
    required this.onChanged,
  });
  final String label;
  final T value;
  final Map<T, String> options;
  final ValueChanged<T> onChanged;
  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    return Semantics(
      container: true,
      label: label,
      child: Container(
        padding: const EdgeInsets.all(3),
        decoration: BoxDecoration(
          color: c.surfaceSunken,
          borderRadius: BorderRadius.circular(Radii.md),
        ),
        child: SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              for (final e in options.entries)
                Semantics(
                  button: true,
                  selected: e.key == value,
                  child: GestureDetector(
                    onTap: () => onChanged(e.key),
                    behavior: HitTestBehavior.opaque,
                    child: AnimatedContainer(
                      duration: context.reducedMotion
                          ? Duration.zero
                          : Motion.fast,
                      constraints: const BoxConstraints(
                        minHeight: ControlHeights.touchTargetMin - 6,
                      ),
                      padding: const EdgeInsetsDirectional.symmetric(
                        horizontal: Space.s3,
                      ),
                      alignment: Alignment.center,
                      decoration: BoxDecoration(
                        color: e.key == value ? c.surface : Colors.transparent,
                        borderRadius: BorderRadius.circular(Radii.sm),
                        boxShadow: e.key == value ? context.elevation(1) : null,
                      ),
                      child: Text(
                        e.value,
                        style: context.text.label.copyWith(
                          fontWeight: e.key == value
                              ? FontWeight.w600
                              : FontWeight.w500,
                          color: e.key == value
                              ? c.textPrimary
                              : c.textSecondary,
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

/// Tab bar + panels (indexed). Selected tab uses the primary colour and an underline.
class AppTabs extends StatefulWidget {
  const AppTabs({super.key, required this.tabs});
  final List<(String, Widget)> tabs;
  @override
  State<AppTabs> createState() => _AppTabsState();
}

class _AppTabsState extends State<AppTabs> {
  int _index = 0;
  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: Row(
            children: [
              for (var i = 0; i < widget.tabs.length; i++)
                Semantics(
                  button: true,
                  selected: i == _index,
                  child: InkWell(
                    onTap: () => setState(() => _index = i),
                    child: Container(
                      constraints: const BoxConstraints(
                        minHeight: ControlHeights.touchTargetMin,
                      ),
                      padding: const EdgeInsetsDirectional.symmetric(
                        horizontal: Space.s4,
                      ),
                      alignment: Alignment.center,
                      decoration: BoxDecoration(
                        border: Border(
                          bottom: BorderSide(
                            color: i == _index ? c.primary : Colors.transparent,
                            width: 3,
                          ),
                        ),
                      ),
                      child: Text(
                        widget.tabs[i].$1,
                        style: context.text.label.copyWith(
                          color: i == _index ? c.primary : c.textSecondary,
                          fontWeight: i == _index
                              ? FontWeight.w600
                              : FontWeight.w500,
                        ),
                      ),
                    ),
                  ),
                ),
            ],
          ),
        ),
        Divider(height: 1, color: c.border),
        const SizedBox(height: Space.s4),
        widget.tabs[_index].$2,
      ],
    );
  }
}
