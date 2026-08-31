# Versioned report definitions

## Purpose and scope

Versioned report definitions let a company tailor management cash-flow and equity-change layouts while preserving deterministic financial-report evidence. The feature is a presentation and aggregation layer over authoritative posted journals. It does not replace the ledger, create accounting facts, or by itself establish statutory compliance.

The admin workspace is available at `/finance/accounting/report-definitions`. Its three-pane layout follows the reference in `docs/design/references/report-definition-designer-reference.png`: definition structure on the left, safe line editing and preview in the centre, and validation/approval/effective-date governance on the right.

## Ownership and immutability

- System templates are code-owned and immutable. They may only be listed and copied.
- Copying creates a company-owned definition and version 1 draft.
- Every record carries `CompanyId`; relational keys and service queries enforce tenant scope.
- An active or retained version is never edited. A later change starts a new draft version.
- A snapshot or export stores the exact definition version ID, version number, SHA-256 definition hash, and rendered payload. Later edits apply prospectively and cannot rewrite that evidence.
- Snapshot exports are produced from retained snapshot content at `GET .../report-suite/snapshots/{snapshotId}/export?format=csv|json`; CSV metadata rows and response headers carry the exact definition identity and hash.

## Lifecycle

1. Copy a system template, or create the next draft from an existing company version.
2. Edit sections, lines, account groups, formulas, signs, scale, decimals, currency mode, dimension filters, and comparison behavior.
3. Save with the expected revision. A stale revision returns a concurrency conflict rather than overwriting another administrator.
4. Validate the saved definition. Successful validation binds a canonical SHA-256 definition hash.
5. Preview against a fiscal period. Preview uses posted journal facts and exposes blockers and provenance without activating the draft.
6. Submit the unchanged, valid draft.
7. A different authorized user approves or rejects it. Self-approval is rejected.
8. Activate an approved version from an effective date. Any previous active version is retired prospectively.
9. Retire an approved or active version with an effective-to date. Retained snapshots remain readable.

All mutation commands accept an idempotency key. Replaying the same company/key/action returns the original version; attempting to reuse a key for a different action returns a conflict.

## Formula grammar

The formula engine is a parser and evaluator, not a scripting host. Supported input is deliberately narrow:

- decimal literals
- line references such as `[OPERATING_CASH]`
- unary `+` and `-`
- `+`, `-`, `*`, and `/`
- parentheses
- `SUM(expression, expression, ...)`

Executable expressions, reflection, method calls, SQL or other queries, tenant-qualified references, and unresolved line references are rejected. Division by zero is rejected. Evaluation is deterministic and rounded using midpoint-to-even behavior.

## Validation and activation gates

Validation emits stable issue codes and explanatory subject references. Activation requires a current successful validation whose hash still matches the definition and a completed approval. Checks include:

- missing or duplicate sections and line codes
- invalid or unresolved formulas and dependency cycles
- missing account-group mappings
- duplicate account coverage across detail lines
- cross-company or unavailable account and dimension references
- conflicting negative formula/display-sign treatment
- a dimension member without a type
- document-currency aggregation combined with a dimension filter
- preview reconciliation differences between mapped journal population and raw detail coverage

The report suite selects only an active version effective for the report period unless an authorized preview explicitly supplies a version. Closed-period snapshot selection includes the version identity and hash in its parameter key.

## Authorization, audit, and telemetry

- Accounting viewers can list definitions, inspect versions, and preview.
- Accounting administrators can copy, edit, validate, submit, activate, retire, and create new versions.
- Finance approvers can decide a submitted version, subject to independent-approval enforcement.
- Audit events are emitted for copy, version creation, update, validation, submission, approval/rejection, activation, and retirement. Events include the company, actor, version target, report kind, definition sources, and rationale.
- Telemetry records lifecycle actions, validation duration, validation outcome, and issue counts by code. Logs include company/version identifiers without formulas or journal payloads.

## Migration and recovery

Migration `20260830200000_AddVersionedReportDefinitions` creates the definition/version/section/line/account-group/comparison/validation/approval/idempotency tables and adds definition identity columns to `financial_report_suite_snapshots`.

Deployment order is database migration, API, then web. Existing snapshots keep nullable definition columns and continue to deserialize. Rollback of the application is safe while the additive schema remains. Do not drop the tables or snapshot columns after definitions have been activated; they are retained financial-report evidence. If activation is incorrect, retire the version and activate an approved correction from a prospective date rather than modifying past snapshots.

## Operational verification

- Run focused `ReportDefinitionTests` for parser safety, cycle detection, lifecycle concurrency, snapshot binding, and persistence indexes.
- Confirm the web and API projects compile.
- In a tenant test, copy a template, validate, submit with one user, approve with another, activate, capture a report snapshot, create a new version, and verify the prior snapshot still reports the original version/hash.
- Confirm another company cannot read or mutate the version IDs created during the test.
