import { useMemo, useState, type ElementType, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { useBreakpoint } from "../../hooks/useBreakpoint";
import { Icon, type IconName } from "./Icon";
import { Pagination } from "./navigation";

export type Tone = "neutral" | "primary" | "accent" | "success" | "warning" | "danger" | "info";

// ---------- Card ----------
export interface CardProps {
  variant?: "default" | "elevated" | "tonal" | "ai";
  tone?: Tone;
  flush?: boolean;
  title?: ReactNode;
  subtitle?: ReactNode;
  actions?: ReactNode;
  to?: string;
  as?: ElementType;
  className?: string;
  children?: ReactNode;
  "aria-label"?: string;
}
export function Card({ variant = "default", tone = "neutral", flush, title, subtitle, actions, to, as, className = "", children, ...rest }: CardProps) {
  const cls = ["ms-card", variant !== "default" && `ms-card--${variant}`, flush && "ms-card--flush", to && "ms-card--interactive", className].filter(Boolean).join(" ");
  const head = (title || actions) && (
    <div className="ms-card__header">
      <div>
        {title && <h3 className="ms-card__title">{title}</h3>}
        {subtitle && <div className="ms-card__subtitle">{subtitle}</div>}
      </div>
      {actions}
    </div>
  );
  if (to) return <Link to={to} className={cls} data-tone={tone} {...rest}>{head}{children}</Link>;
  const Tag = (as ?? "section") as ElementType;
  return <Tag className={cls} data-tone={tone} {...rest}>{head}{children}</Tag>;
}

// ---------- Avatar / Badge / Chip ----------
export function Avatar({ name, size = "md", tone = "primary" }: { name: string; size?: "sm" | "md" | "lg"; tone?: Tone }) {
  const initials = name.trim().split(/\s+/).slice(0, 2).map((p) => [...p][0]).join("");
  return <span className={`ms-avatar ms-avatar--${size}`} data-tone={tone} role="img" aria-label={name}>{initials}</span>;
}

export function Badge({ tone = "neutral", solid, dot, icon, children, label }: { tone?: Tone; solid?: boolean; dot?: boolean; icon?: IconName; children?: ReactNode; label?: string }) {
  if (dot) return <span className="ms-badge ms-badge--dot" data-tone={tone} role="img" aria-label={label} />;
  return (
    <span className={`ms-badge${solid ? " ms-badge--solid" : ""}`} data-tone={tone}>
      {icon && <Icon name={icon} size="xs" />}
      {children}
    </span>
  );
}
/** Numeric/notification badge attached to another element. */
export function BadgeAnchor({ count, label, children }: { count: number; label: string; children: ReactNode }) {
  return (
    <span className="ms-badge-anchor">
      {children}
      {count > 0 && <span className="ms-badge ms-badge--solid" data-tone="danger" aria-label={label}>{count}</span>}
    </span>
  );
}

export interface ChipProps {
  children: ReactNode;
  selected?: boolean;
  onClick?(): void;
  onRemove?(): void;
  removeLabel?: string;
  icon?: IconName;
  variant?: "default" | "ai";
  role?: "checkbox" | "button";
}
export function Chip({ children, selected, onClick, onRemove, removeLabel, icon, variant = "default", role }: ChipProps) {
  const cls = `ms-chip${variant === "ai" ? " ms-chip--ai" : ""}`;
  const inner = (<>{icon && <Icon name={icon} size="xs" />}{children}</>);
  if (onClick) {
    return (
      <button type="button" className={cls} role={role} aria-pressed={role === "checkbox" ? undefined : selected} aria-checked={role === "checkbox" ? selected : undefined} onClick={onClick}>{inner}</button>
    );
  }
  return (
    <span className={cls}>
      {inner}
      {onRemove && <button type="button" className="ms-chip__remove" aria-label={removeLabel} onClick={onRemove}><Icon name="x" size="xs" /></button>}
    </span>
  );
}

export function DemoBadge({ label }: { label: string }) {
  return <span className="ms-demo-badge" data-testid="demo-badge">{label}</span>;
}

// ---------- List ----------
export function List({ children, label }: { children: ReactNode; label?: string }) {
  return <ul className="ms-list" aria-label={label}>{children}</ul>;
}
export function ListItem({ title, meta, leading, trailing, to, onClick }: { title: ReactNode; meta?: ReactNode; leading?: ReactNode; trailing?: ReactNode; to?: string; onClick?(): void }) {
  const body = (
    <>
      {leading}
      <span className="ms-list__main"><span className="ms-list__title">{title}</span>{meta && <span className="ms-list__meta" style={{ display: "block" }}>{meta}</span>}</span>
      {trailing}
    </>
  );
  return (
    <li>
      {to ? <Link to={to} className="ms-list__item">{body}</Link> : onClick ? <button type="button" className="ms-list__item" onClick={onClick}>{body}</button> : <div className="ms-list__item">{body}</div>}
    </li>
  );
}

// ---------- Table (cards on compact screens) ----------
export interface Column<T> { id: string; header: string; cell(row: T): ReactNode; primary?: boolean }
export interface TableProps<T> {
  caption: string;
  columns: Column<T>[];
  rows: T[];
  rowKey(row: T): string;
  onRowClick?(row: T): void;
  pageSize?: number;
  paginationLabels?: { nav: string; prev: string; next: string; page: (n: number) => string; summary: (from: number, to: number, total: number) => string };
}
export function Table<T>({ caption, columns, rows, rowKey, onRowClick, pageSize, paginationLabels }: TableProps<T>) {
  const bp = useBreakpoint();
  const [page, setPage] = useState(1);
  const pageCount = pageSize ? Math.max(1, Math.ceil(rows.length / pageSize)) : 1;
  const current = Math.min(page, pageCount);
  const visible = useMemo(() => (pageSize ? rows.slice((current - 1) * pageSize, current * pageSize) : rows), [rows, pageSize, current]);
  const pager = pageSize && paginationLabels && (
    <Pagination page={current} pageCount={pageCount} onPageChange={setPage}
      labels={{ ...paginationLabels, summary: paginationLabels.summary((current - 1) * pageSize + 1, Math.min(current * pageSize, rows.length), rows.length) }} />
  );
  if (bp === "compact") {
    return (
      <div>
        <ul className="ms-table__cards" aria-label={caption}>
          {visible.map((row) => (
            <li key={rowKey(row)} className="ms-table__card" onClick={() => onRowClick?.(row)} style={onRowClick ? { cursor: "pointer" } : undefined}>
              {columns.map((c) => (
                <div key={c.id} className="ms-table__card-row">
                  <span className="ms-table__card-label">{c.header}</span>
                  <span>{c.cell(row)}</span>
                </div>
              ))}
            </li>
          ))}
        </ul>
        {pager}
      </div>
    );
  }
  return (
    <div>
      <div className="ms-table-wrap">
        <table className="ms-table">
          <caption className="sr-only">{caption}</caption>
          <thead><tr>{columns.map((c) => <th key={c.id} scope="col">{c.header}</th>)}</tr></thead>
          <tbody>
            {visible.map((row) => (
              <tr key={rowKey(row)} data-clickable={onRowClick ? "true" : undefined} onClick={() => onRowClick?.(row)}
                tabIndex={onRowClick ? 0 : undefined} onKeyDown={onRowClick ? (e) => { if (e.key === "Enter") onRowClick(row); } : undefined}>
                {columns.map((c) => (c.primary ? <th key={c.id} scope="row" style={{ textAlign: "start", fontWeight: 600, background: "transparent" }}>{c.cell(row)}</th> : <td key={c.id}>{c.cell(row)}</td>))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {pager}
    </div>
  );
}

// ---------- Timeline ----------
export interface TimelineEntry { id: string; title: ReactNode; meta?: ReactNode; icon?: IconName; tone?: Tone; current?: boolean }
export function Timeline({ entries, label }: { entries: TimelineEntry[]; label: string }) {
  return (
    <ol className="ms-timeline" aria-label={label}>
      {entries.map((e) => (
        <li key={e.id} className="ms-timeline__item" data-current={e.current ? "true" : undefined} aria-current={e.current ? "step" : undefined}>
          <div className="ms-timeline__rail">
            <span className="ms-timeline__dot" data-tone={e.tone ?? "neutral"}>{e.icon && <Icon name={e.icon} size="xs" />}</span>
            <span className="ms-timeline__line" />
          </div>
          <div>
            <div className="ms-timeline__title">{e.title}</div>
            {e.meta && <div className="ms-timeline__meta">{e.meta}</div>}
          </div>
        </li>
      ))}
    </ol>
  );
}

// ---------- Progress ----------
export function Progress({ value, label, tone = "primary" }: { value: number; label: string; tone?: Tone }) {
  const v = Math.max(0, Math.min(100, value));
  return (
    <div className="ms-progress" role="progressbar" aria-label={label} aria-valuenow={v} aria-valuemin={0} aria-valuemax={100} data-tone={tone}>
      <span style={{ inlineSize: `${v}%` }} />
    </div>
  );
}

/** "Care Ring": circular progress, the product's signature motif. */
export function ProgressRing({ value, size = 120, stroke = 10, label, children, tone = "primary" }: { value: number; size?: number; stroke?: number; label: string; children?: ReactNode; tone?: Tone }) {
  const v = Math.max(0, Math.min(100, value));
  const r = (size - stroke) / 2;
  const c = 2 * Math.PI * r;
  return (
    <div style={{ position: "relative", inlineSize: size, blockSize: size, flex: "none" }} role="progressbar" aria-label={label} aria-valuenow={v} aria-valuemin={0} aria-valuemax={100} data-tone={tone}>
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} aria-hidden="true" style={{ transform: "rotate(-90deg)" }}>
        <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--color-surface-sunken)" strokeWidth={stroke} />
        <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--tone-solid)" strokeWidth={stroke} strokeLinecap="round" strokeDasharray={c} strokeDashoffset={c * (1 - v / 100)} style={{ transition: "stroke-dashoffset var(--duration-slow) var(--ease-decelerate)" }} />
      </svg>
      <div style={{ position: "absolute", inset: 0, display: "grid", placeItems: "center", textAlign: "center" }}>{children}</div>
    </div>
  );
}

// ---------- StatCard / AlertCard ----------
export function StatCard({ label, value, icon, delta, deltaTone = "neutral", hint }: { label: string; value: ReactNode; icon?: IconName; delta?: string; deltaTone?: Tone; hint?: string }) {
  return (
    <Card>
      <div className="ms-stat">
        <div className="ms-stat__label">{icon && <Icon name={icon} size="sm" />}{label}</div>
        <div className="ms-stat__value">{value}</div>
        {delta && <div className="ms-stat__delta" data-tone={deltaTone} style={{ color: "var(--tone-fg)" }}>{delta}</div>}
        {hint && <div className="ms-card__subtitle">{hint}</div>}
      </div>
    </Card>
  );
}

const ALERT_ICON: Record<string, IconName> = { info: "info", warning: "alertTriangle", danger: "alertTriangle", success: "checkCircle", primary: "info", accent: "sparkles", neutral: "info" };
export function AlertCard({ tone = "info", title, children, actions, icon, role }: { tone?: Tone; title: ReactNode; children?: ReactNode; actions?: ReactNode; icon?: IconName; role?: "alert" | "status" }) {
  return (
    <div className="ms-alert" data-tone={tone} role={role ?? (tone === "danger" ? "alert" : "status")}>
      <Icon name={icon ?? ALERT_ICON[tone] ?? "info"} className="ms-alert__icon" />
      <div className="ms-alert__main">
        <div className="ms-alert__title">{title}</div>
        {children && <div>{children}</div>}
        {actions && <div className="ms-alert__actions">{actions}</div>}
      </div>
    </div>
  );
}
