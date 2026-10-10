# 2. Rule contract and life cycle

`RuleDefinition` (immutable content of one version): `ruleId`, `version`, `title`, `purpose`, `domain`, `population` (min/max age, unsupported groups), `requiredInputs`, `criteria`, `severity`, `urgency`, `guidanceTemplateKey`, `emergencySigns` (only with validated evidence), `evidence[]`, `limitations[]`, `testCases[]`.
`RuleLifecycle` (everything else): `status`, `isDemo`, `author`, `authoredAt`, `reviewer`, `reviewedAt`, `reviewNote`, `effectiveFrom`, `retiredAt`. History: append-only `rule_event`.

Status: **Draft → UnderReview → Approved → Retired**, or UnderReview → **Rejected** (final). A change is a new version; of several active versions only the newest runs (older: `Unavailable: policy.superseded_by_newer_version`).

## Activation policy (`ActivationPolicy`, the only decision point)
Active only if: status Approved; not a demonstration rule; reviewer recorded with a review time and **not the author**; `effectiveFrom` set and passed; not retired; ≥1 evidence reference, **none** a demonstration fixture and **≥1 Validated**; ≥1 test case. Anything else → Inactive with reason codes. A stored "Approved" flag alone is never enough.

## Review (`RuleCatalogService`)
* create: always Draft, validated (`RuleValidator`), reserved `DEMO-` prefix refused, text screened;
* submit: author's own test cases must pass;
* review: reviewer ≠ author (service **and** permission model; refusal audited), note screened, Approve re-runs the policy and the test cases; Reject needs a note;
* retire: separate permission `clinicalrules.retire` (SystemAdmin).
Permissions: `clinicalrules.read`, `.author` (ContentManager), `.review` (Physician, Pharmacist), `.retire` (SystemAdmin). Authoring/review UI is not built in Phase 7 (API only); the web has a read-only rules page.

## Test cases
A test case is a small synthetic snapshot plus the expected outcome and finding count. They run at submit, at approval and in the suite (`RuleValidator.RunTestCases`).
