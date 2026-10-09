import { useState } from "react";
import { Badge, Button, Card, EmptyState, TextField } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAction, useLoad, useRecordsApi } from "../../records/hooks";
import type { CareRow, ProcessResult, Queue, Report } from "../../records/types";
import { ActionError, DemoFlag, Loaded } from "./shared";
import { PayloadView } from "./ReportsPage";

/** Physician / pharmacist: reports waiting for a human review. They see only what the manufacturer would receive. */
export function ReportReviews() {
  const { t } = useI18n();
  const api = useRecordsApi();
  const list = useLoad((a, s) => a.pendingReviews(s));
  return (
    <>
      <PageHeader title={t("rec.review.title")} subtitle={t("rec.review.sub")} />
      <Loaded load={list.state} reload={list.reload}>{(rows: Report[]) => rows.length === 0 ? <Card><EmptyState icon="checkCircle" title={t("rec.review.empty")} /></Card> : (
        <ul className="rec-list" aria-label={t("rec.review.title")}>{rows.map((r) => <ReviewItem key={r.id} r={r} api={api} onDone={list.reload} />)}</ul>)}
      </Loaded>
    </>
  );
}

function ReviewItem({ r, api, onDone }: { r: Report; api: ReturnType<typeof useRecordsApi>; onDone(): void }) {
  const { t } = useI18n();
  const act = useAction();
  const [note, setNote] = useState("");
  const decide = (decision: "Approve" | "Reject") => act.run(() => api.reviewReport(r.subjectId, r.id, { decision, note: note.trim() || null, expectedVersion: r.version }), onDone);
  return (
    <li><Card title={`${r.productName} · ${r.batchNumber}`} subtitle={<span className="rec-badges"><Badge tone="info">{t(`rec.issue.${r.issueType}`)}</Badge><Badge tone="warning">{t(`rec.severity.${r.severity}`)}</Badge><DemoFlag show={r.isDemo} /></span>}>
      {r.payloadPreview ? <PayloadView p={r.payloadPreview} /> : <p className="ms-muted">{t("rec.review.noPreview")}</p>}
      <TextField label={t("rec.review.note")} hint={t("rec.review.noteHint")} value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} />
      <div className="rec-row__actions">
        <Button loading={act.busy} onClick={() => decide("Approve")}>{t("rec.review.approve")}</Button>
        <Button variant="secondary" disabled={!note.trim()} onClick={() => decide("Reject")}>{t("rec.review.reject")}</Button>
      </div>
      <ActionError error={act.error} />
    </Card></li>
  );
}

/** Provider side of care relationships: requests waiting for an answer, active relationships, and ending them. */
export function CareRequests() {
  const { t } = useI18n();
  const api = useRecordsApi();
  const list = useLoad((a, s) => a.care(s));
  const act = useAction();
  const go = (id: string, a: "accept" | "decline" | "end") => act.run(() => api.careAction(id, a), list.reload);
  return (
    <>
      <PageHeader title={t("rec.carepro.title")} subtitle={t("rec.carepro.sub")} />
      <Loaded load={list.state} reload={list.reload}>{(rows: CareRow[]) => rows.length === 0 ? <Card><EmptyState icon="users" title={t("rec.carepro.empty")} /></Card> : (
        <ul className="rec-list" aria-label={t("rec.carepro.title")}>{rows.map(({ relationship: r, patientName }) => (
          <li key={r.id} className="rec-row">
            <div className="rec-row__main"><span className="rec-row__title">{patientName}</span><span className="rec-row__meta">{t(`rec.care.kind.${r.kind}`)}</span>
              <span className="rec-badges"><Badge tone={r.status === "Active" ? "success" : "info"} icon={r.status === "Active" ? "check" : "clock"}>{t(`rec.care.status.${r.status}`)}</Badge></span></div>
            <div className="rec-row__actions">
              {r.status === "PendingProvider" && <><Button size="sm" onClick={() => go(r.id, "accept")}>{t("rec.care.accept")}</Button><Button size="sm" variant="secondary" onClick={() => go(r.id, "decline")}>{t("rec.care.decline")}</Button></>}
              {r.status === "Active" && <Button size="sm" variant="ghost" onClick={() => go(r.id, "end")}>{t("rec.care.end")}</Button>}
            </div>
          </li>))}
        </ul>)}
      </Loaded>
      <ActionError error={act.error} />
      <p className="ms-muted">{t("rec.carepro.note")}</p>
    </>
  );
}

/** Operators: state of the manufacturer-report queue (no clinical content) and a manual pass over it. */
export function ReportQueue() {
  const { t, fmt } = useI18n();
  const api = useRecordsApi();
  const q = useLoad((a, s) => a.queue(s));
  const act = useAction();
  const [last, setLast] = useState<ProcessResult | null>(null);
  return (
    <>
      <PageHeader title={t("rec.queue.title")} subtitle={t("rec.queue.sub")} />
      <Loaded load={q.state} reload={q.reload}>{(d: Queue) => (
        <div className="rec-stack">
          <Card tone={d.providerConfigured ? "neutral" : "warning"} title={t("rec.queue.provider")}>
            <p><strong>{d.providerIsMock ? t("rec.queue.mock") : d.providerConfigured ? t("rec.queue.real") : t("rec.queue.none")}</strong></p>
            <p className="ms-muted">{t("rec.queue.agreement")}</p>
            <div className="rec-row__actions">
              <Button loading={act.busy} disabled={!d.providerConfigured} onClick={() => act.run(async () => setLast(await api.processQueue(20)), q.reload)}>{t("rec.queue.process")}</Button>
              <Button variant="secondary" iconStart="refresh" onClick={q.reload}>{t("common.retry")}</Button>
            </div>
            {last && <p role="status">{t("rec.queue.result", { sent: last.sent, retrying: last.retrying, failed: last.failed, blocked: last.blocked })}</p>}
            <ActionError error={act.error} />
          </Card>
          <div className="rec-grid rec-grid--2">
            <Card title={t("rec.queue.byStatus")}><dl className="rec-facts">{Object.entries(d.reportsByStatus).map(([k, v]) => <div key={k}><dt>{t(`rec.rep.status.${k}`)}</dt><dd>{fmt.number(v)}</dd></div>)}</dl></Card>
            <Card title={t("rec.queue.byState")}><dl className="rec-facts">{Object.entries(d.outboxByState).map(([k, v]) => <div key={k}><dt>{t(`rec.outbox.${k}`)}</dt><dd>{fmt.number(v)}</dd></div>)}</dl></Card>
          </div>
          <Card title={t("rec.queue.entries")} subtitle={t("rec.queue.noContent")}>
            {d.entries.length === 0 ? <EmptyState icon="inbox" title={t("rec.queue.empty")} /> : (
              <ul className="rec-list" aria-label={t("rec.queue.entries")}>{d.entries.map((e) => (
                <li key={e.id} className="rec-row"><div className="rec-row__main"><span className="rec-row__title"><bdi dir="ltr">{e.reportId.slice(0, 8)}</bdi></span><span className="rec-row__meta">{t("rec.queue.attempts", { n: e.attempts })}{e.lastErrorCode ? ` · ${e.lastErrorCode}` : ""}</span></div>
                  <Badge tone={e.state === "Failed" ? "danger" : e.state === "Sent" ? "success" : "info"}>{t(`rec.outbox.${e.state}`)}</Badge>
                  {e.state === "Failed" && <Button size="sm" variant="secondary" onClick={() => act.run(() => api.retryReport(e.reportId), q.reload)}>{t("rec.queue.retry")}</Button>}</li>))}
              </ul>)}
          </Card>
        </div>)}
      </Loaded>
    </>
  );
}
