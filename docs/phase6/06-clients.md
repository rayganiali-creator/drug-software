# 06 — Web and Flutter integration

Both clients call the same endpoint through their existing authenticated session (`apiFetch` / `ApiSession.send`); they hold **no key and no provider setting**. Strings are shared (`design/i18n/strings.mjs`, `grounded.*`, fa + en).

| | Web (React) | Flutter |
|---|---|---|
| Real assistant | `/app/patient/assistant` when signed in through the API (`GroundedAssistant`); `features/assistant/`, `ai/assistantApi.ts`, `ai/types.ts` | `/assistant/live` (`GroundedAssistantScreen`), `api/assistant_client.dart`; reached from the prototype's banner button |
| Prototype conversation | `/app/patient/assistant/prototype`; also the page for in-browser demo accounts. Labelled "Prototype conversation: scripted sample answers; voice, image and 'ask a pharmacist' do not work yet" | `/assistant` keeps the scripted prototype with an always-visible compact banner "Prototype: scripted sample answers" |
| States | idle · loading · answered · no evidence · refused · escalated (alert) · blocked · unavailable · network · timeout (30 s) · unauthorized · rate limited · invalid · not connected | same |
| Labels | MOCK badge, "written by an outside AI service / local model / the system", DEMO badge, evidence-quality text (never a number) | same |
| Evidence | list `[E1]…` with kind, validation, stale/undated badges, collapsible source details (source, version, publisher, received date), conflicts, limits, missing information | same (collapsible details are plain toggles) |
| Layout / i18n | RTL for Persian, responsive; tokens unchanged | RTL, responsive; shared `AppBadge` now wraps long labels |

Voice, image interpretation and real-time monitoring are **not implemented** and are not offered by the real assistant; the prototype's voice/image controls remain visibly prototype-only.
Desktop/other platform status is unchanged from Phase 5 (see `docs/phase5/08-platform-status.md`); Phase 6 widget tests ran on the Flutter VM only.
