import { useId, useRef, type KeyboardEvent, type ReactNode } from "react";
import { Icon } from "./Icon";

export interface TabItem { id: string; label: string; panel: ReactNode }
export interface TabsProps { tabs: TabItem[]; value: string; onValueChange(id: string): void; label: string }
/** WAI-ARIA tabs: roving tabindex, Arrow/Home/End keys, panels tied by aria-controls. */
export function Tabs({ tabs, value, onValueChange, label }: TabsProps) {
  const base = useId();
  const refs = useRef<Record<string, HTMLButtonElement | null>>({});
  const onKey = (e: KeyboardEvent) => {
    const i = tabs.findIndex((t) => t.id === value);
    const rtl = document.documentElement.dir === "rtl";
    const forward = rtl ? "ArrowLeft" : "ArrowRight";
    const backward = rtl ? "ArrowRight" : "ArrowLeft";
    const targets: Record<string, number> = {
      [forward]: (i + 1) % tabs.length,
      [backward]: (i - 1 + tabs.length) % tabs.length,
      Home: 0,
      End: tabs.length - 1,
    };
    const next = targets[e.key];
    if (next === undefined) return;
    e.preventDefault();
    const t = tabs[next]!;
    onValueChange(t.id);
    refs.current[t.id]?.focus();
  };
  const active = tabs.find((t) => t.id === value) ?? tabs[0]!;
  return (
    <div className="ms-tabs">
      <div className="ms-tabs__list" role="tablist" aria-label={label} onKeyDown={onKey}>
        {tabs.map((t) => (
          <button key={t.id} ref={(el) => { refs.current[t.id] = el; }} type="button" role="tab" className="ms-tab" id={`${base}-tab-${t.id}`}
            aria-selected={t.id === active.id} aria-controls={`${base}-panel-${t.id}`} tabIndex={t.id === active.id ? 0 : -1} onClick={() => onValueChange(t.id)}>
            {t.label}
          </button>
        ))}
      </div>
      <div className="ms-tabs__panel" role="tabpanel" id={`${base}-panel-${active.id}`} aria-labelledby={`${base}-tab-${active.id}`} tabIndex={0}>
        {active.panel}
      </div>
    </div>
  );
}

export interface SegmentedProps { label: string; options: { value: string; label: string }[]; value: string; onValueChange(v: string): void }
export function SegmentedControl({ label, options, value, onValueChange }: SegmentedProps) {
  const name = useId();
  return (
    <div className="ms-segmented" role="radiogroup" aria-label={label}>
      {options.map((o) => (
        <label key={o.value} className="ms-segmented__opt">
          <input type="radio" name={name} value={o.value} checked={value === o.value} onChange={() => onValueChange(o.value)} />
          <span>{o.label}</span>
        </label>
      ))}
    </div>
  );
}

export interface PaginationProps {
  page: number;
  pageCount: number;
  onPageChange(p: number): void;
  labels: { nav: string; prev: string; next: string; page: (n: number) => string; summary: string };
}
export function Pagination({ page, pageCount, onPageChange, labels }: PaginationProps) {
  if (pageCount <= 1) return null;
  return (
    <nav className="ms-pagination" aria-label={labels.nav}>
      <span className="ms-muted" aria-live="polite">{labels.summary}</span>
      <div className="ms-pagination__pages">
        <button type="button" className="ms-page-btn" aria-label={labels.prev} disabled={page <= 1} onClick={() => onPageChange(page - 1)}><Icon name="chevronLeft" size="sm" /></button>
        {Array.from({ length: pageCount }, (_, i) => i + 1).map((n) => (
          <button key={n} type="button" className="ms-page-btn" aria-label={labels.page(n)} aria-current={n === page ? "page" : undefined} onClick={() => onPageChange(n)}>{n}</button>
        ))}
        <button type="button" className="ms-page-btn" aria-label={labels.next} disabled={page >= pageCount} onClick={() => onPageChange(page + 1)}><Icon name="chevronRight" size="sm" /></button>
      </div>
    </nav>
  );
}
