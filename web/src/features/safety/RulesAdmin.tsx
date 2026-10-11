import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { AlertCard, Badge, Button, Card, EmptyState, LoadingState, Select, TextArea, TextField } from "../../components/ui";
import { useAuth } from "../../auth/AuthContext";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { SafetyError, type EvidenceRef, type RuleDefinition, type RuleDetail, type RuleHistory, type RuleOptions, type RuleSummary } from "../../safety/types";
import { SafetyProblem, useCode, useSafetyApi, useSafetyLoad } from "./SafetyCheck";
import "../features.css";
import "./safety.css";

const PERM = { author: "clinicalrules.author", review: "clinicalrules.review", retire: "clinicalrules.retire" } as const;

/** Server refusals as words. Codes are machine codes: translated when we know them, shown as written when we do not. */
function Refusal({ error }: { error: unknown }) {
  const { t } = useI18n();
  const code = useCode();
  if (!(error instanceof SafetyError) || (error.kind !== "invalid" && error.kind !== "conflict")) return error == null ? null : <SafetyProblem error={error} />;
  const codes = [error.code, ...error.errors].filter((c): c is string => !!c);
  return (
    <div role="alert"><AlertCard tone="danger" title={t(error.kind === "conflict" ? "rv.err.conflict" : "rv.err.invalid")}>
      <ul className="sf-list">{[...new Set(codes)].map((c) => <li key={c}>{code("sf.reason", c)}</li>)}</ul>
    </AlertCard></div>
  );
}

/** Flatten a definition to "path -> text" so two versions can be compared field by field. */
function flatten(value: unknown, path = "", out: Record<string, string> = {}): Record<string, string> {
  if (value === null || typeof value !== "object") { out[path] = String(value); return out; }
  if (Array.isArray(value)) {
    if (value.length === 0) out[path] = "[]";
    value.forEach((v, i) => flatten(v, `${path}[${i}]`, out));
    return out;
  }
  Object.entries(value as Record<string, unknown>).forEach(([k, v]) => flatten(v, path ? `${path}.${k}` : k, out));
  return out;
}

export function diffDefinitions(a: RuleDefinition, b: RuleDefinition): { path: string; from: string; to: string }[] {
  const x = flatten({ ...a, version: 0 });
  const y = flatten({ ...b, version: 0 });
  return [...new Set([...Object.keys(x), ...Object.keys(y)])].sort().filter((k) => x[k] !== y[k]).map((k) => ({ path: k, from: x[k] ?? "—", to: y[k] ?? "—" }));
}

const trunc = (s: string) => (s.length > 120 ? `${s.slice(0, 117)}…` : s);

function StatusBadges({ d }: { d: RuleDetail }) {
  const { t } = useI18n();
  const { lifecycle } = d.record;
  return (
    <div className="sf-badges">
      <Badge tone="neutral">{t(`sf.ruleStatus.${lifecycle.status}`)}</Badge>
      <Badge tone={d.activation.state === "Active" ? "success" : d.activation.state === "DemonstrationOnly" ? "warning" : "neutral"} icon={d.activation.state === "Active" ? "check" : "alertTriangle"}>{t(`sf.activation.${d.activation.state}`)}</Badge>
      {lifecycle.isDemo && <Badge tone="warning" icon="alertTriangle">{t("sf.demo.badge")}</Badge>}
    </div>
  );
}

/** One rule version: content, provenance, who wrote and reviewed it, dates, why it is or is not in force, history, comparison, and the actions the server allows. */
export function RuleDetailPage() {
  const { t, fmt } = useI18n();
  const code = useCode();
  const { ruleId = "", version = "1" } = useParams();
  const v = Number(version);
  const api = useSafetyApi();
  const { can } = useAuth();
  const navigate = useNavigate();
  const detail = useSafetyLoad((a, s) => a.rule(ruleId, v, s), [ruleId, v]);
  const history = useSafetyLoad((a, s) => a.ruleHistory(ruleId, v, s), [ruleId, v]);
  const all = useSafetyLoad((a, s) => a.rules(s), []);
  const [other, setOther] = useState("");
  const [otherDetail, setOtherDetail] = useState<RuleDetail | null>(null);
  const [note, setNote] = useState("");
  const [effective, setEffective] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    if (!other) return;
    let live = true;
    api.rule(ruleId, Number(other)).then((d) => { if (live) setOtherDetail(d); }, () => { if (live) setOtherDetail(null); });
    return () => { live = false; };
  }, [other, ruleId, api]);

  const run = async (fn: () => Promise<unknown>) => {
    setBusy(true); setError(null);
    try { await fn(); detail.reload(); history.reload(); all.reload(); } catch (e) { setError(e); } finally { setBusy(false); }
  };

  const versions = all.state.status === "ready" ? all.state.data.filter((r: RuleSummary) => r.ruleId === ruleId && r.version !== v) : [];
  const diff = useMemo(() => (detail.state.status === "ready" && otherDetail ? diffDefinitions(otherDetail.record.definition, detail.state.data.record.definition) : []), [detail.state, otherDetail]);

  if (detail.state.status === "loading") return <LoadingState label={t("common.loading")} rows={4} />;
  if (detail.state.status === "error") return <SafetyProblem error={detail.state.error} onRetry={detail.reload} />;
  const d = detail.state.data;
  const def = d.record.definition;
  const lc = d.record.lifecycle;
  const h: RuleHistory | null = history.state.status === "ready" ? history.state.data : null;
  const blockers = h?.approvalBlockers ?? [];
  const date = (iso: string | null) => (iso ? fmt.date(new Date(iso), "short") : "—");

  return (
    <>
      <PageHeader title={`${def.ruleId} v${def.version}`} subtitle={def.title} actions={<Link className="ms-link" to="..">{t("rv.back")}</Link>} />
      <div className="sf-page">
        <StatusBadges d={d} />
        {lc.isDemo && <AlertCard tone="warning" title={t("rv.demo.title")}>{t("rv.demo.body")}</AlertCard>}
        {d.activation.state !== "Active" && !lc.isDemo && (
          <AlertCard tone="info" title={t("rv.notActive.title")}>
            <ul className="sf-list">{d.activation.reasons.map((r) => <li key={r}>{code("sf.reason", r)}</li>)}</ul>
          </AlertCard>
        )}
        <AlertCard tone="info" title={t("rv.noneApproved.title")}>{t("rv.noneApproved.body")}</AlertCard>

        <Card title={t("rv.people.title")}>
          <dl className="sf-facts">
            <div><dt>{t("rv.author")}</dt><dd dir="auto">{h?.authorName ?? lc.author}{h?.authorIsYou ? ` (${t("rv.you")})` : ""}</dd></div>
            <div><dt>{t("rv.authoredAt")}</dt><dd>{date(lc.authoredAt)}</dd></div>
            <div><dt>{t("rv.reviewer")}</dt><dd dir="auto">{lc.reviewer ? (h?.reviewerName ?? lc.reviewer) : t("rv.noReviewer")}</dd></div>
            <div><dt>{t("rv.reviewedAt")}</dt><dd>{date(lc.reviewedAt)}</dd></div>
            <div><dt>{t("rv.effective")}</dt><dd>{date(lc.effectiveFrom)}</dd></div>
            <div><dt>{t("rv.retired")}</dt><dd>{date(lc.retiredAt)}</dd></div>
          </dl>
          {lc.reviewNote && <p dir="auto"><strong>{t("rv.reviewNote")}: </strong>{lc.reviewNote}</p>}
        </Card>

        <Card title={t("rv.content.title")}>
          <dl className="sf-facts">
            <div><dt>{t("rv.domain")}</dt><dd>{t(`sf.domain.${def.domain}`)}</dd></div>
            <div><dt>{t("rv.criteria")}</dt><dd dir="ltr">{def.criteria.kind}{def.criteria.minReferenceSeverity ? ` ≥ ${def.criteria.minReferenceSeverity}` : ""}{def.criteria.withinDays ? ` · ${def.criteria.withinDays} d` : ""}</dd></div>
            <div><dt>{t("rv.severity")}</dt><dd>{t(`sf.severity.${def.severity}`)}</dd></div>
            <div><dt>{t("rv.urgency")}</dt><dd>{t(`sf.urgency.${def.urgency}`)}</dd></div>
            <div><dt>{t("rv.inputs")}</dt><dd>{def.requiredInputs.map((i) => t(`sf.input.${i}`)).join(" · ")}</dd></div>
            <div><dt>{t("rv.template")}</dt><dd dir="ltr">{def.guidanceTemplateKey}</dd></div>
            <div><dt>{t("rv.population")}</dt><dd>{def.population.minAgeYears ?? "—"} – {def.population.maxAgeYears ?? "—"}</dd></div>
          </dl>
          <p dir="auto"><strong>{t("rv.purpose")}: </strong>{def.purpose}</p>
          {def.limitations.length > 0 && <><h4 className="ms-h4">{t("sf.f.limits")}</h4><ul className="sf-list">{def.limitations.map((l) => <li key={l} dir="auto">{l}</li>)}</ul></>}
          {def.emergencySigns && def.emergencySigns.length > 0 && <><h4 className="ms-h4">{t("sf.f.signs")}</h4><ul className="sf-list">{def.emergencySigns.map((l) => <li key={l} dir="auto">{l}</li>)}</ul></>}
        </Card>

        <Card title={t("rv.evidence.title")} subtitle={t("rv.evidence.sub")}>
          {def.evidence.length === 0 ? <p className="ms-muted">{t("rv.evidence.none")}</p> : (
            <ul className="sf-evidence">{def.evidence.map((e: EvidenceRef) => (
              <li key={`${e.sourceId}-${e.version}`}>
                <strong dir="auto">{e.sourceName}</strong> <span dir="ltr">({e.sourceId} · {e.version})</span>
                <Badge tone={e.validation === "Validated" ? "success" : e.validation === "Demo" ? "warning" : "danger"} icon={e.validation === "Validated" ? "check" : "alertTriangle"}>{t(`sf.validation.${e.validation}`)}</Badge>
                <span className="ms-muted"> {e.publicationDate ? t("sf.f.published", { date: e.publicationDate }) : t("sf.f.publishedUnknown")} · {e.reviewStatus}</span>
              </li>))}
            </ul>)}
          <p className="ms-muted">{t("rv.evidence.policy")}</p>
        </Card>

        <Card title={t("rv.tests.title")} subtitle={t("rv.tests.sub", { n: def.testCases.length })}>
          <ol className="sf-list">{(def.testCases as { name: string; expectedOutcome: string; expectedFindings: number }[]).map((c) => (
            <li key={c.name} dir="auto">{c.name} — {t(`sf.outcome.${c.expectedOutcome}`)}, {t("rv.tests.findings", { n: c.expectedFindings })}</li>))}
          </ol>
          <details><summary>{t("rv.tests.json")}</summary><pre className="sf-json" dir="ltr">{JSON.stringify(def.testCases, null, 2)}</pre></details>
        </Card>

        {/* actions: shown only when the role allows them AND the state makes them possible; the server re-checks every one */}
        {lc.status === "Draft" && !lc.isDemo && can(PERM.author) && h?.authorIsYou && (
          <Card title={t("rv.actions.author")}>
            <div className="sf-badges">
              <Link className="ms-link" to="edit">{t("rv.edit")}</Link>
              <Button loading={busy} onClick={() => void run(() => api.submitRule(ruleId, v))}>{t("rv.submit")}</Button>
            </div>
            <p className="ms-muted">{t("rv.submit.note")}</p>
          </Card>
        )}
        {lc.status === "UnderReview" && !lc.isDemo && can(PERM.review) && (
          <Card title={t("rv.actions.review")}>
            {h?.authorIsYou && <AlertCard tone="warning" title={t("rv.sod.title")}>{t("rv.sod.body")}</AlertCard>}
            {blockers.filter((b) => b !== "review.separation_of_duties").length > 0 && (
              <AlertCard tone="info" title={t("rv.blockers.title")}><ul className="sf-list">{blockers.filter((b) => b !== "review.separation_of_duties").map((b) => <li key={b}>{code("sf.reason", b)}</li>)}</ul></AlertCard>
            )}
            <TextArea label={t("rv.note")} value={note} onChange={(e) => setNote(e.target.value.slice(0, 500))} rows={3} maxLength={500} />
            <TextField label={t("rv.effectiveFrom")} type="date" value={effective} onChange={(e) => setEffective(e.target.value)} dir="ltr" hint={t("rv.effectiveFromHint")} />
            <div className="sf-badges">
              <Button loading={busy} disabled={!!h?.authorIsYou} onClick={() => void run(() => api.reviewRule(ruleId, v, { decision: "Approve", note: note.trim() || null, effectiveFrom: effective ? `${effective}T00:00:00Z` : null }))}>{t("rv.approve")}</Button>
              <Button variant="secondary" loading={busy} disabled={!!h?.authorIsYou || !note.trim()} onClick={() => void run(() => api.reviewRule(ruleId, v, { decision: "Reject", note: note.trim(), effectiveFrom: null }))}>{t("rv.reject")}</Button>
            </div>
            <p className="ms-muted">{t("rv.review.note")}</p>
          </Card>
        )}
        {lc.status === "Approved" && can(PERM.retire) && (
          <Card title={t("rv.actions.retire")}><Button variant="secondary" loading={busy} onClick={() => void run(() => api.retireRule(ruleId, v))}>{t("rv.retire")}</Button></Card>
        )}
        <Refusal error={error} />

        <Card title={t("rv.history.title")}>
          {h && h.events.length > 0 ? <ul className="sf-rules">{h.events.map((e, i) => <li key={i}><span>{fmt.date(new Date(e.at), "short")}</span> <Badge tone="neutral">{code("rv.event", e.action)}</Badge> <span dir="auto">{e.actor}</span> <span className="ms-muted">{t(`sf.ruleStatus.${e.from}`)} → {t(`sf.ruleStatus.${e.to}`)}</span></li>)}</ul> : <p className="ms-muted">{t("rv.history.none")}</p>}
        </Card>

        {versions.length > 0 && (
          <Card title={t("rv.diff.title")}>
            <Select label={t("rv.diff.pick")} value={other} onValueChange={setOther} options={[{ value: "", label: "—" }, ...versions.map((r) => ({ value: String(r.version), label: `v${r.version} · ${t(`sf.ruleStatus.${r.status}`)}` }))]} />
            {other && otherDetail && (diff.length === 0 ? <p className="ms-muted">{t("rv.diff.same")}</p> : (
              <table className="sf-diff"><caption className="ms-muted">{t("rv.diff.caption", { a: other, b: v })}</caption>
                <thead><tr><th scope="col">{t("rv.diff.field")}</th><th scope="col">v{other}</th><th scope="col">v{v}</th></tr></thead>
                <tbody>{diff.map((r) => <tr key={r.path}><th scope="row" dir="ltr">{r.path}</th><td dir="auto">{trunc(r.from)}</td><td dir="auto">{trunc(r.to)}</td></tr>)}</tbody></table>))}
          </Card>
        )}
        <Button variant="ghost" onClick={() => void navigate("..")}>{t("rv.back")}</Button>
      </div>
    </>
  );
}

const EMPTY_EVIDENCE: EvidenceRef = { sourceId: "", sourceName: "", version: "", publicationDate: null, validation: "Unverified", reviewStatus: "none", locator: null };

/** Create a new draft or edit an existing draft. The form only collects; the server validates everything again, and nothing here can approve anything. */
export function RuleEditorPage({ mode }: { mode: "new" | "edit" }) {
  const { t } = useI18n();
  const params = useParams();
  const options = useSafetyLoad((a, s) => a.ruleOptions(s));
  const existing = useSafetyLoad<RuleDetail | null>((a, s) => (mode === "edit" ? a.rule(params.ruleId ?? "", Number(params.version), s) : Promise.resolve(null)), [mode, params.ruleId, params.version]);
  if (options.state.status === "error") return <SafetyProblem error={options.state.error} onRetry={options.reload} />;
  if (existing.state.status === "error") return <SafetyProblem error={existing.state.error} onRetry={existing.reload} />;
  if (options.state.status !== "ready" || existing.state.status !== "ready") return <LoadingState label={t("common.loading")} rows={4} />;
  const o = options.state.data;
  const first = o.domains[0]!;
  const initial: RuleDefinition = existing.state.data?.record.definition ?? {
    ruleId: "", version: 1, title: "", purpose: "", domain: first.domain, population: { minAgeYears: null, maxAgeYears: null, unsupported: [] }, requiredInputs: first.requiredInputs,
    criteria: { kind: first.criteria, minReferenceSeverity: first.criteria === "ReferenceInteraction" ? "Moderate" : null, withinDays: first.criteria === "SevereSymptomRecent" ? 7 : null },
    severity: "Moderate", urgency: "Routine", guidanceTemplateKey: o.templateKeys[0] ?? "", emergencySigns: null, evidence: [{ ...EMPTY_EVIDENCE }], limitations: [], testCases: [],
  };
  return <RuleEditorForm mode={mode} o={o} initial={initial} />;
}

function RuleEditorForm({ mode, o, initial }: { mode: "new" | "edit"; o: RuleOptions; initial: RuleDefinition }) {
  const { t, locale } = useI18n();
  const api = useSafetyApi();
  const navigate = useNavigate();
  const [def, setDef] = useState<RuleDefinition>(initial);
  const [tests, setTests] = useState(JSON.stringify(initial.testCases, null, 2));
  const [limits, setLimits] = useState(initial.limitations.join("\n"));
  const [signs, setSigns] = useState((initial.emergencySigns ?? []).join("\n"));
  const [unsupported, setUnsupported] = useState(initial.population.unsupported.join("\n"));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [parseError, setParseError] = useState(false);
  const [copyFrom, setCopyFrom] = useState("");

  const load = (d: RuleDefinition, keepIdentity: boolean) => {
    setDef(keepIdentity ? d : { ...d, version: d.version + 1 });
    setTests(JSON.stringify(d.testCases, null, 2));
    setLimits(d.limitations.join("\n"));
    setSigns((d.emergencySigns ?? []).join("\n"));
    setUnsupported(d.population.unsupported.join("\n"));
  };

  const set = (patch: Partial<RuleDefinition>) => setDef({ ...def, ...patch });
  const lines = (s: string) => s.split("\n").map((x) => x.trim()).filter(Boolean);
  const pickDomain = (domain: string) => {
    const dom = o.domains.find((x) => x.domain === domain)!;
    set({ domain: dom.domain, requiredInputs: dom.requiredInputs, criteria: { kind: dom.criteria, minReferenceSeverity: dom.criteria === "ReferenceInteraction" ? "Moderate" : null, withinDays: dom.criteria === "SevereSymptomRecent" ? 7 : null } });
  };
  const setEvidence = (i: number, patch: Partial<EvidenceRef>) => set({ evidence: def.evidence.map((e, j) => (j === i ? { ...e, ...patch } : e)) });

  const save = async () => {
    let parsed: unknown[];
    try { parsed = JSON.parse(tests) as unknown[]; if (!Array.isArray(parsed)) throw new Error("x"); setParseError(false); } catch { setParseError(true); return; }
    const body: RuleDefinition = { ...def, testCases: parsed, limitations: lines(limits), emergencySigns: lines(signs).length ? lines(signs) : null, population: { ...def.population, unsupported: lines(unsupported) } };
    setBusy(true); setError(null);
    try {
      const saved = mode === "new" ? await api.createRule(body) : await api.updateRule(body.ruleId, body.version, body);
      void navigate(`../${encodeURIComponent(saved.record.definition.ruleId)}/${saved.record.definition.version}`, { relative: "path" });
    } catch (e) { setError(e); } finally { setBusy(false); }
  };

  return (
    <>
      <PageHeader title={mode === "new" ? t("rv.new.title") : t("rv.edit.title", { id: def.ruleId, v: def.version })} subtitle={t("rv.new.sub")} />
      <div className="sf-page">
        <AlertCard tone="info" title={t("rv.noneApproved.title")}>{t("rv.editor.note")}</AlertCard>
        {locale === "fa" && <AlertCard tone="info" title={t("sf.fa.unreviewed.title")}>{t("sf.fa.unreviewed.body")}</AlertCard>}
        <Card title={t("rv.editor.identity")}>
          <div className="rec-form rec-form--cols">
            <TextField label={t("rv.ruleId")} hint={t("rv.ruleIdHint")} value={def.ruleId} onChange={(e) => set({ ruleId: e.target.value })} disabled={mode === "edit"} dir="ltr" maxLength={64} />
            <TextField label={t("rv.versionNo")} type="number" value={String(def.version)} onChange={(e) => set({ version: Number(e.target.value) || 1 })} disabled={mode === "edit"} dir="ltr" />
          </div>
          <TextField label={t("rv.titleField")} value={def.title} onChange={(e) => set({ title: e.target.value })} maxLength={200} />
          <TextArea label={t("rv.purpose")} value={def.purpose} onChange={(e) => set({ purpose: e.target.value })} rows={3} maxLength={500} />
        </Card>
        <Card title={t("rv.editor.logic")} subtitle={t("rv.editor.logicSub")}>
          <div className="rec-form rec-form--cols">
            <Select label={t("rv.domain")} value={def.domain} onValueChange={pickDomain} options={o.domains.map((d) => ({ value: d.domain, label: t(`sf.domain.${d.domain}`) }))} />
            <Select label={t("rv.severity")} value={def.severity} onValueChange={(v) => set({ severity: v as RuleDefinition["severity"] })} options={o.severities.map((s) => ({ value: s, label: t(`sf.severity.${s}`) }))} />
            <Select label={t("rv.urgency")} value={def.urgency} onValueChange={(v) => set({ urgency: v as RuleDefinition["urgency"] })} options={o.urgencies.map((s) => ({ value: s, label: t(`sf.urgency.${s}`) }))} />
            <Select label={t("rv.template")} value={def.guidanceTemplateKey} onValueChange={(v) => set({ guidanceTemplateKey: v })} options={o.templateKeys.map((k) => ({ value: k, label: k }))} />
            {def.criteria.kind === "ReferenceInteraction" && <Select label={t("rv.minRef")} value={def.criteria.minReferenceSeverity ?? "Moderate"} onValueChange={(v) => set({ criteria: { ...def.criteria, minReferenceSeverity: v } })} options={o.minReferenceSeverities.map((s) => ({ value: s, label: s }))} />}
            {def.criteria.kind === "SevereSymptomRecent" && <TextField label={t("rv.withinDays")} type="number" value={String(def.criteria.withinDays ?? 7)} onChange={(e) => set({ criteria: { ...def.criteria, withinDays: Number(e.target.value) || 1 } })} dir="ltr" />}
            <TextField label={t("rv.minAge")} type="number" value={def.population.minAgeYears === null ? "" : String(def.population.minAgeYears)} onChange={(e) => set({ population: { ...def.population, minAgeYears: e.target.value === "" ? null : Number(e.target.value) } })} dir="ltr" />
            <TextField label={t("rv.maxAge")} type="number" value={def.population.maxAgeYears === null ? "" : String(def.population.maxAgeYears)} onChange={(e) => set({ population: { ...def.population, maxAgeYears: e.target.value === "" ? null : Number(e.target.value) } })} dir="ltr" />
          </div>
          <p className="ms-muted">{t("rv.editor.derived", { kind: def.criteria.kind, inputs: def.requiredInputs.map((i) => t(`sf.input.${i}`)).join(", ") })}</p>
          <TextArea label={t("rv.unsupported")} hint={t("rv.linesHint")} value={unsupported} onChange={(e) => setUnsupported(e.target.value)} rows={2} />
          <TextArea label={t("sf.f.limits")} hint={t("rv.linesHint")} value={limits} onChange={(e) => setLimits(e.target.value)} rows={3} />
          <TextArea label={t("sf.f.signs")} hint={t("rv.signsHint")} value={signs} onChange={(e) => setSigns(e.target.value)} rows={2} />
        </Card>
        <Card title={t("rv.evidence.title")} subtitle={t("rv.evidence.sub")}>
          {def.evidence.map((e, i) => (
            <fieldset key={i} className="sf-fieldset">
              <legend>{t("rv.evidence.n", { n: i + 1 })}</legend>
              <div className="rec-form rec-form--cols">
                <TextField label={t("rv.ev.id")} value={e.sourceId} onChange={(x) => setEvidence(i, { sourceId: x.target.value })} dir="ltr" maxLength={64} />
                <TextField label={t("rv.ev.name")} value={e.sourceName} onChange={(x) => setEvidence(i, { sourceName: x.target.value })} maxLength={200} />
                <TextField label={t("rv.ev.version")} value={e.version} onChange={(x) => setEvidence(i, { version: x.target.value })} dir="ltr" maxLength={64} />
                <TextField label={t("rv.ev.date")} type="date" value={e.publicationDate ?? ""} onChange={(x) => setEvidence(i, { publicationDate: x.target.value || null })} dir="ltr" hint={t("rv.ev.dateHint")} />
                <Select label={t("rv.ev.validation")} value={e.validation} onValueChange={(v) => setEvidence(i, { validation: v as EvidenceRef["validation"] })} options={o.validations.map((s) => ({ value: s, label: t(`sf.validation.${s}`) }))} />
                <TextField label={t("rv.ev.review")} value={e.reviewStatus} onChange={(x) => setEvidence(i, { reviewStatus: x.target.value })} maxLength={64} />
              </div>
              <Button size="sm" variant="ghost" onClick={() => set({ evidence: def.evidence.filter((_, j) => j !== i) })}>{t("common.remove")}</Button>
            </fieldset>
          ))}
          <Button size="sm" variant="secondary" onClick={() => set({ evidence: [...def.evidence, { ...EMPTY_EVIDENCE }] })}>{t("rv.ev.add")}</Button>
          <p className="ms-muted">{t("rv.evidence.policy")}</p>
        </Card>
        <Card title={t("rv.tests.title")} subtitle={t("rv.tests.editSub")}>
          <TextArea label={t("rv.tests.json")} value={tests} onChange={(e) => setTests(e.target.value)} rows={10} dir="ltr" />
          {parseError && <div role="alert"><AlertCard tone="danger" title={t("rv.tests.badJson")}>{t("rv.tests.badJsonBody")}</AlertCard></div>}
          {mode === "new" && <CopyFrom onPick={(d) => { load(d, false); setCopyFrom(`${d.ruleId} v${d.version}`); }} />}
          {copyFrom && <p className="ms-muted">{t("rv.copied", { from: copyFrom })}</p>}
        </Card>
        <Refusal error={error} />
        <div className="sf-badges">
          <Button loading={busy} onClick={() => void save()}>{mode === "new" ? t("rv.saveDraft") : t("rv.saveChanges")}</Button>
          <Button variant="ghost" onClick={() => void navigate(-1)}>{t("common.cancel")}</Button>
        </div>
      </div>
    </>
  );
}

/** Start a new draft from an existing version's content (as the next version number). */
function CopyFrom({ onPick }: { onPick(d: RuleDefinition): void }) {
  const { t } = useI18n();
  const api = useSafetyApi();
  const list = useSafetyLoad((a, s) => a.rules(s));
  const [busy, setBusy] = useState(false);
  if (list.state.status !== "ready" || list.state.data.length === 0) return null;
  return (
    <Select label={t("rv.copy")} value="" disabled={busy} onValueChange={(v) => {
      if (!v) return;
      const [id, ver] = v.split("@");
      setBusy(true);
      api.rule(id!, Number(ver)).then((d) => onPick(d.record.definition), () => undefined).finally(() => setBusy(false));
    }} options={[{ value: "", label: "—" }, ...list.state.data.map((r) => ({ value: `${r.ruleId}@${r.version}`, label: `${r.ruleId} v${r.version}` }))]} />
  );
}

export function NoRule() {
  const { t } = useI18n();
  return <EmptyState icon="clipboard" title={t("rv.none")} />;
}

export const RuleEditorNew = () => <RuleEditorPage mode="new" />;
export const RuleEditorEdit = () => <RuleEditorPage mode="edit" />;
