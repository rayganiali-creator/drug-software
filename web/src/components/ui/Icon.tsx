import type { CSSProperties } from "react";

import { directional, icons } from "./icons.g";

const P = icons;
export type IconName = keyof typeof P;
/** Icons whose meaning has a direction and must flip in RTL. */
const DIRECTIONAL: ReadonlySet<string> = new Set(directional);
const SIZES = { xs: 16, sm: 20, md: 24, lg: 32, xl: 48 } as const;

export interface IconProps {
  name: IconName;
  size?: keyof typeof SIZES;
  /** Accessible name. Omit for decorative icons (they are hidden from assistive tech). */
  label?: string;
  className?: string;
  style?: CSSProperties;
}

export function Icon({ name, size = "md", label, className = "", style }: IconProps) {
  const px = SIZES[size];
  const cls = `ms-icon${DIRECTIONAL.has(name) ? " ms-icon--dir" : ""} ${className}`.trim();
  return (
    <svg
      className={cls}
      style={style}
      width={px}
      height={px}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      role={label ? "img" : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      focusable="false"
      dangerouslySetInnerHTML={{ __html: P[name] }}
    />
  );
}
