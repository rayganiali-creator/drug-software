import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { AlertCard, Badge, Button, Card, Checkbox, EmptyState, Select, TextArea, TextField } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAction, useLoad, useRecordsApi, useSelfId } from "../../records/hooks";
import type { ProductRecord, Report, ReportPayload, ReportStatus } from "../../records/types";
import { ActionError, DemoFlag, Form, Loaded, dayKey } from "./shared";

const FLOW: ReportStatus[] = ["Draft", "PendingConsentOrReview", "ReadyToSend", "Acknowledged"];
const ISSUES = ["AdverseEvent", "AbnormalAppearanceOrPackaging", "ApparentLackOfEffect", "QualityProblem", "Other"] as const;

/** Reports about a product problem, from draft to the (mock) manufacturer. The patient sees exactly what would be sent. */
export function PatientReports() {
  const { t } = useI18n();
  const id = useSelfId();
  const [params] = useSearchParams();
  const reports = useLoad((a, s) => a.reports(id, s), [id]);
  const products = useLoad((a, s) => a.products(id, s), [id]);
  return (
    <>
      <PageHeader title={t("rec.rep.title")} subtitle={t("rec.rep.sub")} />
      <AlertCard tone="info" title={t("rec.rep.limitTitle")}>{t("rec.rep.limitBody")}</AlertCard>
      <div className="rec-grid rec-grid--wide" style={{ marginBlockStart: "var(--space-4)" }}>
        <Card title={t("rec.rep.list")}>
          <Loaded load={reports.state} reload={reports.reload}>{(rows: Report[]) => rows.length === 0 ? <EmptyState icon="fileText" title={t("rec.rep.empty")} body={t("rec.rep.emptyBody")} /> : (
            <ul className="rec-list" aria-label={t("rec.rep.list")}>{rows.map((r) => <ReportRow key={r.id} r={r} onChanged={reports.reload} />)}</ul>)}
          </Loaded>
        </Card>
        <Loaded load={products.state} reload={products.reload}>{(p) => <NewReport products={p} preselect={params.get("product")} onCreated={reports.reload} />}</Loaded>
      </div>
    </>
  );
}

export function Steps({ status }: { status: ReportStatus }) {
  const { t } = useI18n();
  const idx = FLOW.indexOf(status === "Sent" ? "Acknowledged" : status);
  return (
    <ol className="rec-steps" aria-label={t("rec.rep.steps")}>
      {FLOW.map((s, i) => <li key={s} className="rec-step" data-done={idx > i} aria-current={idx === i ? "step" : undefined}>{t(`rec.rep.status.${s}`)}</li>)}
    </ol>
  );
}

export function PayloadView({ p }: { p: ReportPayload }) {
  const { t, fmt } = useI18n();
  return (
    <div className="rec-note">
      <strong>{t("rec.rep.payload")}</strong>
      <p className="ms-muted">{t("rec.rep.payloadNote")}</p>
      <dl className="rec-facts">
        <div><dt>{t("rec.rep.reference")}</dt><dd><bdi dir="ltr">{p.reportReference}</bdi></dd></div>
        <div><dt>{t("rec.batch.title")}</dt><dd>{p.productName} · <bdi dir="ltr">{p.batchNumber}</bdi></dd></div>
        <div><dt>{t("rec.batch.maker")}</dt><dd>{p.manufacturerName ?? "—"}</dd></div>
        <div><dt>{t("rec.batch.expiry")}</dt><dd>{fmt.date(new Date(p.expiryDate), "short")}</dd></div>
        <div><dt>{t("rec.rep.issue")}</dt><dd>{t(`rec.issue.${p.issueType}`)} · {t(`rec.severity.${p.severity}`)}</dd></div>
        <div><dt>{t("rec.rep.when")}</dt><dd>{fmt.date(new Date(p.occurredOn), "short")}</dd></div>
        <div><dt>{t("rec.rep.ageSex")}</dt><dd>{p.ageGroup === "unknown" ? t("rec.rep.notShared") : fmt.digits(p.ageGroup)} · {p.sexGroup === "unknown" ? t("rec.rep.notShared") : t(`rec.sex.${p.sexGroup}`)}</dd></div>
        <div><dt>{t("rec.rep.other")}</dt><dd>{p.concomitantMedications.length ? p.concomitantMedications.join("، ") : t("rec.rep.notShared")}</dd></div>
        {p.description && <div><dt>{t("rec.rep.description")}</dt><dd>{p.description}</dd></div>}
      </dl>
      <p className="ms-muted">{t("rec.rep.neverSent")}</p>
    </div>
  );
}

function ReportRow({ r, onChanged }: { r: Report; onChanged(): void }) {
  const { t, fmt } = useI18n();
  const api = useRecordsApi();
  const id = useSelfId();
  const act = useAction();
  const [consentOpen, setConsentOpen] = useState(false);
  return (
    <li className="rec-row" style={{ flexDirection: "column", alignItems: "stretch" }}>
      <div className="rec-row__main">
        <span className="rec-row__title">{r.productName} · <bdi dir="ltr">{r.batchNumber}</bdi></span>
        <span className="rec-row__meta">{t(`rec.issue.${r.issueType}`)} · {t(`rec.severity.${r.severity}`)} · {fmt.date(new Date(r.occurredOn), "short")}</span>
        <Steps status={r.status} />
        <span className="rec-badges">
          <Badge tone={r.status === "Failed" ? "danger" : r.status === "Acknowledged" ? "success" : "info"} icon={r.status === "Acknowledged" ? "check" : "clock"}>{t(`rec.rep.status.${r.status}`)}</Badge>
          {r.reviewRequired && <Badge tone="neutral" icon="user">{t("rec.rep.needsReview")}</Badge>}
          {r.isMockDelivery && <Badge tone="warning" icon="alertTriangle">{t("rec.rep.mockDelivery")}</Badge>}
          <DemoFlag show={r.isDemo} />
        </span>
        {r.reviewerNote && <span className="rec-row__meta">{t("rec.rep.reviewerNote", { note: r.reviewerNote })}</span>}
        {r.failureCode && r.status === "PendingConsentOrReview" && <span className="rec-row__meta">{t("rec.rep.consentLost")}</span>}
      </div>
      {r.payloadPreview && <PayloadView p={r.payloadPreview} />}
      <div className="rec-row__actions">
        {r.status === "Draft" && (
          <Button size="sm" loading={act.busy} onClick={() => (r.consentActive ? act.run(() => api.submitReport(id, r.id, r.version), onChanged) : setConsentOpen(true))}>{t("rec.rep.submit")}</Button>
        )}
        {["Draft", "PendingConsentOrReview", "ReadyToSend"].includes(r.status) && <Button size="sm" variant="ghost" onClick={() => act.run(() => api.cancelReport(id, r.id), onChanged)}>{t("rec.rep.cancel")}</Button>}
      </div>
      {consentOpen && <ReportConsent onDone={() => { setConsentOpen(false); onChanged(); }} />}
      <ActionError error={act.error} />
    </li>
  );
}

/** The consent needed before a report can leave: its own purpose, shown plainly, with the data it covers chosen by the patient. */
function ReportConsent({ onDone }: { onDone(): void }) {
  const { t } = useI18n();
  const api = useRecordsApi();
  const act = useAction();
  const [profile, setProfile] = useState(false);
  const [meds, setMeds] = useState(false);
  return (
    <div className="rec-note">
      <strong>{t("rec.rep.consentTitle")}</strong>
      <p>{t("rec.rep.consentBody")}</p>
      <Checkbox label={t("rec.rep.consentProfile")} checked={profile} onChange={(e) => setProfile(e.target.checked)} />
      <Checkbox label={t("rec.rep.consentMeds")} checked={meds} onChange={(e) => setMeds(e.target.checked)} />
      <div className="rec-row__actions">
        <Button loading={act.busy} onClick={() => act.run(() => api.grantConsent({ granteeUserId: null, purpose: "ManufacturerReport", scope: ["products", ...(profile ? ["profile"] : []), ...(meds ? ["medications"] : [])], expiresAt: new Date(Date.now() + 30 * 86400000).toISOString(), version: "consent-report-v1" }), onDone)}>{t("rec.rep.consentGrant")}</Button>
        <Button variant="ghost" onClick={onDone}>{t("common.cancel")}</Button>
      </div>
      <ActionError error={act.error} />
    </div>
  );
}

function NewReport({ products, preselect, onCreated }: { products: ProductRecord[]; preselect: string | null; onCreated(): void }) {
  const { t } = useI18n();
  const api = useRecordsApi();
  const id = useSelfId();
  const act = useAction();
  const [picked, setProduct] = useState<string | null>(null);
  const product = picked ?? preselect ?? products[0]?.id ?? "";
  const [issue, setIssue] = useState<string>("AbnormalAppearanceOrPackaging");
  const [severity, setSeverity] = useState("Mild");
  const [when, setWhen] = useState(dayKey());
  const [days, setDays] = useState("");
  const [text, setText] = useState("");
  const [others, setOthers] = useState(false);
  if (products.length === 0) return <Card title={t("rec.rep.new")}><EmptyState icon="box" title={t("rec.rep.needProduct")} /></Card>;
  return (
    <Card title={t("rec.rep.new")} subtitle={t("rec.rep.newSub")}>
      <Form busy={act.busy} submitLabel={t("rec.rep.create")} onSubmit={() => void act.run(() => api.createReport(id, {
        productRecordId: product, issueType: issue, severity, occurredOn: when, durationOfUseDays: days.trim() ? Number(days) : null, description: text.trim() || null, includeConcomitantMedications: others, clientRequestId: crypto.randomUUID(),
      }), () => { setText(""); onCreated(); })}>
        <Select label={t("rec.rep.product")} value={product} onValueChange={setProduct} options={products.map((p) => ({ value: p.id, label: `${p.productName} · ${p.batchNumber}` }))} />
        <div className="rec-form rec-form--cols">
          <Select label={t("rec.rep.issue")} value={issue} onValueChange={setIssue} options={ISSUES.map((i) => ({ value: i, label: t(`rec.issue.${i}`) }))} />
          <Select label={t("rec.rep.severity")} value={severity} onValueChange={setSeverity} options={(["Unknown", "Mild", "Moderate", "Severe"] as const).map((s) => ({ value: s, label: t(`rec.severity.${s}`) }))} />
          <TextField label={t("rec.rep.when")} type="date" value={when} onChange={(e) => setWhen(e.target.value)} dir="ltr" />
          <TextField label={t("rec.rep.days")} inputMode="numeric" value={days} onChange={(e) => setDays(e.target.value)} dir="ltr" />
        </div>
        <TextArea label={t("rec.rep.description")} hint={t("rec.rep.descHint")} value={text} onChange={(e) => setText(e.target.value)} maxLength={1000} rows={3} />
        <Checkbox label={t("rec.rep.includeOthers")} checked={others} onChange={(e) => setOthers(e.target.checked)} />
      </Form>
      <ActionError error={act.error} />
    </Card>
  );
}
