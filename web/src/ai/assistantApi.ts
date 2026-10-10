import { AssistantError, type AskInput, type GroundedAnswer } from "./types";

export type ApiFetch = (path: string, init?: RequestInit) => Promise<Response>;

/** Client of POST /ai/medication-assistant. It holds no key and no rule: provider choice, consent and safety checks are all on the server. */
export class AssistantApi {
  constructor(private readonly apiFetch: ApiFetch | undefined, private readonly timeoutMs = 30_000) {}

  async ask(input: AskInput, outer?: AbortSignal): Promise<GroundedAnswer> {
    if (!this.apiFetch) throw new AssistantError("notConnected");
    const ctl = new AbortController();
    const timer = setTimeout(() => ctl.abort(), this.timeoutMs);
    outer?.addEventListener("abort", () => ctl.abort(), { once: true });
    let res: Response;
    try {
      res = await this.apiFetch("/ai/medication-assistant", {
        method: "POST", signal: ctl.signal,
        body: JSON.stringify({ question: input.question, locale: input.locale, medicationIds: input.medicationIds ?? null, includePatientContext: input.includePatientContext }),
      });
    } catch (e) {
      if (e instanceof DOMException && e.name === "AbortError") { if (outer?.aborted) throw e; throw new AssistantError("timeout"); }
      throw new AssistantError("network");
    } finally {
      clearTimeout(timer);
    }
    // 200 and 503 both carry the structured answer (a provider problem still shows the evidence); other statuses are errors.
    if (res.status === 200 || res.status === 503) {
      try { return (await res.json()) as GroundedAnswer; } catch { throw new AssistantError("server"); }
    }
    if (res.status === 401 || res.status === 403) throw new AssistantError("unauthorized");
    if (res.status === 429) throw new AssistantError("rateLimited");
    if (res.status === 400) throw new AssistantError("invalid");
    throw new AssistantError("server");
  }
}
