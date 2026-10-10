import { useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { AlertCard, Badge, Button, Card, EmptyState, Icon, LoadingState, Switch, TextArea } from "../../components/ui";
import { AssistantApi } from "../../ai/assistantApi";
import { AssistantError, type AnswerStatus, type EvidenceItem, type GroundedAnswer } from "../../ai/types";
import { useAuth } from "../../auth/AuthContext";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import "../features.css";
import "./grounded.css";

const MAX = 500;
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const key = (code: string) => code.replace(/[.]/g, "_");

const statusTone: Record<AnswerStatus, "info" | "danger" | "warning" | "neutral"> = {
  Answered: "neutral", NoEvidence: "info", Refused: "info", Escalated: "danger", Blocked: "warning", Unavailable: "warning",
};

/** Translate a machine code; an unknown code is shown as-is (never hidden, never guessed). */
function useCode() {
  const { t } = useI18n();
  return (prefix: string, code: string) => { const k = `${prefix}.${key(code)}`; const v = t(k); return v === k ? code : v; };
}

function EvidenceList({ items }: { items: EvidenceItem[] }) {
  const { t, fmt } = useI18n();
  const code = useCode();
  if (items.length === 0) return <p className="ms-muted">{t("grounded.evidence.empty")}</p>;
  return (
    <ol className="gr-evidence" aria-label={t("grounded.evidence.title")}>
      {items.map((e) => (
        <li key={e.id} id={`evidence-${e.id}`}>
          <div className="gr-evidence__head">
            <span className="gr-evidence__id" dir="ltr">[{e.id}]</span>
            <strong>{e.medicationName}</strong>
            <Badge tone="neutral">{code("grounded.kind", e.kind)}</Badge>
            <Badge tone={e.validation === "Validated" ? "success" : e.validation === "Demo" ? "warning" : "danger"} icon={e.validation === "Validated" ? "check" : "alertTriangle"}>{t(`kn.status.${({ Demo: "demo", Validated: "validated", Unverified: "unverified", NeedsValidation: "needs", Rejected: "rejected" } as Record<string, string>)[e.validation] ?? "unverified"}`)}</Badge>
            {e.stale && <Badge tone="warning" icon="clock">{t("grounded.evidence.stale")}</Badge>}
            {e.sourceDateUnknown && <Badge tone="neutral" icon="clock">{t("grounded.evidence.receivedUnknown")}</Badge>}
          </div>
          <p className="gr-text">{e.text}</p>
          {e.qualifier && <p className="ms-muted" dir="auto">{e.qualifier.replace("|", " · ")}</p>}
          <details>
            <summary>{t("grounded.evidence.show")}</summary>
            <dl className="gr-facts">
              <div><dt>{t("grounded.evidence.source")}</dt><dd>{e.source.name}</dd></div>
              <div><dt>{t("grounded.evidence.version")}</dt><dd dir="ltr">{e.source.version}</dd></div>
              <div><dt>{t("grounded.evidence.publisher")}</dt><dd>{e.source.publisher}</dd></div>
              <div><dt>{t("grounded.evidence.received")}</dt><dd>{e.source.receivedAt ? fmt.date(new Date(e.source.receivedAt), "short") : t("grounded.evidence.receivedUnknown")}</dd></div>
            </dl>
          </details>
        </li>
      ))}
    </ol>
  );
}

export function AnswerView({ answer }: { answer: GroundedAnswer }) {
  const { t } = useI18n();
  const code = useCode();
  const ev = answer.evidence;
  const tone = statusTone[answer.status];
  const gen = answer.generation;
  const why = answer.status !== "Answered" && answer.status !== "Escalated" && answer.reason ? code("grounded.reason", answer.reason) : null;
  return (
    <div className="gr" aria-live="polite" data-status={answer.status}>
      {answer.status === "Escalated" ? (
        <AlertCard tone="danger" title={t("grounded.status.Escalated")} icon="alertTriangle" role="alert"><p className="gr-text">{answer.text}</p></AlertCard>
      ) : (
        <Card title={t(`grounded.status.${answer.status}`)} variant={tone === "neutral" ? "default" : "tonal"} tone={tone === "neutral" ? undefined : tone}>
          <div className="ms-stack">
            <p className="gr-text">{answer.text}</p>
            {why && <p className="ms-muted"><strong>{t("grounded.reason.title")}: </strong>{why}</p>}
          </div>
        </Card>
      )}

      <div className="gr-badges">
        {answer.isMock
          ? <Badge tone="warning" icon="alertTriangle">{t("grounded.gen.mock")}</Badge>
          : <Badge tone={gen?.external ? "info" : "neutral"} icon="sparkles">{gen && gen.kind !== "Disabled" && gen.kind !== "Mock" ? t(`grounded.gen.${gen.kind}`) : t("grounded.gen.none")}</Badge>}
        {answer.notice.includes("DEMO") && <Badge tone="warning" icon="alertTriangle">{t("demo.badge")}</Badge>}
        {answer.patientContextUsed && <Badge tone="info" icon="user">{t("grounded.contextUsed")}</Badge>}
      </div>
      {answer.patientContextNote && !answer.patientContextUsed && answer.patientContextNote !== "patient_context.used" && (
        <p className="ms-muted">{t("grounded.contextNotUsed", { why: code("grounded.ctx", answer.patientContextNote) })}</p>
      )}

      {answer.nextStep !== "None" && (
        <AlertCard tone={answer.nextStep === "EmergencyServices" ? "danger" : "info"} title={t("grounded.next.title")}>{t(`grounded.next.${answer.nextStep}`)}</AlertCard>
      )}

      {ev && ev.conflicts.length > 0 && (
        <AlertCard tone="warning" title={t("grounded.limit.evidence_conflict")}>
          <ul className="gr-list">{ev.conflicts.map((c, i) => <li key={i}>{t("grounded.evidence.conflict", { name: c.medicationName, ids: c.itemIds.map((x) => `[${x}]`).join(" ") })}</li>)}</ul>
        </AlertCard>
      )}

      {ev && ev.items.length > 0 && (
        <Card title={t("grounded.quality.title")}>
          <div className="ms-stack">
            <div className="gr-badges"><Badge tone={answer.evidenceQuality === "Validated" ? "success" : "warning"} icon={answer.evidenceQuality === "Validated" ? "check" : "info"}>{t(`grounded.quality.${answer.evidenceQuality}`)}</Badge></div>
            <p className="ms-muted">{t("grounded.quality.explain")}</p>
            {answer.limitations && answer.limitations.length > 0 && (
              <>
                <h3 className="ms-h4">{t("grounded.limits.title")}</h3>
                <ul className="gr-list">{answer.limitations.map((l) => <li key={l}>{code("grounded.limit", l)}</li>)}</ul>
              </>
            )}
            {answer.missingInformation && answer.missingInformation.length > 0 && (
              <>
                <h3 className="ms-h4">{t("grounded.missing.title")}</h3>
                <ul className="gr-list">{answer.missingInformation.map((m) => <li key={m}>{code("grounded.kind", m.replace(/^kind[.]/, ""))}</li>)}</ul>
              </>
            )}
          </div>
        </Card>
      )}
      {answer.status === "Escalated" && answer.limitations && answer.limitations.length > 0 && (
        <p className="ms-muted">{answer.limitations.map((l) => code("grounded.limit", l)).join(" ")}</p>
      )}

      {ev && ev.items.length > 0 && (
        <Card title={t("grounded.evidence.title")} subtitle={t("grounded.evidence.sub")}><EvidenceList items={ev.items} /></Card>
      )}
    </div>
  );
}

/**
 * Source-based medication assistant, wired to the real API. All decisions (provider, consent, safety) are made by the server; this page only
 * shows the structured answer: generated text apart from evidence, what is missing or limited, whether it is MOCK or external, and the next step.
 */
export function GroundedAssistant() {
  const { t, locale } = useI18n();
  const { backend } = useAuth();
  const [params] = useSearchParams();
  const api = useMemo(() => new AssistantApi(backend.apiFetch ? (p, i) => backend.apiFetch!(p, i) : undefined), [backend]);
  const drug = params.get("drug");
  const [question, setQuestion] = useState(params.get("q") ?? "");
  const [useMine, setUseMine] = useState(false);
  const [phase, setPhase] = useState<"idle" | "loading" | "done" | "error">("idle");
  const [answer, setAnswer] = useState<GroundedAnswer | null>(null);
  const [error, setError] = useState<AssistantError | null>(null);
  const ctl = useRef<AbortController | null>(null);
  const connected = !!backend.apiFetch;

  const ask = async () => {
    const text = question.trim();
    if (!text || phase === "loading") return;
    ctl.current?.abort();
    ctl.current = new AbortController();
    setPhase("loading"); setError(null);
    try {
      const a = await api.ask({ question: text, locale, medicationIds: drug && GUID.test(drug) ? [drug] : undefined, includePatientContext: useMine }, ctl.current.signal);
      setAnswer(a); setPhase("done");
    } catch (e) {
      if (e instanceof DOMException) return;
      setError(e instanceof AssistantError ? e : new AssistantError("server")); setPhase("error");
    }
  };

  return (
    <>
      <PageHeader title={t("grounded.title")} subtitle={t("grounded.sub")} actions={<Link className="ms-link" to="/app/patient/assistant/prototype">{t("grounded.proto.back")}</Link>} />
      {!connected ? (
        <Card><EmptyState icon="lock" title={t("grounded.notConnectedTitle")} body={t("grounded.notConnectedBody")} /></Card>
      ) : (
        <div className="gr">
          <Card>
            <form className="gr-form" onSubmit={(e) => { e.preventDefault(); void ask(); }}>
              <TextArea label={t("grounded.input")} hint={t("grounded.inputHint")} value={question} onChange={(e) => setQuestion(e.target.value.slice(0, MAX))} rows={3} maxLength={MAX} />
              <span className="gr-count" aria-live="off">{t("grounded.count", { n: question.length, max: MAX })}</span>
              <Switch label={t("grounded.useMine")} checked={useMine} onCheckedChange={setUseMine} />
              <p className="ms-muted">{t("grounded.useMineHint")}</p>
              <div><Button type="submit" iconStart="send" loading={phase === "loading"} disabled={!question.trim()}>{t("grounded.ask")}</Button></div>
            </form>
          </Card>

          {phase === "loading" && <LoadingState label={t("grounded.asking")} rows={3} />}
          {phase === "error" && error && (
            <div role="alert"><AlertCard tone="danger" title={t("grounded.err.title")}>{t(`grounded.err.${error.kind === "notConnected" ? "network" : error.kind}`)}</AlertCard></div>
          )}
          {phase === "done" && answer && <AnswerView answer={answer} />}
          {phase === "idle" && <p className="ms-muted"><Icon name="info" size="xs" /> {t("ai.disclaimerShort")}</p>}
        </div>
      )}
    </>
  );
}
