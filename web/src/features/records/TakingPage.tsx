import { useEffect, useState } from "react";
import { Badge, Button, Card, EmptyState, Select, TextField } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { useMedicationApi } from "../../knowledge/useMedicationApi";
import type { MedicationSummary } from "../../knowledge/types";
import { PageHeader } from "../../layouts/PageHeader";
import { useAction, useLoad, useRecordsApi, useSelfId } from "../../records/hooks";
import type { DoseSlot, PatientMedication, ScheduleEntry } from "../../records/types";
import { ActionError, DemoFlag, Form, Loaded, dayKey, toNumber } from "./shared";

const UNITS = ["mg", "mcg", "g", "ml", "iu", "unit", "tablet", "capsule", "puff", "drop", "patch", "sachet", "ampoule"];

/** Medicines the patient takes, today's planned doses and the schedule. */
export function PatientTaking() {
  const { t } = useI18n();
  const id = useSelfId();
  const day = dayKey();
  const meds = useLoad((a, s) => a.medications(id, false, s), [id]);
  const doses = useLoad((a, s) => a.doses(id, day, s), [id, day]);
  const refresh = () => { meds.reload(); doses.reload(); };
  return (
    <>
      <PageHeader title={t("rec.taking.title")} subtitle={t("rec.taking.sub")} />
      <div className="rec-grid rec-grid--wide">
        <div className="rec-stack">
          <Card title={t("rec.doses.title")} subtitle={t("rec.doses.sub")}>
            <Loaded load={doses.state} reload={doses.reload}>{(slots: DoseSlot[]) => slots.length === 0 ? <EmptyState icon="calendar" title={t("rec.doses.empty")} body={t("rec.doses.emptyBody")} /> : (
              <ul className="rec-list" aria-label={t("rec.doses.title")}>{slots.map((s) => <DoseRow key={`${s.scheduleEntryId}-${s.scheduledFor}`} slot={s} onLogged={doses.reload} />)}</ul>)}
            </Loaded>
          </Card>
          <Card title={t("rec.meds.title")}>
            <Loaded load={meds.state} reload={meds.reload}>{(rows: PatientMedication[]) => rows.length === 0 ? <EmptyState icon="pill" title={t("rec.meds.empty")} body={t("rec.meds.emptyBody")} /> : (
              <ul className="rec-list" aria-label={t("rec.meds.title")}>{rows.map((m) => <MedicineRow key={m.id} med={m} onChanged={refresh} />)}</ul>)}
            </Loaded>
          </Card>
        </div>
        <AddMedicine onAdded={refresh} />
      </div>
    </>
  );
}

function DoseRow({ slot, onLogged }: { slot: DoseSlot; onLogged(): void }) {
  const { t, fmt } = useI18n();
  const api = useRecordsApi();
  const id = useSelfId();
  const act = useAction();
  const log = (status: "Taken" | "Skipped") => act.run(() => api.logIntake(id, { patientMedicationId: slot.patientMedicationId, scheduleEntryId: slot.scheduleEntryId, scheduledFor: slot.scheduledFor, status, takenAt: status === "Taken" ? new Date().toISOString() : null, note: null }), onLogged);
  return (
    <li className="rec-dose">
      <div className="rec-row__main"><span className="rec-row__title">{slot.medicationName}</span><span className="rec-row__meta rec-times">{fmt.time(slot.localTime.slice(0, 5))}</span></div>
      <div className="rec-row__actions">
        {slot.status && <Badge tone={slot.status === "Taken" ? "success" : "neutral"} icon={slot.status === "Taken" ? "check" : "minus"}>{t(`rec.intake.${slot.status}`)}</Badge>}
        <Button size="sm" variant={slot.status === "Taken" ? "tonal" : "primary"} loading={act.busy} onClick={() => log("Taken")}>{t("rec.intake.markTaken")}</Button>
        <Button size="sm" variant="secondary" onClick={() => log("Skipped")}>{t("rec.intake.markSkipped")}</Button>
      </div>
      <ActionError error={act.error} />
    </li>
  );
}

function MedicineRow({ med, onChanged }: { med: PatientMedication; onChanged(): void }) {
  const { t, fmt, locale } = useI18n();
  const api = useRecordsApi();
  const id = useSelfId();
  const act = useAction();
  const [open, setOpen] = useState(false);
  const name = med.referenceName ? (med.referenceName[locale] ?? med.referenceName.en ?? med.displayName) : med.displayName;
  const dose = med.doseAmount ? `${med.doseAmount} ${med.doseUnit ?? ""}` : med.doseText ?? "";
  return (
    <li className="rec-row" style={{ flexDirection: "column", alignItems: "stretch" }}>
      <div className="rec-row" style={{ border: 0, padding: 0 }}>
        <div className="rec-row__main">
          <span className="rec-row__title">{name}</span>
          <span className="rec-row__meta">{dose && <bdi dir="ltr">{dose}</bdi>}{dose ? " · " : ""}{t(`rec.freq.${med.frequency}`, { n: med.frequencyValue ?? 0 })} · {t("rec.meds.since", { date: fmt.date(new Date(med.startDate), "short") })}</span>
          <span className="rec-badges">
            {med.isRegistered ? <Badge tone="info" icon="check">{t("rec.meds.registered")}</Badge> : <Badge tone="warning" icon="alertTriangle">{t("rec.meds.unregistered")}</Badge>}
            <DemoFlag show={med.referenceIsDemo} />
            {med.status !== "Active" && <Badge tone="neutral">{t(`rec.meds.status.${med.status}`)}</Badge>}
          </span>
          {!med.isRegistered && <span className="rec-row__meta">{t("rec.meds.unregisteredNote")}</span>}
        </div>
        <div className="rec-row__actions">
          <Button size="sm" variant="secondary" iconStart="clock" onClick={() => setOpen(!open)} aria-expanded={open}>{t("rec.sched.title")}</Button>
          <Button size="sm" variant="ghost" onClick={() => act.run(() => api.stopMedication(id, med.id, { reason: null, endDate: null, expectedVersion: med.version }), onChanged)}>{t("rec.meds.stop")}</Button>
          <Button size="sm" variant="ghost" iconStart="trash" onClick={() => act.run(() => api.removeMedication(id, med.id), onChanged)}>{t("common.remove")}</Button>
        </div>
      </div>
      <ActionError error={act.error} />
      {open && <Schedule medId={med.id} onChanged={onChanged} />}
    </li>
  );
}

function Schedule({ medId, onChanged }: { medId: string; onChanged(): void }) {
  const { t, fmt } = useI18n();
  const api = useRecordsApi();
  const id = useSelfId();
  const list = useLoad((a) => a.schedule(id, medId), [id, medId]);
  const act = useAction();
  const [time, setTime] = useState("08:00");
  const changed = () => { list.reload(); onChanged(); };
  return (
    <div className="rec-note">
      <Loaded load={list.state} reload={list.reload}>{(rows: ScheduleEntry[]) => rows.length === 0 ? <p className="ms-muted">{t("rec.sched.empty")}</p> : (
        <ul className="rec-list">{rows.map((e) => (
          <li key={e.id} className="rec-row"><span className="rec-times">{fmt.time(e.timeOfDay.slice(0, 5))}</span>
            <Button size="sm" variant="ghost" iconStart="trash" onClick={() => act.run(() => api.removeSchedule(id, e.id), changed)}>{t("common.remove")}</Button></li>))}
        </ul>)}
      </Loaded>
      <Form busy={act.busy} submitLabel={t("rec.sched.add")} onSubmit={() => void act.run(() => api.addSchedule(id, medId, { timeOfDay: `${time}:00`, days: null }), changed)}>
        <TextField label={t("rec.sched.time")} type="time" value={time} onChange={(e) => setTime(e.target.value)} dir="ltr" />
      </Form>
      <ActionError error={act.error} />
    </div>
  );
}

/** Add a medicine: pick it from the reference (never guessed), or say it is not in the list and type its name. */
function AddMedicine({ onAdded }: { onAdded(): void }) {
  const { t, locale } = useI18n();
  const api = useRecordsApi();
  const ref = useMedicationApi();
  const id = useSelfId();
  const act = useAction();
  const [q, setQ] = useState("");
  const [found, setResults] = useState<MedicationSummary[] | null>(null);
  const results = q.trim().length < 2 ? null : found;
  const [picked, setPicked] = useState<MedicationSummary | null>(null);
  const [typed, setTyped] = useState("");
  const [notListed, setNotListed] = useState(false);
  const [amount, setAmount] = useState("");
  const [unit, setUnit] = useState("mg");
  const [freq, setFreq] = useState("TimesPerDay");
  const [times, setTimes] = useState("1");
  const [source, setSource] = useState("SelfReported");

  useEffect(() => {
    const term = q.trim();
    if (term.length < 2) return;
    const ctl = new AbortController();
    const h = setTimeout(() => { ref.search({ q: term, limit: 6 }, ctl.signal).then((r) => setResults(r.items), () => setResults([])); }, 300);
    return () => { clearTimeout(h); ctl.abort(); };
  }, [q, ref]);

  const L = (x: { en: string | null; fa: string | null }) => x[locale] ?? x.en ?? x.fa ?? "";
  const ready = picked || (notListed && typed.trim());
  return (
    <Card title={t("rec.add.title")} subtitle={t("rec.add.sub")}>
      <Form busy={act.busy} submitLabel={t("rec.add")} onSubmit={() => {
        if (!ready) return;
        const start = dayKey();
        void act.run(() => api.addMedication(id, {
          medicationId: picked?.id ?? null, unregisteredName: picked ? null : typed.trim(), doseAmount: toNumber(amount), doseUnit: toNumber(amount) === null ? null : unit, doseText: null,
          frequency: freq as never, frequencyValue: freq === "TimesPerDay" || freq === "EveryNHours" ? (toNumber(times) ?? 1) : null, route: null, startDate: start, endDate: null, source: source as never, prescriberNote: null,
        }), () => { setPicked(null); setTyped(""); setQ(""); setAmount(""); setResults(null); setNotListed(false); onAdded(); });
      }}>
        {picked ? (
          <div className="rec-note" role="status"><strong>{L(picked.name)}</strong> <Badge tone="info" icon="check">{t("rec.meds.registered")}</Badge> <DemoFlag show={picked.isDemo} />
            <div><Button size="sm" variant="ghost" onClick={() => setPicked(null)}>{t("rec.add.change")}</Button></div></div>
        ) : notListed ? (
          <>
            <TextField label={t("rec.add.typedName")} hint={t("rec.add.typedHint")} value={typed} onChange={(e) => setTyped(e.target.value)} maxLength={120} />
            <Button size="sm" variant="ghost" onClick={() => setNotListed(false)}>{t("rec.add.backToSearch")}</Button>
          </>
        ) : (
          <>
            <TextField label={t("rec.add.search")} hint={t("rec.add.searchHint")} value={q} onChange={(e) => setQ(e.target.value)} maxLength={64} />
            {results && results.length > 0 && (
              <ul className="rec-list" aria-label={t("rec.add.results")}>{results.map((m) => (
                <li key={m.id}><button type="button" className="rec-row" style={{ inlineSize: "100%", textAlign: "start", cursor: "pointer" }} onClick={() => setPicked(m)}>
                  <span className="rec-row__main"><span className="rec-row__title">{L(m.name)}</span><span className="rec-row__meta">{L(m.dosageForm)} {m.strengthSummary && <bdi dir="ltr">· {m.strengthSummary}</bdi>}</span></span></button></li>))}
              </ul>)}
            {results && results.length === 0 && <p className="ms-muted" role="status">{t("rec.add.noMatch")}</p>}
            <Button size="sm" variant="secondary" onClick={() => setNotListed(true)}>{t("rec.add.notListed")}</Button>
          </>
        )}
        <div className="rec-form rec-form--cols">
          <TextField label={t("rec.add.dose")} inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} dir="ltr" />
          <Select label={t("rec.add.unit")} value={unit} onValueChange={setUnit} options={UNITS.map((u) => ({ value: u, label: u }))} />
          <Select label={t("rec.add.frequency")} value={freq} onValueChange={setFreq} options={(["TimesPerDay", "EveryNHours", "AsNeeded", "Other"] as const).map((f) => ({ value: f, label: t(`rec.freqName.${f}`) }))} />
          {(freq === "TimesPerDay" || freq === "EveryNHours") && <TextField label={t("rec.add.times")} inputMode="numeric" value={times} onChange={(e) => setTimes(e.target.value)} dir="ltr" />}
          <Select label={t("rec.add.source")} value={source} onValueChange={setSource} options={(["SelfReported", "Physician", "Pharmacist", "Unknown"] as const).map((s) => ({ value: s, label: t(`rec.source.${s}`) }))} />
        </div>
      </Form>
      <ActionError error={act.error} />
      <p className="ms-muted">{t("rec.add.noGuess")}</p>
    </Card>
  );
}

