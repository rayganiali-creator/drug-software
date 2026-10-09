import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Badge, Button, Card, EmptyState, Select, TextField } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAction, useLoad, useRecordsApi, useSelfId } from "../../records/hooks";
import type { PatientMedication, ProductRecord } from "../../records/types";
import { ActionError, DemoFlag, Form, Loaded, dayKey, orNull, useTry } from "./shared";

/** Batch / lot records of the packages the patient received. Manual entry only; scanning is announced as "coming later". */
export function PatientBatches() {
  const { t } = useI18n();
  const id = useSelfId();
  const products = useLoad((a, s) => a.products(id, s), [id]);
  const meds = useLoad((a, s) => a.medications(id, false, s), [id]);
  return (
    <>
      <PageHeader title={t("rec.batch.title")} subtitle={t("rec.batch.sub")} />
      <div className="rec-grid rec-grid--wide">
        <Card title={t("rec.batch.list")}>
          <Loaded load={products.state} reload={products.reload}>{(rows: ProductRecord[]) => rows.length === 0 ? <EmptyState icon="box" title={t("rec.batch.empty")} body={t("rec.batch.emptyBody")} /> : (
            <ul className="rec-list" aria-label={t("rec.batch.list")}>{rows.map((p) => <ProductRow key={p.id} p={p} onChanged={products.reload} />)}</ul>)}
          </Loaded>
        </Card>
        <div className="rec-stack">
          <Loaded load={meds.state} reload={meds.reload}>{(m) => <AddProduct meds={m} onAdded={products.reload} />}</Loaded>
          <Card title={t("rec.scan.title")}>
            <p className="ms-muted">{t("rec.scan.body")}</p>
            <Button variant="secondary" iconStart="image" disabled>{t("rec.scan.button")}</Button>
            <Badge tone="neutral" icon="clock">{t("rec.futureBadge")}</Badge>
          </Card>
        </div>
      </div>
    </>
  );
}

function ProductRow({ p, onChanged }: { p: ProductRecord; onChanged(): void }) {
  const { t, fmt, locale } = useI18n();
  const tryT = useTry();
  const api = useRecordsApi();
  const id = useSelfId();
  const act = useAction();
  const nav = useNavigate();
  const name = p.referenceName ? (p.referenceName[locale] ?? p.referenceName.en ?? p.productName) : p.productName;
  return (
    <li className="rec-row" style={{ flexDirection: "column", alignItems: "stretch" }}>
      <div className="rec-row" style={{ border: 0, padding: 0 }}>
        <div className="rec-row__main">
          <span className="rec-row__title">{name}</span>
          <dl className="rec-facts">
            <div><dt>{t("rec.batch.number")}</dt><dd><bdi dir="ltr">{p.batchNumber}</bdi></dd></div>
            <div><dt>{t("rec.batch.expiry")}</dt><dd>{fmt.date(new Date(p.expiryDate), "short")}</dd></div>
            <div><dt>{t("rec.batch.maker")}</dt><dd>{p.manufacturerName ?? t("rec.batch.unknownMaker")}</dd></div>
            {p.gtin && <div><dt>GTIN</dt><dd><bdi dir="ltr">{p.gtin}</bdi></dd></div>}
          </dl>
          <span className="rec-badges">
            {p.expired && <Badge tone="danger" icon="alertTriangle">{t("rec.batch.expired")}</Badge>}
            <Badge tone={p.verification === "ProfessionalConfirmed" ? "success" : "neutral"} icon={p.verification === "ProfessionalConfirmed" ? "check" : "user"}>{t(`rec.batch.verif.${p.verification}`)}</Badge>
            <Badge tone={p.consistency === "Mismatch" ? "warning" : "neutral"} icon={p.consistency === "Consistent" ? "check" : "info"}>{t(`rec.batch.cons.${p.consistency}`)}</Badge>
            <DemoFlag show={p.isDemo} />
          </span>
          {p.findings.map((f) => <span key={f} className="rec-row__meta">• {tryT(`rec.finding.${f.replace(/[.]/g, "_")}`, f)}</span>)}
        </div>
        <div className="rec-row__actions">
          <Button size="sm" variant="secondary" iconStart="alertTriangle" onClick={() => nav(`../myreports?product=${p.id}`)}>{t("rec.batch.report")}</Button>
          <Button size="sm" variant="ghost" iconStart="trash" onClick={() => act.run(() => api.removeProduct(id, p.id), onChanged)}>{t("common.remove")}</Button>
        </div>
      </div>
      <ActionError error={act.error} />
    </li>
  );
}

function AddProduct({ meds, onAdded }: { meds: PatientMedication[]; onAdded(): void }) {
  const { t } = useI18n();
  const api = useRecordsApi();
  const id = useSelfId();
  const act = useAction();
  const [medId, setMedId] = useState("");
  const [typed, setTyped] = useState("");
  const [batch, setBatch] = useState("");
  const [expiry, setExpiry] = useState("");
  const [made, setMade] = useState("");
  const [received, setReceived] = useState(dayKey());
  const [gtin, setGtin] = useState("");
  const [pharmacy, setPharmacy] = useState("");
  const chosen = meds.find((m) => m.id === medId);
  return (
    <Card title={t("rec.batch.add")} subtitle={t("rec.batch.addSub")}>
      <Form busy={act.busy} submitLabel={t("rec.add")} onSubmit={() => void act.run(() => api.addProduct(id, {
        patientMedicationId: chosen?.id ?? null, medicationId: chosen?.medicationId ?? null, productName: chosen?.medicationId ? null : (chosen?.displayName ?? orNull(typed)),
        batchNumber: batch, manufactureDate: orNull(made), expiryDate: expiry, gtin: orNull(gtin), pharmacyNote: orNull(pharmacy), receivedOn: received, method: "Manual",
      }), () => { setBatch(""); setExpiry(""); setMade(""); setGtin(""); setPharmacy(""); setTyped(""); onAdded(); })}>
        <Select label={t("rec.batch.which")} value={medId} onValueChange={setMedId} options={[{ value: "", label: t("rec.batch.other") }, ...meds.map((m) => ({ value: m.id, label: m.displayName }))]} />
        {!chosen && <TextField label={t("rec.batch.typedName")} value={typed} onChange={(e) => setTyped(e.target.value)} maxLength={200} />}
        <div className="rec-form rec-form--cols">
          <TextField label={t("rec.batch.number")} value={batch} onChange={(e) => setBatch(e.target.value)} maxLength={40} dir="ltr" required />
          <TextField label={t("rec.batch.expiry")} type="date" value={expiry} onChange={(e) => setExpiry(e.target.value)} dir="ltr" required />
          <TextField label={t("rec.batch.made")} type="date" value={made} onChange={(e) => setMade(e.target.value)} dir="ltr" />
          <TextField label={t("rec.batch.received")} type="date" value={received} onChange={(e) => setReceived(e.target.value)} dir="ltr" />
          <TextField label="GTIN" hint={t("rec.batch.gtinHint")} value={gtin} onChange={(e) => setGtin(e.target.value)} maxLength={14} inputMode="numeric" dir="ltr" />
          <TextField label={t("rec.batch.pharmacy")} hint={t("rec.batch.pharmacyHint")} value={pharmacy} onChange={(e) => setPharmacy(e.target.value)} maxLength={200} />
        </div>
      </Form>
      <ActionError error={act.error} />
      <p className="ms-muted">{t("rec.batch.noInvent")}</p>
    </Card>
  );
}
