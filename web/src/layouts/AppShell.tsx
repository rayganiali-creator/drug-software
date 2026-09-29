import { useEffect, useState } from "react";
import { Link, NavLink, Outlet, useNavigate } from "react-router-dom";
import { Logo } from "../components/brand/Logo";
import { BadgeAnchor, Drawer, Dropdown, Icon, IconButton, SearchField, useToast } from "../components/ui";
import { useBreakpoint } from "../hooks/useBreakpoint";
import { useI18n } from "../i18n/I18nProvider";
import { useAsync } from "../services/ServicesProvider";
import { useTheme } from "../theme/ThemeProvider";
import { navByRole, roleIcon, roles, type Role } from "./roles";
import "./shell.css";

const COLLAPSE_KEY = "ms.sidebar.collapsed";

export function DemoBanner() {
  const { t } = useI18n();
  return (
    <div className="demo-banner" role="note">
      <Icon name="alertTriangle" size="xs" />
      <span>{t("demo.banner")}</span>
    </div>
  );
}

export function AppShell({ role }: { role: Role }) {
  const { t, locale, setLocale } = useI18n();
  const { mode, setMode } = useTheme();
  const bp = useBreakpoint();
  const nav = useNavigate();
  const toast = useToast();
  const [collapsed, setCollapsed] = useState(() => {
    try { return localStorage.getItem(COLLAPSE_KEY) === "1"; } catch { return false; }
  });
  const [drawer, setDrawer] = useState(false);
  const [search, setSearch] = useState("");
  const alerts = useAsync((s) => s.notifications.alerts("pt-sara"), []);
  const alertCount = role === "patient" && alerts.status === "ready" ? alerts.data.length : 0;

  useEffect(() => { try { localStorage.setItem(COLLAPSE_KEY, collapsed ? "1" : "0"); } catch { /* ignore */ } }, [collapsed]);

  const items = navByRole[role];
  const compact = bp === "compact";
  const bottomNav = compact && role === "patient";
  const drawerNav = compact && role !== "patient";
  const railOnly = bp === "medium";
  const isCollapsed = railOnly || collapsed;

  const link = (it: (typeof items)[number]) => (
    <NavLink key={it.id} to={it.path ? `/app/${role}/${it.path}` : `/app/${role}`} end={it.end} className="navlink" aria-label={t(it.labelKey)} title={isCollapsed ? t(it.labelKey) : undefined}>
      <Icon name={it.icon} />
      <span className="navlink__label">{t(it.labelKey)}</span>
    </NavLink>
  );

  return (
    <>
      <a className="skip-link" href="#main">{t("a11y.skip")}</a>
      <DemoBanner />
      <div className="shell" data-collapsed={isCollapsed} data-bottomnav={bottomNav} data-nav={compact ? "none" : "side"}>
        <aside className="shell__side" aria-label={t("nav.primary")}>
          <div className="shell__brand"><Link to="/" aria-label={t("app.name")}><Logo compact={isCollapsed} /></Link></div>
          <nav className="shell__nav" aria-label={t(`role.${role}`)}>{items.map(link)}</nav>
          <div className="shell__spacer" />
          <div className="shell__demo-note">{t("demo.sidebarNote")}</div>
          {bp !== "medium" && (
            <IconButton icon={isCollapsed ? "chevronRight" : "chevronLeft"} label={isCollapsed ? t("nav.expand") : t("nav.collapse")} onClick={() => setCollapsed((c) => !c)} />
          )}
        </aside>

        <header className="shell__top">
          {drawerNav && <IconButton icon="menu" label={t("nav.open")} onClick={() => setDrawer(true)} />}
          {compact && <Link to="/" aria-label={t("app.name")}><Logo compact /></Link>}
          <div className="shell__top-search">
            <SearchField label={t("search.label")} clearLabel={t("search.clear")} value={search} onValueChange={setSearch}
              onKeyDown={(e) => { if (e.key === "Enter" && search) toast.show({ message: t("search.prototype"), tone: "info" }); }} />
          </div>
          <div className="shell__top-actions">
            <Dropdown label={t("role.switch")} align="end" triggerIcon={roleIcon[role]}
              triggerContent={<span className="navlink__label" style={{ display: compact ? "none" : "inline" }}>{t(`role.${role}`)}</span>}
              items={roles.map((r) => ({ id: r, label: t(`role.${r}`), icon: roleIcon[r], checked: r === role, onSelect: () => nav(`/app/${r}`) }))} />
            <Dropdown label={t("theme.label")} align="end" triggerIcon={mode === "dark" ? "moon" : "sun"}
              items={(["system", "light", "dark"] as const).map((m) => ({ id: m, label: t(`theme.${m}`), icon: m === "dark" ? "moon" : "sun", checked: mode === m, onSelect: () => setMode(m) }))} />
            <IconButton icon="globe" label={locale === "fa" ? t("lang.switchToEn") : t("lang.switchToFa")} onClick={() => setLocale(locale === "fa" ? "en" : "fa")} />
            {role === "patient" && (
              <BadgeAnchor count={alertCount} label={t("notif.count", { n: alertCount })}>
                <IconButton icon="bell" label={t("notif.label")} onClick={() => nav("/app/patient")} />
              </BadgeAnchor>
            )}
          </div>
        </header>

        <main className="shell__main" id="main" tabIndex={-1}>
          <div className="shell__content"><Outlet /></div>
        </main>

        {bottomNav && (
          <nav className="bottomnav" aria-label={t("nav.primary")}>
            {items.map((it) => (
              <NavLink key={it.id} to={it.path ? `/app/${role}/${it.path}` : `/app/${role}`} end={it.end} className={it.id === "assistant" ? "bottomnav__ai" : undefined}>
                <span className="bottomnav__icon"><Icon name={it.icon} /></span>
                <span>{t(it.labelKey)}</span>
              </NavLink>
            ))}
          </nav>
        )}
      </div>
      <Drawer open={drawer && drawerNav} onClose={() => setDrawer(false)} title={t("app.name")} closeLabel={t("common.close")}>
        <nav className="drawer-nav" aria-label={t(`role.${role}`)} onClick={() => setDrawer(false)}>{items.map(link)}</nav>
      </Drawer>
    </>
  );
}
