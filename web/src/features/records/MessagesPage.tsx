import { useState } from "react";
import { Badge, Button, Card, EmptyState } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAction, useLoad, useRecordsApi } from "../../records/hooks";
import type { GuidanceLevel, GuidanceMessage, GuidancePatient, GuidanceProfessional, GuidanceSamples } from "../../records/types";
import { ActionError, DemoFlag, Loaded } from "./shared";

const tone = { Information: "info", FollowUp: "info", ReviewSoon: "warning", Urgent: "warning" } as const;

/** The six parts of every patient message, always in the same order. */
export function GuidanceBody({ g, level, pro }: { g: GuidancePatient; level: GuidanceLevel; pro?: GuidanceProfessional | null }) {
  const { t } = useI18n();
  const part = (key: string, text: string) => <div><h4>{t(`rec.msg.${key}`)}</h4><p>{text}</p></div>;
  return (
    <div className={`rec-msg${level === "Urgent" ? " rec-msg--urgent" : ""}`}>
      {part("observed", g.observed)}
      {part("why", g.whyItMatters)}
      {part("action", g.suggestedAction)}
      {part("consult", g.whenToConsult)}
      {g.urgentSigns && part("urgent", g.urgentSigns)}
      {part("basis", g.basisAndConfidence)}
      {pro && <div className="rec-note"><h4>{t("rec.msg.pro")}</h4><p><strong>{pro.severityLabel}</strong></p><p>{pro.summary}</p><p>{pro.technicalDetail}</p><p className="ms-muted">{pro.basis}</p></div>}
    </div>
  );
}

export function PatientMessages() {
  const { t, fmt, locale } = useI18n();
  const api = useRecordsApi();
  const list = useLoad((a, s) => a.messages(s));
  const samples = useLoad((a, s) => a.samples(locale, s), [locale]);
  const act = useAction();
  const [showSamples, setShowSamples] = useState(false);
  return (
    <>
      <PageHeader title={t("rec.messages.title")} subtitle={t("rec.messages.sub")} />
      <div className="rec-stack">
        <Loaded load={list.state} reload={list.reload}>{(rows: GuidanceMessage[]) => rows.length === 0 ? <Card><EmptyState icon="inbox" title={t("rec.messages.empty")} body={t("rec.messages.emptyBody")} /></Card> : (
          <ul className="rec-list" aria-label={t("rec.messages.title")}>{rows.map((m) => (
            <li key={m.id}><Card>
              <div className="rec-badges"><Badge tone={tone[m.level]} icon={m.level === "Urgent" ? "alertTriangle" : "info"}>{t(`rec.level.${m.level}`)}</Badge><Badge tone="neutral">{t(`rec.msgStatus.${m.status}`)}</Badge><DemoFlag show={m.isDemo} /><span className="ms-muted">{fmt.date(new Date(m.createdAt), "short")}</span></div>
              <GuidanceBody g={m.patient} level={m.level} />
              <div className="rec-row__actions">
                {m.status === "Sent" && <Button size="sm" onClick={() => act.run(() => api.setMessageStatus(m.id, "Seen"), list.reload)}>{t("rec.messages.seen")}</Button>}
                {m.status !== "Resolved" && <Button size="sm" variant="secondary" onClick={() => act.run(() => api.setMessageStatus(m.id, "Resolved"), list.reload)}>{t("rec.messages.resolve")}</Button>}
              </div>
            </Card></li>))}
          </ul>)}
        </Loaded>
        <ActionError error={act.error} />
        <Card title={t("rec.samples.title")} subtitle={t("rec.samples.sub")}>
          <Button variant="secondary" aria-expanded={showSamples} onClick={() => setShowSamples(!showSamples)}>{showSamples ? t("rec.samples.hide") : t("rec.samples.show")}</Button>
          {showSamples && <Loaded load={samples.state} reload={samples.reload}>{(s: GuidanceSamples) => (
            <div className="rec-stack"><Badge tone="warning" icon="alertTriangle">{t("demo.badge")}</Badge>
              {s.samples.map((x, i) => <Card key={i} variant="default"><Badge tone={tone[x.level]}>{t(`rec.level.${x.level}`)}</Badge><GuidanceBody g={x.patient} level={x.level} pro={x.professional} /></Card>)}
            </div>)}
          </Loaded>}
        </Card>
      </div>
    </>
  );
}
