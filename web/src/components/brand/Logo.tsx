import { useId } from "react";

/** "Care Ring" mark: an open ring (adherence) holding a cross (care) with an AI spark. */
export function LogoMark({ size = 36 }: { size?: number }) {
  const g = useId();
  return (
    <svg width={size} height={size} viewBox="0 0 40 40" aria-hidden="true" focusable="false">
      <defs>
        <linearGradient id={g} x1="6" y1="6" x2="34" y2="34" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="var(--color-primary)" />
          <stop offset="1" stopColor="var(--color-accent)" />
        </linearGradient>
      </defs>
      <path d="M33.500 20A13.500 13.500 0 1 1 26.750 8.300" fill="none" stroke={`url(#${g})`} strokeWidth="4" strokeLinecap="round" />
      <path d="M18 14h4v4h4v4h-4v4h-4v-4h-4v-4h4z" fill="var(--color-primary)" />
      <circle cx="31.500" cy="9" r="3" fill="var(--color-accent)" />
    </svg>
  );
}

export function Logo({ compact }: { compact?: boolean }) {
  return (
    <span style={{ display: "inline-flex", alignItems: "center", gap: "var(--space-2)" }}>
      <LogoMark />
      {!compact && <span style={{ fontWeight: 800, fontSize: "1.125rem", letterSpacing: 0 }}>AI <span style={{ color: "var(--color-primary)" }}>MedSmarter</span></span>}
    </span>
  );
}
