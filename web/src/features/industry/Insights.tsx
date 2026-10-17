import { useCallback, useEffect, useMemo, useState } from "react";
import { useAuth, useUser } from "../../auth/AuthContext";
import { AlertCard, Badge, Card, EmptyState, Select, Table, type Column } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { IndustryApi, type Basis, type Cell, type Insights } from "../../industry/api";
import { PageHeader } from "../../layouts/PageHeader";
import type { Load } from "../../records/hooks";
import { Loaded } from "../records/shared";
import "../features.css";

function useInsights(org: string | undefined, basis: Basis, period: string | null): { state: Load<Insights>; reload(): void } {
  const { backend } = useAuth();
  const api = useMemo(() => new IndustryApi(backend.apiFetch ? (p, i) => backend.apiFetch!(p, i) : undefined), [backend]);
  const [state, setState] = useState<Load<Insights>>({ status: "loading" });
  const [tick, setTick] = useState(0);
  const reload = useCallback(() => setTick((n) => n + 1), []);
  useEffect(() => {
    if (!org) return;
    const ctl = new AbortController();
    api.insights(org, basis, period, ctl.signal).then(
      (data) => { if (!ctl.signal.aborted) setState({ status: "ready", data }); },
      (error) => { if (!ctl.signal.aborted && !(error instanceof DOMException)) setState({ status: "error", error }); },
    );
    return () => ctl.abort();
  }, [api, org, basis, period, tick]);
  return { state, reload };
}

/** A plain table: a hidden cell shows "fewer than k", never a number, and the order is by name, not by size. */
function CellTable({ caption, cells, label, k }: { caption: string; cells: Cell[]; label(c: Cell): string; k: number }) {
  const { t, fmt } = useI18n();
  if (cells.length === 0) return <Card title={caption}><p className="ms-muted">{t("ind2.noCells")}</p></Card>;
  const cols: Column<Cell>[] = [
    { id: "k", header: t("ind2.group"), primary: true, cell: (c) => label(c) },
    { id: "n", header: t("ind2.reports"), cell: (c) => (c.suppressed || c.reports === null ? <span title={t("ind2.suppressedHint", { k: fmt.number(k) })}>{t("ind2.fewerThan", { k: fmt.number(k) })}</span> : fmt.number(c.reports)) },
  ];
  return <Table caption={caption} columns={cols} rows={cells} rowKey={(c) => c.key} />;
}

export function IndustryInsightsPage() {
  const { t, fmt } = useI18n();
  const org = useUser().organizationIds[0];
  const [basis, setBasis] = useState<Basis>("Real");
  const [period, setPeriod] = useState<string | null>(null); // null: the newest closed quarter, chosen by the server
  const { state, reload } = useInsights(org, basis, period);
  return (
    <>
      <PageHeader title={t("ind2.title")} subtitle={t("ind2.sub")} />
      <div className="ms-stack">
        <div className="banner-agg" role="note"><span>{t("ind2.notice")}</span></div>
        <Card>
          <div className="ms-row" role="search" style={{ gap: "var(--space-3)", flexWrap: "wrap" }}>
            <Select label={t("ind2.basis")} value={basis} onValueChange={(v) => setBasis(v as Basis)} options={[{ value: "Real", label: t("ind2.basis.Real") }, { value: "SimulatedOrDemo", label: t("ind2.basis.SimulatedOrDemo") }]} />
            <Select label={t("ind2.period")} value={period ?? (state.status === "ready" ? state.data.period : "")} onValueChange={(v) => setPeriod(v)}
              options={(state.status === "ready" ? state.data.availablePeriods : []).map((p) => ({ value: p, label: p }))} />
          </div>
          <p className="ms-muted">{t("ind2.windowHint")}</p>
        </Card>
        {!org ? <Card><EmptyState icon="building" title={t("ph.noOrg")} /></Card> : (
          <Loaded load={state} reload={reload}>
            {(r) => !r.linked ? (
              <Card><EmptyState icon="lock" title={t("ind2.notLinked.title")} body={t("ind2.notLinked.body")} /></Card>
            ) : (
              <>
                {r.basis === "SimulatedOrDemo" ? <AlertCard tone="warning" title={t("ind2.simulatedTitle")}>{t("ind2.simulatedBody")}</AlertCard> : null}
                <div className="ms-row" style={{ gap: "var(--space-2)", flexWrap: "wrap" }}>
                  <Badge tone={r.basis === "SimulatedOrDemo" ? "warning" : "info"} icon={r.basis === "SimulatedOrDemo" ? "alertTriangle" : "check"}>{t(`ind2.basis.${r.basis}`)}</Badge>
                  <Badge tone="neutral">{t("ind2.k", { k: fmt.number(r.minCellSize) })}</Badge>
                  <Badge tone="neutral">{t("ind2.effective", { period: r.period, from: r.from, until: r.until })}</Badge>
                </div>
                <p className="ms-muted">{t("ind2.extraHidden")}</p>
                {r.productsTruncated ? <p className="ms-muted">{t("ind2.truncated")}</p> : null}
                <CellTable caption={t("ind2.byProduct")} cells={r.byProduct} k={r.minCellSize} label={(c) => c.key === "unregistered" ? t("ind2.unregistered") : (c.label ?? c.key)} />
                <CellTable caption={t("ind2.byIssue")} cells={r.byIssueType} k={r.minCellSize} label={(c) => t(`ind2.issue.${c.key}`)} />
                <CellTable caption={t("ind2.bySeverity")} cells={r.bySeverity} k={r.minCellSize} label={(c) => t(`ind2.severity.${c.key}`)} />
                <CellTable caption={t("ind2.byMonth")} cells={r.byMonth} k={r.minCellSize} label={(c) => c.key} />
              </>
            )}
          </Loaded>
        )}
      </div>
    </>
  );
}
