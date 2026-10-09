import { useState } from "react";
import { Badge, Button, Card, Checkbox, EmptyState, Select } from "../../components/ui";
import { consentPurposes, granteeFreePurposes, type ConsentPurpose } from "../../auth/access.g";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAction, useLoad, useRecordsApi, useSelfId } from "../../records/hooks";
import type { AiContext, CareRow, ConsentEvent, ConsentRow, Provider } from "../../records/types";
import { ActionError, Form, Loaded } from "./shared";

const SCOPES = ["profile", "medications", "allergies", "conditions", "symptoms", "adherence", "products", "prescriptions", "adr", "checkins", "ai_summary"] as const;
const isFree = (p: string) => (granteeFreePurposes as readonly string[]).includes(p);

/** Who may see what: care relationships (agreed by both sides), consents, their history, and what an AI feature would receive. */
export function PatientSharing() {
  const { t } = useI18n();
  const care = useLoad((a, s) => a.care(s));
  const consents = useLoad((a, s) => a.consents(s));
  const history = useLoad((a, s) => a.consentHistory(s));
  const providers = useLoad((a, s) => a.providers(s));
  const refresh = () => { care.reload(); consents.reload(); history.reload(); };
  return (
    <>
      <PageHeader title={t("rec.share.title")} subtitle={t("rec.share.sub")} />
      <div className="rec-grid rec-grid--2">
        <Card title={t("rec.care.title")} subtitle={t("rec.care.sub")}>
          <Loaded load={care.state} reload={care.reload}>{(rows: CareRow[]) => rows.length === 0 ? <EmptyState icon="users" title={t("rec.care.empty")} /> : (
            <ul className="rec-list" aria-label={t("rec.care.title")}>{rows.map((r) => <CareItem key={r.relationship.id} row={r} onChanged={refresh} />)}</ul>)}
          </Loaded>
          <Loaded load={providers.state} reload={providers.reload}>{(p) => <RequestCare providers={p} onDone={refresh} />}</Loaded>
        </Card>
        <Card title={t("rec.consent.title")} subtitle={t("rec.consent.sub")}>
          <Loaded load={consents.state} reload={consents.reload}>{(rows: ConsentRow[]) => rows.length === 0 ? <EmptyState icon="shield" title={t("rec.consent.empty")} /> : (
            <ul className="rec-list" aria-label={t("rec.consent.title")}>{rows.map((c) => <ConsentItem key={c.consent.id} row={c} onChanged={refresh} />)}</ul>)}
          </Loaded>
          <Loaded load={providers.state} reload={providers.reload}>{(p) => <GrantConsent providers={p} onDone={refresh} />}</Loaded>
        </Card>
        <Card title={t("rec.history.title")}>
          <Loaded load={history.state} reload={history.reload}>{(rows: ConsentEvent[]) => rows.length === 0 ? <EmptyState icon="clock" title={t("rec.history.empty")} /> : <HistoryList rows={rows.slice(0, 20)} />}</Loaded>
        </Card>
        <AiContextCard />
      </div>
    </>
  );
}

function HistoryList({ rows }: { rows: ConsentEvent[] }) {
  const { t, fmt } = useI18n();
  return <ul className="rec-list" aria-label={t("rec.history.title")}>{rows.map((e, i) => (
    <li key={`${e.consentId}-${e.kind}-${i}`} className="rec-row"><span>{t(`rec.purpose.${e.purpose}`)}</span><span className="ms-muted">{t(`rec.history.${e.kind}`)} · {fmt.date(new Date(e.at), "short")}</span></li>))}</ul>;
}

function CareItem({ row, onChanged }: { row: CareRow; onChanged(): void }) {
  const { t } = useI18n();
  const api = useRecordsApi();
  const me = useSelfId();
  const act = useAction();
  const r = row.relationship;
  const iAmPatient = r.patientSubjectId === me;
  const waitingForMe = (iAmPatient && r.status === "PendingPatient") || (!iAmPatient && r.status === "PendingProvider");
  const other = iAmPatient ? row.providerName ?? t("rec.care.organization") : row.patientName;
  const act1 = (a: "accept" | "decline" | "end") => act.run(() => api.careAction(r.id, a), onChanged);
  return (
    <li className="rec-row">
      <div className="rec-row__main">
        <span className="rec-row__title">{other}</span>
        <span className="rec-row__meta">{t(`rec.care.kind.${r.kind}`)}</span>
        <span className="rec-badges"><Badge tone={r.status === "Active" ? "success" : r.status.startsWith("Pending") ? "info" : "neutral"} icon={r.status === "Active" ? "check" : "clock"}>{t(`rec.care.status.${r.status}`)}</Badge></span>
      </div>
      <div className="rec-row__actions">
        {waitingForMe && <><Button size="sm" onClick={() => act1("accept")}>{t("rec.care.accept")}</Button><Button size="sm" variant="secondary" onClick={() => act1("decline")}>{t("rec.care.decline")}</Button></>}
        {(r.status === "Active" || (r.status.startsWith("Pending") && !waitingForMe)) && <Button size="sm" variant="ghost" onClick={() => act1("end")}>{r.status === "Active" ? t("rec.care.end") : t("rec.care.withdraw")}</Button>}
      </div>
      <ActionError error={act.error} />
    </li>
  );
}

function RequestCare({ providers, onDone }: { providers: Provider[]; onDone(): void }) {
  const { t } = useI18n();
  const api = useRecordsApi();
  const act = useAction();
  const [who, setWho] = useState(providers[0]?.id ?? "");
  const [kind, setKind] = useState("Treating");
  if (providers.length === 0) return null;
  return (
    <Form busy={act.busy} submitLabel={t("rec.care.request")} onSubmit={() => void act.run(() => api.requestCare(who, kind), onDone)}>
      <div className="rec-form rec-form--cols">
        <Select label={t("rec.care.who")} value={who} onValueChange={setWho} options={providers.map((p) => ({ value: p.id, label: p.displayName }))} />
        <Select label={t("rec.care.kindLabel")} value={kind} onValueChange={setKind} options={[{ value: "Treating", label: t("rec.care.kind.Treating") }, { value: "Dispensing", label: t("rec.care.kind.Dispensing") }]} />
      </div>
      <p className="ms-muted">{t("rec.care.twoSided")}</p>
      <ActionError error={act.error} />
    </Form>
  );
}

function ConsentItem({ row, onChanged }: { row: ConsentRow; onChanged(): void }) {
  const { t, fmt } = useI18n();
  const api = useRecordsApi();
  const act = useAction();
  const c = row.consent;
  return (
    <li className="rec-row">
      <div className="rec-row__main">
        <span className="rec-row__title">{t(`rec.purpose.${c.purpose}`)}{row.granteeName ? ` · ${row.granteeName}` : ""}</span>
        <span className="rec-row__meta">{c.scope.map((s) => t(`rec.scope.${s}`)).join("، ")}</span>
        <span className="rec-badges"><Badge tone={c.status === "Active" ? "success" : "neutral"} icon={c.status === "Active" ? "check" : "clock"}>{t(`rec.consent.status.${c.status}`)}</Badge>
          {c.expiresAt && <span className="rec-row__meta">{t("rec.consent.until", { date: fmt.date(new Date(c.expiresAt), "short") })}</span>}</span>
      </div>
      {c.status === "Active" && <Button size="sm" variant="secondary" onClick={() => act.run(() => api.revokeConsent(c.id), onChanged)}>{t("rec.consent.revoke")}</Button>}
      <ActionError error={act.error} />
    </li>
  );
}

function GrantConsent({ providers, onDone }: { providers: Provider[]; onDone(): void }) {
  const { t } = useI18n();
  const api = useRecordsApi();
  const act = useAction();
  const [purpose, setPurpose] = useState<ConsentPurpose>("Treatment");
  const [who, setWho] = useState(providers[0]?.id ?? "");
  const [scope, setScope] = useState<string[]>(["medications"]);
  const free = isFree(purpose);
  const toggle = (s: string) => setScope((cur) => (cur.includes(s) ? cur.filter((x) => x !== s) : [...cur, s]));
  return (
    <Form busy={act.busy} submitLabel={t("rec.consent.grant")} onSubmit={() => void act.run(() => api.grantConsent({ granteeUserId: free ? null : who, purpose, scope, expiresAt: new Date(Date.now() + 90 * 86400000).toISOString(), version: "consent-text-v1" }), onDone)}>
      <Select label={t("rec.consent.purpose")} value={purpose} onValueChange={(v) => setPurpose(v as ConsentPurpose)} options={consentPurposes.filter((p) => p !== "Research").map((p) => ({ value: p, label: t(`rec.purpose.${p}`) }))} hint={t(`rec.purposeHint.${purpose}`)} />
      {!free && providers.length > 0 && <Select label={t("rec.consent.to")} value={who} onValueChange={setWho} options={providers.map((p) => ({ value: p.id, label: p.displayName }))} />}
      <fieldset><legend>{t("rec.consent.what")}</legend>{SCOPES.map((s) => <Checkbox key={s} label={t(`rec.scope.${s}`)} checked={scope.includes(s)} onChange={() => toggle(s)} />)}</fieldset>
      <p className="ms-muted">{t("rec.consent.days")}</p>
      <ActionError error={act.error} />
    </Form>
  );
}

function AiContextCard() {
  const { t } = useI18n();
  const id = useSelfId();
  const ctx = useLoad((a, s) => a.aiContext(id, s), [id]);
  return (
    <Card title={t("rec.ai.title")} subtitle={t("rec.ai.sub")}>
      <Loaded load={ctx.state} reload={ctx.reload}>{(c: AiContext) => (
        <div className="rec-stack">
          <dl className="rec-facts">
            <div><dt>{t("rec.ai.age")}</dt><dd>{c.ageGroup === "unknown" ? t("rec.rep.notShared") : c.ageGroup}</dd></div>
            <div><dt>{t("rec.ai.meds")}</dt><dd>{c.medications.length}</dd></div>
            <div><dt>{t("rec.ai.allergies")}</dt><dd>{c.allergies.length}</dd></div>
            <div><dt>{t("rec.ai.conditions")}</dt><dd>{c.conditions.length}</dd></div>
            <div><dt>{t("rec.ai.symptoms")}</dt><dd>{c.recentSymptoms.length}</dd></div>
            <div><dt>{t("rec.ai.external")}</dt><dd>{c.externalProcessingConsented ? t("common.yes") : t("common.no")}</dd></div>
          </dl>
          {c.excluded.length > 0 && <p className="ms-muted">{t("rec.ai.excluded", { list: c.excluded.map((e) => t(`rec.cat.${e.category}`)).join("، ") })}</p>}
          <p className="ms-muted">{t("rec.ai.none")}</p>
        </div>)}
      </Loaded>
    </Card>
  );
}
