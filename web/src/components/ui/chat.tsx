import { useRef, type KeyboardEvent, type ReactNode } from "react";
import { Icon } from "./Icon";
import { IconButton } from "./buttons";

export function ChatBubble({ role, children, variant, footer, label }: { role: "user" | "assistant"; children: ReactNode; variant?: "refusal"; footer?: ReactNode; label?: string }) {
  return (
    <div className={`ms-bubble ms-bubble--${role}${variant === "refusal" ? " ms-bubble--refusal" : ""}`} role="article" aria-label={label}>
      <div className="ms-bubble__body">{children}</div>
      {footer}
    </div>
  );
}

export function ThinkingDots({ label }: { label: string }) {
  return (
    <span className="ms-thinking" role="status">
      <span className="sr-only">{label}</span>
      <i /><i /><i />
    </span>
  );
}

export interface ComposerProps {
  value: string;
  onChange(v: string): void;
  onSend(): void;
  onVoice(): void;
  onImage(file: File): void;
  recording?: boolean;
  disabled?: boolean;
  attachment?: string | null;
  onRemoveAttachment?(): void;
  labels: { input: string; placeholder: string; send: string; voice: string; stopVoice: string; image: string; removeAttachment: string; prototypeNote: string };
}
export function MessageComposer({ value, onChange, onSend, onVoice, onImage, recording, disabled, attachment, onRemoveAttachment, labels }: ComposerProps) {
  const file = useRef<HTMLInputElement>(null);
  const onKey = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault();
      if (value.trim()) onSend();
    }
  };
  return (
    <div>
      {attachment && (
        <div className="ms-row" style={{ marginBlockEnd: "var(--space-2)" }}>
          <span className="ms-chip ms-chip--ai"><Icon name="image" size="xs" />{attachment}<button type="button" className="ms-chip__remove" aria-label={labels.removeAttachment} onClick={onRemoveAttachment}><Icon name="x" size="xs" /></button></span>
          <span className="ms-card__subtitle">{labels.prototypeNote}</span>
        </div>
      )}
      <div className="ms-composer">
        <IconButton icon="image" label={labels.image} onClick={() => file.current?.click()} />
        <input ref={file} type="file" accept="image/*" hidden onChange={(e) => { const f = e.target.files?.[0]; if (f) onImage(f); e.target.value = ""; }} />
        <textarea aria-label={labels.input} placeholder={labels.placeholder} value={value} rows={1} disabled={disabled} onChange={(e) => onChange(e.target.value)} onKeyDown={onKey} />
        <IconButton icon={recording ? "stop" : "mic"} label={recording ? labels.stopVoice : labels.voice} pressed={recording} onClick={onVoice} />
        <IconButton icon="send" label={labels.send} filled disabled={disabled || !value.trim()} onClick={onSend} />
      </div>
    </div>
  );
}
