import { useState } from "react";
import { Badge, Button, Card, EmptyState, Select, TextField } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAction, useLoad, useRecordsApi, useSelfId } from "../../records/hooks";
import { RecordsError, type Allergy, type Condition, type Freshness, type Profile, type Severity3, type Sex, type Symptom } from "../../records/types";
import { ActionError, DemoFlag, Form, FreshLine, Loaded, categoryKey, orNull, toNumber } from "./shared";

const SEXES: Sex[] = ["Female", "Male", "Other", "Undisclosed"];
const SEV: Severity3[] = ["Mild", "Moderate", "Severe"];

/** The patient's own health record: what was recorded, when, and the lists that make up the profile. */
export function PatientRecords() {
  const { t } = useI18n();
  const id = useSelfId();
  const api = useRecordsApi();
  const profile = useLoad((a, s) => a.profile(id, s), [id]);
  const fresh = useLoad((a, s) => a.freshness(id, s), [id]);
  const create = useAction();
  const noRecord = profile.state.status === "error" && profile.state.error instanceof RecordsError && profile.state.error.kind === "notFound";

  const refreshAll = () => { profile.reload(); fresh.reload(); };

  return (
    <>
      <PageHeader title={t("rec.records.title")} subtitle={t("rec.records.sub")} />
      {noRecord ? (
        <Card>
          <EmptyState icon="user" title={t("rec.noRecord.title")} body={t("rec.noRecord.body")}
            action={<Button loading={create.busy} onClick={() => create.run(() => api.ensureOwn(), refreshAll)}>{t("rec.noRecord.create")}</Button>} />
          <ActionError error={create.error} />
        </Card>
      ) : (
        <div className="rec-stack">
          <Loaded load={fresh.state} reload={fresh.reload}>{(rows) => <FreshnessCard rows={rows} />}</Loaded>
          <div className="rec-grid rec-grid--2">
            <Loaded load={profile.state} reload={profile.reload}>{(p) => <ProfileCard profile={p} onSaved={refreshAll} />}</Loaded>
            <ConditionsCard onChanged={fresh.reload} />
            <AllergiesCard onChanged={fresh.reload} />
            <SymptomsCard onChanged={fresh.reload} />
          </div>
        </div>
      )}
    </>
  );
}

function FreshnessCard({ rows }: { rows: Freshness[] }) {
  const { t } = useI18n();
  return (
    <Card title={t("rec.fresh.title")} subtitle={t("rec.fresh.sub")}>
      <ul className="rec-list" aria-label={t("rec.fresh.title")}>
        {rows.map((r) => (
          <li key={r.category} className="rec-row">
            <div className="rec-row__main">
              <span className="rec-row__title">{t(categoryKey(r.category))}</span>
              <span className="rec-row__meta">{r.neverRecorded ? "" : t("rec.fresh.count", { n: r.recordCount })}</span>
            </div>
            <FreshLine at={r.lastUpdatedAt} stale={r.isStale} never={r.neverRecorded} />
          </li>
        ))}
      </ul>
    </Card>
  );
}

function ProfileCard({ profile, onSaved }: { profile: Profile; onSaved(): void }) {
  const { t } = useI18n();
  const api = useRecordsApi();
  const id = useSelfId();
  const act = useAction();
  const [year, setYear] = useState(profile.yearOfBirth?.toString() ?? "");
  const [sex, setSex] = useState<string>(profile.sex ?? "");
  const [weight, setWeight] = useState(profile.weightKg?.toString() ?? "");
  const [height, setHeight] = useState(profile.heightCm?.toString() ?? "");
  const [saved, setSaved] = useState(false);
  return (
    <Card title={t("rec.profile.title")} subtitle={<span className="rec-badges"><DemoFlag show={profile.isDemo} notice={profile.notice} /><FreshLine at={profile.version === 0 ? null : profile.updatedAt} never={profile.version === 0} /></span>}>
      <Form busy={act.busy} submitLabel={t("common.save")} onSubmit={() => { setSaved(false); void act.run(() => api.saveProfile(id, { yearOfBirth: toNumber(year), sex: orNull(sex), weightKg: toNumber(weight), heightCm: toNumber(height), timeZone: null, expectedVersion: profile.version }), () => { setSaved(true); onSaved(); }); }}>
        <div className="rec-form rec-form--cols">
          <TextField label={t("rec.profile.year")} hint={t("rec.profile.yearHint")} inputMode="numeric" value={year} onChange={(e) => setYear(e.target.value)} dir="ltr" />
          <Select label={t("rec.profile.sex")} value={sex} onValueChange={setSex} options={[{ value: "", label: t("rec.profile.sexNone") }, ...SEXES.map((s) => ({ value: s, label: t(`rec.sex.${s}`) }))]} />
          <TextField label={t("rec.profile.weight")} inputMode="decimal" value={weight} onChange={(e) => setWeight(e.target.value)} dir="ltr" />
          <TextField label={t("rec.profile.height")} inputMode="decimal" value={height} onChange={(e) => setHeight(e.target.value)} dir="ltr" />
        </div>
        <p className="ms-muted">{t("rec.profile.minimal")}</p>
      </Form>
      <ActionError error={act.error} />
      {saved && <p role="status" className="ms-muted">{t("rec.saved")}</p>}
    </Card>
  );
}

function ConditionsCard({ onChanged }: { onChanged(): void }) {
  const { t } = useI18n();
  const api = useRecordsApi();
  const id = useSelfId();
  const list = useLoad((a, s) => a.conditions(id, s), [id]);
  const act = useAction();
  const [name, setName] = useState("");
  const [status, setStatus] = useState("Active");
  const changed = () => { list.reload(); onChanged(); };
  return (
    <Card title={t("rec.cond.title")}>
      <Loaded load={list.state} reload={list.reload}>{(rows: Condition[]) => rows.length === 0 ? <EmptyState icon="clipboard" title={t("rec.cond.empty")} /> : (
        <ul className="rec-list" aria-label={t("rec.cond.title")}>{rows.map((c) => (
          <li key={c.id} className="rec-row">
            <div className="rec-row__main"><span className="rec-row__title">{c.name}</span><span className="rec-row__meta">{t(`rec.cond.status.${c.status}`)}</span></div>
            <Button size="sm" variant="ghost" iconStart="trash" onClick={() => act.run(() => api.removeCondition(id, c.id), changed)}>{t("common.remove")}</Button>
          </li>))}
        </ul>)}
      </Loaded>
      <Form busy={act.busy} submitLabel={t("rec.add")} onSubmit={() => { if (name.trim()) void act.run(() => api.addCondition(id, { name, onsetDate: null, status, note: null }), () => { setName(""); changed(); }); }}>
        <div className="rec-form rec-form--cols">
          <TextField label={t("rec.cond.name")} value={name} onChange={(e) => setName(e.target.value)} maxLength={200} />
          <Select label={t("rec.cond.statusLabel")} value={status} onValueChange={setStatus} options={(["Active", "Resolved", "Unknown"] as const).map((s) => ({ value: s, label: t(`rec.cond.status.${s}`) }))} />
        </div>
      </Form>
      <ActionError error={act.error} />
    </Card>
  );
}

function AllergiesCard({ onChanged }: { onChanged(): void }) {
  const { t } = useI18n();
  const api = useRecordsApi();
  const id = useSelfId();
  const list = useLoad((a, s) => a.allergies(id, s), [id]);
  const act = useAction();
  const [substance, setSubstance] = useState("");
  const [severity, setSeverity] = useState("Unknown");
  const [reaction, setReaction] = useState("");
  const changed = () => { list.reload(); onChanged(); };
  return (
    <Card title={t("rec.allergy.title")}>
      <Loaded load={list.state} reload={list.reload}>{(rows: Allergy[]) => rows.length === 0 ? <EmptyState icon="shield" title={t("rec.allergy.empty")} /> : (
        <ul className="rec-list" aria-label={t("rec.allergy.title")}>{rows.map((a) => (
          <li key={a.id} className="rec-row">
            <div className="rec-row__main"><span className="rec-row__title">{a.substance}</span><span className="rec-row__meta">{t(`rec.severity.${a.severity}`)}{a.reaction ? ` · ${a.reaction}` : ""}</span></div>
            <Button size="sm" variant="ghost" iconStart="trash" onClick={() => act.run(() => api.removeAllergy(id, a.id), changed)}>{t("common.remove")}</Button>
          </li>))}
        </ul>)}
      </Loaded>
      <Form busy={act.busy} submitLabel={t("rec.add")} onSubmit={() => { if (substance.trim()) void act.run(() => api.addAllergy(id, { kind: "Other", medicationId: null, substance, severity, reaction: orNull(reaction) }), () => { setSubstance(""); setReaction(""); changed(); }); }}>
        <div className="rec-form rec-form--cols">
          <TextField label={t("rec.allergy.substance")} value={substance} onChange={(e) => setSubstance(e.target.value)} maxLength={200} />
          <Select label={t("rec.allergy.severity")} value={severity} onValueChange={setSeverity} options={(["Unknown", ...SEV] as const).map((s) => ({ value: s, label: t(`rec.severity.${s}`) }))} />
          <TextField label={t("rec.allergy.reaction")} value={reaction} onChange={(e) => setReaction(e.target.value)} maxLength={300} />
        </div>
      </Form>
      <p className="ms-muted">{t("rec.noIdentity")}</p>
      <ActionError error={act.error} />
    </Card>
  );
}

function SymptomsCard({ onChanged }: { onChanged(): void }) {
  const { t, fmt } = useI18n();
  const api = useRecordsApi();
  const id = useSelfId();
  const list = useLoad((a, s) => a.symptoms(id, s), [id]);
  const act = useAction();
  const [text, setText] = useState("");
  const [severity, setSeverity] = useState<string>("Mild");
  const changed = () => { list.reload(); onChanged(); };
  return (
    <Card title={t("rec.symptom.title")}>
      <Loaded load={list.state} reload={list.reload}>{(rows: Symptom[]) => rows.length === 0 ? <EmptyState icon="activity" title={t("rec.symptom.empty")} /> : (
        <ul className="rec-list" aria-label={t("rec.symptom.title")}>{rows.map((s) => (
          <li key={s.id} className="rec-row">
            <div className="rec-row__main"><span className="rec-row__title">{s.text}</span><span className="rec-row__meta">{t(`rec.severity.${s.severity}`)} · {fmt.date(new Date(s.onsetAt), "short")}</span></div>
            <div className="rec-row__actions">{s.severity === "Severe" && <Badge tone="warning" icon="alertTriangle">{t("rec.symptom.severeBadge")}</Badge>}
              <Button size="sm" variant="ghost" iconStart="trash" onClick={() => act.run(() => api.removeSymptom(id, s.id), changed)}>{t("common.remove")}</Button></div>
          </li>))}
        </ul>)}
      </Loaded>
      <Form busy={act.busy} submitLabel={t("rec.add")} onSubmit={() => { if (text.trim()) void act.run(() => api.addSymptom(id, { text, severity, onsetAt: new Date().toISOString(), resolvedAt: null, patientMedicationId: null, note: null }), () => { setText(""); changed(); }); }}>
        <div className="rec-form rec-form--cols">
          <TextField label={t("rec.symptom.text")} value={text} onChange={(e) => setText(e.target.value)} maxLength={200} />
          <Select label={t("rec.symptom.severity")} value={severity} onValueChange={setSeverity} options={SEV.map((s) => ({ value: s, label: t(`rec.severity.${s}`) }))} />
        </div>
      </Form>
      <p className="ms-muted">{t("rec.symptom.advice")}</p>
      <ActionError error={act.error} />
    </Card>
  );
}
