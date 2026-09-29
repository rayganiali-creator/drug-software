import { useState } from "react";
import { Link } from "react-router-dom";
import { fetchReadiness, type HealthReport } from "../../api";
import { Demo } from "../../components/health/cards";
import { Avatar, Badge, Button, Card, Icon, List, ListItem, SegmentedControl, Switch } from "../../components/ui";
import { useI18n, type Locale } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAsync } from "../../services/ServicesProvider";
import { useTheme, type ThemeMode } from "../../theme/ThemeProvider";
import "../features.css";

function ApiStatus() {
  const { t } = useI18n();
  const [state, setState] = useState<{ kind: "idle" } | { kind: "loading" } | { kind: "error" } | { kind: "ok"; report: HealthReport }>({ kind: "idle" });
  const check = () => {
    setState({ kind: "loading" });
    fetchReadiness().then((report) => setState({ kind: "ok", report }), () => setState({ kind: "error" }));
  };
  return (
    <Card title={t("profile.devTitle")} subtitle={t("profile.devSub")}>
      <div className="ms-stack" style={{ gap: "var(--space-3)" }}>
        <Button variant="secondary" size="sm" loading={state.kind === "loading"} onClick={check} iconStart="refresh">{t("profile.checkApi")}</Button>
        {state.kind === "error" && <p role="alert" className="ms-muted">{t("profile.apiDown")}</p>}
        {state.kind === "ok" && (
          <ul aria-label={t("profile.devTitle")} className="ms-row">
            <li><Badge tone={state.report.status === "Healthy" ? "success" : "warning"}>{state.report.status}</Badge></li>
            {Object.entries(state.report.checks).map(([k, v]) => <li key={k}><Badge tone={v === "Healthy" ? "success" : "danger"}>{k}: {v}</Badge></li>)}
          </ul>
        )}
      </div>
    </Card>
  );
}

export function Profile() {
  const { t, loc, locale, setLocale } = useI18n();
  const { mode, setMode } = useTheme();
  const patient = useAsync((s) => s.patients.currentPatient());
  const [prefs, setPrefs] = useState({ doseReminders: true, checkinReminders: true, pharmacistMessages: true });
  return (
    <>
      <PageHeader title={t("nav.profile")} />
      <div className="split">
        <div className="ms-stack">
          {patient.status === "ready" && (
            <Card>
              <div className="ms-row" style={{ gap: "var(--space-4)" }}>
                <Avatar name={loc(patient.data.name)} size="lg" />
                <div style={{ flex: 1 }}>
                  <h2 style={{ fontSize: "var(--text-h3-size)" }}>{loc(patient.data.name)}</h2>
                  <p className="ms-muted">{t("patient.ageSex", { age: patient.data.age, sex: t(`sex.${patient.data.sex}`) })}</p>
                </div>
                <Demo />
              </div>
            </Card>
          )}
          <Card title={t("profile.appearance")}>
            <div className="ms-stack">
              <div className="ms-row" style={{ justifyContent: "space-between" }}>
                <span>{t("profile.language")}</span>
                <SegmentedControl label={t("profile.language")} value={locale} onValueChange={(v) => setLocale(v as Locale)} options={[{ value: "fa", label: "فارسی" }, { value: "en", label: "English" }]} />
              </div>
              <div className="ms-row" style={{ justifyContent: "space-between" }}>
                <span>{t("theme.label")}</span>
                <SegmentedControl label={t("theme.label")} value={mode} onValueChange={(v) => setMode(v as ThemeMode)} options={[{ value: "system", label: t("theme.system") }, { value: "light", label: t("theme.light") }, { value: "dark", label: t("theme.dark") }]} />
              </div>
            </div>
          </Card>
          <Card title={t("profile.notifications")}>
            <div className="ms-stack" style={{ gap: 0 }}>
              <Switch label={t("profile.doseReminders")} checked={prefs.doseReminders} onCheckedChange={(v) => setPrefs({ ...prefs, doseReminders: v })} />
              <Switch label={t("profile.checkinReminders")} checked={prefs.checkinReminders} onCheckedChange={(v) => setPrefs({ ...prefs, checkinReminders: v })} />
              <Switch label={t("profile.pharmacistMessages")} checked={prefs.pharmacistMessages} onCheckedChange={(v) => setPrefs({ ...prefs, pharmacistMessages: v })} />
            </div>
          </Card>
        </div>
        <div className="ms-stack">
          <Card title={t("profile.privacy")} subtitle={t("profile.privacySub")} flush>
            <List label={t("profile.privacy")}>
              <ListItem title={t("profile.viewer1")} meta={t("profile.viewer1Meta")} leading={<Icon name="eye" />} />
              <ListItem title={t("profile.viewer2")} meta={t("profile.viewer2Meta")} leading={<Icon name="eye" />} />
              <ListItem title={t("profile.consents")} meta={t("profile.consentsMeta")} leading={<Icon name="lock" />} trailing={<Badge tone="success" icon="check">{t("profile.active")}</Badge>} />
            </List>
          </Card>
          <Card title={t("profile.about")}>
            <p className="ms-muted">{t("demo.footnote")}</p>
            <div className="ms-row" style={{ marginBlockStart: "var(--space-3)" }}>
              <Link className="ms-btn ms-btn--secondary ms-btn--sm" to="/design-system"><Icon name="grid" size="sm" />{t("nav.designSystem")}</Link>
              <Link className="ms-btn ms-btn--ghost ms-btn--sm" to="/">{t("nav.landing")}</Link>
            </div>
          </Card>
          <ApiStatus />
        </div>
      </div>
    </>
  );
}
