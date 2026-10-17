import { useState, type FormEvent } from "react";
import { Link, useParams } from "react-router-dom";
import { AlertCard, Badge, Button, Card, EmptyState, Select, TextArea, TextField } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAction, useLoad } from "../../records/hooks";
import type { CareRow, ProductRecord } from "../../records/types";
import { allStatuses, concernTypes, examples, type Concern, type ConcernDetail, type DispensingErrorExample, type Entry, type EventDetail, type Identifiers, type PharmacyRole, type ReachedPatient, type SafetyEvent, type SafetyEventKind, type SafetyStatus, type Summary } from "../../pharmacy/types";
import { ActionError, Loaded } from "../records/shared";
import { useOrganizationId, usePharmacyApi, usePharmacyLoad } from "./hooks";
import "./pharmacy.css";

const dayToDate = (d: string) => (d || null);

function Unreviewed() {
  const { t, locale } = useI18n();
  return locale === "fa" ? <AlertCard tone="info" title={t("sf.fa.unreviewed.title")}>{t("sf.fa.unreviewed.body")}</AlertCard> : null;
}

function NoOrg() {
  const { t } = useI18n();
  return <Card><EmptyState icon="building" title={t("ph.noOrg")} /></Card>;
}

function List({ rows: all, base }: { rows: Summary[]; base: string }) {
  const { t, fmt } = useI18n();
  const [status, setStatus] = useState<"" | SafetyStatus>("");
  const [q, setQ] = useState("");
  const rows = all.filter((r) => (!status || r.status === status) && (!q.trim() || r.excerpt.toLowerCase().includes(q.trim().toLowerCase())));
  if (all.length === 0) return <Card><EmptyState icon="inbox" title={t("ph.empty")} /></Card>;
  return (
    <>
    <div className="ph-form" role="search">
      <Select label={t("ph.filter.status")} value={status} onValueChange={(v) => setStatus(v as "" | SafetyStatus)} options={[{ value: "", label: t("ph.filter.all") }, ...allStatuses.map((s) => ({ value: s, label: t(`ph.status.${s}`) }))]} />
      <TextField label={t("ph.filter.text")} value={q} onChange={(e) => setQ(e.target.value)} />
    </div>
    {rows.length === 0 ? <Card><EmptyState icon="inbox" title={t("ph.filter.none")} /></Card> :
    <ul className="ph-list" aria-label={t("ph.log.title")}>{rows.map((r) => (
      <li key={r.id}><Card>
        <div className="ph-row"><Badge tone="info">{t(`ph.status.${r.status}`)}</Badge><Badge tone="neutral">{base === "safety-log" ? t(`ph.kind.${r.label}`) : t(`ph.concernType.${r.label}`)}</Badge><span className="ms-muted">{fmt.date(new Date(r.reportedAt), "short")}</span></div>
        <p className="ph-text" dir="auto">{r.excerpt}</p>
        <Link className="ms-link" to={`../${base}/${r.id}`} relative="path">{t("ph.open")}</Link>
      </Card></li>))}
    </ul>}
    </>
  );
}

function Missing({ codes }: { codes: string[] }) {
  const { t } = useI18n();
  return codes.length === 0 ? <p className="ms-muted">{t("ph.miss.none")}</p> : <ul className="ph-bullets">{codes.map((c) => <li key={c}>{t(`ph.miss.${c.replace(/[.]/g, "_")}`)}</li>)}</ul>;
}

// ---------------------------------------------------------------- dispensing errors and near misses

function EventForm({ org, onSaved }: { org: string; onSaved(): void }) {
  const { t } = useI18n();
  const api = usePharmacyApi();
  const act = useAction();
  const [kind, setKind] = useState<SafetyEventKind>("DispensingError");
  const [reached, setReached] = useState<ReachedPatient>("Yes");
  const [what, setWhat] = useState("");
  const [on, setOn] = useState("");
  const [medicine, setMedicine] = useState("");
  const [how, setHow] = useState("");
  const [action, setAction] = useState("");
  const [picked, setPicked] = useState<DispensingErrorExample[]>([]);
  const [rid, setRid] = useState(() => crypto.randomUUID());
  const chooseKind = (k: SafetyEventKind) => { setKind(k); setReached(k === "NearMiss" ? "No" : "Yes"); };
  const submit = (e: FormEvent) => {
    e.preventDefault();
    void act.run(() => api.createEvent(org, { kind, reachedPatient: reached, errorExamples: picked, description: what.trim(), occurredOn: dayToDate(on), medicineAsReported: medicine.trim() || null, howDetectedOrIntercepted: how.trim() || null, immediateActionTaken: action.trim() || null, clientRequestId: rid }),
      () => { setWhat(""); setOn(""); setMedicine(""); setHow(""); setAction(""); setPicked([]); setRid(crypto.randomUUID()); onSaved(); });
  };
  return (
    <Card title={t("ph.form.title")}>
      <form className="ph-form" onSubmit={submit} noValidate>
        <Select label={t("ph.form.kind")} value={kind} onValueChange={(v) => chooseKind(v as SafetyEventKind)} options={(["DispensingError", "NearMiss"] as const).map((k) => ({ value: k, label: t(`ph.kind.${k}`) }))} />
        <Select label={t("ph.form.reached")} value={reached} onValueChange={(v) => setReached(v as ReachedPatient)} options={(["Yes", "No", "Unknown"] as const).map((k) => ({ value: k, label: t(`ph.reached.${k}`) }))} />
        <TextArea label={t("ph.form.what")} value={what} onChange={(e) => setWhat(e.target.value)} maxLength={2000} rows={4} required />
        <TextField label={t("ph.form.when")} type="date" value={on} onChange={(e) => setOn(e.target.value)} hint={t("ph.form.unknownHint")} />
        <TextField label={t("ph.form.medicine")} value={medicine} onChange={(e) => setMedicine(e.target.value)} maxLength={200} />
        <fieldset className="ph-checks">
          <legend>{t("ph.examples.legend")}</legend>
          <p className="ms-muted">{t("ph.examples.note")}</p>
          {examples.map((x) => (
            <label key={x}><input type="checkbox" checked={picked.includes(x)} onChange={(e) => setPicked(e.target.checked ? [...picked, x] : picked.filter((p) => p !== x))} /> <span>{t(`ph.example.${x}`)}</span></label>
          ))}
        </fieldset>
        <TextArea label={t("ph.form.how")} value={how} onChange={(e) => setHow(e.target.value)} maxLength={1000} rows={2} />
        <TextArea label={t("ph.form.action")} value={action} onChange={(e) => setAction(e.target.value)} maxLength={1000} rows={2} />
        <div><Button type="submit" loading={act.busy} disabled={!what.trim()}>{t("ph.form.submit")}</Button></div>
        <ActionError error={act.error} />
      </form>
    </Card>
  );
}

export function SafetyLogPage() {
  const { t } = useI18n();
  const org = useOrganizationId();
  const list = usePharmacyLoad((a, s) => org ? a.events(org, s) : Promise.resolve([] as Summary[]), [org]);
  return (
    <>
      <PageHeader title={t("ph.log.title")} subtitle={t("ph.log.sub")} />
      <div className="ph-page">
        <Unreviewed />
        <AlertCard tone="info" title={t("ph.notice.event")} />
        {!org ? <NoOrg /> : <>
          <Loaded load={list.state} reload={list.reload}>{(rows: Summary[]) => <List rows={rows} base="safety-log" />}</Loaded>
          <EventForm org={org} onSaved={list.reload} />
        </>}
      </div>
    </>
  );
}

export function SafetyEventPage() {
  const { t } = useI18n();
  const { rid = "" } = useParams();
  const load = usePharmacyLoad((a, s) => a.event(rid, s), [rid]);
  return (
    <>
      <PageHeader title={t("ph.title.eventDetail")} />
      <Loaded load={load.state} reload={load.reload}>{(d: EventDetail) => <EventBody d={d} reload={load.reload} />}</Loaded>
    </>
  );
}

function EventBody({ d, reload }: { d: EventDetail; reload(): void }) {
  const { t, fmt } = useI18n();
  const api = usePharmacyApi();
  const e: SafetyEvent = d.event;
  const none = t("ph.f.notRecorded");
  return (
    <div className="ph-page">
      <Unreviewed />
      <AlertCard tone="info" title={t("ph.notice.event")} />
      <div className="ph-row"><Badge tone="info">{t(`ph.status.${e.status}`)}</Badge><Badge tone={e.kind === "NearMiss" ? "success" : "warning"}>{t(`ph.kind.${e.kind}`)}</Badge></div>
      <Card title={t("ph.sec.original")} subtitle={`${t("ph.f.reportedBy", { role: t(`ph.role.${e.reporterRole}`) })} · ${fmt.date(new Date(e.reportedAt), "short")}`}>
        <dl className="ph-facts">
          <div><dt>{t("ph.form.what")}</dt><dd dir="auto">{e.description}</dd></div>
          <div><dt>{t("ph.f.reached")}</dt><dd>{t(`ph.reached.${e.reachedPatient}`)}</dd></div>
          <div><dt>{t("ph.f.occurred")}</dt><dd>{e.occurredOn ?? none}</dd></div>
          <div><dt>{t("ph.f.medicine")}</dt><dd dir="auto">{e.medicineAsReported ?? none}</dd></div>
          <div><dt>{t("ph.f.examples")}</dt><dd>{e.errorExamples.length ? e.errorExamples.map((x) => t(`ph.example.${x}`)).join("، ") : none}</dd></div>
          <div><dt>{t("ph.f.how")}</dt><dd dir="auto">{e.howDetectedOrIntercepted ?? none}</dd></div>
          <div><dt>{t("ph.f.action")}</dt><dd dir="auto">{e.immediateActionTaken ?? none}</dd></div>
          <div><dt>{t("ph.f.external")}</dt><dd>{t("ph.external.NotSubmitted")}</dd></div>
        </dl>
        <p className="ms-muted">{t("ph.examples.note")}</p>
      </Card>
      <Card title={t("ph.sec.missing")}><Missing codes={e.missingInformation} /></Card>
      <Review entries={d.entries} status={e.status} version={e.version} reload={reload}
        addEntry={(k, text) => api.eventEntry(e.id, k, text)} setStatus={(s, n) => api.eventStatus(e.id, s, n, e.version)} />
    </div>
  );
}

// ---------------------------------------------------------------- product concerns


/**
 * Pick the patient (only those the server lists as the caller's ACTIVE care relationships) and then one of THAT patient's batch records (the server answers 403 unless the
 * patient's consent covers products). Nothing is searched across patients and nothing is guessed; with no pick the report is saved as unmatched.
 */
function BatchPicker({ value, onChange }: { value: { subjectId: string; productRecordId: string } | null; onChange(v: { subjectId: string; productRecordId: string } | null): void }) {
  const { t, fmt } = useI18n();
  const care = useLoad((a, s) => a.care(s), []);
  const [patient, setPatient] = useState("");
  const products = useLoad((a, s) => patient ? a.products(patient, s) : Promise.resolve([] as ProductRecord[]), [patient]);
  const rows: CareRow[] = care.state.status === "ready" ? care.state.data.filter((r) => r.relationship.status === "Active") : [];
  const records: ProductRecord[] = products.state.status === "ready" ? products.state.data : [];
  const denied = patient !== "" && products.state.status === "error";
  return (
    <fieldset className="ph-form">
      <legend>{t("ph.pick.title")}</legend>
      <p className="ms-muted">{t("ph.pick.hint")}</p>
      {care.state.status === "error" && <p role="alert">{t("ph.pick.careFailed")}</p>}
      <Select label={t("ph.pick.patient")} value={patient} onValueChange={(v) => { setPatient(v); onChange(null); }}
        options={[{ value: "", label: t("ph.pick.none") }, ...rows.map((r) => ({ value: r.relationship.patientSubjectId, label: r.patientName }))]} />
      {patient && products.state.status === "loading" && <p className="ms-muted">{t("common.loading")}</p>}
      {denied && <p role="alert">{t("ph.pick.denied")}</p>}
      {patient && products.state.status === "ready" && records.length === 0 && <p className="ms-muted">{t("ph.pick.noRecords")}</p>}
      {records.length > 0 && (
        <Select label={t("ph.pick.record")} value={value?.productRecordId ?? ""} onValueChange={(v) => onChange(v ? { subjectId: patient, productRecordId: v } : null)}
          options={[{ value: "", label: t("ph.pick.unmatched") }, ...records.map((r) => ({ value: r.id, label: `${r.productName} · ${r.batchNumber} · ${fmt.date(new Date(r.expiryDate), "short")}` }))]} />
      )}
      {value && <p className="ms-muted">{t("ph.pick.chosen")}</p>}
    </fieldset>
  );
}

function ConcernForm({ org, onSaved }: { org: string; onSaved(): void }) {
  const { t } = useI18n();
  const api = usePharmacyApi();
  const act = useAction();
  const [type, setType] = useState<(typeof concernTypes)[number]>("AppearanceOrPackaging");
  const [what, setWhat] = useState("");
  const [seen, setSeen] = useState("");
  const [name, setName] = useState("");
  const [batch, setBatch] = useState("");
  const [expiry, setExpiry] = useState("");
  const [maker, setMaker] = useState("");
  const [link, setLink] = useState<{ subjectId: string; productRecordId: string } | null>(null);
  const [rid, setRid] = useState(() => crypto.randomUUID());
  const submit = (e: FormEvent) => {
    e.preventDefault();
    const typed: Identifiers | null = name.trim() || batch.trim() || expiry || maker.trim() ? { productName: name.trim() || null, batchNumber: batch.trim() || null, expiryDate: dayToDate(expiry), manufacturerName: maker.trim() || null, gtin: null } : null;
    void act.run(() => api.createConcern(org, { type, description: what.trim(), observedOn: dayToDate(seen), productRecord: link, asReported: typed, clientRequestId: rid }),
      () => { setWhat(""); setSeen(""); setName(""); setBatch(""); setExpiry(""); setMaker(""); setLink(null); setRid(crypto.randomUUID()); onSaved(); });
  };
  return (
    <Card title={t("ph.form.title")}>
      <form className="ph-form" onSubmit={submit} noValidate>
        <Select label={t("ph.form.type")} hint={t("ph.concernType.note")} value={type} onValueChange={(v) => setType(v as (typeof concernTypes)[number])} options={concernTypes.map((k) => ({ value: k, label: t(`ph.concernType.${k}`) }))} />
        <TextArea label={t("ph.form.concern")} value={what} onChange={(e) => setWhat(e.target.value)} maxLength={2000} rows={4} required />
        <TextField label={t("ph.form.observed")} type="date" value={seen} onChange={(e) => setSeen(e.target.value)} hint={t("ph.form.unknownHint")} />
        <p className="ms-muted">{t("ph.form.typedHint")}</p>
        <TextField label={t("ph.form.productName")} value={name} onChange={(e) => setName(e.target.value)} maxLength={200} />
        <TextField label={t("ph.form.batch")} value={batch} onChange={(e) => setBatch(e.target.value)} maxLength={64} />
        <TextField label={t("ph.form.expiry")} type="date" value={expiry} onChange={(e) => setExpiry(e.target.value)} />
        <TextField label={t("ph.form.manufacturer")} value={maker} onChange={(e) => setMaker(e.target.value)} maxLength={200} />
        <BatchPicker value={link} onChange={setLink} />
        <div><Button type="submit" loading={act.busy} disabled={!what.trim()}>{t("ph.form.submit")}</Button></div>
        <ActionError error={act.error} />
      </form>
    </Card>
  );
}

export function ProductConcernsPage() {
  const { t } = useI18n();
  const org = useOrganizationId();
  const list = usePharmacyLoad((a, s) => org ? a.concerns(org, s) : Promise.resolve([] as Summary[]), [org]);
  return (
    <>
      <PageHeader title={t("ph.concern.title")} subtitle={t("ph.concern.sub")} />
      <div className="ph-page">
        <Unreviewed />
        <AlertCard tone="info" title={t("ph.notice.concern")} />
        {!org ? <NoOrg /> : <>
          <Loaded load={list.state} reload={list.reload}>{(rows: Summary[]) => <List rows={rows} base="product-concerns" />}</Loaded>
          <ConcernForm org={org} onSaved={list.reload} />
        </>}
      </div>
    </>
  );
}

function IdentifierFacts({ i }: { i: Identifiers }) {
  const { t, fmt } = useI18n();
  const none = t("ph.f.notRecorded");
  return (
    <dl className="ph-facts">
      <div><dt>{t("ph.f.product")}</dt><dd dir="auto">{i.productName ?? none}</dd></div>
      <div><dt>{t("ph.f.batch")}</dt><dd dir="ltr">{i.batchNumber ?? none}</dd></div>
      <div><dt>{t("ph.f.expiry")}</dt><dd>{i.expiryDate ? fmt.date(new Date(i.expiryDate), "short") : none}</dd></div>
      <div><dt>{t("ph.f.manufacturer")}</dt><dd dir="auto">{i.manufacturerName ?? none}</dd></div>
    </dl>
  );
}

export function ProductConcernPage() {
  const { t } = useI18n();
  const { rid = "" } = useParams();
  const load = usePharmacyLoad((a, s) => a.concern(rid, s), [rid]);
  return (
    <>
      <PageHeader title={t("ph.title.concernDetail")} />
      <Loaded load={load.state} reload={load.reload}>{(d: ConcernDetail) => <ConcernBody d={d} reload={load.reload} />}</Loaded>
    </>
  );
}

function ConcernBody({ d, reload }: { d: ConcernDetail; reload(): void }) {
  const { t, fmt } = useI18n();
  const api = usePharmacyApi();
  const act = useAction();
  const c: Concern = d.concern;
  const [note, setNote] = useState("");
  const canPropose = !c.manufacturerReportProposed && (c.status === "InReview" || c.status === "FollowUpRequired");
  return (
    <div className="ph-page">
      <Unreviewed />
      <AlertCard tone="info" title={t("ph.notice.concern")} />
      <div className="ph-row"><Badge tone="info">{t(`ph.status.${c.status}`)}</Badge><Badge tone="neutral">{t(`ph.concernType.${c.type}`)}</Badge><Badge tone={c.linkState === "Unmatched" ? "warning" : "neutral"}>{t(`ph.link.${c.linkState}`)}</Badge></div>
      <p className="ms-muted">{t("ph.concernType.note")}</p>
      <Card title={t("ph.sec.original")} subtitle={`${t("ph.f.reportedBy", { role: t(`ph.role.${c.reporterRole}`) })} · ${fmt.date(new Date(c.reportedAt), "short")}`}>
        <dl className="ph-facts">
          <div><dt>{t("ph.form.concern")}</dt><dd dir="auto">{c.description}</dd></div>
          <div><dt>{t("ph.form.observed")}</dt><dd>{c.observedOn ?? t("ph.f.notRecorded")}</dd></div>
          <div><dt>{t("ph.f.external")}</dt><dd>{t("ph.external.NotSubmitted")}</dd></div>
        </dl>
      </Card>
      {c.linked && <Card title={t("ph.sec.linked")} subtitle={c.linkedVerificationAtLinking ? t("ph.f.verification", { v: c.linkedVerificationAtLinking }) : undefined}><IdentifierFacts i={c.linked} /></Card>}
      {c.asReported && <Card title={t("ph.sec.typed")}><IdentifierFacts i={c.asReported} /></Card>}
      <Card title={t("ph.sec.flags")}>
        {c.flags.length === 0 ? <p className="ms-muted">{t("ph.miss.none")}</p> : <ul className="ph-bullets">{c.flags.map((f) => <li key={f}>{t(`ph.flag.${f.replace(/[.]/g, "_")}`)}</li>)}</ul>}
      </Card>
      <Review entries={d.entries} status={c.status} version={c.version} reload={reload}
        addEntry={(k, text) => api.concernEntry(c.id, k, text)} setStatus={(s, n) => api.concernStatus(c.id, s, n, c.version)} />
      <Card title={t("ph.proposal.title")}>
        <p className="ms-muted">{t("ph.proposal.body")}</p>
        {c.manufacturerReportProposed ? <p>{t("ph.proposal.done")}</p> : (
          <div className="ph-form">
            <TextArea label={t("ph.proposal.note")} value={note} onChange={(e) => setNote(e.target.value)} maxLength={2000} rows={3} disabled={!canPropose} />
            <div><Button variant="secondary" disabled={!canPropose || !note.trim()} loading={act.busy} onClick={() => void act.run(() => api.propose(c.id, note.trim(), c.version), () => { setNote(""); reload(); })}>{t("ph.proposal.button")}</Button></div>
            <ActionError error={act.error} />
          </div>
        )}
      </Card>
    </div>
  );
}

// ---------------------------------------------------------------- shared review block

function Review({ entries, status, reload, addEntry, setStatus }: { entries: Entry[]; status: SafetyStatus; version: number; reload(): void; addEntry(kind: "ReviewNote" | "FollowUp", text: string): Promise<unknown>; setStatus(status: SafetyStatus, note: string | null): Promise<unknown> }) {
  const { t, fmt } = useI18n();
  const act = useAction();
  const [kind, setKind] = useState<"ReviewNote" | "FollowUp">("ReviewNote");
  const [text, setText] = useState("");
  const [next, setNext] = useState<SafetyStatus>(status);
  const [note, setNote] = useState("");
  const roleLabel = (r: PharmacyRole) => t(`ph.role.${r}`);
  return (
    <>
      <Card title={t("ph.sec.review")}>
        <div className="ph-form">
          <Select label={t("ph.act.status")} value={next} onValueChange={(v) => setNext(v as SafetyStatus)} options={allStatuses.map((s) => ({ value: s, label: t(`ph.status.${s}`) }))} />
          <TextField label={t("ph.act.statusNote")} value={note} onChange={(e) => setNote(e.target.value)} maxLength={2000} />
          <div><Button variant="secondary" disabled={next === status} loading={act.busy} onClick={() => void act.run(() => setStatus(next, note.trim() || null), () => { setNote(""); reload(); })}>{t("ph.act.save")}</Button></div>
          <Select label={t("ph.act.kind")} value={kind} onValueChange={(v) => setKind(v as "ReviewNote" | "FollowUp")} options={[{ value: "ReviewNote", label: t("ph.act.note") }, { value: "FollowUp", label: t("ph.act.followUp") }]} />
          <TextArea label={t("ph.act.text")} value={text} onChange={(e) => setText(e.target.value)} maxLength={2000} rows={3} />
          <div><Button variant="secondary" disabled={!text.trim()} loading={act.busy} onClick={() => void act.run(() => addEntry(kind, text.trim()), () => { setText(""); reload(); })}>{t("ph.act.save")}</Button></div>
          <ActionError error={act.error} />
        </div>
      </Card>
      <Card title={t("ph.sec.history")}>
        {entries.length === 0 ? <p className="ms-muted">{t("ph.empty")}</p> : (
          <ul className="ph-timeline">{entries.map((x) => (
            <li key={x.id}>
              <div className="ph-row"><strong>{t(`ph.entry.${x.kind}`)}</strong><Badge tone="neutral">{roleLabel(x.authorRole)}</Badge><span className="ms-muted">{fmt.date(new Date(x.at), "short")}</span></div>
              {x.toStatus && <p className="ms-muted">{x.fromStatus ? t(`ph.status.${x.fromStatus}`) : "—"} → {t(`ph.status.${x.toStatus}`)}</p>}
              {x.text && <p className="ph-text" dir="auto">{x.text}</p>}
            </li>))}
          </ul>)}
      </Card>
    </>
  );
}
