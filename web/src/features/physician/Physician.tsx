import { useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { ADRCard, AIInsightCard, AdherenceCard, MedicationCard, PatientCard, PrescriptionCard, RiskBadge, RiskCard } from "../../components/health/cards";
import { Button, Card, EmptyState, LineChart, ChartCard, Select, SearchField, SegmentedControl, StatCard, Table, Tabs, Timeline, useToast, Sparkline, type Column } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAsync } from "../../services/ServicesProvider";
import type { Patient } from "../../services/types";
import { Async, PersonCell, chartColors } from "../common";
import "../features.css";

function reasonsFor(p: Patient, t: ReturnType<typeof useI18n>["t"], fmt: ReturnType<typeof useI18n>["fmt"]) {
  const r: string[] = [];
  if (p.adherence < 70) r.push(t("phys.reasonAdherence", { n: fmt.percent(p.adherence) }));
  if (p.lastCheckInDaysAgo >= 3) r.push(t("phys.reasonCheckin", { n: fmt.number(p.lastCheckInDaysAgo) }));
  if (p.symptoms.some((s) => s.severity !== "mild")) r.push(t("phys.reasonSymptom"));
  return r.length ? r : [t("phys.reasonRoutine")];
}

export function PhysicianDashboard() {
  const { t, loc, fmt } = useI18n();
  const nav = useNavigate();
  const patients = useAsync((s) => s.patients.list());
  const adr = useAsync((s) => s.adr.list({ status: "new" }));
  const summary = useAsync((s) => s.ai.physicianSummary("panel"));
  return (
    <>
      <PageHeader title={t("nav.dashboard")} subtitle={t("phys.dashSub")} />
      <Async state={patients} rows={2}>
        {(list) => {
          const attention = list.filter((p) => p.risk === "high" || p.adherence < 70).sort((a, b) => a.adherence - b.adherence);
          const avg = Math.round(list.reduce((n, p) => n + p.adherence, 0) / list.length);
          return (
            <div className="ms-stack">
              <div className="grid-stats">
                <StatCard icon="users" label={t("phys.statPatients")} value={fmt.number(list.length)} />
                <StatCard icon="alertTriangle" label={t("phys.statAttention")} value={fmt.number(attention.length)} delta={t("phys.statAttentionHint")} deltaTone="warning" />
                <StatCard icon="activity" label={t("phys.statAdherence")} value={fmt.percent(avg)} />
                <StatCard icon="clipboard" label={t("phys.statAdr")} value={adr.status === "ready" ? fmt.number(adr.data.length) : "…"} />
              </div>
              <div className="split">
                <section className="ms-stack" aria-label={t("phys.attention")}>
                  <h2 style={{ fontSize: "var(--text-h3-size)" }}>{t("phys.attention")}</h2>
                  <div className="grid-2">
                    {attention.map((p) => <RiskCard key={p.id} patient={p} reasons={reasonsFor(p, t, fmt)} to={`/app/physician/patients/${p.id}`} />)}
                  </div>
                </section>
                <div className="ms-stack">
                  {summary.status === "ready" && <AIInsightCard title={t("phys.panelSummary")} text={loc(summary.data.text)} confidence={summary.data.confidence} sources={summary.data.sources}
                    footer={<Button size="sm" variant="secondary" onClick={() => nav("/app/physician/reports")}>{t("nav.reports")}</Button>} />}
                </div>
              </div>
            </div>
          );
        }}
      </Async>
    </>
  );
}

export function PhysicianPatients() {
  const { t, loc, fmt } = useI18n();
  const nav = useNavigate();
  const patients = useAsync((s) => s.patients.list());
  const [q, setQ] = useState("");
  const [risk, setRisk] = useState("all");
  return (
    <>
      <PageHeader title={t("nav.patients")} subtitle={t("phys.patientsSub")} />
      <Async state={patients}>
        {(list) => {
          const rows = list.filter((p) => (risk === "all" || p.risk === risk) && (!q || (p.name.en + p.name.fa).toLowerCase().includes(q.toLowerCase())));
          const cols: Column<Patient>[] = [
            { id: "name", header: t("patient.name"), primary: true, cell: (p) => <PersonCell patient={p} /> },
            { id: "age", header: t("patient.age"), cell: (p) => fmt.number(p.age) },
            { id: "risk", header: t("risk.label"), cell: (p) => <RiskBadge risk={p.risk} /> },
            { id: "adh", header: t("patient.adherence"), cell: (p) => <span className="ms-row" style={{ flexWrap: "nowrap" }}>{fmt.percent(p.adherence)}<Sparkline values={p.adherenceSeries} label={t("patient.adherenceTrend")} width={72} height={22} /></span> },
            { id: "chk", header: t("patient.lastCheckin"), cell: (p) => (p.lastCheckInDaysAgo === 0 ? t("time.today") : t("time.daysAgo", { n: fmt.number(p.lastCheckInDaysAgo) })) },
            { id: "open", header: "", cell: (p) => <Button size="sm" variant="ghost" iconEnd="chevronRight" aria-label={`${t("common.open")}: ${loc(p.name)}`} onClick={() => nav(`/app/physician/patients/${p.id}`)}>{t("common.open")}</Button> },
          ];
          return (
            <div className="ms-stack">
              <div className="ms-row">
                <div style={{ flex: 1, minInlineSize: "14rem" }}><SearchField label={t("phys.searchPatients")} clearLabel={t("search.clear")} value={q} onValueChange={setQ} /></div>
                <Select label={t("risk.label")} value={risk} onValueChange={setRisk} options={[{ value: "all", label: t("common.all") }, { value: "high", label: t("risk.high") }, { value: "medium", label: t("risk.medium") }, { value: "low", label: t("risk.low") }]} />
              </div>
              {rows.length === 0 ? <EmptyState icon="search" title={t("common.noResults")} body={t("common.noResultsBody")} /> : (
                <Table caption={t("nav.patients")} columns={cols} rows={rows} rowKey={(p) => p.id} pageSize={5} onRowClick={(p) => nav(`/app/physician/patients/${p.id}`)}
                  paginationLabels={{ nav: t("table.pagination"), prev: t("table.prev"), next: t("table.next"), page: (n) => t("table.page", { n: fmt.number(n) }), summary: (a, b, n) => t("table.summary", { a: fmt.number(a), b: fmt.number(b), n: fmt.number(n) }) }} />
              )}
            </div>
          );
        }}
      </Async>
    </>
  );
}

export function PatientOverview() {
  const { id = "" } = useParams();
  const { t, loc, fmt } = useI18n();
  const toast = useToast();
  const nav = useNavigate();
  const [tab, setTab] = useState("overview");
  const patient = useAsync((s) => s.patients.get(id), [id]);
  const meds = useAsync((s) => s.medications.forPatient(id), [id]);
  const rx = useAsync((s) => s.prescriptions.list({ patientId: id }), [id]);
  const adr = useAsync((s) => s.adr.list({ patientId: id }), [id]);
  const summary = useAsync((s) => s.ai.physicianSummary(id), [id]);
  const series = useAsync((s) => s.analytics.adherenceSeries(id), [id]);
  const days = useMemo(() => Array.from({ length: 14 }, (_, i) => fmt.number(i + 1)), [fmt]);

  return (
    <Async state={patient} rows={2}>
      {(p) => (
        <>
          <PageHeader title={loc(p.name)} subtitle={t("phys.overviewSub")}
            actions={<Button variant="secondary" iconStart="chevronLeft" onClick={() => nav("/app/physician/patients")}>{t("nav.patients")}</Button>} />
          <div className="ms-stack">
            <PatientCard patient={p} />
            <Tabs label={t("phys.tabs")} value={tab} onValueChange={setTab} tabs={[
              { id: "overview", label: t("phys.tabOverview"), panel: (
                <div className="split">
                  <Card title={t("phys.recentEvents")}>
                    <Timeline label={t("phys.recentEvents")} entries={[
                      ...p.symptoms.map((s) => ({ id: s.id, title: loc(s.term), meta: t("time.daysAgo", { n: fmt.number(s.daysAgo) }), icon: "activity" as const, tone: "warning" as const })),
                      ...(adr.status === "ready" ? adr.data.map((a) => ({ id: a.id, title: loc(a.event), meta: t("time.daysAgo", { n: fmt.number(a.reportedDaysAgo) }), icon: "alertTriangle" as const, tone: "danger" as const })) : []),
                      { id: "chk", title: t("phys.lastCheckin"), meta: p.lastCheckInDaysAgo === 0 ? t("time.today") : t("time.daysAgo", { n: fmt.number(p.lastCheckInDaysAgo) }), icon: "checkCircle", tone: "primary" as const },
                    ]} />
                  </Card>
                  <AdherenceCard value={p.adherence} series={series.status === "ready" ? series.data.slice(-14) : p.adherenceSeries} />
                </div>
              ) },
              { id: "medications", label: t("nav.medications"), panel: <Async state={meds}>{(list) => <div className="grid-2">{list.map((m) => <MedicationCard key={m.drug.id} med={m} />)}</div>}</Async> },
              { id: "prescriptions", label: t("nav.prescriptions"), panel: <Async state={rx}>{(list) => <div className="grid-2">{list.map((r) => <PrescriptionCard key={r.id} rx={r} />)}</div>}</Async> },
              { id: "adherence", label: t("patient.adherence"), panel: (
                <ChartCard title={t("phys.adherence14")} subtitle={t("phys.adherenceSub")} legend={[{ id: "a", label: t("patient.adherence"), values: [], color: chartColors[0]! }]}>
                  <LineChart xLabels={days} series={[{ id: "a", label: t("patient.adherence"), values: series.status === "ready" ? series.data.slice(-14) : p.adherenceSeries, color: chartColors[0]! }]} yMax={100} summary={t("phys.adherenceChartSummary", { name: loc(p.name) })} />
                </ChartCard>
              ) },
              { id: "symptoms", label: t("phys.tabSymptoms"), panel: p.symptoms.length ? (
                <Card><Timeline label={t("phys.tabSymptoms")} entries={p.symptoms.map((s) => ({ id: s.id, title: loc(s.term), meta: `${t(`severity.${s.severity}`)} · ${t("time.daysAgo", { n: fmt.number(s.daysAgo) })}`, icon: "activity" as const, tone: s.severity === "mild" ? ("info" as const) : ("warning" as const) }))} /></Card>
              ) : <EmptyState icon="checkCircle" title={t("phys.noSymptoms")} /> },
              { id: "adr", label: "ADR", panel: <Async state={adr}>{(list) => list.length ? <div className="grid-2">{list.map((a) => <ADRCard key={a.id} report={a} />)}</div> : <EmptyState icon="checkCircle" title={t("adr.none")} />}</Async> },
              { id: "ai", label: t("phys.tabAi"), panel: (
                <Async state={summary}>{(s) => <AIInsightCard title={t("phys.aiSummary")} text={loc(s.text)} confidence={s.confidence} sources={s.sources}
                  footer={<div className="ms-row"><Button size="sm" onClick={() => toast.show({ message: t("phys.confirmed"), tone: "success" })} iconStart="check">{t("phys.confirmSummary")}</Button><Button size="sm" variant="ghost" onClick={() => toast.show({ message: t("phys.dismissed"), tone: "info" })}>{t("phys.dismiss")}</Button></div>} />}</Async>
              ) },
            ]} />
          </div>
        </>
      )}
    </Async>
  );
}

export function PhysicianPrescriptions() {
  const { t } = useI18n();
  const [status, setStatus] = useState("all");
  const rx = useAsync((s) => s.prescriptions.list(), []);
  return (
    <>
      <PageHeader title={t("nav.prescriptions")} subtitle={t("phys.rxSub")}
        actions={<SegmentedControl label={t("rx.filter")} value={status} onValueChange={setStatus} options={[{ value: "all", label: t("common.all") }, { value: "active", label: t("rx.status.active") }, { value: "pending-review", label: t("rx.status.pending-review") }]} />} />
      <Async state={rx}>{(list) => <div className="grid-2">{list.filter((r) => status === "all" || r.status === status).map((r) => <PrescriptionCard key={r.id} rx={r} showPatient />)}</div>}</Async>
    </>
  );
}

export function PhysicianAdr() {
  const { t } = useI18n();
  const adr = useAsync((s) => s.adr.list());
  return (
    <>
      <PageHeader title={t("nav.adr")} subtitle={t("phys.adrSub")} />
      <Async state={adr}>{(list) => <div className="grid-2">{list.map((a) => <ADRCard key={a.id} report={a} showPatient />)}</div>}</Async>
    </>
  );
}

export function PhysicianReports() {
  const { t, loc, fmt } = useI18n();
  const toast = useToast();
  const reports = useAsync((s) => s.analytics.physicianReports());
  const adherence = useAsync((s) => s.patients.list());
  return (
    <>
      <PageHeader title={t("nav.reports")} subtitle={t("phys.reportsSub")} />
      <Async state={reports}>
        {(list) => (
          <div className="split">
            <div className="ms-stack">
              {list.map((r) => (
                <Card key={r.id}>
                  <div className="ms-row" style={{ justifyContent: "space-between" }}>
                    <div><strong>{loc(r.title)}</strong><div className="ms-muted">{loc(r.kind)} · {t("time.daysAgo", { n: fmt.number(r.updatedDaysAgo) })}</div></div>
                    <Button variant="secondary" size="sm" iconStart="download" onClick={() => toast.show({ message: t("reports.prototype"), tone: "info" })}>{t("reports.export")}</Button>
                  </div>
                </Card>
              ))}
            </div>
            {adherence.status === "ready" && (
              <ChartCard title={t("phys.panelAdherence")} subtitle={t("phys.panelAdherenceSub")}>
                <ul className="ms-stack" style={{ gap: "var(--space-2)" }}>
                  {[...adherence.data].sort((a, b) => a.adherence - b.adherence).map((p) => (
                    <li key={p.id} className="ms-row" style={{ justifyContent: "space-between", flexWrap: "nowrap" }}><span>{loc(p.name)}</span><span className="ms-row" style={{ flexWrap: "nowrap" }}><Sparkline values={p.adherenceSeries} label={t("patient.adherenceTrend")} /><strong>{fmt.percent(p.adherence)}</strong></span></li>
                  ))}
                </ul>
              </ChartCard>
            )}
          </div>
        )}
      </Async>
    </>
  );
}
