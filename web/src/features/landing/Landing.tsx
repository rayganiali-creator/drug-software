import { Link, useNavigate } from "react-router-dom";
import { Logo } from "../../components/brand/Logo";
import { ConfidenceMeter, SourceChips } from "../../components/health/cards";
import { Button, Card, ChatBubble, Chip, Dropdown, Icon, IconButton, ProgressRing, type IconName, type Tone } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { useTheme } from "../../theme/ThemeProvider";
import type { KnowledgeSource } from "../../services/types";
import "../features.css";
import "./landing.css";

const demoSource: KnowledgeSource = { id: "ks-3", title: { en: "Demo Guide: Missed Doses", fa: "راهنمای نمایشی: دوز فراموش‌شده" }, type: { en: "Guideline", fa: "راهنما" }, version: "demo-0.2", demo: true };

const roleCards: { key: string; icon: IconName; tone: Tone; to: string }[] = [
  { key: "patient", icon: "heart", tone: "primary", to: "/app/patient" },
  { key: "pharmacist", icon: "pill", tone: "accent", to: "/app/pharmacist" },
  { key: "physician", icon: "stethoscope", tone: "info", to: "/app/physician" },
  { key: "pharmacy", icon: "store", tone: "warning", to: "/app/pharmacy" },
];

export function Landing() {
  const { t, locale, setLocale } = useI18n();
  const { mode, setMode } = useTheme();
  const nav = useNavigate();
  const problems: [IconName, string][] = [["clock", "problem1"], ["alertTriangle", "problem2"], ["users", "problem3"]];
  const pillars: [IconName, Tone, string][] = [["shield", "primary", "sol1"], ["sparkles", "accent", "sol2"], ["users", "info", "sol3"]];
  return (
    <div className="lp">
      <a className="skip-link" href="#content">{t("a11y.skip")}</a>
      <header className="lp-head">
        <div className="lp-wrap lp-head__in">
          <Link to="/" aria-label={t("app.name")}><Logo /></Link>
          <nav className="lp-nav" aria-label={t("nav.primary")}>
            <a href="#how">{t("lp.navHow")}</a><a href="#roles">{t("lp.navRoles")}</a><a href="#ai">{t("lp.navAi")}</a><a href="#safety">{t("lp.navSafety")}</a><a href="#privacy">{t("lp.navPrivacy")}</a>
          </nav>
          <div className="lp-head__actions">
            <Dropdown label={t("theme.label")} align="end" triggerIcon={mode === "dark" ? "moon" : "sun"} items={(["system", "light", "dark"] as const).map((m) => ({ id: m, label: t(`theme.${m}`), checked: mode === m, onSelect: () => setMode(m) }))} />
            <IconButton icon="globe" label={locale === "fa" ? t("lang.switchToEn") : t("lang.switchToFa")} onClick={() => setLocale(locale === "fa" ? "en" : "fa")} />
            <Button size="sm" onClick={() => nav("/app/patient")} iconEnd="arrowRight">{t("lp.openDemo")}</Button>
          </div>
        </div>
      </header>

      <main id="content">
        <section className="lp-hero" aria-labelledby="hero-title">
          <div className="lp-wrap lp-hero__grid">
            <div>
              <div className="lp-eyebrow"><Icon name="sparkles" size="sm" />{t("lp.eyebrow")}</div>
              <h1 id="hero-title">{t("lp.heroA")} <em>{t("lp.heroB")}</em></h1>
              <p className="lp-lead">{t("lp.heroLead")}</p>
              <div className="lp-cta">
                <Button size="lg" onClick={() => nav("/app/patient")} iconEnd="arrowRight">{t("lp.ctaPatient")}</Button>
                <Button size="lg" variant="secondary" onClick={() => nav("/app/physician")}>{t("lp.ctaPro")}</Button>
              </div>
              <div className="lp-proto"><Icon name="alertTriangle" size="xs" />{t("lp.proto")}</div>
            </div>
            <div aria-hidden="true">
              <div className="lp-device">
                <div className="lp-device__screen">
                  <div className="ms-row" style={{ justifyContent: "space-between" }}>
                    <div><div className="hc-sub">{t("home.goodMorning", { name: t("lp.sampleName") })}</div><strong>{t("med.nextDose")}</strong></div>
                    <ProgressRing value={86} size={64} stroke={7} label={t("adherence.title")}><strong>{locale === "fa" ? "۸۶٪" : "86%"}</strong></ProgressRing>
                  </div>
                  <Card variant="tonal" tone="primary"><div className="ms-row"><span className="hc-tile" data-tone="primary"><Icon name="pill" /></span><div><strong>{locale === "fa" ? "دموپریل" : "Demopril"} <bdi dir="ltr">10 mg</bdi></strong><div className="hc-sub" style={{ color: "inherit" }}>{locale === "fa" ? "۰۸:۰۰" : "08:00"}</div></div></div></Card>
                  <ChatBubble role="assistant">{t("lp.sampleAnswer")}</ChatBubble>
                </div>
              </div>
            </div>
          </div>
        </section>

        <section className="lp-section lp-section--alt" aria-labelledby="problem-title">
          <div className="lp-wrap">
            <div className="lp-eyebrow">{t("lp.problemEyebrow")}</div>
            <h2 className="lp-h2" id="problem-title">{t("lp.problemTitle")}</h2>
            <div className="lp-cards">
              {problems.map(([icon, k]) => (
                <Card key={k}><span className="lp-icon" data-tone="danger"><Icon name={icon} /></span><h3>{t(`lp.${k}Title`)}</h3><p className="ms-muted">{t(`lp.${k}Body`)}</p></Card>
              ))}
            </div>
            <p className="ms-muted" style={{ marginBlockStart: "var(--space-4)" }}>{t("lp.problemNote")}</p>
          </div>
        </section>

        <section className="lp-section" aria-labelledby="solution-title">
          <div className="lp-wrap">
            <div className="lp-eyebrow">{t("lp.solutionEyebrow")}</div>
            <h2 className="lp-h2" id="solution-title">{t("lp.solutionTitle")}</h2>
            <div className="lp-cards">
              {pillars.map(([icon, tone, k]) => (
                <Card key={k}><span className="lp-icon" data-tone={tone}><Icon name={icon} /></span><h3>{t(`lp.${k}Title`)}</h3><p className="ms-muted">{t(`lp.${k}Body`)}</p></Card>
              ))}
            </div>
          </div>
        </section>

        <section className="lp-section lp-section--alt" id="how" aria-labelledby="how-title">
          <div className="lp-wrap">
            <div className="lp-eyebrow">{t("lp.howEyebrow")}</div>
            <h2 className="lp-h2" id="how-title">{t("lp.howTitle")}</h2>
            <ol className="lp-steps">{[1, 2, 3, 4].map((n) => <li key={n}><h3>{t(`lp.step${n}Title`)}</h3><p className="ms-muted">{t(`lp.step${n}Body`)}</p></li>)}</ol>
          </div>
        </section>

        <section className="lp-section" id="roles" aria-labelledby="roles-title">
          <div className="lp-wrap">
            <div className="lp-eyebrow">{t("lp.rolesEyebrow")}</div>
            <h2 className="lp-h2" id="roles-title">{t("lp.rolesTitle")}</h2>
            <div className="lp-cards">
              {roleCards.map((r) => (
                <Card key={r.key}>
                  <span className="lp-icon" data-tone={r.tone}><Icon name={r.icon} /></span>
                  <h3>{t(`lp.role.${r.key}`)}</h3>
                  <ul className="lp-bullets">{[1, 2, 3].map((n) => <li key={n}><Icon name="check" size="sm" /><span>{t(`lp.role.${r.key}.${n}`)}</span></li>)}</ul>
                  <Button variant="secondary" size="sm" onClick={() => nav(r.to)} iconEnd="arrowRight">{t("lp.seeDemo")}</Button>
                </Card>
              ))}
            </div>
          </div>
        </section>

        <section className="lp-section lp-section--alt" id="ai" aria-labelledby="ai-title">
          <div className="lp-wrap lp-ai">
            <div>
              <div className="lp-eyebrow"><Icon name="sparkles" size="sm" />{t("lp.aiEyebrow")}</div>
              <h2 className="lp-h2" id="ai-title">{t("lp.aiTitle")}</h2>
              <p className="lp-lead">{t("lp.aiLead")}</p>
              <ul className="lp-bullets">{[1, 2, 3, 4].map((n) => <li key={n}><Icon name="check" size="sm" /><span>{t(`lp.ai${n}`)}</span></li>)}</ul>
            </div>
            <Card variant="ai" aria-label={t("lp.aiPreview")}>
              <div className="ms-stack" style={{ gap: "var(--space-3)" }}>
                <ChatBubble role="user">{t("lp.sampleQuestion")}</ChatBubble>
                <ChatBubble role="assistant">{t("lp.sampleAnswer")}</ChatBubble>
                <ConfidenceMeter level="high" />
                <SourceChips sources={[demoSource]} />
                <div className="safety-note"><Icon name="shield" size="sm" /><span>{t("ai.safetyNotice")}</span></div>
                <div className="ms-row"><Chip icon="users" variant="ai">{t("ai.escalate")}</Chip></div>
              </div>
            </Card>
          </div>
        </section>

        <section className="lp-section" id="safety" aria-labelledby="safety-title">
          <div className="lp-wrap">
            <div className="lp-eyebrow"><Icon name="shield" size="sm" />{t("lp.safetyEyebrow")}</div>
            <h2 className="lp-h2" id="safety-title">{t("lp.safetyTitle")}</h2>
            <div className="lp-cards">
              {[1, 2, 3].map((n) => <Card key={n}><span className="lp-icon" data-tone="success"><Icon name={n === 1 ? "flask" : n === 2 ? "users" : "bookOpen"} /></span><h3>{t(`lp.safe${n}Title`)}</h3><p className="ms-muted">{t(`lp.safe${n}Body`)}</p></Card>)}
            </div>
          </div>
        </section>

        <section className="lp-section lp-section--alt" id="privacy" aria-labelledby="privacy-title">
          <div className="lp-wrap">
            <div className="lp-eyebrow"><Icon name="lock" size="sm" />{t("lp.privacyEyebrow")}</div>
            <h2 className="lp-h2" id="privacy-title">{t("lp.privacyTitle")}</h2>
            <div className="lp-cards">
              {[1, 2, 3].map((n) => <Card key={n}><span className="lp-icon" data-tone="info"><Icon name={n === 1 ? "eye" : n === 2 ? "lock" : "chartBar"} /></span><h3>{t(`lp.priv${n}Title`)}</h3><p className="ms-muted">{t(`lp.priv${n}Body`)}</p></Card>)}
            </div>
          </div>
        </section>

        <section className="lp-section" aria-labelledby="future-title">
          <div className="lp-wrap">
            <div className="lp-eyebrow">{t("lp.futureEyebrow")}</div>
            <h2 className="lp-h2" id="future-title">{t("lp.futureTitle")}</h2>
            <p className="lp-lead">{t("lp.futureLead")}</p>
            <div className="lp-integrations">{["rx", "pharmacy", "insurance", "drugdb", "identity"].map((k) => <Chip key={k} icon="clock">{t(`lp.int.${k}`)}</Chip>)}</div>
          </div>
        </section>

        <section className="lp-section" aria-labelledby="cta-title" style={{ paddingBlockStart: 0 }}>
          <div className="lp-wrap">
            <div className="lp-cta-band">
              <h2 id="cta-title">{t("lp.ctaTitle")}</h2>
              <p>{t("lp.ctaBody")}</p>
              <Button size="lg" onClick={() => nav("/app/patient")} iconEnd="arrowRight">{t("lp.openDemo")}</Button>
            </div>
          </div>
        </section>
      </main>

      <footer className="lp-foot">
        <div className="lp-wrap ms-row" style={{ justifyContent: "space-between" }}>
          <Logo />
          <span>{t("demo.footnote")}</span>
          <Link to="/design-system">{t("nav.designSystem")}</Link>
        </div>
      </footer>
    </div>
  );
}
