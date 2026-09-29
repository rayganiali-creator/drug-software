# 06 — Audit log

`Audit` module (`AuditService`), status: writer/reader/hash chain **IMPLEMENTED NOW**; durable append-only storage **DEFERRED** (in-memory now).

## What is recorded
Login / failed login / logout / logout-all / token refresh / **refresh-token reuse** / session revoked · role assigned / revoked ·
consent granted / revoked · patient-data, prescription and ADR reads · **every access denial** (with the internal layer:reason) · audit reads · admin actions.
Each entry: id, monotonic sequence, timestamp (UTC), actor, action (constants only), resource type/id, subject (patient) id, result (Success/Denied/Failure),
source (`web`/`android`/`ios`/`api`), correlation id, reason code, and a few short metadata items.

## What is NOT recorded (tested)
Passwords, tokens (access or refresh), authorization headers, medical content, free text from patients. The writer **drops** metadata keys that look like
credentials (`password|secret|token|authorization|bearer|cookie|credential|apikey|otp|pin`), **redacts** JWT-like / long opaque values, limits metadata to
12 items × 200 characters. Failed logins do not store the attempted identifier. Application logs contain identifiers and outcome only.

## Tamper evidence
Entries are chained with SHA-256 (`hash = H(previous hash, fields)`). `GET /audit/verify` recomputes the chain; altering, removing or reordering any entry
makes it report `intact:false` (test simulates a modification).

## Access
* `audit.read.own` (patients): `GET /audit/me` — "who accessed **my** data" (actor display name, time, what).
* `audit.read` (SystemAdmin only): `GET /audit`, `GET /audit/verify`; reading the audit log is itself audited.
* Nobody can modify or delete entries through the API.

## DEFERRED
PostgreSQL append-only table + restricted DB role, external WORM/SIEM export, retention policy, alerting on reuse/denial spikes, per-field access logs for exports.
