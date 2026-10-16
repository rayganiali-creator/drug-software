import { useState, type FormEvent } from "react";
import { Link, useParams } from "react-router-dom";
import { AlertCard, Badge, Button, Card, EmptyState, Select, TextArea, TextField } from "../../components/ui";
import { useAuth } from "../../auth/AuthContext";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAction, useLoad, useSelfId } from "../../records/hooks";
import type { PatientMedication } from "../../records/types";
import { categories, reviewKinds, statuses, type Assistance, type CreateInput, type Definitions, type Detail, type Entry, type EventCategory, type ReviewStatus, type Summary, type SuspectedMedicationInput } from "../../adr/types";
import { ActionError, Loaded } from "../records/shared";
import { useAdrApi, useAdrLoad } from "./hooks";
import "./adr.css";

/** Local calendar day (yyyy-mm-dd) to an instant: the start of that day. A date is all that is asked for; the time is never invented beyond that. */
const dayToInstant = (d: string) => (d ? new Date(`${d}T00:00:00`).toISOString() : null);

function Unreviewed() {
  const { t, locale } = useI18n();
  return locale === "fa" ? <AlertCard tone="info" title={t("sf.fa.unreviewed.title")}>{t("sf.fa.unreviewed.body")}</AlertCard> : null;
}

function Notices() {
  const { t } = useI18n();
  return (
    <div className="adr-page" role="note">
      <Unreviewed />
      <AlertCard tone="info" title={t("adr.signal")}>{t("adr.internal")}</AlertCard>
    </div>
  );
}

function StatusBadges({ s }: { s: Pick<Summary, "status" | "category" | "origin" | "reporterRole"> }) {
  const { t } = useI18n();
  return (
    <div className="adr-row">
      <Badge tone="info">{t(`adr.status.${s.status}`)}</Badge>
      {s.category && <Badge tone={s.category === "Unclassified" ? "neutral" : "warning"}>{t(`adr.cat.${s.category}`)}</Badge>}
      <Badge tone="neutral">{t(`adr.origin.${s.origin}`)}</Badge>
    </div>
  );
}

function RecordList({ rows, empty }: { rows: Summary[]; empty?: boolean }) {
  const { t, fmt } = useI18n();
  if (rows.length === 0) return <Card><EmptyState icon="inbox" title={t("adr.empty")} body={empty ? undefined : t("adr.emptyBody")} /></Card>;
  return (
    <ul className="adr-list" aria-label={t("adr.title.pro")}>{rows.map((r) => (
      <li key={r.id}><Card>
        <StatusBadges s={r} />
        <p className="adr-text" dir="auto">{r.excerpt}</p>
        <p className="ms-muted">{t("adr.reportedBy", { role: t(`adr.role.${r.reporterRole}`) })} · {fmt.date(new Date(r.reportedAt), "short")}</p>
        <Link className="ms-link" to={`../adverse-events/${r.id}`} relative="path">{t("adr.open")}</Link>
      </Card></li>))}
    </ul>
  );
}


/** One list of medicines: either one of the patient's own records or a name as given. Never matched or guessed. */
function MedChooser({ legend, hint, own, items, setItems }: { legend: string; hint: string; own: PatientMedication[]; items: SuspectedMedicationInput[]; setItems(next: SuspectedMedicationInput[]): void }) {
  const { t } = useI18n();
  const [pick, setPick] = useState("");
  const [name, setName] = useState("");
  const label = (m: SuspectedMedicationInput) => m.patientMedicationId ? own.find((x) => x.id === m.patientMedicationId)?.displayName ?? "…" : m.nameAsReported ?? "";
  const add = () => {
    if (pick) setItems([...items, { patientMedicationId: pick, nameAsReported: null }]);
    else if (name.trim()) setItems([...items, { patientMedicationId: null, nameAsReported: name.trim() }]);
    setPick(""); setName("");
  };
  return (
    <fieldset className="adr-form">
      <legend>{legend}</legend>
      <p className="ms-muted">{hint}</p>
      {items.length > 0 && <ul className="adr-meds">{items.map((m, i) => <li key={i} className="adr-row"><span dir="auto">{label(m)}</span><Button size="sm" variant="secondary" type="button" onClick={() => setItems(items.filter((_, j) => j !== i))}>{t("adr.form.remove")}</Button></li>)}</ul>}
      {own.length > 0 && <Select label={t("adr.form.medPick")} value={pick} onValueChange={setPick} options={[{ value: "", label: "—" }, ...own.map((m) => ({ value: m.id, label: m.displayName }))]} />}
      <TextField label={t("adr.form.medName")} value={name} onChange={(e) => setName(e.target.value)} maxLength={200} />
      <div><Button type="button" variant="secondary" onClick={add} disabled={!pick && !name.trim()}>{t("adr.form.add")}</Button></div>
    </fieldset>
  );
}

/** The entry form. A date left empty stays unknown; a medicine is either one of the patient's own records or a name as given, and is never matched or guessed. */
function CreateForm({ subjectId, pro, onSaved }: { subjectId: string; pro: boolean; onSaved(): void }) {
  const { t } = useI18n();
  const api = useAdrApi();
  const act = useAction();
  const meds = useLoad((a, s) => a.medications(subjectId, false, s), [subjectId]);
  const [what, setWhat] = useState("");
  const [onset, setOnset] = useState("");
  const [ended, setEnded] = useState("");
  const [outcome, setOutcome] = useState("");
  const [details, setDetails] = useState("");
  const [chosen, setChosen] = useState<SuspectedMedicationInput[]>([]);
  const [dose, setDose] = useState("");
  const [other, setOther] = useState<SuspectedMedicationInput[]>([]);
  const [rid, setRid] = useState(() => crypto.randomUUID());
  const own: PatientMedication[] = meds.state.status === "ready" ? meds.state.data : [];
  const submit = (e: FormEvent) => {
    e.preventDefault();
    const body: CreateInput = { eventDescription: what.trim(), onsetAt: dayToInstant(onset), resolvedAt: dayToInstant(ended), suspectedMedications: chosen, outcomeAsStated: outcome.trim() || null, additionalDetails: details.trim() || null, clientRequestId: rid, doseAndAdministrationAsReported: dose.trim() || null, concomitantMedications: other };
    void act.run(() => api.create(subjectId, body), () => { setWhat(""); setOnset(""); setEnded(""); setOutcome(""); setDetails(""); setChosen([]); setOther([]); setDose(""); setRid(crypto.randomUUID()); onSaved(); });
  };
  return (
    <Card title={t("adr.form.formTitle")}>
      <form className="adr-form" onSubmit={submit} noValidate>
        <TextArea label={t("adr.form.what")} value={what} onChange={(e) => setWhat(e.target.value)} maxLength={2000} rows={4} required />
        <TextField label={t("adr.form.when")} type="date" value={onset} onChange={(e) => setOnset(e.target.value)} hint={t("adr.form.unknownHint")} />
        <TextField label={t("adr.form.ended")} type="date" value={ended} onChange={(e) => setEnded(e.target.value)} />
        <TextField label={t("adr.form.outcome")} value={outcome} onChange={(e) => setOutcome(e.target.value)} maxLength={500} />
        <TextArea label={t("adr.form.details")} value={details} onChange={(e) => setDetails(e.target.value)} maxLength={2000} rows={3} />
        <MedChooser legend={t("adr.form.meds")} hint={t("adr.form.medsHint")} own={own} items={chosen} setItems={setChosen} />
        <TextField label={t("adr.form.dose")} value={dose} onChange={(e) => setDose(e.target.value)} maxLength={500} />
        <MedChooser legend={t("adr.form.concomitant")} hint={t("adr.form.concomitantHint")} own={own} items={other} setItems={setOther} />
        <div><Button type="submit" loading={act.busy} disabled={!what.trim()}>{pro ? t("adr.form.submitPro") : t("adr.form.submit")}</Button></div>
        <ActionError error={act.error} />
      </form>
    </Card>
  );
}

// ---------------------------------------------------------------- pages

/** Patient: their own reports and a form to add one. */
export function PatientAdverseEvents() {
  const { t } = useI18n();
  const self = useSelfId();
  const list = useAdrLoad((a, s) => a.listFor(self, s), [self]);
  return (
    <>
      <PageHeader title={t("adr.title.patient")} subtitle={t("adr.sub.patient")} />
      <div className="adr-page">
        <Notices />
        <AlertCard tone="warning" title={t("adr.emergency")} />
        <Loaded load={list.state} reload={list.reload}>{(rows: Summary[]) => <RecordList rows={rows} />}</Loaded>
        <p className="ms-muted">{t("adr.patientNote")}</p>
        <CreateForm subjectId={self} pro={false} onSaved={list.reload} />
      </div>
    </>
  );
}

/** Physician / pharmacist: the records they may read now (relationship and consent are checked by the server for each patient). */
export function ProAdverseEventQueue() {
  const { t } = useI18n();
  const [all, setAll] = useState(false);
  const list = useAdrLoad((a, s) => a.queue(all ? statuses : ["Submitted", "InReview", "InformationRequested"], s), [all]);
  return (
    <>
      <PageHeader title={t("adr.title.queue")} subtitle={t("adr.sub.queue")} />
      <div className="adr-page">
        <Notices />
        <Select label={t("adr.filter")} value={all ? "all" : "open"} onValueChange={(v) => setAll(v === "all")} options={[{ value: "open", label: t("adr.filter.open") }, { value: "all", label: t("adr.filter.all") }]} />
        <Loaded load={list.state} reload={list.reload}>{(rows: Summary[]) => <RecordList rows={rows} />}</Loaded>
      </div>
    </>
  );
}

/** Physician / pharmacist: the records of one patient, and entering a new one. */
export function ProPatientAdverseEvents() {
  const { t } = useI18n();
  const { id = "" } = useParams();
  const list = useAdrLoad((a, s) => a.listFor(id, s), [id]);
  return (
    <>
      <PageHeader title={t("adr.title.pro")} subtitle={t("adr.sub.queue")} />
      <div className="adr-page">
        <Notices />
        <Loaded load={list.state} reload={list.reload}>{(rows: Summary[]) => <RecordList rows={rows} />}</Loaded>
        <CreateForm subjectId={id} pro onSaved={list.reload} />
      </div>
    </>
  );
}

function when(fmt: ReturnType<typeof useI18n>["fmt"], iso: string | null) { return iso ? fmt.date(new Date(iso), "short") : null; }

/** One record. The original is shown as written and never edited; what was added later, who added it and what is still unknown are shown beside it. */
export function AdverseEventPage() {
  const { t } = useI18n();
  const { rid = "" } = useParams();
  const { hasRole } = useAuth();
  const pro = hasRole("Physician") || hasRole("Pharmacist");
  const load = useAdrLoad((a, s) => a.get(rid, s), [rid]);
  return (
    <>
      <PageHeader title={t("adr.title.detail")} />
      <Loaded load={load.state} reload={load.reload}>{(d: Detail) => <Body d={d} pro={pro} reload={load.reload} />}</Loaded>
    </>
  );
}

function Body({ d, pro, reload }: { d: Detail; pro: boolean; reload(): void }) {
  const { t, fmt } = useI18n();
  const e = d.event;
  const none = t("adr.f.notRecorded");
  return (
    <div className="adr-page">
      <Notices />
      <StatusBadges s={e} />
      <Card title={t("adr.sec.original")} subtitle={`${t("adr.reportedBy", { role: t(`adr.role.${e.reporterRole}`) })} · ${fmt.date(new Date(e.reportedAt), "short")}`}>
        <dl className="adr-facts">
          <div><dt>{t("adr.f.description")}</dt><dd dir="auto">{e.original.eventDescription}</dd></div>
          <div><dt>{t("adr.f.onset")}</dt><dd>{when(fmt, e.original.onsetAt) ?? none}</dd></div>
          <div><dt>{t("adr.f.ended")}</dt><dd>{when(fmt, e.original.resolvedAt) ?? none}</dd></div>
          <div><dt>{t("adr.f.outcome")}</dt><dd dir="auto">{e.original.outcomeAsStated ?? none}</dd></div>
          <div><dt>{t("adr.f.dose")}</dt><dd dir="auto">{e.original.doseAndAdministrationAsReported ?? none}</dd></div>
          <div><dt>{t("adr.f.details")}</dt><dd dir="auto">{e.original.additionalDetails ?? none}</dd></div>
          <div><dt>{t("adr.f.external")}</dt><dd>{t("adr.external.NotSubmitted")}</dd></div>
        </dl>
      </Card>
      <Card title={t("adr.sec.known")}>
        <dl className="adr-facts">
          <div><dt>{t("adr.f.onset")}</dt><dd>{when(fmt, e.effective.onsetAt.value) ?? none}<span className="ms-muted"> ({t(`adr.prov.${e.effective.onsetAt.provenance}`)})</span></dd></div>
          <div><dt>{t("adr.f.ended")}</dt><dd>{when(fmt, e.effective.resolvedAt.value) ?? none}<span className="ms-muted"> ({t(`adr.prov.${e.effective.resolvedAt.provenance}`)})</span></dd></div>
          <div><dt>{t("adr.f.outcome")}</dt><dd dir="auto">{e.effective.outcomeAsStated.value ?? none}<span className="ms-muted"> ({t(`adr.prov.${e.effective.outcomeAsStated.provenance}`)})</span></dd></div>
        </dl>
        <h4>{t("adr.sec.meds")}</h4>
        {e.suspectedMedications.length === 0 ? <p className="ms-muted">{t("adr.med.none")}</p> : (
          <ul className="adr-meds">{e.suspectedMedications.map((m, i) => <li key={i}><strong dir="auto">{m.displayName ?? m.nameAsReported}</strong><div className="ms-muted">{t(`adr.med.${m.state}`)}</div></li>)}</ul>)}
      </Card>
      <Card title={t("adr.sec.concomitant")}>
        {e.concomitantMedications.length === 0 ? <p className="ms-muted">{t("adr.concomitant.none")}</p> : (
          <ul className="adr-meds">{e.concomitantMedications.map((m, i) => <li key={i}><strong dir="auto">{m.displayName ?? m.nameAsReported}</strong><div className="ms-muted">{t(`adr.med.${m.state}`)}</div></li>)}</ul>)}
      </Card>
      <Card title={t("adr.sec.missing")}>
        {e.missingInformation.length === 0 ? <p className="ms-muted">{t("adr.miss.none")}</p> : <ul className="adr-bullets">{e.missingInformation.map((c) => <li key={c}>{t(`adr.miss.${c.replace(/[.]/g, "_")}`)}</li>)}</ul>}
      </Card>
      {pro && <ProPanel d={d} reload={reload} />}
      <AddInformation d={d} reload={reload} />
      <Card title={t("adr.sec.history")}>
        {d.entries.length === 0 ? <p className="ms-muted">{t("adr.emptyBody")}</p> : <ul className="adr-timeline">{d.entries.map((x) => <EntryItem key={x.id} x={x} />)}</ul>}
      </Card>
    </div>
  );
}

function EntryItem({ x }: { x: Entry }) {
  const { t, fmt } = useI18n();
  const change = x.toStatus ? `${t("adr.kind.from", { from: x.fromStatus ? t(`adr.status.${x.fromStatus}`) : "—" })} ${t("adr.kind.to", { to: t(`adr.status.${x.toStatus}`) })}` : x.toCategory ? `${t("adr.kind.from", { from: x.fromCategory ? t(`adr.cat.${x.fromCategory}`) : "—" })} ${t("adr.kind.to", { to: t(`adr.cat.${x.toCategory}`) })}` : null;
  const sup = [x.supplementOnsetAt && `${t("adr.f.onset")}: ${fmt.date(new Date(x.supplementOnsetAt), "short")}`, x.supplementResolvedAt && `${t("adr.f.ended")}: ${fmt.date(new Date(x.supplementResolvedAt), "short")}`, x.supplementOutcomeAsStated && `${t("adr.f.outcome")}: ${x.supplementOutcomeAsStated}`].filter(Boolean);
  return (
    <li>
      <div className="adr-row"><strong>{t(`adr.kind.${x.kind}`)}</strong><Badge tone="neutral">{t(`adr.prov.${x.provenance}`)}</Badge><Badge tone="neutral">{t(`adr.role.${x.authorRole}`)}</Badge><span className="ms-muted">{fmt.date(new Date(x.at), "short")}</span></div>
      {change && <p className="ms-muted">{change}</p>}
      {sup.length > 0 && <p className="adr-text" dir="auto">{sup.join(" · ")}</p>}
      {x.text && <p className="adr-text" dir="auto">{x.text}</p>}
      <p className="ms-muted">{t(`adr.vis.${x.visibility}`)}</p>
    </li>
  );
}

/** The reporter (or a professional) adds information after the fact. The original is never touched. */
function AddInformation({ d, reload }: { d: Detail; reload(): void }) {
  const { t } = useI18n();
  const api = useAdrApi();
  const act = useAction();
  const e = d.event;
  const [text, setText] = useState("");
  const [onset, setOnset] = useState("");
  const [ended, setEnded] = useState("");
  const [outcome, setOutcome] = useState("");
  const canFill = !e.effective.onsetAt.value || !e.effective.resolvedAt.value || !e.effective.outcomeAsStated.value;
  return (
    <Card title={t("adr.sec.follow")}>
      <div className="adr-form">
        <TextArea label={t("adr.act.followUp")} placeholder={t("adr.act.followUpPh")} value={text} onChange={(ev) => setText(ev.target.value)} maxLength={2000} rows={3} />
        <div><Button disabled={!text.trim()} loading={act.busy} onClick={() => void act.run(() => api.followUp(e.id, text.trim()), () => { setText(""); reload(); })}>{t("adr.act.save")}</Button></div>
        {canFill && (
          <fieldset className="adr-form">
            <legend>{t("adr.act.supplement")}</legend>
            <p className="ms-muted">{t("adr.act.supplementHint")}</p>
            {!e.effective.onsetAt.value && <TextField label={t("adr.f.onset")} type="date" value={onset} onChange={(ev) => setOnset(ev.target.value)} />}
            {!e.effective.resolvedAt.value && <TextField label={t("adr.f.ended")} type="date" value={ended} onChange={(ev) => setEnded(ev.target.value)} />}
            {!e.effective.outcomeAsStated.value && <TextField label={t("adr.f.outcome")} value={outcome} onChange={(ev) => setOutcome(ev.target.value)} maxLength={500} />}
            <div><Button variant="secondary" disabled={!onset && !ended && !outcome.trim()} loading={act.busy} onClick={() => void act.run(() => api.supplement(e.id, { onsetAt: dayToInstant(onset), resolvedAt: dayToInstant(ended), outcomeAsStated: outcome.trim() || null, note: null }), () => { setOnset(""); setEnded(""); setOutcome(""); reload(); })}>{t("adr.act.save")}</Button></div>
          </fieldset>
        )}
        <ActionError error={act.error} />
      </div>
    </Card>
  );
}

/** Professional only: classification, status, review entries, the template summary and the definitions behind the categories. */
function ProPanel({ d, reload }: { d: Detail; reload(): void }) {
  const { t, fmt } = useI18n();
  const api = useAdrApi();
  const act = useAction();
  const e = d.event;
  const defs = useAdrLoad((a, s) => a.definitions(s), []);
  const [cat, setCat] = useState<EventCategory>(e.category ?? "Unclassified");
  const [reason, setReason] = useState("");
  const [status, setStatus] = useState<ReviewStatus>(e.status);
  const [note, setNote] = useState("");
  const [kind, setKind] = useState<(typeof reviewKinds)[number]>("ReviewNote");
  const [text, setText] = useState("");
  const [assist, setAssist] = useState<Assistance | null>(null);
  const done = () => { setReason(""); setNote(""); setText(""); reload(); };
  return (
    <>
      <Card title={t("adr.sec.classify")} subtitle={e.category ? t(`adr.cat.${e.category}`) : undefined}>
        <div className="adr-form">
          <p className="ms-muted">{t("adr.act.classifyHint")}</p>
          <Select label={t("adr.act.classify")} value={cat} onValueChange={(v) => setCat(v as EventCategory)} options={categories.map((c) => ({ value: c, label: t(`adr.cat.${c}`) }))} />
          <TextArea label={t("adr.act.reason")} value={reason} onChange={(ev) => setReason(ev.target.value)} maxLength={2000} rows={2} />
          <div><Button disabled={!reason.trim() || cat === e.category} loading={act.busy} onClick={() => void act.run(() => api.classify(e.id, cat, reason.trim(), e.version), done)}>{t("adr.act.save")}</Button></div>
          <details>
            <summary>{t("adr.sec.defs")}</summary>
            <p className="ms-muted">{t("adr.defs.note")}</p>
            <Loaded load={defs.state} reload={defs.reload}>{(df: Definitions) => (
              <ul className="adr-bullets">{df.definitions.map((x) => <li key={x.category}><strong>{t(`adr.cat.${x.category}`)}</strong>: <span lang="en" dir="ltr">{x.definition}</span><div className="ms-muted" lang="en" dir="ltr">{t("adr.defs.source", { doc: x.source.document, section: x.source.section, page: x.source.page })}</div></li>)}</ul>)}
            </Loaded>
          </details>
        </div>
      </Card>
      <Card title={t("adr.sec.review")}>
        <div className="adr-form">
          <Select label={t("adr.act.status")} value={status} onValueChange={(v) => setStatus(v as ReviewStatus)} options={statuses.map((s) => ({ value: s, label: t(`adr.status.${s}`) }))} />
          <TextField label={t("adr.act.statusNote")} value={note} onChange={(ev) => setNote(ev.target.value)} maxLength={2000} />
          <div><Button variant="secondary" disabled={status === e.status} loading={act.busy} onClick={() => void act.run(() => api.setStatus(e.id, status, note.trim() || null, e.version), done)}>{t("adr.act.save")}</Button></div>
          <Select label={t("adr.act.kind")} value={kind} onValueChange={(v) => setKind(v as (typeof reviewKinds)[number])} options={reviewKinds.map((k) => ({ value: k, label: t(`adr.kindShort.${k}`) }))} />
          {kind === "ProfessionalAssessment" && <p className="ms-muted">{t("adr.act.assessmentHint")}</p>}
          <TextArea label={t("adr.act.text")} value={text} onChange={(ev) => setText(ev.target.value)} maxLength={2000} rows={3} />
          <div><Button variant="secondary" disabled={!text.trim()} loading={act.busy} onClick={() => void act.run(() => api.addEntry(e.id, kind, text.trim()), done)}>{t("adr.act.save")}</Button></div>
          <ActionError error={act.error} />
        </div>
      </Card>
      <Card title={t("adr.sec.assist")}>
        <div className="adr-form">
          <Button variant="secondary" loading={act.busy} onClick={() => assist ? setAssist(null) : void act.run(async () => setAssist(await api.assist(e.id)))}>{assist ? t("adr.act.hideAssist") : t("adr.act.assist")}</Button>
          {assist && (
            <div role="region" aria-label={t("adr.assist.label")}>
              <AlertCard tone="info" title={t("adr.assist.label")}>{t("adr.assist.notice")}</AlertCard>
              <p className="ms-muted">{t("adr.assist.origin")} · {fmt.date(new Date(assist.generatedAt), "short")}</p>
              <ul className="adr-bullets">{assist.summary.map((l, i) => <li key={i} dir="auto">{l.text}</li>)}</ul>
              <h4>{t("adr.assist.uncertainty")}</h4>
              <ul className="adr-bullets">{assist.uncertainty.map((u, i) => <li key={i} dir="auto">{u}</li>)}</ul>
            </div>
          )}
        </div>
      </Card>
    </>
  );
}
