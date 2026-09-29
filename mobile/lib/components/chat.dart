import 'package:flutter/material.dart';

import '../design/theme.dart';
import '../design/tokens.g.dart';
import 'app_icon.dart';
import 'buttons.dart';
import 'containers.dart';

class ChatBubble extends StatelessWidget {
  const ChatBubble({
    super.key,
    required this.text,
    required this.isUser,
    this.refusal = false,
    this.semanticPrefix,
    this.attachment,
  });
  final String text;
  final bool isUser;
  final bool refusal;
  final String? semanticPrefix;
  final String? attachment;
  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    final radius = BorderRadiusDirectional.only(
      topStart: const Radius.circular(Radii.lg),
      topEnd: const Radius.circular(Radii.lg),
      bottomStart: Radius.circular(isUser ? Radii.lg : Radii.xs),
      bottomEnd: Radius.circular(isUser ? Radii.xs : Radii.lg),
    );
    final content = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          text,
          style: context.text.body.copyWith(
            color: isUser ? c.onPrimary : c.textPrimary,
          ),
        ),
        if (attachment != null)
          Padding(
            padding: const EdgeInsets.only(top: Space.s2),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                AppIcon('image', size: IconSizes.xs, color: c.onPrimary),
                const SizedBox(width: 4),
                Text(
                  attachment!,
                  style: context.text.caption.copyWith(color: c.onPrimary),
                ),
              ],
            ),
          ),
      ],
    );
    return Align(
      alignment: isUser
          ? AlignmentDirectional.centerEnd
          : AlignmentDirectional.centerStart,
      child: ConstrainedBox(
        constraints: BoxConstraints(
          maxWidth: MediaQuery.sizeOf(context).width * 0.88 > 560
              ? 560
              : MediaQuery.sizeOf(context).width * 0.88,
        ),
        child: Semantics(
          container: true,
          label: semanticPrefix,
          child: isUser
              ? Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: Space.s4,
                    vertical: Space.s3,
                  ),
                  decoration: BoxDecoration(
                    color: c.primary,
                    borderRadius: radius,
                  ),
                  child: content,
                )
              : StripedBox(
                  stripe: refusal ? c.warning : c.accent,
                  fill: c.surface,
                  borderColor: c.border,
                  radius: radius,
                  child: content,
                ),
        ),
      ),
    );
  }
}

/// Three-dot "assistant is working" indicator; a static indicator when animations are reduced.
class ThinkingDots extends StatefulWidget {
  const ThinkingDots({super.key, required this.label});
  final String label;
  @override
  State<ThinkingDots> createState() => _ThinkingDotsState();
}

class _ThinkingDotsState extends State<ThinkingDots>
    with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1200),
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
  Widget build(BuildContext context) => Semantics(
    label: widget.label,
    liveRegion: true,
    child: ExcludeSemantics(
      child: AnimatedBuilder(
        animation: _c,
        builder: (context, _) => Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            for (var i = 0; i < 3; i++)
              Container(
                width: 6,
                height: 6,
                margin: const EdgeInsets.symmetric(horizontal: 2),
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: context.colors.accent.withValues(
                    alpha: context.reducedMotion
                        ? 0.7
                        : 0.35 +
                              0.65 *
                                  ((_c.value * 3 - i).clamp(0, 1) *
                                      (1 - (_c.value * 3 - i - 1).clamp(0, 1))),
                  ),
                ),
              ),
          ],
        ),
      ),
    ),
  );
}

class MessageComposer extends StatelessWidget {
  const MessageComposer({
    super.key,
    required this.controller,
    required this.onSend,
    required this.onVoice,
    required this.onImage,
    required this.recording,
    required this.enabled,
    required this.labels,
    this.attachment,
    this.onRemoveAttachment,
  });
  final TextEditingController controller;
  final VoidCallback onSend;
  final VoidCallback onVoice;
  final VoidCallback onImage;
  final bool recording;
  final bool enabled;
  final ComposerLabels labels;
  final String? attachment;
  final VoidCallback? onRemoveAttachment;

  @override
  Widget build(BuildContext context) {
    final c = context.colors;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        if (attachment != null)
          Padding(
            padding: const EdgeInsets.only(bottom: Space.s2),
            child: Wrap(
              crossAxisAlignment: WrapCrossAlignment.center,
              spacing: Space.s2,
              children: [
                Container(
                  padding: const EdgeInsetsDirectional.symmetric(
                    horizontal: Space.s3,
                    vertical: 6,
                  ),
                  decoration: BoxDecoration(
                    color: c.accentContainer,
                    borderRadius: BorderRadius.circular(Radii.pill),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      AppIcon(
                        'image',
                        size: IconSizes.xs,
                        color: c.onAccentContainer,
                      ),
                      const SizedBox(width: 4),
                      Text(
                        attachment!,
                        style: context.text.label.copyWith(
                          color: c.onAccentContainer,
                        ),
                      ),
                      const SizedBox(width: 4),
                      Semantics(
                        button: true,
                        label: labels.removeAttachment,
                        child: GestureDetector(
                          onTap: onRemoveAttachment,
                          child: AppIcon(
                            'x',
                            size: IconSizes.xs,
                            color: c.onAccentContainer,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
                Text(labels.prototypeNote, style: context.text.caption),
              ],
            ),
          ),
        Container(
          padding: const EdgeInsetsDirectional.symmetric(
            horizontal: Space.s1,
            vertical: Space.s1,
          ),
          decoration: BoxDecoration(
            color: c.surface,
            borderRadius: BorderRadius.circular(Radii.xl),
            border: Border.all(color: c.borderControl),
          ),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              AppIconButton(
                icon: 'image',
                label: labels.image,
                onPressed: enabled ? onImage : null,
              ),
              Expanded(
                child: Semantics(
                  textField: true,
                  label: labels.input,
                  child: TextField(
                    controller: controller,
                    enabled: enabled,
                    minLines: 1,
                    maxLines: 5,
                    style: context.text.body,
                    textInputAction: TextInputAction.newline,
                    decoration: InputDecoration(
                      hintText: labels.placeholder,
                      hintStyle: context.text.body.copyWith(color: c.textMuted),
                      border: InputBorder.none,
                      isDense: true,
                      contentPadding: const EdgeInsetsDirectional.symmetric(
                        horizontal: Space.s2,
                        vertical: 12,
                      ),
                    ),
                  ),
                ),
              ),
              AppIconButton(
                icon: recording ? 'stop' : 'mic',
                label: recording ? labels.stopVoice : labels.voice,
                pressed: recording,
                onPressed: enabled ? onVoice : null,
              ),
              ValueListenableBuilder<TextEditingValue>(
                valueListenable: controller,
                builder: (context, v, _) => AppIconButton(
                  icon: 'send',
                  label: labels.send,
                  filled: true,
                  onPressed: enabled && v.text.trim().isNotEmpty
                      ? onSend
                      : null,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class ComposerLabels {
  const ComposerLabels({
    required this.input,
    required this.placeholder,
    required this.send,
    required this.voice,
    required this.stopVoice,
    required this.image,
    required this.removeAttachment,
    required this.prototypeNote,
  });
  final String input,
      placeholder,
      send,
      voice,
      stopVoice,
      image,
      removeAttachment,
      prototypeNote;
}
