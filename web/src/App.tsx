import { useEffect, useState } from "react";
import { fetchReadiness, type HealthReport } from "./api";

type State = { kind: "loading" } | { kind: "error" } | { kind: "ok"; report: HealthReport };

export function App({ load = fetchReadiness }: { load?: () => Promise<HealthReport> }) {
  const [state, setState] = useState<State>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;
    load()
      .then((report) => !cancelled && setState({ kind: "ok", report }))
      .catch(() => !cancelled && setState({ kind: "error" }));
    return () => {
      cancelled = true;
    };
  }, [load]);

  return (
    <main style={{ fontFamily: "system-ui, sans-serif", maxWidth: 640, margin: "3rem auto", padding: "0 1rem" }}>
      <h1>AI MedSmarter</h1>
      <p>Foundation build. No clinical features are available yet.</p>
      <h2>System status</h2>
      {state.kind === "loading" && <p>Checking…</p>}
      {state.kind === "error" && <p role="alert">The API is unreachable.</p>}
      {state.kind === "ok" && (
        <>
          <p>
            Overall: <strong>{state.report.status}</strong>
          </p>
          <ul>
            {Object.entries(state.report.checks).map(([name, status]) => (
              <li key={name}>
                {name}: {status}
              </li>
            ))}
          </ul>
        </>
      )}
    </main>
  );
}
