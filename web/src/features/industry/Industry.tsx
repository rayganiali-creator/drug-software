import { useMemo, useState } from "react";
import { Ltr } from "../../components/health/cards";
import { Badge, BarChart, Button, Card, ChartCard, LineChart, Progress, Select, StatCard, Table, useToast, type Column, type Series } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAsync, useServices } from "../../services/ServicesProvider";
import type { Signal } from "../../services/types";
import { Async, chartColors, lastMonths } from "../common";
import "../features.css";

function AggregateBanner() {
  const { t, fmt } = useI18n();
  const { analytics } = useServices();
  return <div className="banner-agg" role="note"><span>{t("ind.aggregate", { k: fmt.number(analytics.k()) })}</span></div>;
}

/** k-anonymity: cells below k are never shown as numbers. */
function useSuppress() {
  const { fmt } = useI18n();
  const { analytics } = useServices();
  const k = analytics.k();
  return { k, cell: (n: number) => (n < k ? `<${fmt.number(k)}` : fmt.number(n)) };
}

export function IndustryOverview() {
  const { t, loc, fmt } = useI18n();
  const trend = useAsync((s) => s.analytics.adrTrend());
  const signals = useAsync((s) => s.analytics.signals());
  const exp = useAsync((s) => s.analytics.experience());
  const months = useMemo(() => lastMonths(12, fmt.monthShort), [fmt]);
  return (
    <>
      <PageHeader title={t("nav.analytics")} subtitle={t("ind.sub")} />
      <AggregateBanner />
      <div className="ms-stack">
        <div className="grid-stats">
          <StatCard icon="fileText" label={t("ind.statReports")} value={trend.status === "ready" ? fmt.number(trend.data.reduce((n, s) => n + s.counts.reduce((a, b) => a + b, 0), 0)) : "…"} hint={t("ind.last12")} />
          <StatCard icon="activity" label={t("ind.statSignals")} value={signals.status === "ready" ? fmt.number(signals.data.filter((s) => s.status !== "closed").length) : "…"} />
          <StatCard icon="heart" label={t("ind.statExperience")} value={exp.status === "ready" ? fmt.number(Math.round(exp.data.reduce((n, e) => n + e.score, 0) / exp.data.length)) : "…"} hint={t("ind.outOf100")} />
          <StatCard icon="flask" label={t("ind.statDrugs")} value={trend.status === "ready" ? fmt.number(trend.data.length) : "…"} />
        </div>
        <Async state={trend}>
          {(list) => {
            const series: Series[] = list.map((s, i) => ({ id: s.drug.id, label: loc(s.drug.name), values: s.counts, color: chartColors[i % chartColors.length]! }));
            return (
              <ChartCard title={t("ind.trendTitle")} subtitle={t("ind.trendSub")} legend={series} footer={t("ind.trendFooter")}>
                <LineChart series={series} xLabels={months} summary={t("ind.trendSummary")} />
              </ChartCard>
            );
          }}
        </Async>
      </div>
    </>
  );
}

export function IndustryAdrTrends() {
  const { t, loc, fmt } = useI18n();
  const { k, cell } = useSuppress();
  const trend = useAsync((s) => s.analytics.adrTrend());
  const [drug, setDrug] = useState("all");
  const months = useMemo(() => lastMonths(12, fmt.monthShort), [fmt]);
  return (
    <>
      <PageHeader title={t("nav.adr-trends")} subtitle={t("ind.trendsSub")} />
      <AggregateBanner />
      <Async state={trend}>
        {(list) => {
          const shown = list.filter((s) => drug === "all" || s.drug.id === drug);
          const series: Series[] = shown.map((s) => ({ id: s.drug.id, label: loc(s.drug.name), values: s.counts, color: chartColors[list.indexOf(s) % chartColors.length]! }));
          const last6 = months.slice(-6);
          type Row = { label: string; idx: number };
          const rows: Row[] = last6.map((label, i) => ({ label, idx: 6 + i }));
          const cols: Column<Row>[] = [{ id: "m", header: t("ind.month"), primary: true, cell: (r) => r.label }, ...shown.map<Column<Row>>((s) => ({ id: s.drug.id, header: loc(s.drug.name), cell: (r) => cell(s.counts[r.idx] ?? 0) }))];
          return (
            <div className="ms-stack">
              <Select label={t("ind.drugFilter")} value={drug} onValueChange={setDrug} options={[{ value: "all", label: t("common.all") }, ...list.map((s) => ({ value: s.drug.id, label: loc(s.drug.name) }))]} />
              <ChartCard title={t("ind.countsTitle")} subtitle={t("ind.suppressNote", { k: fmt.number(k) })} legend={series}>
                <BarChart series={series} xLabels={months} summary={t("ind.countsSummary")} suppressBelow={k} />
              </ChartCard>
              <Table caption={t("ind.countsTitle")} columns={cols} rows={rows} rowKey={(r) => r.label} />
            </div>
          );
        }}
      </Async>
    </>
  );
}

export function IndustryExperience() {
  const { t, loc, fmt } = useI18n();
  const exp = useAsync((s) => s.analytics.experience());
  return (
    <>
      <PageHeader title={t("nav.experience")} subtitle={t("ind.expSub")} />
      <AggregateBanner />
      <Async state={exp}>
        {(list) => (
          <Card title={t("ind.expTitle")} subtitle={t("ind.expNote")}>
            <ul className="ms-stack">
              {list.map((e) => (
                <li key={e.dimension.en} className="ms-stack" style={{ gap: "var(--space-1)" }}>
                  <div className="ms-row" style={{ justifyContent: "space-between" }}><strong>{loc(e.dimension)}</strong><span>{fmt.number(e.score)} <span className="ms-muted">/ {fmt.number(100)} · n={fmt.number(e.n)}</span></span></div>
                  <Progress value={e.score} label={loc(e.dimension)} tone={e.score >= 80 ? "success" : e.score >= 70 ? "primary" : "warning"} />
                </li>
              ))}
            </ul>
          </Card>
        )}
      </Async>
    </>
  );
}

const strengthTone = { weak: "info", moderate: "warning", strong: "danger" } as const;
const statusTone = { new: "accent", monitoring: "warning", closed: "neutral" } as const;
export function IndustrySignals() {
  const { t, loc, fmt } = useI18n();
  const { cell } = useSuppress();
  const signals = useAsync((s) => s.analytics.signals());
  return (
    <>
      <PageHeader title={t("nav.signals")} subtitle={t("ind.sigSub")} />
      <AggregateBanner />
      <Async state={signals}>
        {(list) => {
          const cols: Column<Signal>[] = [
            { id: "d", header: t("pharmacy.drug"), primary: true, cell: (s) => <strong>{loc(s.drug.name)}</strong> },
            { id: "t", header: t("ind.term"), cell: (s) => loc(s.term) },
            { id: "s", header: t("ind.strength"), cell: (s) => <Badge tone={strengthTone[s.strength]} icon="activity">{t(`ind.strength.${s.strength}`)}</Badge> },
            { id: "r", header: t("ind.reports"), cell: (s) => <Ltr>{cell(s.reports)}</Ltr> },
            { id: "st", header: t("pharmacy.status"), cell: (s) => <Badge tone={statusTone[s.status]}>{t(`ind.status.${s.status}`)}</Badge> },
            { id: "f", header: t("ind.firstSeen"), cell: (s) => t("ind.monthsAgo", { n: fmt.number(s.firstSeenMonthsAgo) }) },
          ];
          return <Table caption={t("nav.signals")} columns={cols} rows={list} rowKey={(s) => s.id} />;
        }}
      </Async>
    </>
  );
}

export function IndustryReports() {
  const { t, loc, fmt } = useI18n();
  const toast = useToast();
  const reports = useAsync((s) => s.analytics.reports());
  return (
    <>
      <PageHeader title={t("nav.reports")} subtitle={t("ind.repSub")} />
      <AggregateBanner />
      <Async state={reports}>
        {(list) => (
          <div className="ms-stack">
            {list.map((r) => (
              <Card key={r.id}>
                <div className="ms-row" style={{ justifyContent: "space-between" }}>
                  <div><strong>{loc(r.title)}</strong><div className="ms-muted">{r.period && <>{loc(r.period)} · </>}{loc(r.kind)} · {t("time.daysAgo", { n: fmt.number(r.updatedDaysAgo) })}</div></div>
                  <Button variant="secondary" size="sm" iconStart="download" onClick={() => toast.show({ message: t("reports.prototype"), tone: "info" })}>{t("reports.export")}</Button>
                </div>
              </Card>
            ))}
          </div>
        )}
      </Async>
    </>
  );
}
