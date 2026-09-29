import { createContext, useCallback, useContext, useEffect, useId, useMemo, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { Icon } from "./Icon";
import { IconButton } from "./buttons";

const FOCUSABLE = 'a[href],button:not([disabled]),textarea,input:not([disabled]),select,[tabindex]:not([tabindex="-1"])';

/** Traps Tab inside `ref`, closes on Escape and restores focus to the opener on unmount. */
function useFocusTrap(ref: React.RefObject<HTMLElement | null>, onClose: () => void) {
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;
    const el = ref.current;
    const first = el?.querySelector<HTMLElement>(FOCUSABLE);
    (first ?? el)?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.stopPropagation();
        onClose();
      } else if (e.key === "Tab" && el) {
        const items = [...el.querySelectorAll<HTMLElement>(FOCUSABLE)];
        if (items.length === 0) return e.preventDefault();
        const a = items[0]!;
        const z = items[items.length - 1]!;
        if (e.shiftKey && document.activeElement === a) { e.preventDefault(); z.focus(); }
        else if (!e.shiftKey && document.activeElement === z) { e.preventDefault(); a.focus(); }
      }
    };
    document.addEventListener("keydown", onKey, true);
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey, true);
      document.body.style.overflow = prevOverflow;
      opener?.focus?.();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
}

interface OverlayProps {
  open: boolean;
  onClose(): void;
  title: string;
  closeLabel: string;
  children: ReactNode;
  actions?: ReactNode;
}

function Dialog({ variant, open, onClose, title, closeLabel, children, actions }: OverlayProps & { variant: "center" | "bottom" | "drawer" }) {
  if (!open) return null;
  return createPortal(<DialogInner variant={variant} onClose={onClose} title={title} closeLabel={closeLabel} actions={actions} open>{children}</DialogInner>, document.body);
}

function DialogInner({ variant, onClose, title, closeLabel, children, actions }: OverlayProps & { variant: "center" | "bottom" | "drawer" }) {
  const ref = useRef<HTMLDivElement>(null);
  const titleId = useId();
  useFocusTrap(ref, onClose);
  const cls = variant === "center" ? "ms-dialog" : variant === "bottom" ? "ms-dialog ms-dialog--sheet" : "ms-dialog ms-dialog--drawer";
  return (
    <div className={`ms-overlay ms-overlay--${variant === "center" ? "center" : variant}`} onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div ref={ref} className={cls} role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
        <div className="ms-dialog__head">
          <h2 className="ms-dialog__title" id={titleId}>{title}</h2>
          <IconButton icon="x" label={closeLabel} size="sm" onClick={onClose} />
        </div>
        <div>{children}</div>
        {actions && <div className="ms-dialog__actions">{actions}</div>}
      </div>
    </div>
  );
}

export const Modal = (p: OverlayProps) => <Dialog variant="center" {...p} />;
/** Mobile-first modal anchored to the bottom edge. */
export const BottomSheet = (p: OverlayProps) => <Dialog variant="bottom" {...p} />;
/** Side panel; opens from the inline-end edge (left in RTL, right in LTR). */
export const Drawer = (p: OverlayProps) => <Dialog variant="drawer" {...p} />;

// ---------- Toast / Snackbar ----------
export interface ToastInput { message: string; tone?: "success" | "info" | "danger"; action?: { label: string; onClick(): void }; durationMs?: number }
interface ToastItem extends ToastInput { id: number }
const ToastCtx = createContext<{ show(t: ToastInput): void } | null>(null);

export function ToastProvider({ children, dismissLabel }: { children: ReactNode; dismissLabel: string }) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const nextId = useRef(1);
  const show = useCallback((t: ToastInput) => {
    const id = nextId.current++;
    setItems((cur) => [...cur.slice(-2), { ...t, id }]);
    window.setTimeout(() => setItems((cur) => cur.filter((x) => x.id !== id)), t.durationMs ?? (t.action ? 7000 : 4000));
  }, []);
  const value = useMemo(() => ({ show }), [show]);
  return (
    <ToastCtx.Provider value={value}>
      {children}
      {createPortal(
        <div className="ms-toasts" role="status" aria-live="polite">
          {items.map((t) => (
            <div key={t.id} className="ms-toast" data-tone={t.tone ?? "info"}>
              <Icon name={t.tone === "danger" ? "alertTriangle" : t.tone === "success" ? "checkCircle" : "info"} size="sm" />
              <span>{t.message}</span>
              {t.action && <button type="button" className="ms-toast__action" onClick={() => { t.action?.onClick(); setItems((c) => c.filter((x) => x.id !== t.id)); }}>{t.action.label}</button>}
              <button type="button" className="ms-toast__action" aria-label={dismissLabel} onClick={() => setItems((c) => c.filter((x) => x.id !== t.id))} style={{ marginInlineStart: t.action ? 0 : "auto" }}><Icon name="x" size="xs" /></button>
            </div>
          ))}
        </div>,
        document.body,
      )}
    </ToastCtx.Provider>
  );
}
export function useToast() {
  const v = useContext(ToastCtx);
  if (!v) throw new Error("useToast must be used inside <ToastProvider>");
  return v;
}

// ---------- Tooltip ----------
export function Tooltip({ text, children }: { text: string; children: ReactNode }) {
  const [show, setShow] = useState(false);
  const id = useId();
  return (
    <span className="ms-tooltip" onMouseEnter={() => setShow(true)} onMouseLeave={() => setShow(false)} onFocus={() => setShow(true)} onBlur={() => setShow(false)}
      onKeyDown={(e) => e.key === "Escape" && setShow(false)} aria-describedby={show ? id : undefined}>
      {children}
      {show && <span role="tooltip" id={id} className="ms-tooltip__bubble">{text}</span>}
    </span>
  );
}
