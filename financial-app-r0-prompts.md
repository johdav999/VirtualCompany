# Financial App Release 0 Implementation Prompts

Release: Accounting Core GA  
Source roadmap: [financial-roadmap.md](financial-roadmap.md)  
Prompt order: execute Prompts 1–7 in order. Do not stop after reporting a test, build, migration, or browser checkpoint; fix in-scope failures and continue until the release is complete or genuinely blocked.

## Shared execution contract

Every prompt in this document is an implementation prompt, not an analysis, report-only, or scaffolding task.

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, the repository `AGENTS.md`, and the relevant current code before editing.
- `architecture-inst.md` is required by workspace instructions when present, but it was not present when this pack was generated. If it exists at execution time, read and follow it; do not invent a substitute. `docs/architecture-rules.md` remains mandatory.
- For UI changes, read and follow `ui-instructions.md` and `docs/design.md`. Complete the mandatory screenshot-first workflow before any new page, major component, or significant redesign; save reference images under `docs/design/references/` and visually compare the implementation with them.
- Existing repository behavior wins over older planning text. Preserve tenant isolation, accounting authority, immutable journals, source/version identity, approvals, audit evidence, provider adapters, migration history, simulation isolation, routes, and wire values.
- Keep Domain/Application/Infrastructure/Persistence/API/Web ownership boundaries. Controllers remain transport-only; backend policies remain authoritative; typed Web clients own endpoints; capability registration stays in `FinanceModuleRegistration`.
- Every tenant-owned record, query, command, worker claim, retry/recovery action, cache, export, object reference, log scope, and audit event must enforce company scope. Add cross-company read/write/execution tests.
- All native postings continue through `IAccountingPostingService`. Posted journals, voucher identities, policy facts, source links, evidence, approvals, and closed snapshots are immutable; corrections use explicit reversals and linked replacements.
- External side effects use durable outbox/background execution with stable business idempotency, bounded retry, acknowledgement/reconciliation state, safe operator-visible failures, audit, and telemetry. Ambiguous provider outcomes are never implicit success.
- SQL Server EF migrations are the only schema authority. Preserve fresh install, representative upgrade, local SQL Server, Docker SQL Server, backup/restore, and object-storage recovery compatibility; finish with no pending model changes.
- Do not delete, skip, weaken, or broadly exclude a valid test to obtain green results. A genuinely unrelated quarantine must remain executed in a separate visible lane and include evidence, owner, reason, expiry, and removal condition.
- Do not add mock production data, silent simulation fallback, hidden development providers, direct database repair instructions, secrets, unbounded request work, or deferred in-scope TODOs.
- Every prompt must run focused tests during implementation, then the affected broader suites/builds. Release 0 completion requires the full agreed production matrix, not only filtered accounting tests.

## Current release baseline

- The repository already contains accounting setup, accounts, fiscal periods, voucher sequences, immutable posting, manual journals, customer/supplier accounting, payments and allocations, bank import/reconciliation, reports, close/lock/reopen, exports, audit, recovery verification, Fortnox integration, and the complete provider-switch lifecycle.
- `docs/finance/accounting-release-evidence.md` records strong focused accounting evidence, including SQL Server posting/concurrency/rollback and coordinated SQL/object recovery.
- `docs/finance/accounting-provider-switch-monitoring-release-evidence.md` records a later successful full solution build and focused monitoring suites, but its complete solution test invocation still reported 205 API failures and 14 Web contract failures. Re-measure this baseline before changing code; the checked-in counts are evidence, not an assumption that current failures are identical.
- Existing source modes include operational, Fortnox, simulation, and combined views. Simulation Lab is intentional, but production accounting must never rely on seeded/simulated/mock facts implicitly.
- Numerous Finance hosted services already perform exports, migrations, provider switching, reporting regeneration, approvals, insights, startup sync, bill reconciliation, seeding, and simulation progression. Release 0 hardens them without replacing them with one generic worker framework.

---

## Prompt 1 — Repair and govern the production test matrix

### 1. Title and outcome

Make the repository's production test matrix trustworthy and green by fixing real defects and test-isolation problems, while retaining visible governed execution for any proven unrelated quarantine.

### 2. Current context

- Focused Finance/accounting suites and builds have passed in prior release evidence.
- The latest checked-in complete invocation recorded 1,807 passing and 205 failing API tests plus 14 Web contract failures; older Web evidence recorded a different failure set. The current baseline must be measured once and treated as authoritative.
- Recorded failure themes include dashboard expectations, simulation configuration, SQLite/SQL Server assumptions, finance seeding/query behavior, agent policy fixtures, query-bound bUnit harnesses, navigation expectations, localization registration, timing, and shared-state isolation.
- Tests are intentionally partitioned among Finance, API, Web, Web contract, Platform, Mailbox, Support, and Sales projects per `docs/architecture-rules.md`.

### 3. Dependencies

- None.
- Access to the normal repository toolchain. Tests requiring SQL Server, Docker, browsers, or real external credentials must remain explicitly categorized and must not masquerade as ordinary hermetic unit tests.

### 4. Implementation requirements

- Run the full solution build and all test projects once to capture a machine-readable current baseline with project, test, duration, outcome, and safe failure category. Do not repeatedly rerun the entire matrix during diagnosis.
- Group failures by root cause and implement coherent fixes in production or test code as evidence requires. Prioritize accounting correctness, tenant isolation, authorization, static/shared-state leakage, clocks/timezones, provider configuration, SQL provider differences, localization, and Web/API contract drift.
- Replace test dependence on already-running hosts, shared mutable databases, ports, environment variables, current wall clock, execution order, or static caches with explicit isolated fixtures and `TimeProvider`/owned resources where applicable.
- Keep SQL Server-specific behavior in SQL Server tests. Use SQLite only where provider compatibility is intentionally supported; do not contort production SQL Server behavior to satisfy an incompatible SQLite assumption.
- Align Web contract tests with current authoritative routes/contracts without weakening meaningful behavior. Fix stale tests only after confirming the existing implementation is correct.
- Add a versioned production test manifest/script or CI entry point that identifies required lanes: hermetic full suite, SQL Server, Docker migration/restore, browser, and opt-in real-provider checks.
- If a failure is demonstrably unrelated and cannot be safely fixed in this release, place it in a narrow explicit quarantine lane that still executes and reports failures. Record owner, evidence, reason, expiry date, and removal condition. No finance/accounting, tenant-isolation, authorization, migration, recovery, or external-side-effect test may be quarantined.
- Remove obsolete duplicate test infrastructure only when replacement coverage is equal or stronger and the ownership boundary is clearer.
- Document the resulting matrix, prerequisites, expected duration, artifacts, and failure triage path.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not change production behavior merely to satisfy a stale assertion unless the assertion captures the intended authoritative behavior.
- Do not use blanket filters, broad traits, project exclusions, retries-until-green, or increased timeouts as substitutes for deterministic fixes.
- Do not require external providers or an existing developer database for normal test projects.
- Preserve valid historical regression coverage and test project ownership.

### 6. Acceptance criteria

- Given a clean checkout with documented prerequisites, when the hermetic production matrix runs twice, then it produces the same pass/fail inventory without order-dependent or shared-state failures.
- Given SQL Server-specific accounting behavior, when tested, then it runs in the SQL Server lane and is not falsely validated only through SQLite.
- Given a proven unrelated quarantined test, when CI runs, then the test still executes visibly with owner/reason/expiry and cannot make an accounting release appear green.
- Given the full matrix results, when reviewed, then no unexplained finance/accounting, tenancy, authorization, migration, recovery, or side-effect failure remains.
- Given current API/Web contracts, when Web contract tests run, then route, auth, correlation, not-found, and payload behavior agree without universal stringly typed clients.

### 7. Verification

- Run every test project through the new matrix and retain machine-readable results.
- Run the matrix twice or use targeted repetition for formerly flaky/shared-state classes.
- Run focused Finance/accounting suites after every relevant fix, then one final full matrix.
- Build `VirtualCompany.sln` in Release with documented platform prerequisites.
- Run `git diff --check` and confirm no valid tests were skipped, weakened, or silently filtered.

### 8. Definition of done

- The production matrix, isolation fixes, production defect fixes, governed quarantine mechanism if needed, CI/script entry point, documentation, and final results are complete.
- No unexplained accounting-critical failure or hidden exclusion remains.
- No report-only output, flaky retry workaround, weakened assertion, or deferred in-scope TODO remains.

---

## Prompt 2 — Enforce production finance source mode and accounting enablement

### 1. Title and outcome

Ensure production finance pages, APIs, tools, reports, and exports use explicit operational accounting sources and cannot silently seed, mix, or substitute simulation/mock data.

### 2. Current context

- Finance read contracts expose `FinanceDataSources` filters for combined, operational, Fortnox, and simulation views.
- `CompanyFinanceReadService`, `CompanyFinanceSummaryQueryService`, invoices, bills, payments, and transactions apply source filters.
- Simulation Lab, finance seeding, deterministic simulation generation, and `MockFinanceToolProvider` are intentional development/simulation capabilities.
- Accounting setup status distinguishes configured internal-ledger companies from legacy/simulation companies; strict posting begins only after accounting configuration.
- Existing pages do not consistently make source/authority mode prominent, and production configuration must prove that mock/simulation paths are not implicit defaults.

### 3. Dependencies

- Release 0 Prompt 1.

### 4. Implementation requirements

- Define one authoritative company finance operating-mode/readiness decision covering accounting authority, setup/migration readiness, simulation state, connected provider state, allowed read source, allowed posting source, and plain next action.
- Make normal production Finance routes and API reads default to operational authority rather than `all` or simulation. Preserve explicit source selection only where the user has permission and the UI clearly identifies it.
- Keep Simulation Lab fully functional and isolated behind its existing feature gate/authorization. Simulation records must retain explicit source metadata and must not appear in operational totals, reports, exports, close checks, readiness, or agent answers unless simulation is intentionally selected.
- Register `MockFinanceToolProvider` only in explicit test/development/simulation configuration. Production startup must fail or omit it deterministically; it must never be a fallback when operational providers are unavailable.
- Ensure finance agents/tools receive the authoritative source mode and reject ambiguous/mixed-source questions or actions rather than silently combining facts.
- Add source/authority metadata to affected summary/detail/report/export contracts and retained artifacts so every number can identify internal, Fortnox/provider, simulation, migrated, or combined provenance.
- Update backend command policies so simulation/provider/internal records cannot be posted, approved, allocated, reconciled, exported, or migrated under the wrong authority.
- Add startup/readiness validation for invalid production combinations, including simulation auto-seeding enabled, mock provider enabled, missing internal setup, conflicting authorities, and unsupported mixed source.
- Add an additive migration only if durable operating-mode/provenance state is required; preserve all existing source references and migration behavior.
- Update operator configuration and rollout documentation with safe production defaults and explicit Simulation Lab behavior.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not delete Simulation Lab or historical simulated records; isolate and label them.
- Do not infer authority from which records happen to exist. Use `AccountingConfiguration`, `AccountingAuthorityPeriod`, provider connection/switch state, and explicit company simulation state.
- UI source labels are not a security boundary; every command/query enforces mode server-side.
- Preserve Fortnox and provider-switch coexistence/read behavior.

### 6. Acceptance criteria

- Given a production company with internal authority, when normal Finance pages/APIs/tools load, then operational internal data is used and simulation/mock records do not affect totals.
- Given an explicit authorized Simulation Lab context, when simulation is selected, then records are labeled and isolated and cannot be posted into production accounting inadvertently.
- Given production configuration with a mock provider or unsafe automatic simulation seeding, when startup/readiness runs, then the condition fails visibly with remediation.
- Given conflicting or unavailable accounting authority, when a financial mutation is attempted, then the backend blocks it before any partial state or side effect.
- Given an export/report/agent answer, when evidence is inspected, then source and accounting authority are explicit and drillable.

### 7. Verification

- Unit/policy tests for every operating-mode combination and source filter.
- API authorization, tenant-isolation, source spoofing, wrong-authority, and safe error tests.
- Production configuration/startup tests proving mock/simulation fallback is disabled.
- Regression tests for Simulation Lab, finance seeding, Fortnox reads/writes, provider switching, reports, close, dashboards, and agents.
- API/Web builds; migration/no-pending-model checks if schema changes.

### 8. Definition of done

- The authoritative operating-mode decision, production defaults, source metadata, backend enforcement, tool behavior, startup/readiness checks, tests, migration if needed, and runbook are complete.
- Simulation and mock capabilities remain available only in explicit safe contexts.
- No silent mixed-source result, production mock fallback, ambiguous authority, or in-scope TODO remains.

---

## Prompt 3 — Deterministic end-to-end accounting integrity scenario

### 1. Title and outcome

Implement one production-shaped executable accounting scenario that proves setup, operational source documents, approvals, posting, settlement, reconciliation, reporting, close, export, and recovery agree on the same immutable accounting truth.

### 2. Current context

- The current repository has focused tests for setup, posting, customer invoices, supplier bills, payments, allocations, bank reconciliation, tax review, close, exports, migration, and recovery, but evidence is distributed across many classes.
- `IAccountingPostingService` is the governed posting seam.
- `CustomerInvoiceAccountingService`, `SupplierBillAccountingService`, payment/allocation services, `CompanyBankTransactionService`, `AccountingReportingService`, `CompanyReportingPeriodCloseService`, and recovery verification already implement the main flow.
- Existing seed/simulation data cannot serve as production authority for this scenario.

### 3. Dependencies

- Release 0 Prompts 1–2.

### 4. Implementation requirements

- Create a deterministic production test scenario using real domain/application services and production-shaped persisted records, not controller shortcuts, mocks of accounting decisions, or simulation records.
- Cover: company/accounting setup; chart roles/period/series; one customer invoice and one supplier bill with source evidence; preview/approval/posting; incoming/outgoing payments; partial and final allocations; bank statement import; bank-payment matching; one suspense/correction branch; tax summary review; AR/AP/control/bank reconciliation; P&L/balance sheet/trial balance/general ledger; close validation; close/lock; durable export; and recovery verification.
- Record stable expected source/version identities, voucher/document references, debit/credit totals, control-account balances, open-item states, tax facts, report checksums, export checksum, evidence links, approvals, audits, and recovery checksum.
- Exercise idempotent replay of each command, a stale approval/source attempt, a duplicate bank row, and one forced transactional rollback.
- Add a reusable but narrowly owned test fixture/builder that creates only inputs; all decisions and side effects must go through production services.
- If the scenario exposes conflicting existing semantics, fix the authoritative production behavior and add focused regression tests rather than coding exceptions into the scenario.
- Make the scenario runnable in the hermetic/SQL Server matrix as appropriate and document its data/control flow.
- Add no production sample company or seeded business data.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not bypass authorization where the API path itself is under test; service-level and API-level scenarios may share expected evidence but have distinct responsibilities.
- Do not assert implementation-private call order when accounting outcomes/evidence provide a stronger contract.
- Closed-period and export/recovery facts remain immutable.
- Country-neutral mode must be described honestly; this scenario proves bookkeeping integrity, not Swedish statutory compliance.

### 6. Acceptance criteria

- Given a clean test database, when the scenario completes, then total debits equal credits and AR, AP, bank, tax, open-item, and financial-statement values reconcile exactly.
- Given command replays, when the same idempotency/source versions are used, then no journal, allocation, bank row, approval, export, or audit business event is duplicated.
- Given stale or cross-company inputs, when attempted, then no partial accounting or side-effect state is created.
- Given close and export completion, when recovery verification runs, then all journal/source/evidence/snapshot/export checksums match expected values.
- Given a forced failure inside posting/allocation/reconciliation, when the transaction rolls back, then subsequent retry produces the same final evidence as the clean path.

### 7. Verification

- Run the scenario against SQL Server for transactional/concurrency behavior; use SQLite only for explicitly compatible projections.
- Add API-level authorization/tenant scenario coverage for the most sensitive transitions.
- Run focused policy/unit tests around every defect discovered.
- Run full Finance and affected API suites plus accounting Web contract regressions.
- Verify deterministic output across repeated clean database runs.

### 8. Definition of done

- The executable scenario, production fixes, reusable test inputs, expected evidence, documentation, and matrix integration are complete.
- The scenario proves the current accounting core from setup through recovery without simulation authority.
- No mock accounting decision, hard-coded success, report-only artifact, or deferred in-scope TODO remains.

---

## Prompt 4 — Harden Finance workers and operator recovery

### 1. Title and outcome

Make every Finance background workflow safe under duplicate delivery, concurrency, process death, cancellation, transient/permanent failure, and poison work, with an operator-visible path to retry, reconcile, or stop without direct database edits.

### 2. Current context

- Finance registers hosted services for seed backfill, report regeneration, exports, historical migration, six provider-switch stages, monitoring, approval backfill, insights snapshots, analytics refresh, integration startup sync, bill registration reconciliation, seeding, and simulation progression.
- Several accounting/provider-switch workers already use durable leases, bounded batches, retries, evidence hashes, and safe failures.
- Background execution/outbox entities and platform infrastructure already exist; worker implementations are capability-specific by design.
- There is no single release proof that every Finance worker meets the same operational failure contract.

### 3. Dependencies

- Release 0 Prompts 1–3.

### 4. Implementation requirements

- Inventory every Finance hosted service/job runner and document its durable unit of work, company scope, trigger, claim/lease behavior, batch bound, idempotency identity, retry classes, cancellation semantics, progress, terminal states, audit/telemetry, and operator action.
- Add missing durable work records/checkpoints only where current work can be lost, replayed unsafely, or become invisible. Reuse platform background/outbox primitives when they fit; do not replace capability state with a generic opaque payload.
- Make claims atomic and company-explicit even when global query filters are bypassed. Prevent concurrent claim/execute of the same work and retain lease owner/expiry/attempt evidence safely.
- Treat expired leases/process death as bounded recoverable failure. Increment consecutive failure correctly, preserve completed checkpoints, and stop after configured exhaustion with an actionable terminal reason.
- Classify cancellation, validation, authorization, concurrency, provider rate limit, transport timeout, ambiguous provider result, persistence failure, object-storage failure, and poison payload. Retry only transient failures.
- Ensure ambiguous external results enter reconciliation and are not automatically replayed. Add provider lookup/reconciliation where the provider supports it.
- Add operator read models and authorized actions for queue/progress, retry eligible work, reconcile ambiguity, cancel where safe, and acknowledge/close permanent failure. Preserve every attempt/audit event.
- Add readiness/health signals for backlog age, leased work, exhausted failures, poison work, missing worker configuration, and reconciliation-required outcomes.
- Add consistent structured logging and metrics with company/work/correlation identifiers, duration, attempts, backlog, completion, failure class, and retry timing—without secrets or source-document content.
- Keep HTTP requests bounded: enqueue or request durable work and return status/progress links.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not create one generic Finance worker/manager that erases capability rules or state.
- Hosted services are registered once, create scopes, and resolve scoped DbContext services correctly.
- Operator cancellation cannot undo issued/posted/external-success facts; it stops only safe future work and may require reconciliation/forward correction.
- Do not let automatic retries exceed provider rate limits or duplicate external side effects.

### 6. Acceptance criteria

- Given duplicate/concurrent worker execution, when the same work is claimed, then one logical execution advances and no business side effect duplicates.
- Given process death after a persisted checkpoint, when the lease expires, then work resumes after the checkpoint and exhausts safely after the configured bound.
- Given validation/permanent failure, when classified, then no retry loop occurs and the operator receives a safe remediation.
- Given timeout after possible provider success, when handled, then state is reconciliation-required and no blind replay occurs.
- Given an operator with view/admin permissions, when inspecting/retrying/stopping work, then allowed actions are server-authoritative, tenant-safe, audited, and require no data edits.

### 7. Verification

- Failure-injection tests for every worker category and failure class, including lease expiry, cancellation, duplicate claims, poison work, object failure, and provider ambiguity.
- Authorization/tenant-isolation tests for operator reads/actions and background `IgnoreQueryFilters` paths.
- Hosted-service registration/scope tests and configuration validation tests.
- Backlog/readiness/metric/log tests with secret-safety assertions.
- Full Finance/API regression suites and the Release 0 end-to-end scenario.

### 8. Definition of done

- Every Finance worker has a documented and tested durable execution/recovery contract, operator-visible state/actions, readiness signals, and appropriate telemetry.
- All discovered unsafe or invisible intermediate states are fixed with migrations where necessary.
- No infinite retry, duplicate side effect, swallowed failure, direct-data-edit recovery instruction, or in-scope TODO remains.

---

## Prompt 5 — Accounting performance, data lifecycle, and service objectives

### 1. Title and outcome

Establish measured production capacity for accounting writes, reads, reports, exports, synchronization, and queues, with bounded queries, correct indexes, safe retention, and actionable service objectives.

### 2. Current context

- Finance contains transactional posting, large read projections, reports/drill-down, dashboards, provider sync, migration, exports, reconciliation, and many worker queues.
- Most list contracts have some bounds, but supported company volumes, query budgets, retention rules, and service objectives are not defined consistently.
- Accounting evidence, journals, source identities, approvals, audits, closed snapshots, provider references, and recovery artifacts have strong retention requirements and cannot be purged casually.
- Existing telemetry includes `AccountingOperationsTelemetry`, finance seed metrics, logs, health/readiness, and provider diagnostics.

### 3. Dependencies

- Release 0 Prompts 1–4.

### 4. Implementation requirements

- Define documented supported-volume profiles for small/medium launch companies: accounts, fiscal periods, journals/lines, invoices/bills, payments/allocations, bank rows, documents/evidence, audits, provider references, exports, worker backlog, and concurrent users/jobs.
- Add deterministic SQL Server performance fixtures/generators for tests only; do not seed production. Generate production-shaped company-scoped relationships and source identities.
- Establish measurable service objectives/budgets for posting, common lists/details, trial balance/general ledger/statements, close validation, export request/completion, provider sync lag, reconciliation backlog, and worker queue age.
- Capture representative SQL/query plans and measure bounded API/service operations at target volumes. Fix N+1 queries, cartesian includes, unbounded materialization, client evaluation, missing pagination, repeated parsing, and avoidable tracking.
- Add or adjust tenant-leading composite/filtered indexes through EF migrations based on measured query shapes. Inspect migration SQL and avoid unsupported SQL Server filters or multiple-cascade paths.
- Add pagination/continuation and maximum ranges to any unbounded accounting/report/operator endpoint while preserving compatible defaults and typed clients.
- Define retention classes for immutable accounting truth, statutory/source evidence, operational attempts/logs, generated exports, simulation data, and ephemeral caches. Implement authorized preview and bounded cleanup/archive only for data eligible under policy.
- Never delete posted journals, source/evidence identity, finalized reports/returns, approvals/audits required for explanation, or provider reconciliation evidence. Expired exports may lose binary content only if metadata/manifests and regeneration/retention policy make that safe.
- Add dashboards/metrics/alerts for SLO breach, slow query, queue age, sync lag, export age, reconciliation backlog, object failure, and cleanup outcome, with company-safe cardinality.
- Document capacity assumptions, measurement commands, index rationale, retention/archival policy, and scaling triggers.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Optimize from measured evidence; do not add speculative caches or denormalized aggregates without correctness/invalidation contracts.
- Any cache/read model must remain company-scoped and preserve source IDs/drill-down.
- Retention is not a workaround for performance. Destructive cleanup requires explicit eligible targets, preview, audit, bounded execution, and recovery policy.
- Tests may generate volume data; production code may not create mock business records.

### 6. Acceptance criteria

- Given each supported-volume profile, when critical operations run, then measured latency/throughput stays within documented budgets and queries remain bounded.
- Given two companies with large data, when plans/queries execute, then tenant-leading predicates/indexes are used and no cross-company results/caches appear.
- Given eligible expired operational artifacts, when cleanup preview/run executes, then only authorized records/objects are affected and immutable accounting evidence remains.
- Given a performance/SLO breach, when telemetry is inspected, then the operation, company-safe context, duration/backlog, and remediation signal are visible.
- Given migrations on representative databases, when applied locally and in Docker SQL Server, then indexes are valid and no pending model change remains.

### 7. Verification

- SQL Server performance tests at documented volumes with repeatable measurements and query-plan evidence.
- Correctness comparisons before/after optimization for reports, balances, source drill-down, and tenant filters.
- Migration fresh/upgrade tests, local/Docker compatibility, and pending-model check.
- Retention preview/execution authorization, tenant, idempotency, failure, audit, and recovery tests.
- Full Finance/API suites and Release 0 end-to-end scenario.

### 8. Definition of done

- Supported volumes, service objectives, performance fixtures, measured optimizations, indexes/migration, pagination, retention/archive controls, telemetry, tests, and operator documentation are complete.
- All critical accounting paths meet the agreed budgets or the release is explicitly blocked with evidence.
- No speculative cache, unbounded query, unsafe purge, mock production data, or deferred in-scope TODO remains.

---

## Prompt 6 — Authenticated accounting UI UAT, accessibility, and operational polish

### 1. Title and outcome

Make every accounting route production-usable through authenticated English/Swedish user-acceptance testing, accessibility and responsive fixes, complete operational states, and visually consistent action paths.

### 2. Current context

- Accounting Web routes include setup, accounts, periods, journals, manual journal drafts, reconciliation, reports, connections, and the provider migration workspace, alongside Finance invoices, supplier bills, payments, transactions, issues, cash, and settings.
- Existing design references cover chart of accounts, setup, journals, reconciliation, reports/close, authority/connections, manual journals, and migration monitoring/workspace.
- Component and focused surface tests exist, but release evidence says authenticated live-browser coverage with realistic persisted data is incomplete and broader Web/contract failures have existed.
- Prompts 1–5 establish green contracts, explicit source mode, an end-to-end scenario, worker/operator states, and performance/readiness data.

### 3. Dependencies

- Release 0 Prompts 1–5.
- An authenticated local/release test environment with deterministic production-shaped accounting data from Prompt 3 or an equivalent isolated fixture. Do not expose real customer data.

### 4. Implementation requirements

- Create a route/role/locale/state UAT matrix for every accounting and directly connected Finance route, covering accounting viewer, admin, approver, employee denial, no company, not configured, operational, provider authority, simulation-only, loading, empty, populated, blocked, failed, stale, and recovery states as applicable.
- Use the existing reference screenshots and design system. If any page requires a significant redesign or new major component, first write the screenshot prompt, generate/save a new reference under `docs/design/references/`, implement against it, and compare visually.
- Run authenticated browser flows for setup; account/fiscal-year management; manual journal approval/posting/correction; customer/supplier accounting; payments/allocations; bank reconciliation/suspense correction; reports/tax review/close/export; authority/connections; provider migration; and operations/recovery links.
- Fix navigation, stale UI state, typed-client contract, auth/correlation, loading/empty/error/retry, focus management, validation summary, confirmation, timeline, allowed-action, localization, responsive, and visual issues found.
- Ensure every screen plainly answers what is happening, what needs attention, and what to do next. Show source/authority mode without internal jargon.
- Complete English and Swedish resources. Prevent raw enums, reason codes, hashes, provider payloads, tenant terms, and simulation tooling from leaking into ordinary production copy.
- Meet keyboard-only operation, visible focus, semantic headings/landmarks, labels/descriptions, table navigation, dialog focus/escape, error association, color contrast, zoom/reflow, and screen-reader status announcement expectations.
- Verify narrow phone/tablet layouts without turning the Web app into a separate mobile accounting product.
- Add bUnit and Web contract regressions for every fixed defect and retain browser screenshots/evidence without shipping references as UI assets.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and `docs/design.md`.
- Do not redesign stable pages merely for novelty; use evidence-led polish and existing tokens/components.
- Disabled/hidden UI is not authorization. Server responses remain authoritative and are tested.
- Do not start multiple untracked Web hosts; follow the repository local Web verification instructions exactly.
- Do not use mock production data or bypass authentication for final browser evidence.

### 6. Acceptance criteria

- Given each accounting route, role, and relevant state, when opened through an authenticated browser, then content/actions match backend authorization and no stale or broken transition remains.
- Given English and Swedish locales, when primary flows run, then all visible text, validation, errors, statuses, and empty states are localized and human-readable.
- Given keyboard-only, screen-reader-oriented semantics, 200% zoom, and narrow layouts, when key flows run, then tasks remain complete without hidden controls or lost context.
- Given a failure or worker/operator issue, when surfaced, then the user sees safe remediation and a direct action path rather than a dead end.
- Given the design references, when final screenshots are compared, then hierarchy, spacing, cards, tables, states, and responsiveness are visually consistent.

### 7. Verification

- bUnit/component and Web contract tests for all fixes and state/role/locale matrices.
- Authenticated browser UAT with retained screenshots and console/network error review.
- Automated accessibility scan plus manual keyboard/focus/zoom checks on every major accounting page pattern.
- API and Web Release builds and the full Web/Web contract lanes from Prompt 1.
- Confirm reference images are present only as design evidence and not used as static product UI.

### 8. Definition of done

- The UAT matrix, browser evidence, accessibility/responsive/localization fixes, regression tests, updated references where required, and operator-facing action paths are complete.
- No critical/high UAT or accessibility defect remains; lower findings have explicit owners and do not obscure accounting state/action.
- No auth bypass, mock production page, raw internal language, dead-end failure, or in-scope TODO remains.

---

## Prompt 7 — Automated migration, recovery, and Accounting Core GA evidence

### 1. Title and outcome

Automate and prove fresh install, representative upgrade, coordinated SQL/object backup, local and Docker restore, recovery checks, and the final Accounting Core GA release decision.

### 2. Current context

- `VirtualCompany.Persistence.Migrations` is the SQL Server migration authority.
- `restore-local-sql-db.ps1`, `restore-virtualcompany-db.ps1`, and `verify-accounting-recovery.ps1` already support local/Docker restoration and accounting/object verification.
- Existing release evidence proves earlier representative restore rehearsals, but Release 0 changes and the final production matrix require a new automated proof.
- Prompts 1–6 establish test health, source-mode safety, an end-to-end scenario, worker recovery, supported-volume/SLO controls, and UI evidence.

### 3. Dependencies

- Release 0 Prompts 1–6.
- Available isolated local SQL Server and Docker SQL Server environments for the release rehearsal.
- Configured isolated object storage or filesystem-backed production-equivalent test storage. No production database/object bucket may be used.

### 4. Implementation requirements

- Add a controlled automation entry point that creates unique isolated targets and runs: fresh migration; representative historical restore/upgrade; application compatibility checks; `DBCC CHECKDB`; EF migration history/model validation; and teardown.
- Choose and document a representative pre-native-accounting/provider-switch backup or construct a deterministic historical baseline through checked-in migrations. Do not use only an already-current database.
- Create a coordinated SQL backup and object manifest/archive with checksums, lengths, stable object keys, backup metadata, and `RESTORE VERIFYONLY`/equivalent verification.
- Restore the same coordinated pair through both local SQL Server and Docker scripts, apply remaining migrations, start only controlled verification services if needed, and run the Prompt 3 accounting scenario/recovery verification against restored facts.
- Verify company/accounting configuration, authority, accounts, periods, voucher uniqueness, balanced journals, source/version/evidence links, approvals/audits, payments/allocations, bank reconciliation, report snapshots, exports, provider references/switch evidence, worker terminal/checkpoint state, and object hashes.
- Harden scripts for safe explicit targets, bounded timeouts, non-admin paths, temporary-file cleanup, actionable failures, and secret-free output. Never delete broad/computed paths without validated workspace/target containment.
- Integrate the production test matrix, SQL lanes, restore rehearsal, browser evidence, performance results, pending-model check, secret/license scan, and build artifacts into a controlled release pipeline or repeatable release command.
- Update `docs/finance/accounting-operations-runbook.md` with deployment order, worker feature enablement, readiness checks, recovery objectives, backup coordination, forward-fix rollback, and escalation.
- Produce a checked-in Release 0 evidence document with exact date/environment, migration order, commands/results, checksums safe to retain, supported volumes/SLO results, browser evidence, test/quarantine inventory, residual risks, and explicit go/no-go decision.
- Clean up every isolated database, container, temporary backup, staged file, object archive, ACL change, and managed host created by the rehearsal; preserve evidence artifacts intended for source control.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not mutate, drop, restore over, or take broad cleanup action against a production or developer source database.
- Application rollback keeps additive schema and immutable accounting evidence; disable workers/features and forward-fix. A data rollback requires the coordinated SQL/object pair and explicit treatment of post-backup writes.
- Do not mark GA if there is a pending model change, failed restore/checksum, unexplained accounting test, critical/high UAT issue, unresolved ambiguous provider result, or blocking readiness signal.
- Release evidence must report failures honestly; it is not marketing copy.

### 6. Acceptance criteria

- Given an empty local and Docker SQL Server target, when automation runs, then all migrations apply and the application/accounting verification succeeds.
- Given the representative historical baseline, when upgraded through current migrations on both paths, then accounting and provider-switch data remain compatible and no destructive workaround is used.
- Given the coordinated backup pair, when restored locally and in Docker, then database integrity, accounting totals, references, snapshots, exports, worker evidence, and object checksums match.
- Given a failure at any stage, when automation stops, then the exact stage/remediation is visible and only validated isolated resources are cleaned up.
- Given the complete release evidence, when reviewed, then the go/no-go decision follows explicit gates and has no hidden filtered failure.

### 7. Verification

- Execute the automation against isolated local and Docker SQL Server targets and retain concise results.
- Run `dotnet ef migrations list` and `has-pending-model-changes` with the production migrations project/startup configuration.
- Run the full production test matrix, SQL Server tests, authenticated browser matrix, performance budgets, and secret/license checks.
- Parse/validate all release and restore PowerShell scripts and test safe failure/cleanup paths.
- Run final Release solution build and `git diff --check`.

### 8. Definition of done

- Fresh install, representative upgrade, coordinated backup, local/Docker restore, object verification, safe automation, cleanup, runbook, and checked-in GA evidence are complete.
- All Accounting Core GA exit criteria in `financial-roadmap.md` pass and the release decision is explicit.
- If any release stop remains, Release 0 is reported blocked rather than complete; no fake proof, unsafe cleanup, hidden failure, or deferred in-scope TODO remains.
