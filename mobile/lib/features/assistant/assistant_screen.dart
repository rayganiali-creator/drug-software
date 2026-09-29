import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../components/app_icon.dart';
import '../../components/buttons.dart';
import '../../components/chat.dart';
import '../../components/containers.dart';
import '../../components/display.dart';
import '../../components/health.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../core/l10n.dart';
import '../../core/models.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';

/// Reveals text progressively; immediate when the user prefers reduced motion.
class StreamedText extends StatefulWidget {
  const StreamedText({
    super.key,
    required this.text,
    required this.animate,
    required this.onDone,
    required this.style,
  });
  final String text;
  final bool animate;
  final VoidCallback onDone;
  final TextStyle style;
  @override
  State<StreamedText> createState() => _StreamedTextState();
}

class _StreamedTextState extends State<StreamedText>
    with SingleTickerProviderStateMixin {
  // Frame-driven (not a Timer) so it stays in sync with rendering and tests can settle it.
  late final AnimationController _c;
  late final List<String> _words;
  bool _started = false;
  bool _notified = false;

  void _finish() {
    if (_notified) return;
    _notified = true;
    // Parent setState must not happen while this widget is building.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) widget.onDone();
    });
  }

  @override
  void initState() {
    super.initState();
    _words = widget.text.split(RegExp(r'(?<=\s)'));
    _c = AnimationController(
      vsync: this,
      duration: Duration(milliseconds: 18 * _words.length + 1),
    );
    _c.addStatusListener((status) {
      if (status == AnimationStatus.completed) _finish();
    });
    if (!widget.animate) _c.value = 1;
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (widget.animate && !_started) {
      _started = true;
      if (context.reducedMotion) {
        _c.value = 1;
      } else {
        _c.forward();
      }
    }
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: _c,
    builder: (context, _) => Text(
      _words.take((_c.value * _words.length).ceil()).join(),
      style: widget.style,
    ),
  );
}

class AssistantScreen extends StatefulWidget {
  const AssistantScreen({super.key, this.initialQuestion, this.drugId});
  final String? initialQuestion;
  final String? drugId;
  @override
  State<AssistantScreen> createState() => _AssistantScreenState();
}

class _AssistantScreenState extends State<AssistantScreen> {
  final _input = TextEditingController();
  final _scroll = ScrollController();
  final List<ChatMessage> _messages = [];
  final Set<String> _escalated = {};
  bool _thinking = false;
  bool _recording = false;
  String? _attachment;
  String? _animateId;
  String? _contextDrugId;
  Timer? _voiceTimer;
  late Future<List<Localized>> _quick;
  late Future<List<Conversation>> _history;

  @override
  void initState() {
    super.initState();
    _contextDrugId = widget.drugId;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (widget.initialQuestion != null && mounted) {
        _send(widget.initialQuestion!);
      }
    });
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _quick = context.services.ai.quickQuestions();
    _history = context.services.ai.conversations();
  }

  @override
  void dispose() {
    _voiceTimer?.cancel();
    _input.dispose();
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _send(String raw) async {
    final text = raw.trim();
    if (text.isEmpty || _thinking) return;
    final svc = context.services;
    final locale = context.app.locale.languageCode;
    final attach = _attachment;
    setState(() {
      _input.clear();
      _attachment = null;
      _messages.add(
        ChatMessage.user(Localized(text, text), attachment: attach),
      );
      _thinking = true;
    });
    _scrollDown();
    try {
      final a = await svc.ai.ask(
        text: text,
        locale: locale,
        contextDrugId: _contextDrugId,
      );
      if (!mounted) return;
      setState(() {
        _animateId = a.id;
        _messages.add(ChatMessage.assistant(a, id: a.id));
      });
    } catch (_) {
      if (mounted) {
        showAppToast(context, context.t('ai.error'), tone: Tone.danger);
      }
    } finally {
      if (mounted) setState(() => _thinking = false);
      _scrollDown();
    }
  }

  void _scrollDown() => WidgetsBinding.instance.addPostFrameCallback((_) {
    if (!_scroll.hasClients) return;
    final target = _scroll.position.maxScrollExtent;
    context.reducedMotion
        ? _scroll.jumpTo(target)
        : _scroll.animateTo(
            target,
            duration: Motion.base,
            curve: Motion.standard,
          );
  });

  void _toggleVoice() {
    if (_recording) {
      _voiceTimer?.cancel();
      setState(() => _recording = false);
      return;
    }
    setState(() => _recording = true);
    // Prototype: no microphone is used; a sample phrase is inserted.
    _voiceTimer = Timer(const Duration(milliseconds: 1600), () {
      if (!mounted) return;
      setState(() {
        _recording = false;
        _input.text = context.t('ai.voiceSample');
      });
    });
  }

  void _showEvidence(AssistantAnswer a) {
    showAppBottomSheet<void>(
      context,
      title: context.t('ai.evidenceTitle'),
      closeLabel: context.t('common.close'),
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          ConfidenceMeter(level: a.confidence),
          const SizedBox(height: Space.s2),
          Text(context.t('ai.confidence.explain'), style: context.text.caption),
          const SizedBox(height: Space.s3),
          for (var i = 0; i < a.evidence.length; i++)
            Padding(
              padding: const EdgeInsets.only(bottom: Space.s3),
              child: EvidenceCard(evidence: a.evidence[i], index: i + 1),
            ),
        ],
      ),
    );
  }

  void _escalate(AssistantAnswer a) {
    showAppBottomSheet<void>(
      context,
      title: context.t('ai.escalateTitle'),
      closeLabel: context.t('common.close'),
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(context.t('ai.escalateBody'), style: context.text.body),
          const SizedBox(height: Space.s3),
          AppCard(
            tone: Tone.neutral,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(context.t('ai.willSend'), style: context.text.label),
                const SizedBox(height: Space.s1),
                Text(context.loc(a.text), style: context.text.bodySmall),
              ],
            ),
          ),
          const SizedBox(height: Space.s3),
          AlertCard(
            tone: Tone.info,
            title: context.t('ai.escalateWhoTitle'),
            body: context.t('ai.escalateWho'),
          ),
          const SizedBox(height: Space.s4),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              Builder(
                builder: (sheetContext) => AppButton(
                  label: context.t('common.cancel'),
                  variant: ButtonVariant.ghost,
                  onPressed: () => Navigator.of(sheetContext).pop(),
                ),
              ),
              const SizedBox(width: Space.s2),
              Builder(
                builder: (sheetContext) => AppButton(
                  label: context.t('ai.escalateConfirm'),
                  iconStart: 'send',
                  onPressed: () async {
                    final nav = Navigator.of(sheetContext);
                    await context.services.ai.escalate(a.id);
                    nav.pop();
                    if (!mounted) return;
                    setState(() => _escalated.add(a.id));
                    showAppToast(
                      context,
                      context.t('ai.escalatedToast'),
                      tone: Tone.success,
                    );
                  },
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Future<void> _openHistory() async {
    final picked = await context.push<Conversation>('/assistant/history');
    if (picked != null && mounted) {
      setState(() {
        _messages
          ..clear()
          ..addAll(picked.messages);
        _animateId = null;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final wide = MediaQuery.sizeOf(context).width >= Breakpoints.expanded;
    final lastAssistantId = _messages.where((m) => !m.isUser).lastOrNull?.id;
    final chat = Column(
      children: [
        Expanded(
          child: ListView(
            controller: _scroll,
            padding: const EdgeInsetsDirectional.symmetric(
              horizontal: Space.s4,
              vertical: Space.s3,
            ),
            children: [
              if (_messages.isEmpty)
                FutureBuilder<List<Localized>>(
                  future: _quick,
                  builder: (context, snap) => EmptyState(
                    icon: 'sparkles',
                    title: context.t('ai.emptyTitle'),
                    body: context.t('ai.emptyBody'),
                    action: Wrap(
                      alignment: WrapAlignment.center,
                      spacing: Space.s2,
                      children: [
                        for (final q in snap.data ?? const <Localized>[])
                          AppChip(
                            label: context.loc(q),
                            ai: true,
                            onTap: () => _send(context.loc(q)),
                          ),
                      ],
                    ),
                  ),
                ),
              for (final m in _messages)
                Padding(
                  padding: const EdgeInsets.only(bottom: Space.s4),
                  child: m.isUser
                      ? ChatBubble(
                          text: context.loc(m.userText!),
                          isUser: true,
                          semanticPrefix: context.t('ai.youSaid'),
                          attachment: m.attachment,
                        )
                      : _AssistantMessage(
                          key: ValueKey(m.id),
                          answer: m.answer!,
                          animate: m.id == _animateId,
                          isLast: m.id == lastAssistantId,
                          escalated: _escalated.contains(m.answer!.id),
                          onEvidence: _showEvidence,
                          onEscalate: _escalate,
                          onFollowUp: _send,
                        ),
                ),
              if (_thinking)
                Align(
                  alignment: AlignmentDirectional.centerStart,
                  child: ThinkingDots(label: context.t('ai.thinking')),
                ),
            ],
          ),
        ),
        Container(
          padding: const EdgeInsetsDirectional.fromSTEB(
            Space.s4,
            Space.s3,
            Space.s4,
            Space.s3,
          ),
          decoration: BoxDecoration(
            color: context.colors.surface,
            border: Border(top: BorderSide(color: context.colors.border)),
          ),
          child: SafeArea(
            top: false,
            child: ConstrainedBox(
              constraints: BoxConstraints(
                maxHeight: MediaQuery.sizeOf(context).height * 0.25,
              ),
              child: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    if (_contextDrugId != null)
                      FutureBuilder<Drug>(
                        future: context.services.medications.drug(
                          _contextDrugId!,
                        ),
                        builder: (context, snap) => snap.hasData
                            ? Align(
                                alignment: AlignmentDirectional.centerStart,
                                child: Padding(
                                  padding: const EdgeInsets.only(
                                    bottom: Space.s2,
                                  ),
                                  child: AppChip(
                                    label: context.t('ai.aboutDrug', {
                                      'name': context.loc(snap.data!.name),
                                    }),
                                    icon: 'pill',
                                    onRemove: () =>
                                        setState(() => _contextDrugId = null),
                                    removeLabel: context.t('ai.removeContext'),
                                  ),
                                ),
                              )
                            : const SizedBox.shrink(),
                      ),
                    MessageComposer(
                      controller: _input,
                      onSend: () => _send(_input.text),
                      onVoice: _toggleVoice,
                      onImage: () => setState(() => _attachment = 'photo.jpg'),
                      recording: _recording,
                      enabled: !_thinking,
                      attachment: _attachment,
                      onRemoveAttachment: () =>
                          setState(() => _attachment = null),
                      labels: ComposerLabels(
                        input: context.t('ai.input'),
                        placeholder: context.t('ai.placeholder'),
                        send: context.t('ai.send'),
                        voice: context.t('ai.voice'),
                        stopVoice: context.t('ai.stopVoice'),
                        image: context.t('ai.image'),
                        removeAttachment: context.t('ai.removeAttachment'),
                        prototypeNote: context.t('ai.imageNote'),
                      ),
                    ),
                    if (_recording)
                      Padding(
                        padding: const EdgeInsets.only(top: Space.s1),
                        child: Semantics(
                          liveRegion: true,
                          child: Text(
                            context.t('ai.recording'),
                            style: context.text.caption,
                          ),
                        ),
                      ),
                    Padding(
                      padding: const EdgeInsets.only(top: Space.s1),
                      child: Text(
                        context.t('ai.footerNote'),
                        style: context.text.caption,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ],
    );

    final header = MediaQuery.withClampedTextScaling(
      maxScaleFactor: 1.15,
      child: Container(
        padding: const EdgeInsetsDirectional.fromSTEB(
          Space.s4,
          Space.s3,
          Space.s2,
          Space.s3,
        ),
        decoration: BoxDecoration(
          gradient: LinearGradient(
            begin: AlignmentDirectional.centerStart,
            end: AlignmentDirectional.centerEnd,
            colors: [context.colors.accentContainer, context.colors.surface],
          ),
          border: Border(bottom: BorderSide(color: context.colors.border)),
        ),
        child: Row(
          children: [
            Container(
              width: 44,
              height: 44,
              decoration: BoxDecoration(
                color: context.colors.surface,
                borderRadius: BorderRadius.circular(Radii.md),
              ),
              child: Center(
                child: AppIcon('sparkles', color: context.colors.accent),
              ),
            ),
            const SizedBox(width: Space.s3),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Semantics(
                    header: true,
                    child: Text(
                      context.t('nav.assistant'),
                      style: context.text.h4,
                    ),
                  ),
                  Text(
                    context.t('ai.disclaimerShort'),
                    style: context.text.caption,
                  ),
                ],
              ),
            ),
            const DemoBadge(),
            AppIconButton(
              icon: 'plus',
              label: context.t('ai.newChat'),
              onPressed: () => setState(() {
                _messages.clear();
                _animateId = null;
              }),
            ),
            if (!wide)
              AppIconButton(
                icon: 'clock',
                label: context.t('ai.history'),
                onPressed: _openHistory,
              ),
          ],
        ),
      ),
    );

    return SafeArea(
      bottom: false,
      child: wide
          ? Row(
              children: [
                Expanded(
                  child: Column(
                    children: [
                      header,
                      Expanded(child: chat),
                    ],
                  ),
                ),
                VerticalDivider(width: 1, color: context.colors.border),
                SizedBox(
                  width: 288,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Padding(
                        padding: const EdgeInsets.all(Space.s4),
                        child: Semantics(
                          header: true,
                          child: Text(
                            context.t('ai.history'),
                            style: context.text.h4,
                          ),
                        ),
                      ),
                      Expanded(
                        child: _HistoryList(
                          future: _history,
                          onPick: (c) => setState(() {
                            _messages
                              ..clear()
                              ..addAll(c.messages);
                            _animateId = null;
                          }),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            )
          : Column(
              children: [
                header,
                Expanded(child: chat),
              ],
            ),
    );
  }
}

class _AssistantMessage extends StatefulWidget {
  const _AssistantMessage({
    super.key,
    required this.answer,
    required this.animate,
    required this.isLast,
    required this.escalated,
    required this.onEvidence,
    required this.onEscalate,
    required this.onFollowUp,
  });
  final AssistantAnswer answer;
  final bool animate;
  final bool isLast;
  final bool escalated;
  final ValueChanged<AssistantAnswer> onEvidence;
  final ValueChanged<AssistantAnswer> onEscalate;
  final ValueChanged<String> onFollowUp;
  @override
  State<_AssistantMessage> createState() => _AssistantMessageState();
}

class _AssistantMessageState extends State<_AssistantMessage> {
  late bool _done = !widget.animate;

  @override
  Widget build(BuildContext context) {
    final a = widget.answer;
    final red = a.kind == AnswerKind.redFlag;
    final c = context.colors;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (red)
          AlertCard(
            tone: Tone.danger,
            title: a.title != null
                ? context.loc(a.title!)
                : context.t('ai.redFlagTitle'),
            body: '${context.loc(a.text)}\n\n${context.t('ai.redFlagNote')}',
          )
        else
          Semantics(
            liveRegion: true,
            child: Align(
              alignment: AlignmentDirectional.centerStart,
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 560),
                child: StripedBox(
                  stripe: a.kind == AnswerKind.refusal ? c.warning : c.accent,
                  fill: c.surface,
                  borderColor: c.border,
                  radius: const BorderRadiusDirectional.only(
                    topStart: Radius.circular(Radii.lg),
                    topEnd: Radius.circular(Radii.lg),
                    bottomEnd: Radius.circular(Radii.lg),
                    bottomStart: Radius.circular(Radii.xs),
                  ),
                  child: StreamedText(
                    text: context.loc(a.text),
                    animate: widget.animate,
                    style: context.text.body,
                    onDone: () {
                      if (mounted && !_done) setState(() => _done = true);
                    },
                  ),
                ),
              ),
            ),
          ),
        if (_done && !red) ...[
          const SizedBox(height: Space.s2),
          ConfidenceMeter(level: a.confidence),
          const SizedBox(height: Space.s2),
          SourceChips(
            sources: a.sources,
            onOpen: a.evidence.isEmpty ? null : () => widget.onEvidence(a),
          ),
          const SizedBox(height: Space.s1),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Padding(
                padding: const EdgeInsets.only(top: 2),
                child: AppIcon('shield', size: IconSizes.sm, color: c.info),
              ),
              const SizedBox(width: Space.s2),
              Expanded(
                child: Text(
                  context.t('ai.safetyNotice'),
                  style: context.text.caption,
                ),
              ),
            ],
          ),
          const SizedBox(height: Space.s2),
          Wrap(
            spacing: Space.s2,
            runSpacing: Space.s1,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              if (a.evidence.isNotEmpty)
                AppButton(
                  label: context.t('ai.viewEvidence'),
                  variant: ButtonVariant.secondary,
                  size: ButtonSize.sm,
                  iconStart: 'bookOpen',
                  onPressed: () => widget.onEvidence(a),
                ),
              if (a.canEscalate)
                widget.escalated
                    ? AppChip(
                        label: context.t('ai.escalated'),
                        icon: 'checkCircle',
                      )
                    : AppButton(
                        label: context.t('ai.escalate'),
                        variant: ButtonVariant.tonal,
                        size: ButtonSize.sm,
                        iconStart: 'users',
                        onPressed: () => widget.onEscalate(a),
                      ),
              AppIconButton(
                icon: 'thumbUp',
                label: context.t('ai.helpful'),
                onPressed: () => showAppToast(
                  context,
                  context.t('ai.feedbackThanks'),
                  tone: Tone.success,
                ),
              ),
              AppIconButton(
                icon: 'thumbDown',
                label: context.t('ai.notHelpful'),
                onPressed: () =>
                    showAppToast(context, context.t('ai.feedbackThanks')),
              ),
            ],
          ),
          if (widget.isLast && a.followUps.isNotEmpty)
            Wrap(
              spacing: Space.s2,
              children: [
                for (final f in a.followUps)
                  AppChip(
                    label: context.loc(f),
                    ai: true,
                    onTap: () => widget.onFollowUp(context.loc(f)),
                  ),
              ],
            ),
        ],
        if (red)
          Padding(
            padding: const EdgeInsets.only(top: Space.s2),
            child: Text(
              context.t('ai.staticNotice'),
              style: context.text.caption,
            ),
          ),
      ],
    );
  }
}

class _HistoryList extends StatelessWidget {
  const _HistoryList({required this.future, required this.onPick});
  final Future<List<Conversation>> future;
  final ValueChanged<Conversation> onPick;
  @override
  Widget build(BuildContext context) => FutureBuilder<List<Conversation>>(
    future: future,
    builder: (context, snap) {
      if (!snap.hasData) {
        return const Center(child: CircularProgressIndicator());
      }
      return ListView(
        children: [
          for (final c in snap.data!)
            AppListTile(
              title: context.loc(c.title),
              meta: context.fmt.relativeMinutes(
                c.updatedMinutesAgo,
                (k, p) => context.t(k, p),
              ),
              onTap: () => onPick(c),
              leading: const AppIcon('message'),
            ),
        ],
      );
    },
  );
}

class ConversationHistoryScreen extends StatefulWidget {
  const ConversationHistoryScreen({super.key});
  @override
  State<ConversationHistoryScreen> createState() =>
      _ConversationHistoryScreenState();
}

class _ConversationHistoryScreenState extends State<ConversationHistoryScreen> {
  late final Future<List<Conversation>> _future = context.services.ai
      .conversations();
  @override
  Widget build(BuildContext context) => Scaffold(
    body: SafeArea(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsetsDirectional.fromSTEB(
              Space.s2,
              Space.s2,
              Space.s4,
              Space.s2,
            ),
            child: Row(
              children: [
                AppIconButton(
                  icon: 'chevronLeft',
                  label: context.t('common.back'),
                  onPressed: () => context.pop(),
                ),
                Expanded(
                  child: Semantics(
                    header: true,
                    child: Text(
                      context.t('ai.history'),
                      style: context.text.h3,
                    ),
                  ),
                ),
              ],
            ),
          ),
          Expanded(
            child: _HistoryList(future: _future, onPick: (c) => context.pop(c)),
          ),
        ],
      ),
    ),
  );
}
