import { Link } from "react-router-dom";
import { AlertCard } from "../components/ui";
import { useI18n } from "../i18n/I18nProvider";

export type RealPage = { to: string; labelKey: string };

/**
 * Shown on every page that is still backed by the in-browser sample data (no server call, nothing saved). It says so in plain words and, where a stored-data page exists
 * for the same job, points to it. The global demo banner says the data is fictional; this says the page is not connected.
 */
export function SampleDataNotice({ real }: { real?: RealPage }) {
  const { t } = useI18n();
  return (
    <div role="note" data-testid="sample-data-notice" style={{ marginBlockEnd: "var(--space-4)" }}>
      <AlertCard tone="warning" title={t("sample.title")}>
        {t("sample.body")}
        {real ? <> <Link className="ms-link" to={real.to}>{t(real.labelKey)}</Link></> : null}
      </AlertCard>
    </div>
  );
}
