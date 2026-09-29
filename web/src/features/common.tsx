import type { ReactNode } from "react";
import { Avatar, ErrorState, LoadingState } from "../components/ui";
import { useI18n } from "../i18n/I18nProvider";
import type { AsyncState } from "../services/ServicesProvider";
import type { Patient } from "../services/types";

/** Renders the right loading / error / ready UI for a service call. */
export function Async<T>({ state, children, rows }: { state: AsyncState<T> & { reload(): void }; children(data: T): ReactNode; rows?: number }) {
  const { t } = useI18n();
  if (state.status === "loading") return <LoadingState label={t("common.loading")} rows={rows} />;
  if (state.status === "error") return <ErrorState title={t("common.errorTitle")} body={t("common.errorBody")} retryLabel={t("common.retry")} onRetry={state.reload} />;
  return <>{children(state.data)}</>;
}

export function PersonCell({ patient }: { patient: Patient }) {
  const { loc } = useI18n();
  return (
    <span className="ms-row" style={{ gap: "var(--space-2)", flexWrap: "nowrap" }}>
      <Avatar name={loc(patient.name)} size="sm" tone={patient.risk === "high" ? "danger" : patient.risk === "medium" ? "warning" : "success"} />
      <span>{loc(patient.name)}</span>
    </span>
  );
}

/** Last N calendar months, oldest first, for chart axes. */
export function lastMonths(n: number, monthShort: (d: Date) => string): string[] {
  const now = new Date();
  return Array.from({ length: n }, (_, i) => monthShort(new Date(now.getFullYear(), now.getMonth() - (n - 1 - i), 15)));
}

export const chartColors = ["var(--color-chart1)", "var(--color-chart2)", "var(--color-chart3)", "var(--color-chart4)", "var(--color-chart5)"];
