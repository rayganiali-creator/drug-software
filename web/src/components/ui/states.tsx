import type { CSSProperties, ReactNode } from "react";
import { Icon, type IconName } from "./Icon";
import { Button } from "./buttons";
import type { Tone } from "./display";

export function EmptyState({ icon = "inbox", title, body, action }: { icon?: IconName; title: string; body?: string; action?: ReactNode }) {
  return (
    <div className="ms-state" role="status">
      <span className="ms-state__icon"><Icon name={icon} size="lg" /></span>
      <div className="ms-state__title">{title}</div>
      {body && <p className="ms-state__body">{body}</p>}
      {action}
    </div>
  );
}

export function ErrorState({ title, body, retryLabel, onRetry }: { title: string; body?: string; retryLabel: string; onRetry?(): void }) {
  return (
    <div className="ms-state" role="alert" data-tone={"danger" satisfies Tone}>
      <span className="ms-state__icon"><Icon name="alertTriangle" size="lg" /></span>
      <div className="ms-state__title">{title}</div>
      {body && <p className="ms-state__body">{body}</p>}
      {onRetry && <Button variant="secondary" iconStart="refresh" onClick={onRetry}>{retryLabel}</Button>}
    </div>
  );
}

export function Skeleton({ width = "100%", height = 16, radius }: { width?: number | string; height?: number | string; radius?: number | string }) {
  const style: CSSProperties = { inlineSize: width, blockSize: height, borderRadius: radius };
  return <span className="ms-skeleton" style={style} aria-hidden="true" />;
}

export function LoadingState({ label, rows = 3 }: { label: string; rows?: number }) {
  return (
    <div role="status" aria-busy="true" aria-live="polite" className="ms-stack">
      <span className="sr-only">{label}</span>
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="ms-card" aria-hidden="true">
          <div className="ms-stack" style={{ gap: "var(--space-3)" }}>
            <Skeleton width="40%" height={18} />
            <Skeleton height={14} />
            <Skeleton width="70%" height={14} />
          </div>
        </div>
      ))}
    </div>
  );
}
