import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { AdherenceCard, CheckInCard, Ltr } from "../../components/health/cards";
import { AlertCard, Avatar, Button, Card, Chip, ErrorState, Icon, List, ListItem, LoadingState, Modal, Timeline, useToast, type IconName, type Tone } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { useAsync, useServices } from "../../services/ServicesProvider";
import type { Dose } from "../../services/types";
import { PageHeader } from "../../layouts/PageHeader";
import "../features.css";

function greetingKey(h: number) { return h < 12 ? "home.goodMorning" : h < 18 ? "home.goodAfternoon" : "home.goodEvening"; }
const activityIcon: Record<string, IconName> = { dose: "check", checkin: "checkCircle", ai: "sparkles", review: "clipboard" };

function minutesUntil(time: string, tomorrow: boolean, now: Date) {
  const target = Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5)) + (tomorrow ? 1440 : 0);
  return target - (now.getHours() * 60 + now.getMinutes());
}

export function NextDoseHero({ doses, onTake, onLater }: { doses: Dose[]; onTake(d: Dose): void; onLater(): void }) {
  const { t, loc, fmt } = useI18n();
  const next = doses.find((d) => d.isNext);
  if (!next) {
    return (
      <Card className="nextdose nextdose--done">
        <div className="nextdose__label"><Icon name="checkCircle" size="sm" />{t("home.allDone")}</div>
        <div className="nextdose__name">{t("home.allDoneBody")}</div>
      </Card>
    );
  }
  const mins = minutesUntil(next.time, next.tomorrow, new Date());
  const count = mins <= 0 ? t("home.dueNow") : mins < 60 ? t("home.inMinutes", { n: fmt.number(mins) }) : t("home.inHours", { h: fmt.number(Math.floor(mins / 60)), m: fmt.number(mins % 60) });
  return (
    <Card className="nextdose" aria-label={t("med.nextDose")}>
      <div className="nextdose__label"><Icon name="clock" size="sm" />{t("med.nextDose")}{next.tomorrow && <> · {t("home.tomorrow")}</>}</div>
      <div className="nextdose__name">{loc(next.drug.name)} <Ltr>{next.drug.strength}</Ltr></div>
      <div className="nextdose__meta">{loc(next.drug.form)} · {loc(next.drug.instructions)}</div>
      <div className="nextdose__time" aria-label={fmt.time(next.time)}>{fmt.time(next.time)}</div>
      <div className="nextdose__count">{count}</div>
      <div className="nextdose__actions">
        <Button size="lg" iconStart="check" onClick={() => onTake(next)} disabled={next.tomorrow}>{t("home.tookIt")}</Button>
        <Button size="lg" variant="ghost" onClick={onLater}>{t("home.remindLater")}</Button>
      </div>
    </Card>
  );
}

export function PatientHome() {
  const { t, loc, fmt } = useI18n();
  const services = useServices();
  const nav = useNavigate();
  const toast = useToast();
  const patient = useAsync((s) => s.patients.currentPatient());
  const doses = useAsync((s) => s.medications.todaysDoses("pt-sara"));
  const alerts = useAsync((s) => s.notifications.alerts("pt-sara"));
  const activity = useAsync((s) => s.notifications.activity("pt-sara"));
  const quick = useAsync((s) => s.ai.quickQuestions());
  const series = useAsync((s) => s.analytics.adherenceSeries("pt-sara"));
  const checked = useAsync((s) => s.patients.hasCheckedInToday());
  const [allAlerts, setAllAlerts] = useState(false);
  const now = useMemo(() => new Date(), []);

  if ([patient, doses, alerts].some((x) => x.status === "error")) {
    return <ErrorState title={t("common.errorTitle")} body={t("common.errorBody")} retryLabel={t("common.retry")} onRetry={() => { patient.reload(); doses.reload(); alerts.reload(); }} />;
  }
  if (patient.status !== "ready" || doses.status !== "ready") {
    return <><PageHeader title={t("nav.home")} /><LoadingState label={t("common.loading")} rows={3} /></>;
  }

  const first = loc(patient.data.name).split(" ")[0] ?? "";
  const takeDose = async (d: Dose) => {
    const updated = await services.medications.setDose(d.id, "taken");
    doses.setData(updated);
    toast.show({
      message: t("home.doseRecorded", { name: loc(d.drug.name) }), tone: "success",
      action: { label: t("common.undo"), onClick: async () => doses.setData(await services.medications.setDose(d.id, "upcoming")) },
    });
    if ("vibrate" in navigator) navigator.vibrate?.(15);
  };
  const shown = alerts.status === "ready" ? alerts.data.slice(0, 2) : [];
  const timeline = doses.data.filter((d) => !d.tomorrow).map((d) => ({
    id: d.id,
    title: <>{loc(d.drug.name)} <Ltr>{d.drug.strength}</Ltr></>,
    meta: <>{fmt.time(d.time)} · {d.status === "taken" ? t("dose.taken") : d.status === "missed" ? t("dose.missed") : t("dose.upcoming")}
      {d.status === "missed" && <> <Button size="sm" variant="ghost" onClick={() => takeDose(d)}>{t("dose.markTaken")}</Button></>}</>,
    icon: (d.status === "taken" ? "check" : d.status === "missed" ? "alertTriangle" : "clock") as IconName,
    tone: (d.status === "taken" ? "success" : d.status === "missed" ? "warning" : "neutral") as Tone,
    current: d.isNext,
  }));

  return (
    <>
      <PageHeader title={t("nav.home")} visuallyHidden />
      <div className="home-grid">
        <section className="a-greet greeting" aria-label={t("home.greetingLabel")}>
          <div className="ms-row" style={{ gap: "var(--space-4)" }}>
            <Avatar name={loc(patient.data.name)} size="lg" />
            <div>
              <p className="greeting__hello">{t(greetingKey(now.getHours()), { name: first })}</p>
              <p className="greeting__date">{fmt.weekday(now)}، {fmt.date(now)}</p>
            </div>
          </div>
        </section>

        <div className="a-next"><NextDoseHero doses={doses.data} onTake={takeDose} onLater={() => toast.show({ message: t("home.laterToast"), tone: "info" })} /></div>

        {shown.length > 0 && (
          <section className="a-alerts ms-stack" aria-label={t("home.alerts")} style={{ gap: "var(--space-3)" }}>
            {shown.map((a) => (
              <AlertCard key={a.id} tone={a.severity as Tone} title={loc(a.title)} actions={<Button size="sm" variant="secondary" onClick={() => nav("/app/patient/medications")}>{t("home.viewMedications")}</Button>}>{loc(a.body)}</AlertCard>
            ))}
            {alerts.status === "ready" && alerts.data.length > shown.length && <Button variant="ghost" size="sm" onClick={() => setAllAlerts(true)}>{t("home.allAlerts")}</Button>}
          </section>
        )}

        <Card variant="ai" className="a-ai" title={t("home.askTitle")} subtitle={t("home.askBody")} actions={<Icon name="sparkles" />}>
          <div className="quick-chips">
            {quick.status === "ready" && quick.data.slice(0, 3).map((q, i) => <Chip key={i} variant="ai" onClick={() => nav(`/app/patient/assistant?q=${encodeURIComponent(loc(q))}`)}>{loc(q)}</Chip>)}
          </div>
          <Button variant="ai" iconStart="sparkles" onClick={() => nav("/app/patient/assistant")}>{t("home.openAssistant")}</Button>
        </Card>

        <Card className="a-today" title={t("home.today")} subtitle={t("home.todaySub", { n: fmt.number(timeline.length) })}>
          {timeline.length ? <Timeline entries={timeline} label={t("home.today")} /> : <p className="ms-muted">{t("home.noDoses")}</p>}
        </Card>

        <div className="a-checkin"><CheckInCard done={checked.status === "ready" && checked.data} onStart={() => nav("/app/patient/checkin")} /></div>
        <div className="a-adherence"><AdherenceCard value={patient.data.adherence} series={series.status === "ready" ? series.data.slice(-14) : patient.data.adherenceSeries} /></div>

        <Card className="a-activity" title={t("home.recent")} flush>
          {activity.status === "ready" && (
            <List label={t("home.recent")}>
              {activity.data.map((a) => (
                <ListItem key={a.id} title={loc(a.text)} meta={fmt.relativeMinutes(a.minutesAgo)} leading={<span className="hc-tile" data-tone="neutral" style={{ inlineSize: 36, blockSize: 36 }}><Icon name={activityIcon[a.kind] ?? "info"} size="sm" /></span>} />
              ))}
            </List>
          )}
        </Card>
      </div>

      <Modal open={allAlerts} onClose={() => setAllAlerts(false)} title={t("home.alerts")} closeLabel={t("common.close")}>
        <div className="ms-stack">{alerts.status === "ready" && alerts.data.map((a) => <AlertCard key={a.id} tone={a.severity as Tone} title={loc(a.title)}>{loc(a.body)}</AlertCard>)}</div>
      </Modal>
    </>
  );
}
