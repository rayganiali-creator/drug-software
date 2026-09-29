import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { AlertCard, Button, Card, Chip, EmptyState, ErrorState, Icon, LoadingState, Progress, TextArea, Timeline, useToast, type IconName } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAsync, useServices } from "../../services/ServicesProvider";
import "../features.css";

const moodIcon: IconName[] = ["moodBad", "moodLow", "moodOk", "moodGood", "moodGreat"];

export function CheckIn() {
  const { t, loc, fmt } = useI18n();
  const services = useServices();
  const nav = useNavigate();
  const toast = useToast();
  const cfg = useAsync((s) => s.patients.checkInConfig());
  const done = useAsync((s) => s.patients.hasCheckedInToday());
  const [step, setStep] = useState(0);
  const [mood, setMood] = useState<number | null>(null);
  const [symptoms, setSymptoms] = useState<string[]>([]);
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ urgent: boolean } | null>(null);

  if (cfg.status === "loading" || done.status === "loading") return <><PageHeader title={t("nav.checkin")} /><LoadingState label={t("common.loading")} rows={2} /></>;
  if (cfg.status === "error" || done.status === "error") return <ErrorState title={t("common.errorTitle")} retryLabel={t("common.retry")} onRetry={() => { cfg.reload(); done.reload(); }} />;
  if (cfg.status !== "ready" || done.status !== "ready") return null;

  const submit = async () => {
    setBusy(true);
    try {
      const r = await services.patients.submitCheckIn({ mood: mood ?? 3, symptomIds: symptoms, note });
      setResult(r);
      toast.show({ message: t("checkin.saved"), tone: "success" });
    } finally { setBusy(false); }
  };

  if (result || done.data) {
    const history = cfg.data.history.map((h) => ({
      id: String(h.daysAgo),
      title: <>{loc(cfg.data.moods.find((m) => m.value === h.mood)!.label)}</>,
      meta: <>{t("time.daysAgo", { n: fmt.number(h.daysAgo) })}{h.symptoms.length > 0 && <> · {h.symptoms.map((id) => loc(cfg.data.symptoms.find((s) => s.id === id)!.label)).join("، ")}</>}</>,
      icon: moodIcon[h.mood - 1]!, tone: "primary" as const,
    }));
    return (
      <>
        <PageHeader title={t("nav.checkin")} />
        <div className="split">
          <div className="ms-stack">
            <Card variant="tonal" tone="success">
              <EmptyState icon="checkCircle" title={t("checkin.doneTitle")} body={t("checkin.doneBody")} action={<Button onClick={() => nav("/app/patient")}>{t("checkin.backHome")}</Button>} />
            </Card>
            {result?.urgent && (
              <AlertCard tone="danger" title={t("checkin.urgentTitle")} role="alert" actions={<Button variant="danger" iconStart="phone">{t("checkin.urgentAction")}</Button>}>{t("checkin.urgentBody")}</AlertCard>
            )}
          </div>
          <Card title={t("checkin.history")}><Timeline entries={history} label={t("checkin.history")} /></Card>
        </div>
      </>
    );
  }

  const steps = [t("checkin.stepMood"), t("checkin.stepSymptoms"), t("checkin.stepNote")];
  const urgentSelected = symptoms.some((id) => cfg.data.symptoms.find((s) => s.id === id)?.urgent);
  const canNext = step === 0 ? mood !== null : true;

  return (
    <>
      <PageHeader title={t("nav.checkin")} subtitle={t("checkin.sub")} />
      <Card>
        <div className="ms-stack">
          <div>
            <div className="ms-row" style={{ justifyContent: "space-between" }}>
              <strong>{steps[step]}</strong>
              <span className="ms-muted">{t("checkin.step", { n: fmt.number(step + 1), total: fmt.number(steps.length) })}</span>
            </div>
            <Progress value={((step + 1) / steps.length) * 100} label={t("checkin.progress")} />
          </div>

          {step === 0 && (
            <div className="moods" role="radiogroup" aria-label={t("checkin.moodQuestion")}>
              {cfg.data.moods.map((m) => (
                <button key={m.value} type="button" role="radio" aria-checked={mood === m.value} className="mood" onClick={() => setMood(m.value)}>
                  <Icon name={moodIcon[m.value - 1]!} size="lg" />{loc(m.label)}
                </button>
              ))}
            </div>
          )}

          {step === 1 && (
            <div className="ms-stack" style={{ gap: "var(--space-3)" }}>
              <p>{t("checkin.symptomQuestion")}</p>
              <div className="ms-row" role="group" aria-label={t("checkin.symptomQuestion")}>
                {cfg.data.symptoms.map((s) => (
                  <Chip key={s.id} role="checkbox" selected={symptoms.includes(s.id)} onClick={() => setSymptoms((cur) => (cur.includes(s.id) ? cur.filter((x) => x !== s.id) : [...cur, s.id]))}>{loc(s.label)}</Chip>
                ))}
              </div>
              {urgentSelected && <AlertCard tone="danger" title={t("checkin.urgentTitle")} role="alert">{t("checkin.urgentBody")}</AlertCard>}
            </div>
          )}

          {step === 2 && (
            <div className="ms-stack">
              <TextArea label={t("checkin.noteLabel")} hint={t("checkin.noteHint")} value={note} onChange={(e) => setNote(e.target.value)} rows={4} />
              <Card variant="tonal" tone="neutral" title={t("checkin.summary")}>
                <p>{t("checkin.stepMood")}: <strong>{loc(cfg.data.moods.find((m) => m.value === mood)?.label ?? { en: "-", fa: "-" })}</strong></p>
                <p>{t("checkin.stepSymptoms")}: <strong>{symptoms.length ? symptoms.map((id) => loc(cfg.data.symptoms.find((s) => s.id === id)!.label)).join("، ") : t("checkin.none")}</strong></p>
              </Card>
              <p className="ms-muted">{t("checkin.prototypeNote")}</p>
            </div>
          )}

          <div className="ms-row" style={{ justifyContent: "space-between" }}>
            <Button variant="ghost" onClick={() => setStep((s) => Math.max(0, s - 1))} disabled={step === 0} iconStart="chevronLeft">{t("common.back")}</Button>
            {step < 2 ? <Button onClick={() => setStep((s) => s + 1)} disabled={!canNext} iconEnd="chevronRight">{t("common.next")}</Button> : <Button loading={busy} onClick={submit} iconStart="check">{t("checkin.submit")}</Button>}
          </div>
        </div>
      </Card>
    </>
  );
}
