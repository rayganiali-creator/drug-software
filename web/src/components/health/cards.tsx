import { useState, type ReactNode } from "react";
import { useI18n } from "../../i18n/I18nProvider";
import type { AdverseReport, Confidence, Dose, Drug, Evidence, Interaction, KnowledgeSource, Patient, PatientMedication, Prescription, Risk } from "../../services/types";
import { AlertCard, Avatar, Badge, Card, Chip, DemoBadge, Icon, ProgressRing, Sparkline, type Tone, Button } from "../ui";
import type { IconName } from "../ui";
import "./health.css";

/** Small "DEMO DATA" marker shown on every card that renders Mock records. */
export function Demo() {
  const { t } = useI18n();
  return <DemoBadge label={t("demo.badge")} />;
}

const riskTone: Record<Risk, Tone> = { low: "success", medium: "warning", high: "danger" };
const riskIcon: Record<Risk, IconName> = { low: "checkCircle", medium: "alertTriangle", high: "alertTriangle" };

export function RiskBadge({ risk }: { risk: Risk }) {
  const { t } = useI18n();
  return <Badge tone={riskTone[risk]} icon={riskIcon[risk]}>{t(`risk.${risk}`)}</Badge>;
}

/** bdi keeps "10 mg" left-to-right inside RTL sentences. */
export const Ltr = ({ children }: { children: ReactNode }) => <bdi dir="ltr">{children}</bdi>;

// ---------- MedicationCard ----------
export function MedicationCard({ med, dose, to, variant = "summary" }: { med: PatientMedication; dose?: Dose; to?: string; variant?: "summary" | "detail" }) {
  const { t, loc, fmt } = useI18n();
  const { drug, item } = med;
  return (
    <Card to={to} className="hc-med" aria-label={loc(drug.name)}>
      <div className="hc-med__head">
        <span className="hc-tile" data-tone="primary"><Icon name="pill" /></span>
        <div className="hc-med__title">
          <h3 className="hc-name">{loc(drug.name)} <Ltr>{drug.strength}</Ltr></h3>
          <div className="hc-sub">{t("med.activeIngredient")}: {loc(drug.activeIngredient.name)}</div>
        </div>
        <Demo />
      </div>
      <div className="ms-row hc-chips">
        <Chip icon="pill">{loc(drug.form)}</Chip>
        <Chip icon="activity">{loc(drug.route)}</Chip>
        <Chip icon="clock">{item.times.map((x) => fmt.time(x)).join(" · ")}</Chip>
      </div>
      {dose && (
        <div className="hc-nextdose" data-tone={dose.status === "missed" ? "warning" : "primary"}>
          <Icon name="clock" size="sm" />
          <span>
            {dose.status === "taken" ? t("dose.taken") : dose.status === "missed" ? t("dose.missed") : t("med.nextDose")}
            {" · "}<strong>{fmt.time(dose.time)}</strong>
          </span>
        </div>
      )}
      {variant === "detail" && (
        <>
          <div className="hc-block"><h4>{t("med.instructions")}</h4><p>{loc(drug.instructions)}</p></div>
          <div className="hc-block">
            <h4>{t("med.schedule")}</h4>
            <p>{loc(item.dose)} · {loc(item.frequency)}</p>
          </div>
          <div className="ms-stack" style={{ gap: "var(--space-2)" }}>
            {drug.warnings.map((w, i) => (
              <AlertCard key={i} tone="warning" title={t("med.warning")}>{loc(w)}</AlertCard>
            ))}
          </div>
        </>
      )}
    </Card>
  );
}

// ---------- DrugCard ----------
export function DrugCard({ drug, to, extra }: { drug: Drug; to?: string; extra?: ReactNode }) {
  const { t, loc } = useI18n();
  return (
    <Card to={to} aria-label={loc(drug.name)}>
      <div className="hc-med__head">
        <span className="hc-tile" data-tone="accent"><Icon name="flask" /></span>
        <div className="hc-med__title">
          <h3 className="hc-name">{loc(drug.name)}</h3>
          <div className="hc-sub">{loc(drug.activeIngredient.name)}</div>
        </div>
        <Demo />
      </div>
      <div className="ms-row hc-chips">
        <Chip><Ltr>{drug.strength}</Ltr></Chip>
        <Chip>{loc(drug.form)}</Chip>
        <Chip>{loc(drug.route)}</Chip>
      </div>
      <div className="hc-sub">{t("drug.category")}: {loc(drug.category)}</div>
      {extra}
    </Card>
  );
}

// ---------- PrescriptionCard ----------
const rxTone: Record<Prescription["status"], Tone> = { active: "success", "pending-review": "warning", dispensed: "info" };
export function PrescriptionCard({ rx, showPatient, actions, to }: { rx: Prescription; showPatient?: boolean; actions?: ReactNode; to?: string }) {
  const { t, loc, fmt } = useI18n();
  const [nowMs] = useState(() => Date.now());
  const issued = new Date(nowMs - rx.issuedDaysAgo * 86400000);
  return (
    <Card to={to} aria-label={`${t("rx.title")} ${rx.id}`}>
      <div className="hc-med__head">
        <span className="hc-tile" data-tone="secondary"><Icon name="clipboard" /></span>
        <div className="hc-med__title">
          <h3 className="hc-name"><Ltr>{rx.id.toUpperCase()}</Ltr>{showPatient && <> · {loc(rx.patient.name)}</>}</h3>
          <div className="hc-sub">{loc(rx.prescriber)} · {fmt.date(issued, "short")}</div>
        </div>
        <Badge tone={rxTone[rx.status]}>{t(`rx.status.${rx.status}`)}</Badge>
      </div>
      <ul className="hc-rxitems">
        {rx.items.map((it) => (
          <li key={it.drug.id}>
            <Icon name="pill" size="sm" />
            <span><strong>{loc(it.drug.name)}</strong> <Ltr>{it.drug.strength}</Ltr> · {loc(it.dose)} · {loc(it.frequency)}</span>
          </li>
        ))}
      </ul>
      <div className="hc-foot"><span className="hc-sub">{t("rx.refills", { n: fmt.number(rx.refillsLeft) })}</span><Demo />{actions}</div>
    </Card>
  );
}

// ---------- PatientCard ----------
export function PatientCard({ patient, to, compact, trailing }: { patient: Patient; to?: string; compact?: boolean; trailing?: ReactNode }) {
  const { t, loc, fmt } = useI18n();
  return (
    <Card to={to} aria-label={loc(patient.name)}>
      <div className="hc-med__head">
        <Avatar name={loc(patient.name)} size={compact ? "md" : "lg"} tone={riskTone[patient.risk]} />
        <div className="hc-med__title">
          <h3 className="hc-name">{loc(patient.name)}</h3>
          <div className="hc-sub">{t("patient.ageSex", { age: fmt.number(patient.age), sex: t(`sex.${patient.sex}`) })}</div>
        </div>
        <RiskBadge risk={patient.risk} />
      </div>
      {!compact && (
        <>
          <div className="ms-row hc-chips">
            {patient.conditions.map((c, i) => <Chip key={i}>{loc(c)}</Chip>)}
            {patient.allergies.map((a, i) => <Badge key={i} tone="danger" icon="alertTriangle">{t("patient.allergy")}: {loc(a)}</Badge>)}
          </div>
          <div className="hc-foot">
            <span className="hc-sub">{t("patient.adherence")}: <strong>{fmt.percent(patient.adherence)}</strong></span>
            <Sparkline values={patient.adherenceSeries} label={t("patient.adherenceTrend")} />
            <Demo />
            {trailing}
          </div>
        </>
      )}
    </Card>
  );
}

// ---------- RiskCard ----------
export function RiskCard({ patient, reasons, to }: { patient: Patient; reasons: string[]; to?: string }) {
  const { t, loc } = useI18n();
  return (
    <Card to={to} className="hc-risk" aria-label={loc(patient.name)}>
      <div className="hc-med__head">
        <Avatar name={loc(patient.name)} tone={riskTone[patient.risk]} />
        <div className="hc-med__title"><h3 className="hc-name">{loc(patient.name)}</h3><div className="hc-sub">{t("risk.needsAttention")}</div></div>
        <RiskBadge risk={patient.risk} />
      </div>
      <ul className="hc-reasons">{reasons.map((r) => <li key={r}><Icon name="alertTriangle" size="xs" />{r}</li>)}</ul>
    </Card>
  );
}

// ---------- ADRCard ----------
const sevTone = { mild: "info", moderate: "warning", serious: "danger" } as const;
export function ADRCard({ report, showPatient, actions }: { report: AdverseReport; showPatient?: boolean; actions?: ReactNode }) {
  const { t, loc, fmt } = useI18n();
  return (
    <Card aria-label={loc(report.event)}>
      <div className="hc-med__head">
        <span className="hc-tile" data-tone={sevTone[report.severity]}><Icon name="alertTriangle" /></span>
        <div className="hc-med__title">
          <h3 className="hc-name">{loc(report.event)}</h3>
          <div className="hc-sub">{loc(report.drug.name)}{showPatient && <> · {loc(report.patient.name)}</>} · {t("time.daysAgo", { n: fmt.number(report.reportedDaysAgo) })}</div>
        </div>
        <Badge tone={sevTone[report.severity]}>{t(`severity.${report.severity}`)}</Badge>
      </div>
      <div className="ms-row hc-chips">
        <Chip>{t(`adr.status.${report.status}`)}</Chip>
        <Chip>{t("adr.causality")}: {t(`adr.causality.${report.causality}`)}</Chip>
      </div>
      <div className="hc-foot"><Demo />{actions}</div>
    </Card>
  );
}

// ---------- InteractionCard ----------
const ixTone = { minor: "info", moderate: "warning", major: "danger" } as const;
export function InteractionCard({ interaction }: { interaction: Interaction }) {
  const { t, loc } = useI18n();
  return (
    <Card aria-label={t("ix.title")}>
      <div className="hc-ix">
        <span className="hc-ix__drug">{loc(interaction.drugA.name)}</span>
        <Icon name="flask" size="sm" />
        <span className="hc-ix__drug">{loc(interaction.drugB.name)}</span>
        <Badge tone={ixTone[interaction.severity]} icon="alertTriangle">{t(`ix.severity.${interaction.severity}`)}</Badge>
        <Demo />
      </div>
      <p>{loc(interaction.summary)}</p>
      <p className="hc-sub"><strong>{t("ix.management")}:</strong> {loc(interaction.management)}</p>
      <div className="ms-row"><Chip variant="ai" icon="bookOpen">{loc(interaction.source.title)}</Chip></div>
    </Card>
  );
}

// ---------- AdherenceCard ----------
export function AdherenceCard({ value, series, title }: { value: number; series: number[]; title?: string }) {
  const { t, fmt } = useI18n();
  const tone: Tone = value >= 85 ? "success" : value >= 70 ? "warning" : "danger";
  return (
    <Card title={title ?? t("adherence.title")} subtitle={t("adherence.period")} aria-label={t("adherence.title")}>
      <div className="hc-adh" data-tone={tone}>
        <ProgressRing value={value} tone={tone} label={t("adherence.title")}>
          <div><div className="hc-adh__value">{fmt.percent(value)}</div></div>
        </ProgressRing>
        <div className="hc-adh__side">
          <Sparkline values={series} label={t("patient.adherenceTrend")} width={140} height={44} color="var(--tone-solid)" />
          <p className="hc-sub">{value >= 85 ? t("adherence.good") : value >= 70 ? t("adherence.ok") : t("adherence.low")}</p>
        </div>
      </div>
    </Card>
  );
}

// ---------- CheckInCard ----------
export function CheckInCard({ done, onStart }: { done: boolean; onStart(): void }) {
  const { t } = useI18n();
  return (
    <Card variant="tonal" tone={done ? "success" : "primary"}>
      <div className="hc-med__head">
        <span className="hc-tile" data-tone={done ? "success" : "primary"}><Icon name={done ? "checkCircle" : "heart"} /></span>
        <div className="hc-med__title">
          <h3 className="hc-name">{done ? t("checkin.doneTitle") : t("checkin.cardTitle")}</h3>
          <div className="hc-sub" style={{ color: "inherit" }}>{done ? t("checkin.doneBody") : t("checkin.cardBody")}</div>
        </div>
        {!done && <Button size="sm" onClick={onStart} iconEnd="arrowRight">{t("checkin.start")}</Button>}
      </div>
    </Card>
  );
}

// ---------- AI: Confidence / Insight / Evidence ----------
const confTone: Record<Confidence, Tone> = { high: "success", medium: "warning", low: "danger" };
const confBars: Record<Confidence, number> = { high: 3, medium: 2, low: 1 };
export function ConfidenceMeter({ level }: { level: Confidence }) {
  const { t } = useI18n();
  return (
    <span className="hc-conf" data-tone={confTone[level]} title={t("ai.confidence.explain")}>
      <span className="hc-conf__bars" aria-hidden="true">{[1, 2, 3].map((n) => <i key={n} data-on={n <= confBars[level]} />)}</span>
      <span>{t("ai.confidence")}: <strong>{t(`ai.confidence.${level}`)}</strong></span>
    </span>
  );
}

export function SourceChips({ sources, onOpen }: { sources: KnowledgeSource[]; onOpen?(): void }) {
  const { t, loc, fmt } = useI18n();
  if (sources.length === 0) return <span className="hc-sub">{t("ai.noSources")}</span>;
  return (
    <div className="ms-row" aria-label={t("ai.sources")} role="group">
      {sources.map((s, i) => (
        <Chip key={s.id} variant="ai" icon="bookOpen" onClick={onOpen}>
          [{fmt.number(i + 1)}] {loc(s.title)}
        </Chip>
      ))}
    </div>
  );
}

export function AIInsightCard({ title, text, confidence, sources, footer }: { title: string; text: string; confidence: Confidence; sources: KnowledgeSource[]; footer?: ReactNode }) {
  const { t } = useI18n();
  return (
    <Card variant="ai" aria-label={title}>
      <div className="hc-med__head">
        <span className="hc-tile" data-tone="accent"><Icon name="sparkles" /></span>
        <div className="hc-med__title"><h3 className="hc-name">{title}</h3><div className="hc-sub">{t("ai.draftNotice")}</div></div>
        <Demo />
      </div>
      <p>{text}</p>
      <ConfidenceMeter level={confidence} />
      <SourceChips sources={sources} />
      {footer}
    </Card>
  );
}

export function EvidenceCard({ evidence, index }: { evidence: Evidence; index: number }) {
  const { t, loc, fmt } = useI18n();
  return (
    <Card variant="default" aria-label={loc(evidence.source.title)}>
      <div className="hc-med__head">
        <span className="hc-tile" data-tone="accent"><Icon name="bookOpen" /></span>
        <div className="hc-med__title">
          <h3 className="hc-name">[{fmt.number(index)}] {loc(evidence.source.title)}</h3>
          <div className="hc-sub">{loc(evidence.source.type)} · {t("ai.version")} <Ltr>{evidence.source.version}</Ltr> · {loc(evidence.section)}</div>
        </div>
      </div>
      <blockquote className="hc-quote">{loc(evidence.excerpt)}</blockquote>
      <Demo />
    </Card>
  );
}
