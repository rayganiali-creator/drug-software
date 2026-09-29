# Phase 1 — notes & known issues

## Explicitly NOT in Phase 1
Authentication/authorization, any module behaviour, outbox publisher, Kafka producer/consumers, search indices, LLM/RAG,
i18n/RTL, OpenTelemetry, WAF/gateway, Terraform/K8s manifests, release/signing pipelines for mobile.

## Known issues
1. **Compose is dev-only.** OpenSearch runs with the security plugin disabled and Kafka uses PLAINTEXT; ports bind to 127.0.0.1. Production needs TLS/auth (decision D-21).
2. **Readiness endpoint is unauthenticated** and reveals which dependency is down (names only). Restrict at the gateway before any real deployment.
3. **Action versions are major tags** (`actions/checkout@v4`); pin to commit SHAs before production. CI has not run on GitHub yet (verified locally only).
4. **Docker builds behind a TLS-intercepting proxy** fail with untrusted-root errors (NuGet/npm/pip); in normal networks they build as-is.
5. **EF logs `relation platform.__ef_migrations_history does not exist`** as an error on the very first migration run (EF probes the history table); SQL command logging is otherwise silenced (`Fatal`) so parameters can never reach logs.
6. **Starlette TestClient emits a deprecation warning** about `httpx` in AI tests (upstream; harmless).
7. Health check for Kafka/OpenSearch has a 3 s timeout each and runs sequentially per request — fine for probes, revisit with caching if probed aggressively.
8. Mobile: only Android/iOS scaffolds; not built into an APK/IPA here (no Android SDK/Xcode). `flutter analyze` and `flutter test` pass.
9. `Serilog.Formatting.Compact` is used transitively via Serilog.AspNetCore (no direct package reference).
