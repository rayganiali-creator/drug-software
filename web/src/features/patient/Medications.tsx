import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { InteractionCard, MedicationCard, SourceChips } from "../../components/health/cards";
import { AlertCard, Button, Card, EmptyState, ErrorState, LoadingState, SegmentedControl } from "../../components/ui";
import { PageHeader } from "../../layouts/PageHeader";
import { useI18n } from "../../i18n/I18nProvider";
import { useAsync } from "../../services/ServicesProvider";
import "../features.css";

export function Medications() {
  const { t } = useI18n();
  const [filter, setFilter] = useState<"all" | "today">("all");
  const meds = useAsync((s) => s.medications.forPatient("pt-sara"));
  const doses = useAsync((s) => s.medications.todaysDoses("pt-sara"));
  return (
    <>
      <PageHeader title={t("nav.medications")} subtitle={t("med.pageSub")}
        actions={<SegmentedControl label={t("med.filter")} value={filter} onValueChange={(v) => setFilter(v as "all" | "today")} options={[{ value: "all", label: t("med.filterAll") }, { value: "today", label: t("med.filterToday") }]} />} />
      {meds.status === "loading" && <LoadingState label={t("common.loading")} />}
      {meds.status === "error" && <ErrorState title={t("common.errorTitle")} body={t("common.errorBody")} retryLabel={t("common.retry")} onRetry={meds.reload} />}
      {meds.status === "ready" && doses.status === "ready" && (() => {
        const todays = new Set(doses.data.filter((d) => !d.tomorrow).map((d) => d.drug.id));
        const list = meds.data.filter((m) => filter === "all" || todays.has(m.drug.id));
        if (!list.length) return <EmptyState icon="pill" title={t("med.emptyTitle")} body={t("med.emptyBody")} />;
        return (
          <div className="grid-2">
            {list.map((m) => {
              const dose = doses.data.find((d) => d.drug.id === m.drug.id && (d.isNext || d.status !== "taken")) ?? doses.data.find((d) => d.drug.id === m.drug.id);
              return <MedicationCard key={m.drug.id} med={m} dose={dose} to={`/app/patient/medications/${m.drug.id}`} />;
            })}
          </div>
        );
      })()}
      <p className="ms-muted" style={{ marginBlockStart: "var(--space-6)" }}>{t("demo.footnote")}</p>
    </>
  );
}

export function MedicationDetail() {
  const { id = "" } = useParams();
  const { t, loc } = useI18n();
  const nav = useNavigate();
  const meds = useAsync((s) => s.medications.forPatient("pt-sara"));
  const doses = useAsync((s) => s.medications.todaysDoses("pt-sara"));
  const ix = useAsync((s) => s.prescriptions.list({ patientId: "pt-sara" }).then((rxs) => Promise.all(rxs.map((r) => s.prescriptions.interactionsFor(r.id)))).then((a) => a.flat()));
  if (meds.status === "loading" || doses.status === "loading") return <LoadingState label={t("common.loading")} />;
  if (meds.status === "error" || doses.status === "error") return <ErrorState title={t("common.errorTitle")} retryLabel={t("common.retry")} onRetry={meds.reload} />;
  if (meds.status !== "ready" || doses.status !== "ready") return null;
  const med = meds.data.find((m) => m.drug.id === id);
  if (!med) return <EmptyState icon="pill" title={t("med.notFound")} action={<Button onClick={() => nav("/app/patient/medications")}>{t("nav.medications")}</Button>} />;
  const dose = doses.data.find((d) => d.drug.id === id && d.isNext) ?? doses.data.find((d) => d.drug.id === id);
  const related = ix.status === "ready" ? ix.data.filter((x) => x.drugA.id === id || x.drugB.id === id) : [];
  return (
    <>
      <PageHeader title={loc(med.drug.name)} subtitle={loc(med.drug.category)}
        actions={<><Button variant="secondary" iconStart="chevronLeft" onClick={() => nav("/app/patient/medications")}>{t("common.back")}</Button><Button variant="ai" iconStart="sparkles" onClick={() => nav(`/app/patient/assistant?drug=${id}`)}>{t("med.askAbout")}</Button></>} />
      <div className="split">
        <MedicationCard med={med} dose={dose} variant="detail" />
        <div className="ms-stack">
          {related.length > 0 && (
            <>
              <AlertCard tone="info" title={t("med.pharmacistReview")}>{t("med.pharmacistReviewBody")}</AlertCard>
              {related.map((x) => <InteractionCard key={x.id} interaction={x} />)}
            </>
          )}
          <Card title={t("med.sourceTitle")} subtitle={t("med.sourceSub")}>
            <SourceChips sources={related.map((x) => x.source)} />
            <p className="ms-muted" style={{ marginBlockStart: "var(--space-3)" }}>{t("demo.footnote")}</p>
          </Card>
        </div>
      </div>
    </>
  );
}
