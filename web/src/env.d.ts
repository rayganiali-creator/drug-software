/// <reference types="vite/client" />
interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string;
  /** "1" when the demo API asks for a shared access code. A flag only; the code is never a build variable. */
  readonly VITE_DEMO_GATE?: string;
}
