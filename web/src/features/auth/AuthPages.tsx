import { useEffect, useState } from "react";
import { Link, Navigate, useLocation, useNavigate } from "react-router-dom";
import { Logo } from "../../components/brand/Logo";
import { AlertCard, Badge, Button, Card, EmptyState, Icon, IconButton, LoadingState } from "../../components/ui";
import { canEnter, homeFor } from "../../auth/areas";
import { useAuth } from "../../auth/AuthContext";
import type { DemoAccount, SignInError } from "../../auth/types";
import { useI18n } from "../../i18n/I18nProvider";
import { useTheme } from "../../theme/ThemeProvider";
import "../../layouts/shell.css";
import "./auth.css";

const errorKey: Record<SignInError, string> = {
  invalid: "auth.errInvalid", disabled: "auth.errDisabled", noRole: "auth.errNoRole", throttled: "auth.errThrottled", network: "auth.errNetwork",
};

/** Sign-in screen. In this phase it lists FICTIONAL demo accounts (no passwords); a real identity provider replaces it later. */
export function LoginPage() {
  const { t, locale, setLocale } = useI18n();
  const { mode, setMode } = useTheme();
  const { status, user, reason, backend, signIn } = useAuth();
  const nav = useNavigate();
  const loc = useLocation();
  const next = (loc.state as { next?: string } | null)?.next;
  const [accounts, setAccounts] = useState<DemoAccount[] | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<SignInError | null>(null);

  useEffect(() => {
    let live = true;
    void backend.demoAccounts().then((a) => { if (live) setAccounts(a); }, () => { if (live) setAccounts([]); });
    return () => { live = false; };
  }, [backend]);

  if (status === "authenticated" && user) return <Navigate to={next && appAllows(next, user) ? next : homeFor(user)} replace />;

  async function choose(a: DemoAccount) {
    setBusy(a.id);
    setError(null);
    const r = await signIn(a.id);
    setBusy(null);
    if (!r.ok) setError(r.error);
    else nav(next && appAllows(next, r.user) ? next : homeFor(r.user), { replace: true });
  }

  const display = (a: DemoAccount) => a.displayName[locale] ?? a.displayName.en;

  return (
    <div className="auth-page">
      <a className="skip-link" href="#main">{t("a11y.skip")}</a>
      <div className="demo-banner" role="note"><Icon name="alertTriangle" size="xs" /><span>{t("demo.banner")}</span></div>
      <header className="auth-head">
        <Link to="/" aria-label={t("app.name")}><Logo /></Link>
        <div className="auth-head__actions">
          <IconButton icon={mode === "dark" ? "sun" : "moon"} label={mode === "dark" ? t("theme.light") : t("theme.dark")} onClick={() => setMode(mode === "dark" ? "light" : "dark")} />
          <IconButton icon="globe" label={locale === "fa" ? t("lang.switchToEn") : t("lang.switchToFa")} onClick={() => setLocale(locale === "fa" ? "en" : "fa")} />
        </div>
      </header>
      <main id="main" tabIndex={-1} className="auth-main">
        <h1>{t("auth.title")}</h1>
        <p className="ms-muted">{t("auth.subtitle")}</p>

        {reason === "expired" && <AlertCard tone="warning" title={t("auth.expired.title")} role="alert">{t("auth.expired.body")}</AlertCard>}
        {reason === "disabled" && <AlertCard tone="danger" title={t("auth.disabled.title")} role="alert">{t("auth.disabled.body")}</AlertCard>}
        {reason === "signedOut" && <AlertCard tone="success" title={t("auth.signedOut")} role="status" />}
        {error && error !== "disabled" && <AlertCard tone="danger" title={t(errorKey[error])} role="alert" />}

        <AlertCard tone="info" icon="shield" title={t("auth.demoEnv")}>{t("auth.demoHow")}</AlertCard>

        <section aria-labelledby="accounts-title" className="auth-accounts">
          <h2 id="accounts-title">{t("auth.chooseAccount")}</h2>
          {accounts === null ? <LoadingState label={t("common.loading")} rows={2} /> : accounts.length === 0 ? (
            <EmptyState icon="shield" title={t("auth.errNetwork")} />
          ) : (
            <ul className="acct-grid">
              {accounts.map((a) => (
                <li key={a.id}>
                  <button type="button" className="acct-card" data-primary={a.primary} disabled={busy !== null} onClick={() => void choose(a)} aria-label={t("auth.signInAs", { name: display(a) })}>
                    <span className="acct-card__name">{busy === a.id ? t("auth.signingIn") : display(a)}</span>
                    <span className="acct-card__desc">{a.description[locale] ?? a.description.en}</span>
                    <span className="acct-card__roles">
                      {a.roles.map((r) => <Badge key={r} tone="primary">{t(`authRole.${r}`)}</Badge>)}
                      {a.disabled && <Badge tone="danger">{t("auth.disabled.title")}</Badge>}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
      </main>
    </div>
  );
}

function appAllows(path: string, user: NonNullable<ReturnType<typeof useAuth>["user"]>): boolean {
  const m = /^\/app\/(patient|physician|pharmacist|pharmacy|industry|admin|workspace)(\/|$)/.exec(path);
  if (!m) return path.startsWith("/app/account");
  return canEnter(user, m[1] as Parameters<typeof canEnter>[1]);
}

/** Neutral "no access" page. Does not say which permission is missing (no information leak). */
export function UnauthorizedPage() {
  const { t } = useI18n();
  const { user } = useAuth();
  const nav = useNavigate();
  return (
    <div style={{ padding: "var(--space-12) var(--space-4)" }} data-testid="unauthorized">
      <EmptyState icon="shield" title={t("auth.unauthorized.title")} body={t("auth.unauthorized.body")} action={<Button onClick={() => nav(homeFor(user))}>{t("auth.unauthorized.home")}</Button>} />
    </div>
  );
}

export function NoPatientAccess() {
  const { t } = useI18n();
  return <Card><EmptyState icon="shield" title={t("auth.noPatientAccess.title")} body={t("auth.noPatientAccess.body")} /></Card>;
}
