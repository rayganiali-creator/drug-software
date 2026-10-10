import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { AlertCard, Badge, Button, Card, EmptyState, ErrorState, LoadingState } from "../../components/ui";
import { useAuth } from "../../auth/AuthContext";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { SafetyApi } from "../../safety/api";
import { SafetyError, type Assessment, type AssessmentSummary, type Finding, type FindingSeverity, type RuleSummary } from "../../safety/types";
import "../features.css";
import "./safety.css";

const key = (code: string) => code.replace(/[.]/g, "_");
const severityTone: Record<FindingSeverity, "info" | "warning" | "danger" | "neutral"> = { Informational: "neutral", Minor: "info", Moderate: "warning", Major: "warning", Critical: "danger" };

/** Translate a machine code; an unknown code is shown as it is (never hidden, never guessed). */
function useCode() {
  const { t } = useI18n();
  return (prefix: string, code: string) => { const k = `${prefix}.${key(code)}`; const v = t(k); return v === k ? code : v; };
}

export function useSafetyApi(): SafetyApi {
  const { backend } = useAuth();
  return useMemo(() => new SafetyApi(backend.apiFetch ? (p, i) => backend.apiFetch!(p, i) : undefined), [backend]);
}

/** What the person sees when a call fails: blocked (policy) and unavailable (technical) are different states with different words. */
export function SafetyProblem({ error, onRetry }: { error: unknown; onRetry?(): void }) {
  const { t } = useI18n();
  const kind = error instanceof SafetyError ? error.kind : "server";
  if (kind === "notConnected") return <EmptyState icon="lock" title={t("sf.err.notConnected.title")} body={t("sf.err.notConnected.body")} />;
  if (kind === "unauthorized") return <div role="status"><EmptyState icon="shield" title={t("sf.err.blocked.title")} body={t("sf.err.blocked.body")} /></div>;
  if (kind === "rateLimited") return <EmptyState icon="clock" title={t("sf.err.rate.title")} body={t("sf.err.rate.body")} />;
  if (kind === "invalid") return <ErrorState title={t("sf.err.invalid.title")} body={t("sf.err.invalid.body")} retryLabel={t("common.retry")} onRetry={onRetry ?? (() => undefined)} />;
  return <div role="alert"><ErrorState title={t("sf.err.unavailable.title")} body={t("sf.err.unavailable.body")} retryLabel={t("common.retry")} onRetry={onRetry ?? (() => undefined)} /></div>;
}

function FindingCard({ f, evaluationsStatus }: { f: Finding; evaluationsStatus: string }) {
  const { t } = useI18n();
  const code = useCode();
  const names = f.subjects.map((s) => s.label).join(" · ");
  return (
    <li className="sf-finding" data-actionable={f.actionable} data-demo={f.isDemo}>
      <div className="sf-finding__head">
        <strong dir="auto">{t(`sf.domain.${f.domain}`)}</strong>
        <Badge tone={severityTone[f.severity]} icon={f.severity === "Critical" || f.severity === "Major" ? "alertTriangle" : "info"}>{t(`sf.severity.${f.severity}`)}</Badge>
        {f.isDemo && <Badge tone="warning" icon="alertTriangle">{t("sf.demo.badge")}</Badge>}
        {!f.isDemo && !f.actionable && <Badge tone="neutral" icon="info">{t("sf.notActionable")}</Badge>}
        {f.evidenceConflict && <Badge tone="warning" icon="alertTriangle">{t("sf.conflict")}</Badge>}
        {f.inputsStale && <Badge tone="warning" icon="clock">{t("sf.staleInput")}</Badge>}
      </div>
      <p className="sf-finding__what" dir="auto"><strong>{t("sf.f.observed")}: </strong>{names}</p>
      <p><strong>{t("sf.f.why")}: </strong>{t(`sf.why.${f.domain}`)}</p>
      <p><strong>{t("sf.f.next")}: </strong>{t(`sf.next.${f.domain}`)}</p>
      {f.isDemo && <p className="ms-muted">{t("sf.demo.finding")}</p>}
      {f.emergencySigns && f.emergencySigns.length > 0 && f.actionable && (
        <div className="sf-signs"><strong>{t("sf.f.signs")}</strong><ul>{f.emergencySigns.map((s) => <li key={s} dir="auto">{s}</li>)}</ul><p className="ms-muted">{t("sf.f.signsSourced")}</p></div>
      )}
      <details>
        <summary>{t("sf.f.details")}</summary>
        <dl className="sf-facts">
          <div><dt>{t("sf.f.rule")}</dt><dd dir="ltr">{f.ruleId} v{f.ruleVersion}</dd></div>
          <div><dt>{t("sf.f.ruleStatus")}</dt><dd>{evaluationsStatus}</dd></div>
          <div><dt>{t("sf.f.urgency")}</dt><dd>{t(`sf.urgency.${f.urgency}`)}</dd></div>
          {f.referenceSeverity && <div><dt>{t("sf.f.refSeverity")}</dt><dd>{f.referenceSeverity}</dd></div>}
        </dl>
        <h4 className="ms-h4">{t("sf.f.evidence")}</h4>
        <ul className="sf-evidence">
          {f.evidence.map((e) => (
            <li key={`${e.sourceId}-${e.version}`}>
              <strong dir="auto">{e.sourceName}</strong> <span dir="ltr">({e.version})</span>
              <Badge tone={e.validation === "Validated" ? "success" : e.validation === "Demo" ? "warning" : "danger"} icon={e.validation === "Validated" ? "check" : "alertTriangle"}>{t(`sf.validation.${e.validation}`)}</Badge>
              <span className="ms-muted"> {e.publicationDate ? t("sf.f.published", { date: e.publicationDate }) : t("sf.f.publishedUnknown")}</span>
            </li>
          ))}
        </ul>
        {f.limitations.length > 0 && <><h4 className="ms-h4">{t("sf.f.limits")}</h4><ul className="sf-list">{f.limitations.map((l) => <li key={l} dir="auto">{code("sf.limit", l)}</li>)}</ul></>}
      </details>
    </li>
  );
}

export function AssessmentView({ a, onRerun, busy }: { a: Assessment; onRerun?(): void; busy?: boolean }) {
  const { t, fmt, locale } = useI18n();
  const code = useCode();
  const r = a.result;
  const tone = r.status === "CompletedWithFindings" ? "warning" : r.status === "Failed" ? "danger" : r.status === "CompletedNoMatches" ? "neutral" : "info";
  const evaluationsStatus = (f: Finding) => {
    const e = r.evaluations.find((x) => x.ruleId === f.ruleId && x.version === f.ruleVersion);
    return e ? t(`sf.activation.${e.activation}`) : "—";
  };
  const actionable = r.findings.filter((f) => f.actionable);
  const others = r.findings.filter((f) => !f.actionable);
  return (
    <div className="sf" data-status={r.status}>
      {locale === "fa" && <AlertCard tone="info" title={t("sf.fa.unreviewed.title")}>{t("sf.fa.unreviewed.body")}</AlertCard>}

      <Card title={t(`sf.status.${r.status}`)} variant={tone === "neutral" ? "default" : "tonal"} tone={tone === "neutral" ? undefined : tone}>
        <div className="ms-stack">
          <p className="sf-lead">{t(`sf.statusBody.${r.status}`)}</p>
          <div className="sf-badges">
            {r.containsDemonstration && <Badge tone="warning" icon="alertTriangle">{t("sf.demo.badge")}</Badge>}
            {!r.complete && r.status !== "NoApprovedCoverage" && <Badge tone="info" icon="info">{t("sf.incomplete")}</Badge>}
            {a.outdated && <Badge tone="warning" icon="clock">{t("sf.outdated.badge")}</Badge>}
          </div>
          <p className="ms-muted"><strong>{t("sf.notSafety")}</strong></p>
          {r.containsDemonstration && <p className="ms-muted">{a.notice}</p>}
        </div>
      </Card>

      {a.outdated && (
        <AlertCard tone="warning" title={t("sf.outdated.title")}>
          <p>{t("sf.outdated.body")}</p>
          <ul className="sf-list">{a.outdatedReasons.map((x) => <li key={x}>{code("sf.outdated", x)}</li>)}</ul>
          {onRerun && <Button size="sm" variant="secondary" iconStart="refresh" loading={busy} onClick={onRerun}>{t("sf.run")}</Button>}
        </AlertCard>
      )}

      {r.status === "Failed" && <div role="alert"><AlertCard tone="danger" title={t("sf.failed.title")}>{t("sf.failed.body")}</AlertCard></div>}

      {actionable.length > 0 && (
        <Card title={t("sf.findings.title")} subtitle={t("sf.findings.sub")}>
          <ol className="sf-findings" aria-label={t("sf.findings.title")}>{actionable.map((f) => <FindingCard key={f.key} f={f} evaluationsStatus={evaluationsStatus(f)} />)}</ol>
        </Card>
      )}
      {others.length > 0 && (
        <Card title={r.containsDemonstration ? t("sf.demoFindings.title") : t("sf.otherFindings.title")} subtitle={r.containsDemonstration ? t("sf.demoFindings.sub") : t("sf.otherFindings.sub")}>
          <ol className="sf-findings" aria-label={t("sf.otherFindings.title")}>{others.map((f) => <FindingCard key={f.key} f={f} evaluationsStatus={evaluationsStatus(f)} />)}</ol>
        </Card>
      )}
      {r.findings.length === 0 && r.status !== "Failed" && (
        <Card title={t("sf.nothing.title")}>
          <p>{r.status === "NoApprovedCoverage" ? t("sf.nothing.noCoverage") : r.status === "Incomplete" ? t("sf.nothing.incomplete") : t("sf.nothing.noMatch")}</p>
        </Card>
      )}

      {a.openGuidanceNoLongerMatching.length > 0 && (
        <AlertCard tone="info" title={t("sf.noLonger.title")}>{t("sf.noLonger.body", { n: a.openGuidanceNoLongerMatching.length })}</AlertCard>
      )}

      <Card title={t("sf.coverage.title")} subtitle={t("sf.coverage.sub")}>
        <dl className="sf-facts">
          <div><dt>{t("sf.coverage.active")}</dt><dd>{fmt.number(r.coverage.activeRules)}</dd></div>
          <div><dt>{t("sf.coverage.demo")}</dt><dd>{fmt.number(r.coverage.demonstrationRules)}</dd></div>
          <div><dt>{t("sf.coverage.inactive")}</dt><dd>{fmt.number(r.coverage.inactiveRules)}</dd></div>
          <div><dt>{t("sf.coverage.notEvaluable")}</dt><dd>{fmt.number(r.coverage.notEvaluable)}</dd></div>
        </dl>
        <h4 className="ms-h4">{t("sf.coverage.covered")}</h4>
        {r.coverage.domainsCovered.length === 0 ? <p className="ms-muted">{t("sf.coverage.none")}</p> : <ul className="sf-list">{r.coverage.domainsCovered.map((d) => <li key={d}>{t(`sf.domain.${d}`)}</li>)}</ul>}
        <h4 className="ms-h4">{t("sf.coverage.unsupported")}</h4>
        <ul className="sf-list">{r.coverage.unsupportedDomains.map((d) => <li key={d}>{code("sf.unsupported", d)}</li>)}</ul>
      </Card>

      <Card title={t("sf.rules.title")} subtitle={t("sf.rules.sub")}>
        <ul className="sf-rules" aria-label={t("sf.rules.title")}>
          {r.evaluations.map((e) => (
            <li key={`${e.ruleId}-${e.version}`}>
              <span dir="ltr" className="sf-rules__id">{e.ruleId} v{e.version}</span>
              <Badge tone={e.outcome === "Matched" ? "warning" : e.outcome === "NoMatch" || e.outcome === "NotApplicable" ? "neutral" : e.outcome === "Unavailable" ? "neutral" : "info"} icon={e.outcome === "Matched" ? "alertTriangle" : "info"}>{t(`sf.outcome.${e.outcome}`)}</Badge>
              <Badge tone="neutral">{t(`sf.activation.${e.activation}`)}</Badge>
              {e.partial && <Badge tone="info" icon="info">{t("sf.partial")}</Badge>}
              {e.reasons.length > 0 && <span className="ms-muted"> {e.reasons.map((x) => code("sf.reason", x)).join(" · ")}</span>}
            </li>
          ))}
          {r.evaluations.length === 0 && <li className="ms-muted">{t("sf.rules.none")}</li>}
        </ul>
      </Card>

      <Card title={t("sf.inputs.title")} subtitle={t("sf.inputs.sub")}>
        <ul className="sf-rules" aria-label={t("sf.inputs.title")}>
          {r.inputs.map((i) => (
            <li key={i.category}>
              <strong>{t(`sf.input.${i.category}`)}</strong>
              <Badge tone={i.availability === "Available" ? "success" : "warning"} icon={i.availability === "Available" ? "check" : "alertTriangle"}>{t(`sf.avail.${i.availability}`)}</Badge>
              {i.isStale && <Badge tone="warning" icon="clock">{t("sf.staleInput")}</Badge>}
              <span className="ms-muted"> {i.lastUpdatedAt ? t("rec.fresh.updated", { date: fmt.date(new Date(i.lastUpdatedAt), "short") }) : ""}</span>
            </li>
          ))}
        </ul>
      </Card>

      <Card title={t("sf.meta.title")}>
        <dl className="sf-facts">
          <div><dt>{t("sf.meta.when")}</dt><dd>{fmt.date(new Date(r.evaluatedAt), "short")}</dd></div>
          <div><dt>{t("sf.meta.engine")}</dt><dd dir="ltr">{r.engineVersion}</dd></div>
          <div><dt>{t("sf.meta.ruleSet")}</dt><dd dir="ltr">{r.ruleSetVersion}</dd></div>
          <div><dt>{t("sf.meta.guidance")}</dt><dd>{t(`sf.guidance.${a.guidanceState}`)}</dd></div>
        </dl>
        <ul className="sf-list">{r.limitations.map((l) => <li key={l}>{code("sf.limit", l)}</li>)}</ul>
      </Card>
    </div>
  );
}

type LoadState<T> = { status: "loading" } | { status: "error"; error: unknown } | { status: "ready"; data: T };

/** Loads once and again when the dependencies change or on demand. Late answers of an outdated request are ignored. */
function useSafetyLoad<T>(fn: (api: SafetyApi, signal: AbortSignal) => Promise<T>, deps: unknown[] = []): { state: LoadState<T>; reload(): void } {
  const api = useSafetyApi();
  const [state, setState] = useState<LoadState<T>>({ status: "loading" });
  const [tick, setTick] = useState(0);
  const fnRef = useRef(fn);
  useEffect(() => { fnRef.current = fn; });
  useEffect(() => {
    const ctl = new AbortController();
    fnRef.current(api, ctl.signal).then(
      (data) => { if (!ctl.signal.aborted) setState({ status: "ready", data }); },
      (error) => { if (!ctl.signal.aborted && !(error instanceof DOMException)) setState({ status: "error", error }); },
    );
    return () => ctl.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api, tick, ...deps]);
  const reload = useCallback(() => setTick((n) => n + 1), []);
  return { state, reload };
}

/** Shared body of the patient page and the professional page: show the newest stored assessment, run a new one on request, never run by itself. */
function SafetyBody({ subjectId }: { subjectId: string }) {
  const { t, locale } = useI18n();
  const api = useSafetyApi();
  const latest = useSafetyLoad((a, s) => a.latest(subjectId, s), [subjectId]);
  const history = useSafetyLoad((a, s) => a.list(subjectId, s), [subjectId]);
  const [fresh, setFresh] = useState<Assessment | null>(null);
  const [running, setRunning] = useState(false);
  const [runError, setRunError] = useState<unknown>(null);

  const run = async () => {
    setRunning(true); setRunError(null);
    try {
      setFresh(await api.run(subjectId, locale));
      history.reload();
    } catch (e) {
      setRunError(e);
    } finally {
      setRunning(false);
    }
  };

  const shown = fresh ?? (latest.state.status === "ready" ? latest.state.data : null);
  const empty = !fresh && latest.state.status === "error" && latest.state.error instanceof SafetyError && latest.state.error.kind === "notFound";
  const rows: AssessmentSummary[] = history.state.status === "ready" ? history.state.data : [];
  return (
    <div className="sf-page">
      <Card>
        <div className="ms-stack">
          <p>{t("sf.intro")}</p>
          <div><Button iconStart="refresh" loading={running} onClick={() => void run()}>{shown ? t("sf.run") : t("sf.runFirst")}</Button></div>
          {runError != null && <SafetyProblem error={runError} onRetry={() => void run()} />}
        </div>
      </Card>
      {!fresh && latest.state.status === "loading" && <LoadingState label={t("common.loading")} rows={3} />}
      {!fresh && latest.state.status === "error" && !empty && <SafetyProblem error={latest.state.error} onRetry={latest.reload} />}
      {empty && <Card><EmptyState icon="clipboard" title={t("sf.none.title")} body={t("sf.none.body")} /></Card>}
      {shown && <AssessmentView a={shown} onRerun={() => void run()} busy={running} />}
      {rows.length > 1 && (
        <Card title={t("sf.history.title")}>
          <ul className="sf-rules" aria-label={t("sf.history.title")}>{rows.map((h) => (
            <li key={h.id}><span>{new Date(h.evaluatedAt).toLocaleString(locale === "fa" ? "fa-IR" : "en-GB")}</span> <Badge tone="neutral">{t(`sf.status.${h.status}`)}</Badge> <span className="ms-muted">{t("sf.history.counts", { n: h.findingCount })}</span></li>
          ))}</ul>
        </Card>
      )}
    </div>
  );
}

/** A patient's own check. */
export function PatientSafety() {
  const { t } = useI18n();
  const { user } = useAuth();
  return (
    <>
      <PageHeader title={t("sf.title")} subtitle={t("sf.sub")} />
      {user ? <SafetyBody subjectId={user.id} /> : null}
    </>
  );
}

/** A professional's view of one patient's check (the server decides, per request, what the care relationship and consent allow). */
export function ProSafety() {
  const { t } = useI18n();
  const { id } = useParams();
  return (
    <>
      <PageHeader title={t("sf.pro.title")} subtitle={t("sf.pro.sub")} actions={<Link className="ms-link" to="../care">{t("sf.pro.back")}</Link>} />
      {id ? <SafetyBody subjectId={id} /> : null}
    </>
  );
}

/** The rule set as it stands: coverage and every rule with its status. Read-only; review actions are API-only in Phase 7. */
export function RulesPage() {
  const { t } = useI18n();
  const code = useCode();
  const cov = useSafetyLoad((a, s) => a.coverage(s));
  const rules = useSafetyLoad((a, s) => a.rules(s));
  const reload = () => { cov.reload(); rules.reload(); };
  const failed = cov.state.status === "error" ? cov.state.error : rules.state.status === "error" ? rules.state.error : null;
  const ready = cov.state.status === "ready" && rules.state.status === "ready";
  return (
    <>
      <PageHeader title={t("sf.rulesPage.title")} subtitle={t("sf.rulesPage.sub")} />
      {!ready && failed == null && <LoadingState label={t("common.loading")} rows={3} />}
      {failed != null && <SafetyProblem error={failed} onRetry={reload} />}
      {ready && cov.state.status === "ready" && rules.state.status === "ready" && (
        <div className="sf-page">
          {cov.state.data.activeRules === 0 && <AlertCard tone="info" title={t("sf.rulesPage.noneActive")}>{t("sf.rulesPage.noneActiveBody")}</AlertCard>}
          <Card title={t("sf.coverage.title")}>
            <dl className="sf-facts">
              <div><dt>{t("sf.coverage.active")}</dt><dd>{cov.state.data.activeRules}</dd></div>
              <div><dt>{t("sf.coverage.demo")}</dt><dd>{cov.state.data.demonstrationRules}</dd></div>
              <div><dt>{t("sf.coverage.inactive")}</dt><dd>{cov.state.data.inactiveRules}</dd></div>
              <div><dt>{t("sf.meta.ruleSet")}</dt><dd dir="ltr">{cov.state.data.ruleSetVersion}</dd></div>
            </dl>
            <h4 className="ms-h4">{t("sf.coverage.unsupported")}</h4>
            <ul className="sf-list">{cov.state.data.unsupportedDomains.map((d) => <li key={d}>{code("sf.unsupported", d)}</li>)}</ul>
          </Card>
          <Card title={t("sf.rulesPage.list")}>
            <ul className="sf-rules" aria-label={t("sf.rulesPage.list")}>
              {rules.state.data.map((r: RuleSummary) => (
                <li key={`${r.ruleId}-${r.version}`}>
                  <span dir="ltr" className="sf-rules__id">{r.ruleId} v{r.version}</span> <span dir="auto">{r.title}</span>
                  <Badge tone="neutral">{t(`sf.ruleStatus.${r.status}`)}</Badge>
                  <Badge tone={r.activation === "Active" ? "success" : r.activation === "DemonstrationOnly" ? "warning" : "neutral"} icon={r.activation === "Active" ? "check" : "alertTriangle"}>{t(`sf.activation.${r.activation}`)}</Badge>
                  {r.isDemo && <Badge tone="warning" icon="alertTriangle">{t("sf.demo.badge")}</Badge>}
                  {r.activationReasons.length > 0 && <span className="ms-muted"> {r.activationReasons.map((x) => code("sf.reason", x)).join(" · ")}</span>}
                </li>
              ))}
              {rules.state.data.length === 0 && <li className="ms-muted">{t("sf.rules.none")}</li>}
            </ul>
          </Card>
        </div>
      )}
    </>
  );
}
