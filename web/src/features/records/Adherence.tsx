import { useParams } from "react-router-dom";
import { AlertCard, Badge, Card, EmptyState } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useLoad } from "../../records/hooks";
import type { AdherenceMedication, AdherenceSummary } from "../../records/types";
import { Loaded } from "./shared";

const key = (code: string) => code.replace(/[.]/g, "_");

/** Per-medicine view of what the patient reported. Unknown instructions are listed, never guessed; a dose with no record is "not recorded", never "missed". */
function MedicationRow({ m }: { m: AdherenceMedication }) {
  const { t, fmt } = useI18n();
  const gap = (g: string) => { const k = `adh.gap.${key(g)}`; const v = t(k); return v === k ? g : v; };
  return (
    <li className="rec-row" style={{ flexDirection: "column", alignItems: "stretch" }}>
      <div className="rec-row__main">
        <span className="rec-row__title" dir="auto">{m.medicationName}</span>
        <span className="rec-badges">
          {!m.recordingAllowed && <Badge tone="warning" icon="alertTriangle">{t("adh.cannotRecord")}</Badge>}
          {m.schedule.length > 0 && <Badge tone="neutral" icon="clock">{t("adh.scheduled", { n: m.schedule.length })}</Badge>}
        </span>
      </div>
      <dl className="rec-facts" aria-label={m.medicationName}>
        <div><dt>{t("adh.planned")}</dt><dd>{fmt.number(m.plannedDoses)}</dd></div>
        <div><dt>{t("adh.taken")}</dt><dd>{fmt.number(m.taken)}</dd></div>
        <div><dt>{t("adh.skipped")}</dt><dd>{fmt.number(m.skipped)}</dd></div>
        <div><dt>{t("adh.missed")}</dt><dd>{fmt.number(m.missed)}</dd></div>
        <div><dt>{t("adh.notRecorded")}</dt><dd>{fmt.number(m.notRecorded)}</dd></div>
        {m.unplanned > 0 && <div><dt>{t("adh.unplanned")}</dt><dd>{fmt.number(m.unplanned)}</dd></div>}
      </dl>
      {m.instructionGaps.length > 0 && (
        <div className="rec-note"><strong>{t("adh.gaps")}</strong>
          <ul className="rec-list">{m.instructionGaps.map((g) => <li key={g}>{gap(g)}</li>)}</ul></div>
      )}
    </li>
  );
}

export function AdherenceView({ s }: { s: AdherenceSummary }) {
  const { t, fmt } = useI18n();
  return (
    <div className="rec-stack">
      <div className="rec-badges">
        <Badge tone="info" icon="user">{t("adh.patientReported")}</Badge>
        {s.intakeStale && <Badge tone="warning" icon="clock">{t("adh.stale")}</Badge>}
        {s.intakeLastUpdatedAt && <span className="ms-muted">{t("rec.fresh.updated", { date: fmt.date(new Date(s.intakeLastUpdatedAt), "short") })}</span>}
      </div>
      <p className="ms-muted">{t("adh.notice")}</p>
      {s.medications.length === 0
        ? <EmptyState icon="pill" title={t("adh.none")} body={t("adh.noneBody")} />
        : <ul className="rec-list" aria-label={t("adh.title")}>{s.medications.map((m) => <MedicationRow key={m.patientMedicationId} m={m} />)}</ul>}
      <AlertCard tone="info" title={t("adh.missedDose.title")}>{t("adh.missedDose.body")}</AlertCard>
    </div>
  );
}

/** The patient's own adherence summary (a card on the taking page). */
export function AdherenceCard({ subjectId }: { subjectId: string }) {
  const { t } = useI18n();
  const load = useLoad((a, s) => a.adherence(subjectId, 14, s), [subjectId]);
  return (
    <Card title={t("adh.title")} subtitle={t("adh.sub", { n: 14 })}>
      <Loaded load={load.state} reload={load.reload}>{(s: AdherenceSummary) => <AdherenceView s={s} />}</Loaded>
    </Card>
  );
}

/** A professional's read-only view of one patient's reported adherence (the server enforces relationship and consent for the adherence scope). */
export function ProAdherence() {
  const { t } = useI18n();
  const { id } = useParams();
  const load = useLoad((a, s) => a.adherence(id ?? "", 14, s), [id]);
  return (
    <>
      <PageHeader title={t("adh.pro.title")} subtitle={t("adh.pro.sub")} />
      <Loaded load={load.state} reload={load.reload}>{(s: AdherenceSummary) => <AdherenceView s={s} />}</Loaded>
    </>
  );
}
