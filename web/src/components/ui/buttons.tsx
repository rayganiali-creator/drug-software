import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from "react";
import { Icon, type IconName } from "./Icon";

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: "primary" | "secondary" | "tonal" | "ghost" | "danger" | "ai";
  size?: "sm" | "md" | "lg";
  block?: boolean;
  loading?: boolean;
  iconStart?: IconName;
  iconEnd?: IconName;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = "primary", size = "md", block, loading, iconStart, iconEnd, className = "", children, disabled, type = "button", ...rest },
  ref,
) {
  const cls = ["ms-btn", `ms-btn--${variant}`, size !== "md" && `ms-btn--${size}`, block && "ms-btn--block", className].filter(Boolean).join(" ");
  return (
    <button ref={ref} type={type} className={cls} disabled={disabled || loading} aria-busy={loading || undefined} {...rest}>
      {loading ? <span className="ms-btn__spinner" aria-hidden="true" /> : iconStart ? <Icon name={iconStart} size="sm" /> : null}
      {children}
      {iconEnd && !loading ? <Icon name={iconEnd} size="sm" /> : null}
    </button>
  );
});

export interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, "aria-label"> {
  icon: IconName;
  /** Required: icon-only buttons need an accessible name. */
  label: string;
  size?: "sm" | "md";
  filled?: boolean;
  pressed?: boolean;
  badge?: ReactNode;
}

export const IconButton = forwardRef<HTMLButtonElement, IconButtonProps>(function IconButton(
  { icon, label, size = "md", filled, pressed, className = "", type = "button", ...rest },
  ref,
) {
  const cls = ["ms-iconbtn", size === "sm" && "ms-iconbtn--sm", filled && "ms-iconbtn--filled", className].filter(Boolean).join(" ");
  return (
    <button ref={ref} type={type} className={cls} aria-label={label} title={label} aria-pressed={pressed} {...rest}>
      <Icon name={icon} size={size === "sm" ? "sm" : "md"} />
    </button>
  );
});
