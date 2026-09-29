import { Badge, Card } from "../../components/ui";
import { useAuth } from "../../auth/AuthContext";
import { useI18n } from "../../i18n/I18nProvider";
import { PageHeader } from "../../layouts/PageHeader";
import "./auth.css";

/** Placeholder home for roles whose tools arrive in later phases (content and AI management). */
export function Workspace() {
  const { t } = useI18n();
  const { user } = useAuth();
  return (
    <>
      <PageHeader title={t("ws.title")} subtitle={t("ws.body")} />
      <Card>
        <ul className="acct-perms" style={{ listStyle: "none", margin: 0, padding: 0 }} aria-label={t("acct.permissions")}>
          {user?.permissions.map((p) => <li key={p}><Badge>{p}</Badge></li>)}
        </ul>
      </Card>
    </>
  );
}
