import { useState } from "react";
import { AlertCard, Badge, Button, Card, EmptyState, LoadingState, Select, Table, useToast, type Column } from "../../components/ui";
import { roleNames, type RoleName } from "../../auth/access.g";
import { useAuth } from "../../auth/AuthContext";
import type { AdminUserView, AuditView } from "../../auth/types";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import "./auth.css";
import { useLoad } from "./useBackend";

/** System administration: users, roles and the audit log. Deliberately has NO patient-record views. */
export function AdminConsole() {
  const { t } = useI18n();
  return (
    <>
      <PageHeader title={t("admin.title")} subtitle={t("admin.sub")} />
      <div className="ms-stack">
        <AlertCard tone="info" icon="shield" title={t("admin.noPatientData")} />
        <Users />
        <Audit />
      </div>
    </>
  );
}

function Users() {
  const { t } = useI18n();
  const { backend } = useAuth();
  const toast = useToast();
  const [rows, ready, reload] = useLoad(() => backend.adminUsers(), [] as AdminUserView[]);
  const [picked, setPicked] = useState<Record<string, RoleName>>({});
  const done = (ok: boolean, okKey: string) => { toast.show({ message: t(ok ? okKey : "admin.roleError"), tone: ok ? "success" : "danger" }); reload(); };
  return (
    <Card title={t("admin.users")} flush>
      {!ready ? <LoadingState label={t("common.loading")} rows={2} /> : rows.length === 0 ? <EmptyState icon="users" title={t("common.empty")} /> : (
        <div className="ms-stack" style={{ padding: "var(--space-3)" }}>
          {rows.map((u) => (
            <div key={u.id} className="acct-row" data-testid="admin-user">
              <div>
                <strong>{u.displayName}</strong> {u.status !== "Active" && <Badge tone="danger">{u.status}</Badge>}
                <div className="acct-perms" style={{ marginBlockStart: "var(--space-1)" }}>
                  {u.roles.map((r) => (
                    <Button key={r} size="sm" variant="secondary" iconEnd="x" aria-label={t("admin.revoke", { role: t(`authRole.${r}`) })}
                      onClick={() => void backend.adminRevokeRole(u.id, r).then((ok) => done(ok, "admin.roleRemoved"))}>{t(`authRole.${r}`)}</Button>
                  ))}
                </div>
              </div>
              <div className="ms-row">
                <Select label={t("admin.assign")} value={picked[u.id] ?? ""} onValueChange={(v) => setPicked((p) => ({ ...p, [u.id]: v as RoleName }))}
                  options={[{ value: "", label: "—" }, ...roleNames.filter((r) => !u.roles.includes(r)).map((r) => ({ value: r, label: t(`authRole.${r}`) }))]} />
                <Button size="sm" disabled={!picked[u.id]} onClick={() => void backend.adminAssignRole(u.id, picked[u.id]!).then((ok) => { setPicked((p) => ({ ...p, [u.id]: "" as RoleName })); done(ok, "admin.roleAdded"); })}>{t("admin.assign")}</Button>
              </div>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}

function Audit() {
  const { t, fmt } = useI18n();
  const { backend } = useAuth();
  const toast = useToast();
  const [rows, ready] = useLoad(() => backend.adminAudit(), [] as AuditView[]);
  const cols: Column<AuditView>[] = [
    { id: "t", header: t("admin.time"), cell: (r) => fmt.date(new Date(r.at), "short"), primary: true },
    { id: "a", header: t("admin.action"), cell: (r) => r.action },
    { id: "r", header: t("admin.result"), cell: (r) => <Badge tone={r.result === "Success" ? "success" : "danger"}>{r.result}</Badge> },
    { id: "w", header: t("admin.actor"), cell: (r) => r.actorName },
  ];
  return (
    <Card title={t("admin.audit")} subtitle={t("admin.auditSub")}
      actions={<Button size="sm" variant="secondary" onClick={() => void backend.adminVerifyAudit().then((ok) => toast.show({ message: t(ok ? "admin.chainOk" : "admin.chainBad"), tone: ok ? "success" : "danger" }))}>{t("admin.auditVerify")}</Button>}>
      {!ready ? <LoadingState label={t("common.loading")} rows={2} /> : rows.length === 0 ? <EmptyState icon="fileText" title={t("common.empty")} /> : (
        <Table caption={t("admin.audit")} columns={cols} rows={rows} rowKey={(r) => r.at + r.action + r.actorName} pageSize={10} />
      )}
    </Card>
  );
}
