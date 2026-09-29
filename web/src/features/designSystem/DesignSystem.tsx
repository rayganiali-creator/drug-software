import { useState } from "react";
import { Link } from "react-router-dom";
import { Logo } from "../../components/brand/Logo";
import { ADRCard, AIInsightCard, AdherenceCard, CheckInCard, DrugCard, EvidenceCard, InteractionCard, MedicationCard, PatientCard, PrescriptionCard, RiskCard } from "../../components/health/cards";
import {
  AlertCard, Avatar, Badge, BadgeAnchor, BarChart, BottomSheet, Button, Card, ChartCard, ChatBubble, Checkbox, Chip, Drawer, Dropdown, EmptyState, ErrorState, Icon, IconButton,
  LineChart, List, ListItem, LoadingState, MessageComposer, Modal, Pagination, Progress, ProgressRing, Radio, RadioGroup, SearchField, SegmentedControl, Select, Skeleton, Sparkline,
  StatCard, Switch, Table, Tabs, TextArea, TextField, Timeline, Tooltip, useToast, type IconName,
} from "../../components/ui";
import { colorNames, themeColors } from "../../design/tokens.g";
import { useI18n } from "../../i18n/I18nProvider";
import { useAsync } from "../../services/ServicesProvider";
import { useTheme } from "../../theme/ThemeProvider";
import { Async } from "../common";
import "../features.css";
import "./ds.css";

const iconNames: IconName[] = ["home", "pill", "sparkles", "checkCircle", "user", "users", "search", "bell", "menu", "x", "plus", "send", "mic", "image", "clock", "alertTriangle", "info", "check", "shield", "lock", "heart", "activity", "clipboard", "flask", "box", "truck", "chartBar", "trendUp", "fileText", "sliders", "sun", "moon", "globe", "bookOpen", "message", "phone", "calendar", "arrowRight", "more", "filter", "download", "eye", "trash", "refresh", "stethoscope", "store", "building", "cross", "grid", "list", "inbox", "chevronRight", "moodGood"];

function Section({ id, title, children }: { id: string; title: string; children: React.ReactNode }) {
  return (
    <section className="ds-section" aria-labelledby={id}>
      <h2 id={id}>{title}</h2>
      <div className="ms-stack">{children}</div>
    </section>
  );
}

export function DesignSystem() {
  const { t, fmt } = useI18n();
  const { mode, setMode } = useTheme();
  const toast = useToast();
  const [seg, setSeg] = useState("a");
  const [tab, setTab] = useState("one");
  const [radio, setRadio] = useState("x");
  const [sw, setSw] = useState(true);
  const [q, setQ] = useState("");
  const [sel, setSel] = useState("1");
  const [modal, setModal] = useState<null | "modal" | "sheet" | "drawer">(null);
  const [page, setPage] = useState(2);
  const data = useAsync(async (s) => {
    const [meds, rx, patients, adr, ix, doses] = await Promise.all([s.medications.forPatient("pt-sara"), s.prescriptions.list(), s.patients.list(), s.adr.list(), s.prescriptions.interactionsFor("rx-1001"), s.medications.todaysDoses("pt-sara")]);
    const ai = await s.ai.conversations();
    return { meds, rx, patients, adr, ix, doses, evidence: ai[0]!.messages.flatMap((m) => (m.role === "assistant" ? m.answer.evidence : []))[0]! };
  });
  const scale = ["display", "h1", "h2", "h3", "h4", "body", "body-small", "caption", "label", "button"];
  const spaces = [1, 2, 3, 4, 5, 6, 8, 10, 12];
  const radii = ["xs", "sm", "md", "lg", "xl", "pill"];
  const months = ["1", "2", "3", "4", "5", "6"].map((n) => fmt.number(Number(n)));
  const series = [{ id: "a", label: "A", values: [12, 18, 15, 24, 22, 30], color: "var(--color-chart1)" }, { id: "b", label: "B", values: [8, 10, 14, 12, 18, 16], color: "var(--color-chart2)" }];
  const colors = themeColors[mode === "dark" ? "dark" : "light"];

  return (
    <div className="ds">
      <header className="ds-head">
        <div className="ds-wrap ms-row" style={{ justifyContent: "space-between" }}>
          <Link to="/" aria-label={t("app.name")}><Logo /></Link>
          <div className="ms-row">
            <SegmentedControl label={t("theme.label")} value={mode} onValueChange={(v) => setMode(v as "system" | "light" | "dark")} options={[{ value: "system", label: t("theme.system") }, { value: "light", label: t("theme.light") }, { value: "dark", label: t("theme.dark") }]} />
            <Link className="ms-btn ms-btn--secondary ms-btn--sm" to="/app/patient">{t("lp.openDemo")}</Link>
          </div>
        </div>
      </header>
      <main className="ds-wrap" id="main" tabIndex={-1}>
        <h1 style={{ marginBlock: "var(--space-8) var(--space-2)" }}>{t("nav.designSystem")}</h1>
        <p className="ms-muted">{t("ds.intro")}</p>

        <Section id="ds-colors" title={t("ds.colors")}>
          <div className="ds-swatches">
            {colorNames.map((n) => (
              <div key={n} className="ds-swatch">
                <span style={{ background: `var(--color-${n.replace(/([A-Z])/g, "-$1").toLowerCase()})` }} />
                <b>{n}</b><code dir="ltr">{colors[n]}</code>
              </div>
            ))}
          </div>
        </Section>

        <Section id="ds-type" title={t("ds.typography")}>
          <Card>{scale.map((s) => <div key={s} className="ds-type"><code dir="ltr">{s}</code><span className={`t-${s}`}>{t("ds.sampleText")}</span></div>)}</Card>
        </Section>

        <Section id="ds-space" title={t("ds.spacingRadius")}>
          <div className="ms-row">{spaces.map((s) => <div key={s} className="ds-space"><span style={{ inlineSize: `var(--space-${s})`, blockSize: `var(--space-${s})` }} /><code dir="ltr">s{s}</code></div>)}</div>
          <div className="ms-row">{radii.map((r) => <div key={r} className="ds-space"><span className="ds-radius" style={{ borderRadius: `var(--radius-${r})` }} /><code dir="ltr">{r}</code></div>)}</div>
          <div className="ms-row">{[1, 2, 3, 4].map((e) => <div key={e} className="ds-elev" style={{ boxShadow: `var(--shadow-${e})` }}><code dir="ltr">elevation {e}</code></div>)}</div>
        </Section>

        <Section id="ds-icons" title={t("ds.icons")}>
          <div className="ds-icons">{iconNames.map((n) => <div key={n} title={n}><Icon name={n} size="md" /><code dir="ltr">{n}</code></div>)}</div>
        </Section>

        <Section id="ds-actions" title={t("ds.actions")}>
          <div className="ms-row">
            <Button>{t("ds.primary")}</Button><Button variant="secondary">{t("ds.secondary")}</Button><Button variant="tonal">{t("ds.tonal")}</Button><Button variant="ai" iconStart="sparkles">{t("ds.ai")}</Button>
            <Button variant="ghost">{t("ds.ghost")}</Button><Button variant="danger">{t("ds.danger")}</Button><Button loading>{t("common.loading")}</Button><Button disabled>{t("ds.disabled")}</Button>
          </div>
          <div className="ms-row"><Button size="sm">{t("ds.small")}</Button><Button size="md">{t("ds.medium")}</Button><Button size="lg">{t("ds.large")}</Button>
            <IconButton icon="plus" label={t("ds.add")} /><IconButton icon="mic" label={t("ai.voice")} pressed /><IconButton icon="send" label={t("ai.send")} filled />
            <Tooltip text={t("ds.tooltip")}><IconButton icon="info" label={t("ds.tooltip")} /></Tooltip>
            <BadgeAnchor count={3} label="3"><IconButton icon="bell" label={t("notif.label")} /></BadgeAnchor>
            <Dropdown label={t("ds.menu")} triggerContent={t("ds.menu")} items={[{ id: "a", label: t("ds.option", { n: 1 }), onSelect: () => toast.show({ message: t("ds.option", { n: 1 }) }) }, { id: "b", label: t("ds.option", { n: 2 }), icon: "check", onSelect: () => undefined }]} />
          </div>
        </Section>

        <Section id="ds-inputs" title={t("ds.inputs")}>
          <div className="grid-2">
            <TextField label={t("ds.textField")} hint={t("ds.hint")} placeholder={t("ds.placeholder")} />
            <TextField label={t("ds.textField")} error={t("ds.error")} defaultValue="…" />
            <SearchField label={t("search.label")} clearLabel={t("search.clear")} value={q} onValueChange={setQ} />
            <Select label={t("ds.select")} value={sel} onValueChange={setSel} options={[{ value: "1", label: t("ds.option", { n: 1 }) }, { value: "2", label: t("ds.option", { n: 2 }) }]} />
            <TextArea label={t("ds.textArea")} rows={3} />
            <div className="ms-stack" style={{ gap: 0 }}>
              <Checkbox label={t("ds.checkbox")} defaultChecked />
              <RadioGroup legend={t("ds.radio")} name="ds-radio" value={radio} onValueChange={setRadio}><Radio value="x" label={t("ds.option", { n: 1 })} /><Radio value="y" label={t("ds.option", { n: 2 })} /></RadioGroup>
              <Switch label={t("ds.switch")} checked={sw} onCheckedChange={setSw} />
            </div>
          </div>
        </Section>

        <Section id="ds-nav" title={t("ds.navigation")}>
          <SegmentedControl label={t("ds.segmented")} value={seg} onValueChange={setSeg} options={[{ value: "a", label: t("ds.option", { n: 1 }) }, { value: "b", label: t("ds.option", { n: 2 }) }, { value: "c", label: t("ds.option", { n: 3 }) }]} />
          <Tabs label={t("ds.tabs")} value={tab} onValueChange={setTab} tabs={[{ id: "one", label: t("ds.option", { n: 1 }), panel: <p>{t("ds.sampleText")}</p> }, { id: "two", label: t("ds.option", { n: 2 }), panel: <p>{t("ds.sampleText")} 2</p> }]} />
          <Pagination page={page} pageCount={5} onPageChange={setPage} labels={{ nav: t("table.pagination"), prev: t("table.prev"), next: t("table.next"), page: (n) => t("table.page", { n }), summary: t("table.summary", { a: 6, b: 10, n: 25 }) }} />
        </Section>

        <Section id="ds-feedback" title={t("ds.feedback")}>
          <div className="grid-2">
            <AlertCard tone="info" title={t("ds.alertInfo")}>{t("ds.sampleText")}</AlertCard>
            <AlertCard tone="success" title={t("ds.alertSuccess")}>{t("ds.sampleText")}</AlertCard>
            <AlertCard tone="warning" title={t("ds.alertWarning")}>{t("ds.sampleText")}</AlertCard>
            <AlertCard tone="danger" title={t("ds.alertDanger")}>{t("ds.sampleText")}</AlertCard>
          </div>
          <div className="ms-row">
            <Button variant="secondary" onClick={() => toast.show({ message: t("ds.toast"), tone: "success" })}>{t("ds.showToast")}</Button>
            <Button variant="secondary" onClick={() => toast.show({ message: t("ds.snackbar"), action: { label: t("common.undo"), onClick: () => undefined } })}>{t("ds.showSnackbar")}</Button>
            <Button variant="secondary" onClick={() => setModal("modal")}>{t("ds.modal")}</Button>
            <Button variant="secondary" onClick={() => setModal("sheet")}>{t("ds.bottomSheet")}</Button>
            <Button variant="secondary" onClick={() => setModal("drawer")}>{t("ds.drawer")}</Button>
          </div>
          <div className="ms-row"><Badge tone="primary">{t("ds.badge")}</Badge><Badge tone="success" icon="check">{t("ds.badge")}</Badge><Badge tone="warning" icon="alertTriangle">{t("ds.badge")}</Badge><Badge tone="danger" solid>{t("ds.badge")}</Badge><Badge tone="accent" icon="sparkles">AI</Badge>
            <Chip>{t("ds.chip")}</Chip><Chip selected onClick={() => undefined} role="checkbox">{t("ds.chip")}</Chip><Chip variant="ai" icon="bookOpen">{t("ds.chip")}</Chip><Chip onRemove={() => undefined} removeLabel={t("common.close")}>{t("ds.chip")}</Chip>
            <Avatar name="Sara Ahmadi" /><Avatar name="Ali Rezaei" tone="accent" size="sm" /></div>
          <div className="grid-3"><Card><Progress value={64} label={t("ds.progress")} /></Card><Card><div className="ms-row"><ProgressRing value={72} label={t("ds.progress")} size={96}><strong>{fmt.percent(72)}</strong></ProgressRing><Sparkline values={[3, 6, 4, 8, 7, 9]} label={t("ds.sparkline")} width={120} height={40} /></div></Card><Card><Skeleton width="60%" height={20} /><div style={{ height: 8 }} /><Skeleton height={14} /></Card></div>
          <div className="grid-3">
            <Card><EmptyState icon="inbox" title={t("common.noResults")} body={t("common.noResultsBody")} /></Card>
            <Card><ErrorState title={t("common.errorTitle")} body={t("common.errorBody")} retryLabel={t("common.retry")} onRetry={() => undefined} /></Card>
            <Card><LoadingState label={t("common.loading")} rows={1} /></Card>
          </div>
        </Section>

        <Section id="ds-data" title={t("ds.data")}>
          <div className="split">
            <Card title={t("ds.timeline")}><Timeline label={t("ds.timeline")} entries={[{ id: "1", title: t("dose.taken"), meta: fmt.time("08:00"), icon: "check", tone: "success" }, { id: "2", title: t("med.nextDose"), meta: fmt.time("20:00"), icon: "clock", current: true }, { id: "3", title: t("dose.upcoming"), meta: fmt.time("22:30"), icon: "clock" }]} /></Card>
            <Card title={t("ds.list")} flush><List label={t("ds.list")}><ListItem title={t("ds.option", { n: 1 })} meta={t("ds.sampleText")} leading={<Icon name="pill" />} trailing={<Icon name="chevronRight" />} /><ListItem title={t("ds.option", { n: 2 })} meta={t("ds.sampleText")} leading={<Icon name="flask" />} trailing={<Icon name="chevronRight" />} /></List></Card>
          </div>
          <div className="grid-stats"><StatCard icon="users" label={t("phys.statPatients")} value={fmt.number(128)} delta="+4" deltaTone="success" /><StatCard icon="activity" label={t("phys.statAdherence")} value={fmt.percent(84)} /></div>
          <div className="split">
            <ChartCard title={t("ds.lineChart")} legend={series}><LineChart series={series} xLabels={months} summary={t("ds.lineChart")} /></ChartCard>
            <ChartCard title={t("ds.barChart")} legend={series}><BarChart series={series} xLabels={months} summary={t("ds.barChart")} /></ChartCard>
          </div>
          <Table caption={t("ds.table")} rowKey={(r) => r.id} rows={[{ id: "1", n: t("ds.option", { n: 1 }), v: 12 }, { id: "2", n: t("ds.option", { n: 2 }), v: 8 }]}
            columns={[{ id: "n", header: t("ds.name"), primary: true, cell: (r) => r.n }, { id: "v", header: t("ds.value"), cell: (r) => fmt.number(r.v) }]} />
        </Section>

        <Section id="ds-chat" title={t("ds.chat")}>
          <Card>
            <div className="ms-stack">
              <ChatBubble role="user">{t("lp.sampleQuestion")}</ChatBubble>
              <ChatBubble role="assistant">{t("lp.sampleAnswer")}</ChatBubble>
              <MessageComposer value="" onChange={() => undefined} onSend={() => undefined} onVoice={() => undefined} onImage={() => undefined}
                labels={{ input: t("ai.input"), placeholder: t("ai.placeholder"), send: t("ai.send"), voice: t("ai.voice"), stopVoice: t("ai.stopVoice"), image: t("ai.image"), removeAttachment: t("ai.removeAttachment"), prototypeNote: t("ai.imageNote") }} />
            </div>
          </Card>
        </Section>

        <Section id="ds-health" title={t("ds.healthcare")}>
          <Async state={data}>
            {(d) => (
              <div className="ms-stack">
                <div className="grid-2">
                  <MedicationCard med={d.meds[0]!} dose={d.doses.find((x) => x.isNext)} />
                  <DrugCard drug={d.meds[1]!.drug} />
                  <PrescriptionCard rx={d.rx[0]!} showPatient />
                  <PatientCard patient={d.patients[1]!} />
                  <ADRCard report={d.adr[0]!} showPatient />
                  <RiskCard patient={d.patients[3]!} reasons={[t("phys.reasonAdherence", { n: fmt.percent(54) }), t("phys.reasonSymptom")]} />
                  {d.ix[0] && <InteractionCard interaction={d.ix[0]} />}
                  <AdherenceCard value={86} series={d.patients[0]!.adherenceSeries} />
                  <CheckInCard done={false} onStart={() => undefined} />
                  <AIInsightCard title={t("phys.aiSummary")} text={t("lp.sampleAnswer")} confidence="medium" sources={[d.ix[0]!.source]} />
                  <EvidenceCard evidence={d.evidence} index={1} />
                </div>
              </div>
            )}
          </Async>
        </Section>
      </main>

      <Modal open={modal === "modal"} onClose={() => setModal(null)} title={t("ds.modal")} closeLabel={t("common.close")} actions={<><Button variant="ghost" onClick={() => setModal(null)}>{t("common.cancel")}</Button><Button onClick={() => setModal(null)}>{t("common.ok")}</Button></>}><p>{t("ds.sampleText")}</p></Modal>
      <BottomSheet open={modal === "sheet"} onClose={() => setModal(null)} title={t("ds.bottomSheet")} closeLabel={t("common.close")}><p>{t("ds.sampleText")}</p></BottomSheet>
      <Drawer open={modal === "drawer"} onClose={() => setModal(null)} title={t("ds.drawer")} closeLabel={t("common.close")}><p>{t("ds.sampleText")}</p></Drawer>
    </div>
  );
}
