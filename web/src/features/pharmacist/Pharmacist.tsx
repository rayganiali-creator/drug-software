import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { ADRCard, AIInsightCard, InteractionCard, PrescriptionCard, RiskBadge } from "../../components/health/cards";
import { AlertCard, Badge, Button, Card, Checkbox, EmptyState, Icon, List, ListItem, StatCard, Table, TextArea, Timeline, useToast, Sparkline, type Column } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAsync, useServices } from "../../services/ServicesProvider";
import type { Patient, ReviewItem } from "../../services/types";
import { Async, PersonCell } from "../common";
import "../features.css";

export function PharmacistDashboard() {
  const { t, loc, fmt } = useI18n();
  const nav = useNavigate();
  const reviews = useAsync((s) => s.pharmacist.reviews());
  const questions = useAsync((s) => s.pharmacist.questions());
  const adr = useAsync((s) => s.adr.list({ status: "new" }));
  const follow = useAsync((s) => s.pharmacist.followUps());
  const openReviews = reviews.status === "ready" ? reviews.data.filter((r) => r.status === "open") : [];
  return (
    <>
      <PageHeader title={t("nav.workQueue")} subtitle={t("pharm.dashSub")} />
      <div className="ms-stack">
        <div className="grid-stats">
          <StatCard icon="clipboard" label={t("pharm.statReviews")} value={reviews.status === "ready" ? fmt.number(openReviews.length) : "…"} />
          <StatCard icon="message" label={t("pharm.statQuestions")} value={questions.status === "ready" ? fmt.number(questions.data.length) : "…"} />
          <StatCard icon="alertTriangle" label={t("pharm.statAdr")} value={adr.status === "ready" ? fmt.number(adr.data.length) : "…"} />
          <StatCard icon="calendar" label={t("pharm.statFollow")} value={follow.status === "ready" ? fmt.number(follow.data.filter((f) => !f.done && f.dueInDays <= 0).length) : "…"} />
        </div>
        <div className="split">
          <Card title={t("pharm.queueReviews")} flush actions={<Button variant="ghost" size="sm" onClick={() => nav("/app/pharmacist/reviews")}>{t("common.viewAll")}</Button>}>
            <Async state={reviews} rows={1}>{() => (
              <List label={t("pharm.queueReviews")}>
                {openReviews.map((r) => (
                  <ListItem key={r.id} to="/app/pharmacist/reviews" title={loc(r.patient.name)} meta={`${r.prescription.id.toUpperCase()} · ${t("pharm.waiting", { n: fmt.number(r.waitingMinutes) })}`}
                    trailing={<span className="ms-row" style={{ flexWrap: "nowrap" }}>{r.interactions.length > 0 && <Badge tone="warning" icon="flask">{t("pharm.flags", { n: fmt.number(r.interactions.length) })}</Badge>}<Badge tone={r.priority === "high" ? "danger" : "neutral"}>{t(`pharm.priority.${r.priority}`)}</Badge></span>} />
                ))}
              </List>
            )}</Async>
          </Card>
          <Card title={t("pharm.queueQuestions")} flush actions={<Button variant="ghost" size="sm" onClick={() => nav("/app/pharmacist/questions")}>{t("common.viewAll")}</Button>}>
            {questions.status === "ready" && (
              <List label={t("pharm.queueQuestions")}>
                {questions.data.map((q) => <ListItem key={q.id} to="/app/pharmacist/questions" title={loc(q.text)} meta={`${loc(q.patient.name)} · ${fmt.relativeMinutes(q.askedMinutesAgo)}`} leading={<Icon name="message" />} />)}
              </List>
            )}
          </Card>
        </div>
      </div>
    </>
  );
}

export function PharmacistReviews() {
  const { t, loc, fmt } = useI18n();
  const toast = useToast();
  const services = useServices();
  const reviews = useAsync((s) => s.pharmacist.reviews());
  const [sel, setSel] = useState<string | null>(null);
  return (
    <>
      <PageHeader title={t("nav.reviews")} subtitle={t("pharm.reviewsSub")} />
      <Async state={reviews}>
        {(list) => {
          const current: ReviewItem | undefined = list.find((r) => r.id === sel) ?? list.find((r) => r.status === "open") ?? list[0];
          return (
            <div className="review-layout">
              <div className="pick" role="list" aria-label={t("nav.reviews")}>
                {list.map((r) => (
                  <button key={r.id} type="button" role="listitem" aria-current={current?.id === r.id ? "true" : undefined} onClick={() => setSel(r.id)}>
                    <strong>{loc(r.patient.name)} {r.status === "done" && <Icon name="check" size="xs" />}</strong>
                    <span className="hc-sub" style={{ color: "inherit" }}>{r.prescription.id.toUpperCase()} · {t("pharm.waiting", { n: fmt.number(r.waitingMinutes) })}</span>
                  </button>
                ))}
              </div>
              {current && (
                <div className="ms-stack">
                  <PrescriptionCard rx={current.prescription} showPatient />
                  <h2 style={{ fontSize: "var(--text-h3-size)" }}>{t("nav.interactions")}</h2>
                  {current.interactions.length ? current.interactions.map((x) => <InteractionCard key={x.id} interaction={x} />) : <AlertCard tone="success" title={t("pharm.noFlags")} />}
                  <Card title={t("pharm.actions")}>
                    <div className="ms-row">
                      <Button iconStart="check" disabled={current.status === "done"} onClick={async () => { await services.pharmacist.completeReview(current.id); reviews.reload(); toast.show({ message: t("pharm.approved"), tone: "success" }); }}>{t("pharm.approve")}</Button>
                      <Button variant="secondary" iconStart="phone" onClick={() => toast.show({ message: t("pharm.callPrototype"), tone: "info" })}>{t("pharm.callPrescriber")}</Button>
                      <Button variant="secondary" iconStart="fileText" onClick={() => toast.show({ message: t("pharm.interventionLogged"), tone: "success" })}>{t("pharm.logIntervention")}</Button>
                    </div>
                  </Card>
                </div>
              )}
            </div>
          );
        }}
      </Async>
    </>
  );
}

export function PharmacistInteractions() {
  const { t } = useI18n();
  const all = useAsync(async (s) => {
    const rxs = await s.prescriptions.list();
    const groups = await Promise.all(rxs.map((r) => s.prescriptions.interactionsFor(r.id)));
    const seen = new Set<string>();
    return groups.flat().filter((x) => (seen.has(x.id) ? false : (seen.add(x.id), true)));
  });
  return (
    <>
      <PageHeader title={t("nav.interactions")} subtitle={t("pharm.ixSub")} />
      <Async state={all}>{(list) => <div className="grid-2">{list.map((x) => <InteractionCard key={x.id} interaction={x} />)}</div>}</Async>
    </>
  );
}

export function PharmacistAdr() {
  const { t } = useI18n();
  const toast = useToast();
  const adr = useAsync((s) => s.adr.list());
  return (
    <>
      <PageHeader title={t("nav.adr")} subtitle={t("pharm.adrSub")} />
      <Async state={adr}>{(list) => <div className="grid-2">{list.map((a) => <ADRCard key={a.id} report={a} showPatient actions={a.status !== "reviewed" && <Button size="sm" variant="secondary" onClick={() => toast.show({ message: t("pharm.reviewStarted"), tone: "info" })}>{t("pharm.startReview")}</Button>} />)}</div>}</Async>
    </>
  );
}

export function PharmacistAdherence() {
  const { t, fmt } = useI18n();
  const patients = useAsync((s) => s.patients.list());
  return (
    <>
      <PageHeader title={t("nav.adherence")} subtitle={t("pharm.adhSub")} />
      <Async state={patients}>
        {(list) => {
          const rows = [...list].sort((a, b) => a.adherence - b.adherence);
          const cols: Column<Patient>[] = [
            { id: "p", header: t("patient.name"), primary: true, cell: (p) => <PersonCell patient={p} /> },
            { id: "a", header: t("patient.adherence"), cell: (p) => <strong>{fmt.percent(p.adherence)}</strong> },
            { id: "tr", header: t("patient.adherenceTrend"), cell: (p) => <Sparkline values={p.adherenceSeries} label={t("patient.adherenceTrend")} /> },
            { id: "r", header: t("risk.label"), cell: (p) => <RiskBadge risk={p.risk} /> },
            { id: "f", header: t("pharm.flagLabel"), cell: (p) => (p.adherence < 70 ? <Badge tone="warning" icon="alertTriangle">{t("pharm.needsFollowUp")}</Badge> : <Badge tone="success" icon="check">{t("pharm.onTrack")}</Badge>) },
          ];
          return <Table caption={t("nav.adherence")} columns={cols} rows={rows} rowKey={(p) => p.id} />;
        }}
      </Async>
    </>
  );
}

export function PharmacistQuestions() {
  const { t, loc, fmt } = useI18n();
  const toast = useToast();
  const questions = useAsync((s) => s.pharmacist.questions());
  const [sel, setSel] = useState<string | null>(null);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [sent, setSent] = useState<Set<string>>(new Set());
  return (
    <>
      <PageHeader title={t("nav.questions")} subtitle={t("pharm.qSub")} />
      <Async state={questions}>
        {(list) => {
          const current = list.find((q) => q.id === sel) ?? list[0];
          if (!current) return <EmptyState icon="inbox" title={t("pharm.qEmpty")} />;
          const reply = answers[current.id] ?? "";
          return (
            <div className="review-layout">
              <div className="pick" role="list" aria-label={t("nav.questions")}>
                {list.map((q) => (
                  <button key={q.id} type="button" role="listitem" aria-current={current.id === q.id ? "true" : undefined} onClick={() => setSel(q.id)}>
                    <strong>{loc(q.patient.name)} {sent.has(q.id) && <Icon name="check" size="xs" />}</strong>
                    <span className="hc-sub" style={{ color: "inherit" }}>{loc(q.text)}</span>
                    <span className="hc-sub" style={{ color: "inherit" }}>{fmt.relativeMinutes(q.askedMinutesAgo)}</span>
                  </button>
                ))}
              </div>
              <div className="ms-stack">
                <Card title={loc(current.patient.name)} subtitle={fmt.relativeMinutes(current.askedMinutesAgo)}><p>{loc(current.text)}</p></Card>
                <AIInsightCard title={t("pharm.aiDraft")} text={loc(current.aiDraft)} confidence={current.sources.length ? "medium" : "low"} sources={current.sources}
                  footer={<Button size="sm" variant="secondary" iconStart="plus" onClick={() => setAnswers({ ...answers, [current.id]: loc(current.aiDraft) })}>{t("pharm.useDraft")}</Button>} />
                <Card title={t("pharm.yourReply")}>
                  <div className="ms-stack" style={{ gap: "var(--space-3)" }}>
                    <TextArea label={t("pharm.yourReply")} hint={t("pharm.replyHint")} rows={4} value={reply} onChange={(e) => setAnswers({ ...answers, [current.id]: e.target.value })} />
                    <Checkbox label={t("pharm.confirmReviewed")} checked={sent.has(current.id) || undefined} onChange={() => undefined} disabled />
                    <div><Button iconStart="send" disabled={!reply.trim() || sent.has(current.id)} onClick={() => { setSent(new Set(sent).add(current.id)); toast.show({ message: t("pharm.replySent"), tone: "success" }); }}>{t("pharm.approveSend")}</Button></div>
                  </div>
                </Card>
              </div>
            </div>
          );
        }}
      </Async>
    </>
  );
}

export function PharmacistFollowUps() {
  const { t, loc, fmt } = useI18n();
  const follow = useAsync((s) => s.pharmacist.followUps());
  const [done, setDone] = useState<Record<string, boolean>>({});
  return (
    <>
      <PageHeader title={t("nav.followups")} subtitle={t("pharm.fuSub")} />
      <Async state={follow}>
        {(list) => (
          <div className="split">
            <Card title={t("pharm.fuTasks")}>
              <div className="ms-stack" style={{ gap: 0 }}>
                {list.map((f) => (
                  <Checkbox key={f.id} label={<span><strong>{loc(f.patient.name)}</strong> · {loc(f.reason)}</span>} checked={done[f.id] ?? f.done} onChange={(e) => setDone({ ...done, [f.id]: e.target.checked })} />
                ))}
              </div>
            </Card>
            <Card title={t("pharm.fuTimeline")}>
              <Timeline label={t("pharm.fuTimeline")} entries={[...list].sort((a, b) => a.dueInDays - b.dueInDays).map((f) => ({
                id: f.id, title: loc(f.patient.name), meta: `${loc(f.reason)} · ${f.dueInDays === 0 ? t("time.today") : f.dueInDays < 0 ? t("time.daysAgo", { n: fmt.number(-f.dueInDays) }) : t("pharm.inDays", { n: fmt.number(f.dueInDays) })}`,
                icon: (done[f.id] ?? f.done) ? "check" : "calendar", tone: (done[f.id] ?? f.done) ? "success" : f.dueInDays <= 0 ? "warning" : "neutral", current: f.dueInDays === 0 && !(done[f.id] ?? f.done),
              }))} />
            </Card>
          </div>
        )}
      </Async>
    </>
  );
}
