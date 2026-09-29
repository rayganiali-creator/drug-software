import {
  createContext, forwardRef, useContext, useEffect, useId, useRef, useState,
  type InputHTMLAttributes, type KeyboardEvent, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes,
} from "react";
import { Icon, type IconName } from "./Icon";

interface FieldShellProps {
  id: string;
  label?: string;
  hint?: string;
  error?: string;
  children: ReactNode;
}
function FieldShell({ id, label, hint, error, children }: FieldShellProps) {
  return (
    <div className="ms-field">
      {label && <label className="ms-field__label" htmlFor={id}>{label}</label>}
      {children}
      {hint && !error && <div className="ms-field__hint" id={`${id}-hint`}>{hint}</div>}
      {error && (
        <div className="ms-field__error" id={`${id}-err`} role="alert">
          <Icon name="alertTriangle" size="xs" />
          {error}
        </div>
      )}
    </div>
  );
}
const describedBy = (id: string, hint?: string, error?: string) => (error ? `${id}-err` : hint ? `${id}-hint` : undefined);

export interface TextFieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  hint?: string;
  error?: string;
  icon?: IconName;
  multiline?: false;
}
export const TextField = forwardRef<HTMLInputElement, TextFieldProps>(function TextField({ label, hint, error, icon, id, ...rest }, ref) {
  const auto = useId();
  const fid = id ?? auto;
  return (
    <FieldShell id={fid} label={label} hint={hint} error={error}>
      <div className="ms-input" data-invalid={error ? "true" : undefined}>
        {icon && <Icon name={icon} size="sm" className="ms-input__icon" />}
        <input ref={ref} id={fid} aria-invalid={error ? true : undefined} aria-describedby={describedBy(fid, hint, error)} {...rest} />
      </div>
    </FieldShell>
  );
});

export interface TextAreaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: string;
  hint?: string;
  error?: string;
}
export const TextArea = forwardRef<HTMLTextAreaElement, TextAreaProps>(function TextArea({ label, hint, error, id, ...rest }, ref) {
  const auto = useId();
  const fid = id ?? auto;
  return (
    <FieldShell id={fid} label={label} hint={hint} error={error}>
      <div className="ms-input ms-input--multiline" data-invalid={error ? "true" : undefined}>
        <textarea ref={ref} id={fid} aria-invalid={error ? true : undefined} aria-describedby={describedBy(fid, hint, error)} {...rest} />
      </div>
    </FieldShell>
  );
});

export interface SearchFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "type" | "onChange"> {
  label: string;
  value: string;
  onValueChange(v: string): void;
  clearLabel: string;
}
export function SearchField({ label, value, onValueChange, clearLabel, placeholder, ...rest }: SearchFieldProps) {
  return (
    <div className="ms-input" role="search">
      <Icon name="search" size="sm" className="ms-input__icon" />
      <input type="search" aria-label={label} placeholder={placeholder ?? label} value={value} onChange={(e) => onValueChange(e.target.value)} {...rest} />
      {value && (
        <button type="button" className="ms-iconbtn ms-iconbtn--sm" aria-label={clearLabel} onClick={() => onValueChange("")}>
          <Icon name="x" size="xs" />
        </button>
      )}
    </div>
  );
}

export interface SelectOption { value: string; label: string }
export interface SelectProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, "onChange"> {
  label?: string;
  hint?: string;
  error?: string;
  options: SelectOption[];
  onValueChange(v: string): void;
}
/** Native <select> (best mobile/screen-reader behaviour) with token styling. */
export function Select({ label, hint, error, options, onValueChange, id, ...rest }: SelectProps) {
  const auto = useId();
  const fid = id ?? auto;
  return (
    <FieldShell id={fid} label={label} hint={hint} error={error}>
      <div className="ms-input" data-invalid={error ? "true" : undefined}>
        <select id={fid} aria-describedby={describedBy(fid, hint, error)} onChange={(e) => onValueChange(e.target.value)} {...rest}>
          {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
        <Icon name="chevronDown" size="sm" className="ms-input__icon" />
      </div>
    </FieldShell>
  );
}

// ---------- Dropdown menu (roving focus, Esc, arrow keys) ----------
export interface MenuItem { id: string; label: string; icon?: IconName; onSelect(): void; checked?: boolean }
export interface DropdownProps {
  label: string;
  triggerIcon?: IconName;
  triggerContent?: ReactNode;
  items: MenuItem[];
  align?: "start" | "end";
}
export function Dropdown({ label, triggerIcon, triggerContent, items, align = "start" }: DropdownProps) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const listId = useId();

  useEffect(() => {
    if (!open) return;
    const first = root.current?.querySelector<HTMLElement>('[role^="menuitem"]');
    first?.focus();
    const onDoc = (e: MouseEvent) => {
      if (!root.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, [open]);

  const onKey = (e: KeyboardEvent) => {
    if (e.key === "Escape") {
      setOpen(false);
      root.current?.querySelector<HTMLElement>("[aria-haspopup]")?.focus();
      return;
    }
    if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
    e.preventDefault();
    const els = [...(root.current?.querySelectorAll<HTMLElement>('[role^="menuitem"]') ?? [])];
    const i = els.indexOf(document.activeElement as HTMLElement);
    const next = e.key === "ArrowDown" ? (i + 1) % els.length : (i - 1 + els.length) % els.length;
    els[next]?.focus();
  };

  return (
    <div className="ms-menu" ref={root} onKeyDown={onKey}>
      <button type="button" className="ms-btn ms-btn--secondary ms-btn--sm" aria-haspopup="menu" aria-expanded={open} aria-controls={open ? listId : undefined} aria-label={label} onClick={() => setOpen((o) => !o)}>
        {triggerIcon && <Icon name={triggerIcon} size="sm" />}
        {triggerContent}
        <Icon name="chevronDown" size="xs" />
      </button>
      {open && (
        <div className={`ms-menu__list${align === "end" ? " ms-menu__list--end" : ""}`} role="menu" id={listId} aria-label={label}>
          {items.map((it) => (
            <button key={it.id} type="button" role={it.checked === undefined ? "menuitem" : "menuitemradio"} aria-checked={it.checked} className="ms-menu__item"
              onClick={() => { it.onSelect(); setOpen(false); }}>
              {it.icon && <Icon name={it.icon} size="sm" />}
              <span style={{ flex: 1 }}>{it.label}</span>
              {it.checked && <Icon name="check" size="sm" />}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

// ---------- Checkbox / Radio / Switch ----------
export interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "type"> { label: ReactNode }
export const Checkbox = forwardRef<HTMLInputElement, CheckboxProps>(function Checkbox({ label, className = "", ...rest }, ref) {
  return (
    <label className={`ms-check ${className}`}>
      <input ref={ref} type="checkbox" {...rest} />
      <span className="ms-check__box" aria-hidden="true"><Icon name="check" size="xs" /></span>
      <span>{label}</span>
    </label>
  );
});

interface RadioCtx { name: string; value: string; onChange(v: string): void }
const RadioContext = createContext<RadioCtx | null>(null);
export function RadioGroup({ legend, name, value, onValueChange, children }: { legend: string; name: string; value: string; onValueChange(v: string): void; children: ReactNode }) {
  return (
    <RadioContext.Provider value={{ name, value, onChange: onValueChange }}>
      <fieldset className="ms-fieldset" role="radiogroup">
        <legend>{legend}</legend>
        {children}
      </fieldset>
    </RadioContext.Provider>
  );
}
export function Radio({ value, label, disabled }: { value: string; label: ReactNode; disabled?: boolean }) {
  const ctx = useContext(RadioContext);
  if (!ctx) throw new Error("<Radio> must be inside <RadioGroup>");
  return (
    <label className="ms-check ms-check--radio">
      <input type="radio" name={ctx.name} value={value} checked={ctx.value === value} disabled={disabled} onChange={() => ctx.onChange(value)} />
      <span className="ms-check__box" aria-hidden="true" />
      <span>{label}</span>
    </label>
  );
}

export interface SwitchProps { label: ReactNode; checked: boolean; onCheckedChange(v: boolean): void; disabled?: boolean }
export function Switch({ label, checked, onCheckedChange, disabled }: SwitchProps) {
  return (
    <label className="ms-switch">
      <input type="checkbox" role="switch" checked={checked} disabled={disabled} onChange={(e) => onCheckedChange(e.target.checked)} />
      <span className="ms-switch__track" aria-hidden="true"><span className="ms-switch__thumb" /></span>
      <span>{label}</span>
    </label>
  );
}
