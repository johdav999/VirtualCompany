# Year-end rollover technical readiness — 2026-08-30

## Scope delivered

Prompt 8 adds formal year-end runs, immutable readiness snapshots, retained-earnings proposals, account/currency/dimension opening candidates, independent sign-offs, atomic execution, reconciliation checksums, subsequent events, correction/reopen links, operation receipts, history, audit events, metrics, API operations, a typed web client, and a responsive accounting workspace.

The source fiscal year is never rewritten. The generated journal chain posts into the immediate next fiscal period through `IAccountingPostingService`; source type, run ID, evidence version, and stable idempotency keys retain the relationship. The outer serializable transaction owns both posting calls, so an exception rolls both back.

## Readiness policy

The service blocks submission/execution unless it can prove:

- the exact twelve source periods are closed and reporting locked;
- the next target period is immediate, open, and unlocked;
- retained-earnings and opening-clearing accounts are distinct, active equity accounts;
- final close governance evidence is locked;
- required financial statements, cash-flow/equity-change reports, compliance, final audit package, source ledger, accounting configuration, and journal cutoff are current;
- no failed/partial integration or conflicting authority work remains.

Execution re-runs that policy and compares the live evidence hash to the approved hash. Reconciliation derives posted values from candidate IDs and blocks finalization on any difference.

## Verification evidence

- Domain tests cover exact-year/account invariants, self-review, evidence staleness, the complete golden lifecycle, mismatch blocking, currency/dimension preservation, and all subsequent-event resolution paths.
- API tests cover the governed route surface, atomic transaction and source identity requirements, additive migration structure, role/tenant denial, and company query filters.
- The SQL Server upgrade test migrates from Prompt 7 to Prompt 8 and verifies all nine tables when `VIRTUALCOMPANY_TEST_SQLSERVER` is configured; it is explicitly skipped otherwise.
- Web tests cover the six-gate workflow, approval language, immutable prior-year guidance, mismatch blocking, subsequent events, and retained screenshot-first assets.

## UAT evidence ledger

Reference: `docs/design/references/year-end-rollover-reference.png`  
Reference prompt: `docs/design/references/year-end-rollover-reference-prompt.md`

The authenticated, seeded year-end dataset and live host were not available during this implementation session, so interactive browser completion is not claimed. The strongest safe substitute was used: the generated desktop reference was visually inspected before Razor work, the compiled UI was reviewed against that reference, responsive breakpoints and semantic state labels were checked, and focused domain/HTTP/UI tests were run.

| ID | Severity | Finding | Resolution / disposition |
|---|---:|---|---|
| YE-01 | Critical | A partial two-journal execution could corrupt opening state. | Resolved: both central posting calls run inside one outer serializable transaction; failure rolls back and records a stable failed state. |
| YE-02 | Critical | Approval could authorize changed evidence. | Resolved: submit/review/execute bind to one SHA-256 evidence hash and execution re-evaluates authoritative state. |
| YE-03 | Critical | Prior-year journals could be edited to handle later evidence. | Resolved: no mutation path exists; subsequent events require disclosure, a linked post-forward journal, or an approved reopen request. |
| YE-04 | High | Opening totals could match while currency/dimension detail differs. | Resolved: candidates and reconciliation retain account, source currency, dimension key/facts, and signed amount. |
| YE-05 | High | Preparer self-approval could bypass segregation of duties. | Resolved in domain and service role checks with a stable self-review reason code. |
| YE-06 | High | Cross-company event/journal/document IDs could be linked. | Resolved: company ownership and document access are rechecked server-side; API routes require exact company context and the year-end governance role. |
| YE-07 | Medium | Refresh could leave duplicate candidates. | Resolved: previous candidates are removed before the replacement snapshot candidate set is saved; old snapshots remain retained as stale evidence. |
| YE-08 | Medium | Dense evidence tables could become unusable on narrow screens. | Resolved statically: KPI, lifecycle, evidence, create, and two-column layouts collapse at 1050 px/680 px, while the opening table scrolls horizontally. Live keyboard, zoom, and viewport QA remains for a seeded host. |

## Release boundary

The implementation is technically ready after the focused tests, full solution build, EF migration ordering/drift checks, and route inventory validation pass. A production release must additionally run the conditional SQL Server migration test, exercise the workflow with representative multi-currency/dimension data, verify observability dashboards/alerts, and obtain qualified accounting approval for the organization’s year-end policy and retained-earnings account mapping.
