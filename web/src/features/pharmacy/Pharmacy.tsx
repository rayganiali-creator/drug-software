import { useNavigate } from "react-router-dom";
import { DrugCard, Ltr, PrescriptionCard } from "../../components/health/cards";
import { AlertCard, Badge, Button, Card, EmptyState, Icon, List, ListItem, Progress, StatCard, Table, useToast, type Column, type IconName } from "../../components/ui";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import { useAsync, useServices } from "../../services/ServicesProvider";
import type { DispenseStage, InventoryItem } from "../../services/types";
import { Async } from "../common";
import "../features.css";

const stages: DispenseStage[] = ["received", "preparing", "ready", "handed"];
const kindIcon: Record<string, IconName> = { refill: "refresh", question: "message", delivery: "truck" };

export function PharmacyOverview() {
  const { t, loc, fmt } = useI18n();
  const nav = useNavigate();
  const inv = useAsync((s) => s.pharmacy.inventory());
  const disp = useAsync((s) => s.pharmacy.dispensing());
  const req = useAsync((s) => s.pharmacy.requests());
  const alerts = useAsync((s) => s.pharmacy.alerts());
  return (
    <>
      <PageHeader title={t("nav.overview")} subtitle={t("pharmacy.sub")} />
      <div className="ms-stack">
        <div className="grid-stats">
          <StatCard icon="truck" label={t("pharmacy.statToDispense")} value={disp.status === "ready" ? fmt.number(disp.data.filter((d) => d.stage !== "handed").length) : "…"} />
          <StatCard icon="box" label={t("pharmacy.statLow")} value={inv.status === "ready" ? fmt.number(inv.data.filter((i) => i.status === "low").length) : "…"} />
          <StatCard icon="inbox" label={t("pharmacy.statRequests")} value={req.status === "ready" ? fmt.number(req.data.length) : "…"} />
          <StatCard icon="bell" label={t("pharmacy.statAlerts")} value={alerts.status === "ready" ? fmt.number(alerts.data.length) : "…"} />
        </div>
        <div className="split">
          <Card title={t("nav.alerts")} actions={<Button variant="ghost" size="sm" onClick={() => nav("/app/pharmacy/alerts")}>{t("common.viewAll")}</Button>}>
            <Async state={alerts} rows={1}>{(list) => <div className="ms-stack" style={{ gap: "var(--space-3)" }}>{list.map((a) => <AlertCard key={a.id} tone={a.severity} title={`${loc(a.drug.name)} · ${t(`pharmacy.alert.${a.kind}`)}`}>{loc(a.text)}</AlertCard>)}</div>}</Async>
          </Card>
          <Card title={t("nav.requests")} flush actions={<Button variant="ghost" size="sm" onClick={() => nav("/app/pharmacy/requests")}>{t("common.viewAll")}</Button>}>
            <Async state={req} rows={1}>{(list) => <List label={t("nav.requests")}>{list.map((r) => <ListItem key={r.id} title={loc(r.patient.name)} meta={`${t(`pharmacy.kind.${r.kind}`)} · ${loc(r.drug.name)} · ${fmt.relativeMinutes(r.agoMinutes)}`} leading={<Icon name={kindIcon[r.kind] ?? "inbox"} />} />)}</List>}</Async>
          </Card>
        </div>
      </div>
    </>
  );
}

export function PharmacyInventory() {
  const { t, loc, fmt } = useI18n();
  const inv = useAsync((s) => s.pharmacy.inventory());
  const tone = { ok: "success", low: "danger", expiring: "warning" } as const;
  return (
    <>
      <PageHeader title={t("nav.inventory")} subtitle={t("pharmacy.invSub")} />
      <Async state={inv}>
        {(list) => {
          const cols: Column<InventoryItem>[] = [
            { id: "d", header: t("pharmacy.drug"), primary: true, cell: (i) => <span><strong>{loc(i.drug.name)}</strong> <Ltr>{i.drug.strength}</Ltr></span> },
            { id: "b", header: t("pharmacy.batch"), cell: (i) => <Ltr>{i.batch}</Ltr> },
            { id: "s", header: t("pharmacy.stock"), cell: (i) => (
              <div style={{ minInlineSize: 120 }}>
                <div>{fmt.number(i.stock)} / {fmt.number(i.reorderLevel)}</div>
                <Progress value={Math.min(100, (i.stock / (i.reorderLevel * 2)) * 100)} label={t("pharmacy.stock")} tone={i.status === "low" ? "danger" : "primary"} />
              </div>
            ) },
            { id: "e", header: t("pharmacy.expiry"), cell: (i) => t("pharmacy.months", { n: fmt.number(i.expiresInMonths) }) },
            { id: "st", header: t("pharmacy.status"), cell: (i) => <Badge tone={tone[i.status]} icon={i.status === "ok" ? "check" : "alertTriangle"}>{t(`pharmacy.status.${i.status}`)}</Badge> },
          ];
          return <Table caption={t("nav.inventory")} columns={cols} rows={list} rowKey={(i) => i.drug.id} />;
        }}
      </Async>
    </>
  );
}

export function PharmacyPrescriptions() {
  const { t } = useI18n();
  const rx = useAsync((s) => s.prescriptions.list());
  return (
    <>
      <PageHeader title={t("nav.prescriptions")} subtitle={t("pharmacy.rxSub")} />
      <Async state={rx}>{(list) => <div className="grid-2">{list.map((r) => <PrescriptionCard key={r.id} rx={r} showPatient />)}</div>}</Async>
    </>
  );
}

export function PharmacyDispensing() {
  const { t, loc } = useI18n();
  const toast = useToast();
  const services = useServices();
  const disp = useAsync((s) => s.pharmacy.dispensing());
  return (
    <>
      <PageHeader title={t("nav.dispensing")} subtitle={t("pharmacy.dispSub")} />
      <Async state={disp}>
        {(list) => (
          <div className="kanban">
            {stages.map((st) => {
              const items = list.filter((d) => d.stage === st);
              return (
                <section key={st} className="kanban__col" aria-label={t(`pharmacy.stage.${st}`)}>
                  <h3><span>{t(`pharmacy.stage.${st}`)}</span><Badge>{items.length}</Badge></h3>
                  {items.length === 0 && <p className="hc-sub">{t("pharmacy.emptyStage")}</p>}
                  {items.map((d) => (
                    <Card key={d.id}>
                      <strong>{loc(d.patient.name)}</strong>
                      <div className="hc-sub"><Ltr>{d.prescription.id.toUpperCase()}</Ltr> · {d.prescription.items.map((i) => loc(i.drug.name)).join("، ")}</div>
                      {st !== "handed" && (
                        <div style={{ marginBlockStart: "var(--space-3)" }}>
                          <Button size="sm" variant="tonal" iconEnd="chevronRight" onClick={async () => { await services.pharmacy.advance(d.id); disp.reload(); toast.show({ message: t("pharmacy.advanced"), tone: "success" }); }}>{t("pharmacy.advance")}</Button>
                        </div>
                      )}
                    </Card>
                  ))}
                </section>
              );
            })}
          </div>
        )}
      </Async>
    </>
  );
}

export function PharmacyRequests() {
  const { t, loc, fmt } = useI18n();
  const toast = useToast();
  const req = useAsync((s) => s.pharmacy.requests());
  return (
    <>
      <PageHeader title={t("nav.requests")} subtitle={t("pharmacy.reqSub")} />
      <Async state={req}>
        {(list) => list.length === 0 ? <EmptyState icon="inbox" title={t("pharmacy.noRequests")} /> : (
          <Card flush>
            <List label={t("nav.requests")}>
              {list.map((r) => (
                <ListItem key={r.id} title={`${loc(r.patient.name)} · ${t(`pharmacy.kind.${r.kind}`)}`} meta={`${loc(r.drug.name)} · ${loc(r.note)} · ${fmt.relativeMinutes(r.agoMinutes)}`} leading={<Icon name={kindIcon[r.kind] ?? "inbox"} />}
                  trailing={<Button size="sm" variant="secondary" onClick={() => toast.show({ message: t("pharmacy.requestHandled"), tone: "success" })}>{t("pharmacy.handle")}</Button>} />
              ))}
            </List>
          </Card>
        )}
      </Async>
    </>
  );
}

export function PharmacyAlerts() {
  const { t, loc } = useI18n();
  const alerts = useAsync((s) => s.pharmacy.alerts());
  const inventory = useAsync((s) => s.pharmacy.inventory());
  return (
    <>
      <PageHeader title={t("nav.alerts")} subtitle={t("pharmacy.alertsSub")} />
      <Async state={alerts}>{(list) => <div className="ms-stack">{list.map((a) => <AlertCard key={a.id} tone={a.severity} title={`${loc(a.drug.name)} · ${t(`pharmacy.alert.${a.kind}`)}`}>{loc(a.text)}</AlertCard>)}</div>}</Async>
      <div className="grid-3" style={{ marginBlockStart: "var(--space-6)" }}>
        <Async state={inventory}>{(inv) => <>{inv.filter((i) => i.status !== "ok").map((i) => <DrugCard key={i.drug.id} drug={i.drug} />)}</>}</Async>
      </div>
    </>
  );
}
