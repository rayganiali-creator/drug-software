import { useEffect, useRef, type ReactNode } from "react";
import { useI18n } from "../i18n/I18nProvider";

/** Page title (h1). Receives focus on route change so screen readers announce the new page. */
export function PageHeader({ title, subtitle, actions, visuallyHidden }: { title: string; subtitle?: ReactNode; actions?: ReactNode; visuallyHidden?: boolean }) {
  const { t } = useI18n();
  const ref = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    document.title = `${title} · ${t("app.name")}`;
    ref.current?.focus({ preventScroll: true });
  }, [title, t]);
  return (
    <header className={visuallyHidden ? "sr-only" : "pagehead"}>
      <div>
        <h1 ref={ref} tabIndex={-1} className="pagehead__title">{title}</h1>
        {subtitle && <p className="pagehead__sub">{subtitle}</p>}
      </div>
      {actions && <div className="pagehead__actions">{actions}</div>}
    </header>
  );
}
