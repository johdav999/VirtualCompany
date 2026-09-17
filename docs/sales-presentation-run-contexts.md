# Sales presentation run contexts

`SalesPresentationRun` is the bounded owner of situation-specific presentation preparation. Reusable source files, extracted slides, and rendered slide images remain owned by the immutable `SalesPresentationPresetVersion` asset. A run pins exactly one version and stores only its authorized business context, overrides, preparation evidence, readiness, and runtime provenance.

## Supported contexts

- `sales_meeting`: resolved from a company-owned meeting session. Runtime compatibility is provided by the run-bound compatibility deck while existing stage, narration, browser-room, Teams, question, capture, and history consumers migrate incrementally.
- `campaign_activity`: resolved by the campaign presentation scheduler for a contact, account, or campaign event. Preparation creates tasks, handoffs, or a meeting binding without starting a live presentation.
- `ad_hoc`: resolved by `ISalesPresentationRunContextResolver` from optional company-owned account, contact, lead, and deal records. With no customer context, missing evidence is explicit and presenter review is required. Ad-hoc creation is preparation-only and never fabricates a meeting or provider identifier.

## Adding a future context

1. Add a stable context value without changing existing stored values.
2. Add typed relational context identifiers where the relationship is queryable; do not hide core ownership in JSON.
3. Implement a company-scoped resolver that validates authorization and relationship consistency and returns explicit evidence and blockers.
4. Define allowed defaults and overrides in the application contract.
5. Recheck presenter, knowledge, tool, approval, consent, and retention policy when preparing or executing.
6. Materialize run artifacts only from authorized sources. Missing evidence must remain unavailable or require review.
7. Route tasks, handoffs, meeting creation, and live runtime through their established durable boundaries.
8. Add audit events, metrics, idempotency, retry classification, cross-company tests, and a user-visible recovery path.

Do not introduce a generic untyped context payload or provider-specific schema into the domain.

## Legacy compatibility and retention

The legacy inventory includes session-owned decks and slides plus consumers in runtime, stage access, narration, browser presentation, meeting questions, closing artifacts, capture, and meeting preparation. `SalesPresentationLegacyCompatibilityRecord` provides an idempotent, resumable disposition for each deck:

- `preset_backed` records already reference both a run and preset asset.
- `compatibility_only` records remain on the legacy projection because historical reusable ownership cannot be proven safely.
- `failed` records are operator-visible and retryable.

Filename or content-hash equality never promotes a legacy deck to reusable content. Content hashes are retained only as reconciliation evidence. Existing legacy tables, columns, routes, and storage keys remain intact.

Object deletion is reference-aware: a replaced draft source is deleted only when no preset asset, legacy deck, preset slide, or legacy slide references its storage key. Published versions, runs, campaign activity links, meeting history, artifacts, and compatibility records use restrictive relational references so archive operations preserve history.
