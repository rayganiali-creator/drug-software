import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ConfidenceMeter, Demo, EvidenceCard, SourceChips } from "../../components/health/cards";
import { AlertCard, BottomSheet, Button, Card, ChatBubble, Chip, Drawer, EmptyState, Icon, IconButton, List, ListItem, MessageComposer, ThinkingDots, useToast } from "../../components/ui";
import { useBreakpoint, useReducedMotion } from "../../hooks/useBreakpoint";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAsync, useServices } from "../../services/ServicesProvider";
import type { AssistantAnswer, ChatMessage, Conversation } from "../../services/types";
import "../features.css";

let uid = 0;
const nextId = () => `m${Date.now()}-${uid++}`;

/** Reveals text progressively (unless the user prefers reduced motion). */
function StreamedText({ text, animate, onDone }: { text: string; animate: boolean; onDone(): void }) {
  const [n, setN] = useState(animate ? 0 : text.length);
  useEffect(() => {
    if (!animate) { onDone(); return; }
    const words = text.split(/(\s+)/);
    let i = 0;
    const timer = window.setInterval(() => {
      i += 2;
      setN(words.slice(0, i).join("").length);
      if (i >= words.length) { window.clearInterval(timer); setN(text.length); onDone(); }
    }, 35);
    return () => window.clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [text, animate]);
  return <>{text.slice(0, n)}</>;
}

function AssistantMessage({ answer, animate, onEvidence, onEscalate, onFollowUp, escalated, isLast }: {
  answer: AssistantAnswer; animate: boolean; isLast: boolean; escalated: boolean;
  onEvidence(a: AssistantAnswer): void; onEscalate(a: AssistantAnswer): void; onFollowUp(q: string): void;
}) {
  const { t, loc } = useI18n();
  const toast = useToast();
  const [done, setDone] = useState(!animate);
  const isRed = answer.kind === "red-flag";
  return (
    <div className="msgmeta">
      {isRed ? (
        <AlertCard tone="danger" title={answer.title ? loc(answer.title) : t("ai.redFlagTitle")} icon="alertTriangle" role="alert">
          {loc(answer.text)}
          <div className="ai-note" style={{ marginBlockStart: "var(--space-2)", fontSize: "var(--text-caption-size)" }}>{t("ai.redFlagNote")}</div>
        </AlertCard>
      ) : (
        <ChatBubble role="assistant" variant={answer.kind === "refusal" ? "refusal" : undefined} label={t("ai.assistantSaid")}>
          <StreamedText text={loc(answer.text)} animate={animate} onDone={() => setDone(true)} />
        </ChatBubble>
      )}
      {done && !isRed && (
        <>
          <ConfidenceMeter level={answer.confidence} />
          <SourceChips sources={answer.sources} onOpen={() => onEvidence(answer)} />
          <div className="safety-note"><Icon name="shield" size="sm" /><span>{t("ai.safetyNotice")}</span></div>
          <div className="msgactions">
            {answer.evidence.length > 0 && <Button size="sm" variant="secondary" iconStart="bookOpen" onClick={() => onEvidence(answer)}>{t("ai.viewEvidence")}</Button>}
            {answer.canEscalate && (escalated
              ? <Chip icon="checkCircle">{t("ai.escalated")}</Chip>
              : <Button size="sm" variant="tonal" iconStart="users" onClick={() => onEscalate(answer)}>{t("ai.escalate")}</Button>)}
            <IconButton size="sm" icon="thumbUp" label={t("ai.helpful")} onClick={() => toast.show({ message: t("ai.feedbackThanks"), tone: "success" })} />
            <IconButton size="sm" icon="thumbDown" label={t("ai.notHelpful")} onClick={() => toast.show({ message: t("ai.feedbackThanks"), tone: "info" })} />
          </div>
          {isLast && answer.followUps.length > 0 && (
            <div className="quick-chips" style={{ marginBlock: 0 }} role="group" aria-label={t("ai.suggested")}>
              {answer.followUps.map((f, i) => <Chip key={i} variant="ai" onClick={() => onFollowUp(loc(f))}>{loc(f)}</Chip>)}
            </div>
          )}
        </>
      )}
      {isRed && <div className="safety-note"><Icon name="shield" size="sm" /><span>{t("ai.staticNotice")}</span></div>}
    </div>
  );
}

export function Assistant() {
  const { t, loc, locale, fmt } = useI18n();
  const services = useServices();
  const bp = useBreakpoint();
  const reduced = useReducedMotion();
  const toast = useToast();
  const [params, setParams] = useSearchParams();
  const history = useAsync((s) => s.ai.conversations());
  const quick = useAsync((s) => s.ai.quickQuestions());
  const drugCtx = useAsync(async (s) => (params.get("drug") ? s.medications.drug(params.get("drug")!) : null), [params.get("drug")]);

  const [convId, setConvId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [draft, setDraft] = useState("");
  const [thinking, setThinking] = useState(false);
  const [recording, setRecording] = useState(false);
  const [attachment, setAttachment] = useState<string | null>(null);
  const [evidence, setEvidence] = useState<AssistantAnswer | null>(null);
  const [toEscalate, setToEscalate] = useState<AssistantAnswer | null>(null);
  const [escalated, setEscalated] = useState<Set<string>>(new Set());
  const [historyOpen, setHistoryOpen] = useState(false);
  const [animateId, setAnimateId] = useState<string | null>(null);
  const logRef = useRef<HTMLDivElement>(null);
  const compact = bp === "compact";
  const contextDrug = drugCtx.status === "ready" ? drugCtx.data : null;

  const send = useCallback(async (raw: string) => {
    const text = raw.trim();
    if (!text || thinking) return;
    const attach = attachment;
    setDraft(""); setAttachment(null);
    setMessages((m) => [...m, { id: nextId(), role: "user", text, attachment: attach ?? undefined }]);
    setThinking(true);
    try {
      const answer = await services.ai.ask({ text, locale, contextDrugId: params.get("drug") ?? undefined });
      const id = nextId();
      setAnimateId(id);
      setMessages((m) => [...m, { id, role: "assistant", answer }]);
    } catch {
      toast.show({ message: t("ai.error"), tone: "danger" });
    } finally {
      setThinking(false);
    }
  }, [attachment, locale, params, services.ai, t, thinking, toast]);

  // Deep link: /assistant?q=... sends once.
  const sentQ = useRef(false);
  useEffect(() => {
    const q = params.get("q");
    if (q && !sentQ.current) { sentQ.current = true; void send(q); setParams((p) => { p.delete("q"); return p; }, { replace: true }); }
  }, [params, send, setParams]);

  useEffect(() => { logRef.current?.scrollTo?.({ top: logRef.current.scrollHeight, behavior: reduced ? "auto" : "smooth" }); }, [messages, thinking, reduced]);

  const toggleVoice = () => {
    if (recording) { setRecording(false); return; }
    setRecording(true);
    window.setTimeout(() => { setRecording(false); setDraft(t("ai.voiceSample")); }, 1600);
  };

  const openConversation = (c: Conversation) => { setConvId(c.id); setMessages(c.messages); setAnimateId(null); setHistoryOpen(false); };
  const newChat = () => { setConvId(null); setMessages([]); setAnimateId(null); setHistoryOpen(false); };

  const lastAssistantId = useMemo(() => [...messages].reverse().find((m) => m.role === "assistant")?.id, [messages]);

  const historyList = (
    <List label={t("ai.history")}>
      {history.status === "ready" && history.data.map((c) => (
        <ListItem key={c.id} title={loc(c.title)} meta={fmt.relativeMinutes(c.updatedMinutesAgo)} onClick={() => openConversation(c)} trailing={c.id === convId ? <Icon name="check" size="sm" /> : undefined} />
      ))}
    </List>
  );

  const evidenceBody = evidence && (
    <div className="ms-stack">
      <ConfidenceMeter level={evidence.confidence} />
      <p className="ms-muted">{t("ai.confidence.explain")}</p>
      {evidence.evidence.map((e, i) => <EvidenceCard key={e.source.id} evidence={e} index={i + 1} />)}
    </div>
  );
  const Evidence = compact ? BottomSheet : Drawer;

  return (
    <>
      <PageHeader title={t("nav.assistant")} subtitle={t("ai.pageSub")}
        actions={<>
          {(compact || bp === "medium") && <Button variant="secondary" iconStart="clock" onClick={() => setHistoryOpen(true)}>{t("ai.history")}</Button>}
          <Button variant="secondary" iconStart="plus" onClick={newChat}>{t("ai.newChat")}</Button>
        </>} />
      <AlertCard tone="warning" title={t("grounded.proto.title")} icon="info">
        {t("grounded.proto.body")} <Link className="ms-link" to="/app/patient/assistant">{t("grounded.proto.open")}</Link>
      </AlertCard>
      <div className="assistant">
        <Card flush className="assistant__panel" aria-label={t("nav.assistant")}>
          <div className="assistant__head">
            <span className="hc-tile" data-tone="accent"><Icon name="sparkles" /></span>
            <div style={{ flex: 1 }}>
              <strong>{t("ai.name")}</strong>
              <div className="hc-sub">{t("ai.disclaimerShort")}</div>
            </div>
            <Demo />
          </div>
          <div className="assistant__log" ref={logRef} role="log" aria-live="polite" aria-label={t("ai.conversation")}>
            {messages.length === 0 && (
              <EmptyState icon="sparkles" title={t("ai.emptyTitle")} body={t("ai.emptyBody")}
                action={<div className="quick-chips" role="group" aria-label={t("ai.quick")}>{quick.status === "ready" && quick.data.map((q, i) => <Chip key={i} variant="ai" onClick={() => void send(loc(q))}>{loc(q)}</Chip>)}</div>} />
            )}
            {messages.map((m) =>
              m.role === "user" ? (
                <ChatBubble key={m.id} role="user" label={t("ai.youSaid")}>
                  {typeof m.text === "string" ? m.text : loc(m.text)}
                  {m.attachment && <div style={{ marginBlockStart: "var(--space-2)", opacity: 0.9 }}><Icon name="image" size="xs" /> {m.attachment}</div>}
                </ChatBubble>
              ) : (
                <AssistantMessage key={m.id} answer={m.answer} animate={!reduced && m.id === animateId} isLast={m.id === lastAssistantId} escalated={escalated.has(m.answer.id)}
                  onEvidence={setEvidence} onEscalate={setToEscalate} onFollowUp={(q) => void send(q)} />
              ),
            )}
            {thinking && <ThinkingDots label={t("ai.thinking")} />}
          </div>
          <div className="assistant__foot">
            {contextDrug && (
              <div className="ms-row"><Chip icon="pill" onRemove={() => { setParams((p) => { p.delete("drug"); return p; }); }} removeLabel={t("ai.removeContext")}>{t("ai.aboutDrug", { name: loc(contextDrug.name) })}</Chip></div>
            )}
            <MessageComposer value={draft} onChange={setDraft} onSend={() => void send(draft)} onVoice={toggleVoice} recording={recording} disabled={thinking}
              onImage={(f) => setAttachment(f.name)} attachment={attachment} onRemoveAttachment={() => setAttachment(null)}
              labels={{ input: t("ai.input"), placeholder: t("ai.placeholder"), send: t("ai.send"), voice: t("ai.voice"), stopVoice: t("ai.stopVoice"), image: t("ai.image"), removeAttachment: t("ai.removeAttachment"), prototypeNote: t("ai.imageNote") }} />
            {recording && <p role="status" className="hc-sub"><Icon name="record" size="xs" style={{ color: "var(--color-danger)", display: "inline" }} /> {t("ai.recording")}</p>}
            <p className="hc-sub">{t("ai.footerNote")}</p>
          </div>
        </Card>

        {bp !== "compact" && bp !== "medium" && (
          <aside className="assistant__side" aria-label={t("ai.history")}>
            <Card title={t("ai.history")} flush>{historyList}</Card>
          </aside>
        )}
      </div>

      <Evidence open={!!evidence} onClose={() => setEvidence(null)} title={t("ai.evidenceTitle")} closeLabel={t("common.close")}>{evidenceBody}</Evidence>
      <Drawer open={historyOpen} onClose={() => setHistoryOpen(false)} title={t("ai.history")} closeLabel={t("common.close")}>{historyList}</Drawer>

      {(
        <EscalateDialog answer={toEscalate} compact={compact} onClose={() => setToEscalate(null)}
          onConfirm={async (a) => {
            await services.ai.escalate(a.id);
            setEscalated((s) => new Set(s).add(a.id));
            setToEscalate(null);
            toast.show({ message: t("ai.escalatedToast"), tone: "success" });
          }} />
      )}
      
    </>
  );
}

function EscalateDialog({ answer, compact, onClose, onConfirm }: { answer: AssistantAnswer | null; compact: boolean; onClose(): void; onConfirm(a: AssistantAnswer): Promise<void> }) {
  const { t, loc } = useI18n();
  const [busy, setBusy] = useState(false);
  const Dialog = compact ? BottomSheet : Drawer;
  return (
    <Dialog open={!!answer} onClose={onClose} title={t("ai.escalateTitle")} closeLabel={t("common.close")}
      actions={<><Button variant="ghost" onClick={onClose}>{t("common.cancel")}</Button><Button loading={busy} iconStart="send" onClick={async () => { if (!answer) return; setBusy(true); await onConfirm(answer); setBusy(false); }}>{t("ai.escalateConfirm")}</Button></>}>
      {answer && (
        <div className="ms-stack">
          <p>{t("ai.escalateBody")}</p>
          <Card variant="tonal" tone="neutral"><strong>{t("ai.willSend")}</strong><p style={{ marginBlockStart: "var(--space-2)" }}>{loc(answer.text)}</p></Card>
          <AlertCard tone="info" title={t("ai.escalateWhoTitle")}>{t("ai.escalateWho")}</AlertCard>
        </div>
      )}
    </Dialog>
  );
}
