import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { AlertCard, Badge, Button, Card, EmptyState, ErrorState, Icon, LoadingState, SearchField, type Tone } from "../../components/ui";
import { useBreakpoint } from "../../hooks/useBreakpoint";
import { useI18n, type Locale } from "../../i18n/I18nProvider";
import { useMedicationApi } from "../../knowledge/useMedicationApi";
import {
  KnowledgeError, type InteractionSeverity, type LocalizedText, type MedicationDetail, type MedicationSummary, type StatementKind, type ValidationStatus,
} from "../../knowledge/types";
import { PageHeader } from "../../layouts/PageHeader";
import "../features.css";
import "./knowledge.css";

const PAGE = 20;

/** Picks the text for the current language, falling back to the other one (never an empty label for a record that has a name). */
function useL() {
  const { locale } = useI18n();
  const other: Locale = locale === "fa" ? "en" : "fa";
  return (x: LocalizedText | null | undefined) => (x ? (x[locale] ?? x[other] ?? "") : "");
}

const statusTone: Record<ValidationStatus, Tone> = { Demo: "warning", Validated: "success", Unverified: "danger", NeedsValidation: "warning", Rejected: "danger" };
const statusKey: Record<ValidationStatus, string> = { Demo: "kn.status.demo", Validated: "kn.status.validated", Unverified: "kn.status.unverified", NeedsValidation: "kn.status.needs", Rejected: "kn.status.rejected" };
const sevTone: Record<InteractionSeverity, Tone> = { Unknown: "neutral", Minor: "info", Moderate: "warning", Major: "danger", Contraindicated: "danger" };

/** Shows the validation state with text AND icon, never colour alone. Demo and unverified data can never look "official". */
export function StatusBadge({ status }: { status: ValidationStatus }) {
  const { t } = useI18n();
  return <Badge tone={statusTone[status]} icon={status === "Validated" ? "check" : "alertTriangle"}>{t(statusKey[status])}</Badge>;
}

function ErrorPanel({ error, onRetry }: { error: unknown; onRetry(): void }) {
  const { t } = useI18n();
  const kind = error instanceof KnowledgeError ? error.kind : "server";
  if (kind === "notConnected") return <EmptyState icon="lock" title={t("kn.notConnected.title")} body={t("kn.notConnected.body")} />;
  if (kind === "unauthorized") return <EmptyState icon="shield" title={t("kn.unauthorized.title")} />;
  if (kind === "rateLimited") return <EmptyState icon="clock" title={t("kn.rate.title")} body={t("kn.rate.body")} action={<Button variant="secondary" onClick={onRetry}>{t("common.retry")}</Button>} />;
  if (kind === "invalid") return <EmptyState icon="search" title={t("kn.invalid.title")} body={t("kn.invalid.body")} />;
  if (kind === "notFound") return <EmptyState icon="search" title={t("kn.notFound.title")} body={t("kn.notFound.body")} />;
  return <ErrorState title={t("kn.error.title")} body={t("kn.error.body")} retryLabel={t("common.retry")} onRetry={onRetry} />;
}

type SearchState = { status: "loading" } | { status: "error"; error: unknown } | { status: "ready"; items: MedicationSummary[]; total: number; loadingMore: boolean };

/** Drug reference: debounced search over the real API, with a master-detail layout on wide screens. */
export function DrugSearch() {
  const { t } = useI18n();
  const L = useL();
  const api = useMedicationApi();
  const bp = useBreakpoint();
  const wide = bp === "expanded" || bp === "large";
  const loc = useLocation();
  const base = useMemo(() => loc.pathname.replace(/\/drugs.*$/, "/drugs"), [loc.pathname]);
  const [params, setParams] = useSearchParams();
  const selected = wide ? params.get("id") : null;
  const [query, setQuery] = useState(params.get("q") ?? "");
  const [debounced, setDebounced] = useState(query);
  const [tick, setTick] = useState(0);
  const [state, setState] = useState<SearchState>({ status: "loading" });
  const inFlight = useRef<AbortController | null>(null);

  useEffect(() => {
    const h = setTimeout(() => setDebounced(query), 300);
    return () => clearTimeout(h);
  }, [query]);

  const trimmed = debounced.trim();
  const tooShort = trimmed.length === 1;

  useEffect(() => {
    if (tooShort) return;
    inFlight.current?.abort();
    const ctl = new AbortController();
    inFlight.current = ctl;
    api.search({ q: trimmed, limit: PAGE, offset: 0 }, ctl.signal).then(
      (r) => { if (!ctl.signal.aborted) setState({ status: "ready", items: r.items, total: r.total, loadingMore: false }); },
      (error) => { if (!ctl.signal.aborted && !(error instanceof DOMException)) setState({ status: "error", error }); },
    );
    return () => ctl.abort();
  }, [api, trimmed, tooShort, tick]);

  // keep the query in the URL (shareable, survives reload) without adding history entries
  useEffect(() => {
    setParams((p) => { const n = new URLSearchParams(p); if (trimmed) n.set("q", trimmed); else n.delete("q"); return n; }, { replace: true });
  }, [trimmed, setParams]);

  const loadMore = useCallback(() => {
    setState((s) => (s.status === "ready" ? { ...s, loadingMore: true } : s));
    const offset = state.status === "ready" ? state.items.length : 0;
    api.search({ q: trimmed, limit: PAGE, offset }).then(
      (r) => setState((s) => (s.status === "ready" ? { status: "ready", items: [...s.items, ...r.items], total: r.total, loadingMore: false } : s)),
      (error) => setState({ status: "error", error }),
    );
  }, [api, state, trimmed]);

  const list = (
    <section aria-label={t("kn.title")} className="ms-stack">
      <SearchField label={t("kn.search")} clearLabel={t("search.clear")} value={query} onValueChange={(v) => { setQuery(v); setState({ status: "loading" }); }} placeholder={t("kn.searchHint")} maxLength={64} />
      {tooShort && <p className="ms-muted" role="status">{t("kn.invalid.body")}</p>}
      {!tooShort && state.status === "loading" && <LoadingState label={t("common.loading")} rows={3} />}
      {!tooShort && state.status === "error" && <ErrorPanel error={state.error} onRetry={() => { setState({ status: "loading" }); setTick((n) => n + 1); }} />}
      {!tooShort && state.status === "ready" && (state.items.length === 0 ? (
        <EmptyState icon="search" title={t("kn.empty.title")} body={t("kn.empty.body")} />
      ) : (
        <>
          <p className="ms-muted" role="status" aria-live="polite">{t("kn.showing", { from: 1, to: state.items.length, total: state.total })}</p>
          <ul className="kn-list" aria-label={t("kn.title")}>
            {state.items.map((m) => (
              <li key={m.id}>
                <Link className="kn-item" to={wide ? `?${new URLSearchParams({ ...(trimmed ? { q: trimmed } : {}), id: m.id })}` : `${base}/${m.id}`} aria-current={selected === m.id ? "true" : undefined} data-selected={selected === m.id}>
                  <span className="kn-item__name">{L(m.name)}</span>
                  <span className="kn-item__meta">
                    {[m.brandName ? L(m.brandName) : t("kn.d.generic"), L(m.dosageForm)].filter(Boolean).join(" · ")}
                    {m.strengthSummary && <> · <bdi dir="ltr">{m.strengthSummary}</bdi></>}
                  </span>
                  <span className="kn-item__ing">{m.ingredients.map(L).join(" + ")}</span>
                  <span className="kn-item__badges">
                    <StatusBadge status={m.validation} />
                    {m.lifecycle !== "Active" && <Badge tone="neutral">{t(m.lifecycle === "Inactive" ? "kn.life.inactive" : "kn.life.draft")}</Badge>}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
          {state.items.length < state.total && <Button variant="secondary" loading={state.loadingMore} onClick={loadMore}>{t("kn.more")}</Button>}
        </>
      ))}
    </section>
  );

  return (
    <>
      <PageHeader title={t("kn.title")} subtitle={t("kn.sub")} />
      {wide ? (
        <div className="kn-split">
          {list}
          <div className="kn-pane" aria-live="polite">
            {selected ? <DrugDetailView id={selected} /> : <Card><EmptyState icon="pill" title={t("kn.pickOne")} /></Card>}
          </div>
        </div>
      ) : list}
    </>
  );
}

/** Route wrapper for a single medicine on compact/medium screens. */
export function DrugDetail() {
  const { id } = useParams();
  const { t } = useI18n();
  const nav = useNavigate();
  const loc = useLocation();
  const back = loc.pathname.replace(/\/drugs\/.*$/, "/drugs");
  return (
    <>
      <PageHeader title={t("kn.title")} actions={<Button variant="ghost" iconStart="chevronLeft" onClick={() => nav(back)}>{t("kn.back")}</Button>} />
      {id ? <DrugDetailView id={id} /> : null}
    </>
  );
}

export function DrugDetailView({ id }: { id: string }) {
  const { t, fmt } = useI18n();
  const L = useL();
  const api = useMedicationApi();
  const [tick, setTick] = useState(0);
  type Result = { status: "error"; error: unknown } | { status: "ready"; d: MedicationDetail };
  const key = `${id}:${tick}`;
  const [loaded, setLoaded] = useState<{ key: string; result: Result } | null>(null);
  const state: { status: "loading" } | Result = loaded && loaded.key === key ? loaded.result : { status: "loading" };

  useEffect(() => {
    const ctl = new AbortController();
    api.detail(id, ctl.signal).then(
      (d) => { if (!ctl.signal.aborted) setLoaded({ key, result: { status: "ready", d } }); },
      (error) => { if (!ctl.signal.aborted && !(error instanceof DOMException)) setLoaded({ key, result: { status: "error", error } }); },
    );
    return () => ctl.abort();
  }, [api, id, key]);

  if (state.status === "loading") return <LoadingState label={t("common.loading")} rows={3} />;
  if (state.status === "error") return <ErrorPanel error={state.error} onRetry={() => setTick((n) => n + 1)} />;
  const d = state.d;
  const noticeKey = d.isDemo ? "kn.notice.demo" : d.validation === "Validated" ? "kn.notice.validated" : "kn.notice.unverified";
  const kinds = [...new Set(d.statements.map((s) => s.kind))] as StatementKind[];
  const sourceName = (sid: string) => d.sources.find((s) => s.id === sid)?.name ?? "";

  return (
    <article className="kn-detail ms-stack" aria-labelledby="kn-name">
      <AlertCard tone={d.isDemo || d.validation !== "Validated" ? "warning" : "info"} icon={d.validation === "Validated" && !d.isDemo ? "info" : "alertTriangle"} title={t(noticeKey)} />
      <header>
        <h2 id="kn-name" className="kn-title" tabIndex={-1}>{L(d.name)}</h2>
        <div className="kn-item__badges">
          <StatusBadge status={d.validation} />
          {d.lifecycle !== "Active" && <Badge tone="neutral">{t(d.lifecycle === "Inactive" ? "kn.life.inactive" : "kn.life.draft")}</Badge>}
        </div>
        <p className="ms-muted">{t("kn.d.updated", { date: fmt.date(new Date(d.updatedAt)), n: fmt.number(d.version) })}</p>
      </header>

      <Card title={t("kn.d.form")} flush>
        <dl className="kn-facts">
          <div><dt>{t("kn.d.brand")}</dt><dd>{d.brand ? L(d.brand.name) : t("kn.d.generic")}</dd></div>
          <div><dt>{t("kn.d.form")}</dt><dd>{L(d.dosageForm)}</dd></div>
          <div><dt>{t("kn.d.route")}</dt><dd>{d.routes.map(L).join("، ") || "—"}</dd></div>
          <div><dt>{t("kn.d.strength")}</dt><dd><bdi dir="ltr">{d.strengthSummary || "—"}</bdi></dd></div>
          {d.manufacturer && <div><dt>{t("kn.d.manufacturer")}</dt><dd>{L(d.manufacturer.name)}</dd></div>}
          {d.classifications.length > 0 && <div><dt>{t("kn.d.classes")}</dt><dd>{d.classifications.map((c) => L(c.name)).join("، ")}</dd></div>}
          {d.synonyms.length > 0 && <div><dt>{t("kn.d.synonyms")}</dt><dd>{d.synonyms.join("، ")}</dd></div>}
        </dl>
      </Card>

      <Card title={t("kn.d.ingredients")}>
        <ul className="kn-plain">
          {d.ingredients.map((i) => (
            <li key={i.ingredientId}><strong>{L(i.name)}</strong>{i.strengthValue !== null && <> — <bdi dir="ltr">{i.strengthValue} {i.strengthUnit}{i.perUnit ? ` / ${i.perUnit}` : ""}</bdi></>}</li>
          ))}
        </ul>
      </Card>

      <Card title={t("kn.d.statements")}>
        <div className="ms-stack">
          {kinds.map((k) => (
            <section key={k} aria-label={t(`kn.kind.${k}`)}>
              <h3 className="kn-h3">{t(`kn.kind.${k}`)}</h3>
              <ul className="kn-plain">
                {d.statements.filter((s) => s.kind === k).map((s) => (
                  <li key={s.id}>
                    <span>{L(s.text)}</span>{" "}
                    <StatusBadge status={s.validation} />{" "}
                    <span className="ms-muted kn-src">{sourceName(s.sourceId)}</span>
                  </li>
                ))}
              </ul>
            </section>
          ))}
          {d.missingKinds.length > 0 && (
            <section className="kn-missing" aria-label={t("kn.d.missing")}>
              <h3 className="kn-h3"><Icon name="info" size="sm" /> {t("kn.d.missing")}</h3>
              <p className="ms-muted">{t("kn.d.missingHint", { kinds: d.missingKinds.map((k) => t(`kn.kind.${k}`)).join("، ") })}</p>
            </section>
          )}
        </div>
      </Card>

      <Card title={t("kn.d.interactions")}>
        {d.interactions.length === 0 ? <p className="ms-muted">{t("kn.d.noInteractions")}</p> : (
          <ul className="kn-plain">
            {d.interactions.map((i) => (
              <li key={i.id}>
                <Badge tone={sevTone[i.severity]} icon="alertTriangle">{t(`kn.sev.${i.severity}`)}</Badge>{" "}
                <strong>{t("kn.d.with", { name: L(i.otherIngredientName) })}</strong>
                <div>{L(i.mechanism)}</div>
                <div className="ms-muted">{L(i.management)}</div>
                <StatusBadge status={i.validation} />
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card title={t("kn.d.identifiers")}>
        {d.identifiers.length === 0 ? <p className="ms-muted">{t("kn.d.noIdentifiers")}</p> : (
          <ul className="kn-plain">{d.identifiers.map((x) => <li key={x.scheme + x.value}>{x.scheme}: <bdi dir="ltr">{x.value}</bdi></li>)}</ul>
        )}
      </Card>

      <Card title={t("kn.d.sources")}>
        <ul className="kn-plain">
          {d.sources.map((s) => (
            <li key={s.id}><strong>{s.name}</strong> · {s.publisher} · {t("kn.d.sourceVersion", { v: s.version })} · {t("kn.d.licence", { name: s.licenseName })}</li>
          ))}
        </ul>
      </Card>
    </article>
  );
}
