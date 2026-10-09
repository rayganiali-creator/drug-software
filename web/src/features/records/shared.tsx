import { useState, type FormEvent, type ReactNode } from "react";
import { AlertCard, Badge, Button, EmptyState, ErrorState, LoadingState } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { RecordsError, type Category } from "../../records/types";
import type { Load } from "../../records/hooks";
import "../features.css";
import "./records.css";

/** Local calendar day as yyyy-mm-dd (the doses of "today" are planned in the patient's own day). */
export const dayKey = (d = new Date()) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

export function useTry() {
  const { t } = useI18n();
  return (key: string, fallback: string) => { const v = t(key); return v === key ? fallback : v; };
}

/** What went wrong, in words a person can act on. Codes from the server are machine codes and are only shown through a translation. */
export function errorText(t: (k: string) => string, e: RecordsError): string {
  for (const code of [e.code, ...e.errors]) {
    if (!code) continue;
    const key = `rec.err.${code.replace(/[.]/g, "_")}`;
    if (t(key) !== key) return t(key);
  }
  const byKind: Record<string, string> = { conflict: "rec.err.conflict", invalid: "rec.err.invalid", unauthorized: "rec.err.unauthorized", network: "rec.err.network", rateLimited: "rec.err.rate", notFound: "rec.err.notFound", notAvailable: "rec.err.notAvailable" };
  return t(byKind[e.kind] ?? "rec.err.server");
}

export function ActionError({ error }: { error: RecordsError | null }) {
  const { t } = useI18n();
  if (!error) return null;
  return <div role="alert"><AlertCard tone="danger" title={t("rec.err.title")}>{errorText(t, error)}</AlertCard></div>;
}

export function ErrorPanel({ error, onRetry }: { error: unknown; onRetry(): void }) {
  const { t } = useI18n();
  const kind = error instanceof RecordsError ? error.kind : "server";
  if (kind === "notConnected") return <EmptyState icon="lock" title={t("rec.notConnected.title")} body={t("rec.notConnected.body")} />;
  if (kind === "unauthorized") return <EmptyState icon="shield" title={t("rec.unauthorized.title")} body={t("rec.unauthorized.body")} />;
  if (kind === "rateLimited") return <EmptyState icon="clock" title={t("kn.rate.title")} body={t("kn.rate.body")} action={<Button variant="secondary" onClick={onRetry}>{t("common.retry")}</Button>} />;
  return <ErrorState title={t("rec.error.title")} body={t("rec.error.body")} retryLabel={t("common.retry")} onRetry={onRetry} />;
}

/** Loading / error / ready in one place so every page shows the same states. */
export function Loaded<T>({ load, reload, children }: { load: Load<T>; reload(): void; children(data: T): ReactNode }) {
  const { t } = useI18n();
  if (load.status === "loading") return <LoadingState label={t("common.loading")} rows={3} />;
  if (load.status === "error") return <ErrorPanel error={load.error} onRetry={reload} />;
  return <>{children(load.data)}</>;
}

export function DemoFlag({ show, notice }: { show: boolean; notice?: string | null }) {
  const { t } = useI18n();
  return show ? <Badge tone="warning" icon="alertTriangle" label={notice ?? undefined}>{t("demo.badge")}</Badge> : null;
}

/** Small "last updated" line; stale data says so instead of looking current. */
export function FreshLine({ at, stale, never }: { at: string | null; stale?: boolean; never?: boolean }) {
  const { t, fmt } = useI18n();
  if (never || !at) return <Badge tone="neutral" icon="clock">{t("rec.fresh.never")}</Badge>;
  return (
    <span className="rec-fresh">
      <span className="ms-muted">{t("rec.fresh.updated", { date: fmt.date(new Date(at), "short") })}</span>
      {stale && <Badge tone="warning" icon="alertTriangle">{t("rec.fresh.stale")}</Badge>}
    </span>
  );
}

export const categoryKey = (c: Category) => `rec.cat.${c}`;

/** A form that disables itself while saving and shows the server's answer. */
export function Form({ onSubmit, busy, submitLabel, children, secondary }: { onSubmit(): void; busy: boolean; submitLabel: string; children: ReactNode; secondary?: ReactNode }) {
  return (
    <form className="rec-form" onSubmit={(e: FormEvent) => { e.preventDefault(); onSubmit(); }}>
      {children}
      <div className="rec-form__actions">
        <Button type="submit" loading={busy}>{submitLabel}</Button>
        {secondary}
      </div>
    </form>
  );
}

export function useToggle(initial = false): [boolean, () => void, (v: boolean) => void] {
  const [v, set] = useState(initial);
  return [v, () => set((x) => !x), set];
}

export const toNumber = (s: string): number | null => { const n = Number(s.replace(",", ".")); return s.trim() === "" || Number.isNaN(n) ? null : n; };
export const orNull = (s: string): string | null => (s.trim() === "" ? null : s.trim());
