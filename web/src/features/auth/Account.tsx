import { Badge, Button, Card, EmptyState, LoadingState, Table, useToast, type Column } from "../../components/ui";
import { permissions as P } from "../../auth/access.g";
import { useAuth } from "../../auth/AuthContext";
import type { AccessLogView, ConsentView, SessionView } from "../../auth/types";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import "./auth.css";
import { useLoad } from "./useBackend";

/** Sessions, sharing consents and the "who accessed my data" log. Each block needs its own permission. */
export function Account() {
  const { t } = useI18n();
  const { user, can } = useAuth();
  if (!user) return null;
  return (
    <>
      <PageHeader title={t("acct.title")} subtitle={t("acct.sub")} />
      <div className="ms-stack">
        {can(P.sessionRead) && <Sessions />}
        {can(P.consentRead) && <Consents />}
        {can(P.auditReadOwn) && <AccessLog />}
        <Card title={t("acct.permissions")} subtitle={t("acct.permsNote")}>
          <div className="ms-stack">
            <div className="acct-perms">{user.roles.map((r) => <Badge key={r} tone="primary">{t(`authRole.${r}`)}</Badge>)}</div>
            <ul className="acct-perms" style={{ listStyle: "none", margin: 0, padding: 0 }} aria-label={t("acct.permissions")}>
              {user.permissions.map((p) => <li key={p}><Badge>{p}</Badge></li>)}
            </ul>
          </div>
        </Card>
      </div>
    </>
  );
}

function Sessions() {
  const { t, fmt } = useI18n();
  const { backend, signOut } = useAuth();
  const toast = useToast();
  const [rows, ready, reload] = useLoad(() => backend.sessions(), [] as SessionView[]);
  return (
    <Card title={t("acct.sessions")} actions={<Button size="sm" variant="secondary" onClick={() => void backend.signOutEverywhere().then(() => signOut())}>{t("acct.signOutAll")}</Button>}>
      {!ready ? <LoadingState label={t("common.loading")} rows={1} /> : (
        <ul className="ms-stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>
          {rows.map((s) => (
            <li key={s.id} className="acct-row">
              <div>
                <strong>{s.deviceName}</strong> {s.isCurrent && <Badge tone="success">{t("acct.sessionCurrent")}</Badge>}
                <div className="ms-muted">{t("acct.lastSeen", { when: fmt.date(new Date(s.lastSeenAt), "short") })}</div>
              </div>
              {!s.isCurrent && (
                <Button size="sm" variant="secondary" onClick={() => void backend.revokeSession(s.id).then(() => { toast.show({ message: t("acct.sessionRevoked"), tone: "success" }); reload(); })}>{t("acct.sessionRevoke")}</Button>
              )}
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

function Consents() {
  const { t, fmt } = useI18n();
  const { backend } = useAuth();
  const toast = useToast();
  const [rows, ready, reload] = useLoad(() => backend.consents(), [] as ConsentView[]);
  const tone = { active: "success", expired: "warning", revoked: "neutral" } as const;
  const label = { active: "acct.consentActive", expired: "acct.consentExpired", revoked: "acct.consentRevokedStatus" } as const;
  return (
    <Card title={t("acct.consents")} subtitle={t("acct.consentsSub")}>
      {!ready ? <LoadingState label={t("common.loading")} rows={1} /> : rows.length === 0 ? <EmptyState icon="shield" title={t("acct.consentNone")} /> : (
        <ul className="ms-stack" style={{ listStyle: "none", margin: 0, padding: 0 }} aria-label={t("acct.consents")}>
          {rows.map((c) => (
            <li key={c.id} className="acct-row" data-testid="consent-row">
              <div>
                <strong>{c.granteeName}</strong> <Badge tone={tone[c.status]}>{t(label[c.status])}</Badge>
                <div className="ms-muted">{t("acct.consentPurpose")}: {t(`purpose.${c.purpose}`)} · {t("acct.consentExpires", { date: fmt.date(new Date(c.expiresAt), "short") })}</div>
                <div className="ms-muted">{t("acct.consentScope")}: {c.scope.map((s) => t(`scope.${s}`)).join("، ")}</div>
              </div>
              {c.status === "active" && (
                <Button size="sm" variant="secondary" onClick={() => void backend.revokeConsent(c.id).then(() => { toast.show({ message: t("acct.consentRevokedToast"), tone: "success" }); reload(); })}>{t("acct.consentRevoke")}</Button>
              )}
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

function AccessLog() {
  const { t, fmt } = useI18n();
  const { backend } = useAuth();
  const [rows, ready] = useLoad(() => backend.accessLog(), [] as AccessLogView[]);
  const cols: Column<AccessLogView>[] = [
    { id: "when", header: t("admin.time"), cell: (r) => fmt.date(new Date(r.at), "short"), primary: true },
    { id: "who", header: t("admin.actor"), cell: (r) => r.actorName },
    { id: "what", header: t("admin.action"), cell: (r) => t("acct.accessDataViewed", { what: t(`scope.${r.what}`) }) },
  ];
  return (
    <Card title={t("acct.accessLog")} subtitle={t("acct.accessLogSub")}>
      {!ready ? <LoadingState label={t("common.loading")} rows={1} /> : rows.length === 0 ? <EmptyState icon="eye" title={t("acct.accessLogNone")} /> : (
        <Table caption={t("acct.accessLog")} columns={cols} rows={rows} rowKey={(r) => r.at + r.actorName + r.what} />
      )}
    </Card>
  );
}
