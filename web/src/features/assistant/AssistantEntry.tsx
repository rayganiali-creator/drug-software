import { useAuth } from "../../auth/AuthContext";
import { Assistant } from "./Assistant";
import { GroundedAssistant } from "./GroundedAssistant";

/** Signed in through the API: the real, source-based assistant. Otherwise (in-browser demo accounts) only the scripted prototype exists, and it says so. */
export function AssistantEntry() {
  const { backend } = useAuth();
  return backend.apiFetch ? <GroundedAssistant /> : <Assistant />;
}
