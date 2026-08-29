# Financial App P3 Implementation Prompts

Priority: P3 — Advanced Ledger and Subledgers  
Source roadmap: [financial-roadmap.md](financial-roadmap.md) Release 5  
Prompt order: execute Prompts 1–10 in order. The package deepens the native ledger; it must extend the existing posting authority rather than create parallel ledgers.

## Shared execution contract

Every prompt in this package is an implementation prompt, not an analysis or scaffolding task.

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, repository `AGENTS.md` files, and the current implementation before editing.
- Preserve accounting configuration, authority periods, account roles, fiscal periods, voucher series, immutable journals, reversals, manual journals, tax facts, evidence, source/version idempotency, financial-statement mappings, Releases 0–2, and P2 banking behavior.
- `IAccountingPostingService` remains the only native posting boundary. Extend its contracts/policies carefully; do not duplicate balancing, period, voucher, authority, approval, evidence, retry, or idempotency behavior in subledgers.
- Keep document currency, functional currency, rate provenance, dimensions, schedules, assets, mappings, and control reconciliations in normalized company-scoped relational state. Historical rates, posted facts, snapshots, and schedule outcomes are immutable or corrected by linked reversals/adjustments.
- Every company-owned record, query, command, uniqueness rule, worker claim, journal identity, report filter, audit event, and cache/object key must enforce company scope and have cross-company tests.
- Schema work requires additive SQL Server EF migrations, updated snapshots, representative upgrades, and `has-pending-model-changes` verification under the `Database and EF Core` rules in `docs/architecture-rules.md`.
- UI work follows `ui-instructions.md` and the mandatory screenshot-first workflow in `docs/design.md`; backend policies remain authoritative and English/Swedish localization is complete.
- Unsupported currency, rate, dimension, tax, asset, inventory, or schedule combinations must stop explicitly. Never silently fall back to base currency, default dimensions, current rates, or generic manual journals.
- Finish each prompt with production implementation, focused tests, broader regression validation, operator/accounting documentation, and no deferred in-scope TODOs.

---

## Prompt 1 — Functional currency and authoritative exchange-rate foundation

### 1. Title and outcome

Implement versioned exchange-rate sources and deterministic currency conversion so every later foreign-currency posting can reproduce the rate, date, precision, and rounding used.

### 2. Current context

- `AccountingConfiguration` has a base currency, documents carry currency, and invoice/bill accounting accepts exchange-rate inputs in limited paths.
- `AccountingPostingService` currently requires every journal line to use the company base currency.
- There is no governed rate catalogue, source precedence, rate approval, historical-rate lookup, or retained conversion result.

### 3. Dependencies

- Releases 0–2.
- Select supported rate source(s), licensing/retention rules, update cadence, and manual-rate approval policy.

### 4. Implementation requirements

- Add currency definitions, rate providers, rate sets, observations, source/version/effective timestamps, quotation convention, approval state, import identity, and correction history.
- Implement deterministic lookup/conversion policy for transaction date, settlement date, period end, unavailable/stale rates, inverse/cross rates, precision, and rounding residuals.
- Add provider/manual import commands, durable scheduled refresh where external, protected raw evidence/checksums, authorization, audit, telemetry, and safe failure states.
- Add read APIs for supported currencies, exact historical observation, lookup explanation, and readiness; expose no provider credential or unbounded raw payload.
- Add migration, tenant/date/source indexes, uniqueness rules, and retention guidance.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Posted documents/journals retain the exact selected rate identity and calculated amounts; later provider corrections never mutate historical accounting.
- A missing/stale/ambiguous required rate blocks accounting rather than assuming 1.0 or today's rate.

### 6. Acceptance criteria

- Given an approved historical observation, when conversion is repeated, then the same source, rate, precision, rounding, and result are returned.
- Given conflicting/manual/stale rates, when policy cannot decide safely, then a typed review/blocker is produced without posting.
- Given another company's rate override, when queried or referenced, then it is inaccessible.

### 7. Verification

- Conversion, inverse/cross-rate, precision, date-selection, precedence, stale/missing, correction, and concurrency tests.
- Provider/manual import, authorization/tenant, migration, retention, audit-redaction, and deterministic golden-fixture tests.

### 8. Definition of done

- A reproducible company-scoped rate authority exists for every enabled currency path, with no current-rate fallback or mutable historical result.

---

## Prompt 2 — Multi-currency documents, open items, and journal facts

### 1. Title and outcome

Extend native customer invoices, supplier bills, payments, and open items to retain document and functional amounts and post balanced functional-currency journals.

### 2. Current context

- Native receivables and supplier-bill accounting retain document currency, totals, snapshots, allocations, and journals.
- Foreign-currency accounting is currently blocked or requires shallow exchange-rate input; ledger lines retain only one currency/amount authority.

### 3. Dependencies

- P3 Prompt 1.
- Swedish tax pack limitations and supported currency/document combinations must be explicitly reviewed.

### 4. Implementation requirements

- Add document, functional, and where required tax-base amounts; rate identity/date; conversion/rounding facts; and currency-aware outstanding balances to immutable invoice/bill snapshots and open-item records.
- Extend draft preview, issue/post, supplier accounting, credit/correction, recurring generation, statement/aging, approval, and control-reconciliation policies.
- Extend proposed/posted accounting line contracts and persistence to retain document currency amounts while balancing and reporting in functional currency.
- Preserve backward compatibility for existing base-currency records through explicit migration semantics; do not invent historical rates for ambiguous data.
- Update APIs, typed clients, exports, audit, telemetry, migration, and source/version idempotency hashes.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Tax decisions and statutory outputs remain governed by the selected pack; enabling a currency does not imply tax/jurisdiction support.
- Issued documents, posted journals, and their rate facts are immutable.

### 6. Acceptance criteria

- Given a supported foreign-currency invoice or bill, when posted, then document totals and functional journal amounts reproduce from the retained rate facts.
- Given a base-currency historical record, when read after migration, then its amounts remain unchanged and its currency provenance is explicit.
- Given an unsupported currency/tax combination, when previewed, then issue/post is blocked before numbering or journal allocation.

### 7. Verification

- Draft, posting, rounding, tax, credit, aging, statement, recurrence, idempotency, and control-account tests across representative currencies.
- SQL migration/upgrade tests, API tenant/authorization tests, export/report regressions, and no-pending-model check.

### 8. Definition of done

- Supported documents and open items carry complete dual-amount evidence and produce one balanced functional-currency accounting truth.

---

## Prompt 3 — Foreign-currency settlement and realized gain/loss

### 1. Title and outcome

Implement currency-aware settlement so partial/full payments recognize realized exchange differences exactly once and open items reconcile in both document and functional currency.

### 2. Current context

- Payments, allocations, bank reconciliation, cash settlement posting, customer refunds, and AR/AP control reconciliation exist.
- Existing allocation models are not a complete FX settlement subledger and do not govern realized gain/loss across partial payments.

### 3. Dependencies

- P3 Prompts 1–2.
- P2 advanced reconciliation and bank settlement behavior.

### 4. Implementation requirements

- Extend payment/allocation facts with payment currency, settlement rate identity/date, allocated document/functional amounts, residuals, and realized gain/loss.
- Implement deterministic policy for partial payments, multiple settlement rates, over/underpayment, fees, refunds, credits, write-offs, and final residual rounding.
- Resolve governed gain/loss/rounding accounts through account roles and post via `IAccountingPostingService` in the same atomic/idempotent business operation.
- Add corrections/reversals that preserve original settlement evidence and reopen or adjust open items correctly.
- Update reconciliation views, statements, aging, control reconciliations, APIs, audit, telemetry, and migration.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not change the issued document's historical functional amount when settlement rates differ.
- Provider-reported settlement without authoritative bank/payment evidence remains reconciliation-required.

### 6. Acceptance criteria

- Given a partial foreign-currency payment at a new rate, when allocated, then the open balance and realized difference reconcile in both currencies.
- Given replay or concurrent allocation, when processed, then one allocation and one gain/loss posting exist.
- Given reversal, when completed, then original evidence remains and open-item/control balances return to the correct state.

### 7. Verification

- Multi-payment, partial, overpayment, fee, refund, rounding, reversal, replay, concurrency, and locked-period tests.
- SQL atomicity, tenant isolation, bank-reconciliation, statements/aging, and AR/AP control-account regression tests.

### 8. Definition of done

- Foreign-currency settlement is deterministic, idempotent, reversible, and reconciled by document and functional currency.

---

## Prompt 4 — Period-end currency revaluation

### 1. Title and outcome

Add controlled period-end revaluation of foreign monetary balances with reproducible proposals, approval, posting, reversal, and close evidence.

### 2. Current context

- Periods, close validation, manual/adjusting journals, exchange-rate foundations, open items, snapshots, approvals, and immutable postings exist.
- No revaluation run, population snapshot, unrealized gain/loss policy, automatic reversal, or subledger-to-ledger reconciliation exists.

### 3. Dependencies

- P3 Prompts 1–3.

### 4. Implementation requirements

- Add revaluation run, population item, rate-set binding, proposal, approval, posting, reversal, exclusion/review, and reconciliation records.
- Calculate supported foreign cash, AR, AP, and configured monetary accounts at period-end rates with deterministic grouping, rounding, and unrealized gain/loss roles.
- Support preview, review, submit, approve, post, regenerate-before-approval, and next-period automatic reversal with stable idempotency.
- Block close for stale, failed, unposted, unreconciled, or superseded required runs.
- Add APIs, background scheduling where configured, reporting drill-down, audit, telemetry, migration, and runbook.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- A posted revaluation is immutable; correction uses reversal/replacement and never mutates original open-item facts.
- Regeneration may not duplicate a posted period/source population.

### 6. Acceptance criteria

- Given foreign monetary balances, when a run is posted, then its population, rate set, journal, and control totals reproduce exactly.
- Given changed rates before approval, when regenerated, then stale approval/proposals are invalidated.
- Given next period, when automatic reversal runs twice, then one reversal journal exists.

### 7. Verification

- Population, rounding, rate, exclusion, approval, idempotency, reversal, close-blocker, and control-reconciliation tests.
- SQL concurrency/migration tests, tenant/authorization tests, performance tests, and report/UI regressions.

### 8. Definition of done

- Period-end FX revaluation is an auditable subledger workflow with deterministic close checks and no manual spreadsheet dependency.

---

## Prompt 5 — Governed accounting dimensions and allocations

### 1. Title and outcome

Promote cost centers/projects into governed dimensions with effective dates, validation, allocation, posting, and report drill-down.

### 2. Current context

- Journal lines, budgets, some invoice/bill lines, and provider mappings carry optional cost-center or dimension facts.
- There is no authoritative dimension catalogue, combination policy, required-dimension rule, allocation engine, or complete dimension reporting.

### 3. Dependencies

- P3 Prompt 2 for currency-aware line facts.
- Decide initial dimension types and whether Fortnox project/cost-center mappings remain read-compatible.

### 4. Implementation requirements

- Add dimension types, members, hierarchies where required, effective dates, lifecycle, external mappings, required/allowed account policies, and valid combinations.
- Replace important string/JSON-only dimension authority with relational assignments while retaining immutable posted display/source snapshots.
- Validate dimensions in manual journals, invoices, bills, schedules, assets, bank adjustments, budgets, forecasts, imports, and provider mappings before posting.
- Add versioned percentage/fixed allocation templates with rounding, preview, approval where material, idempotent application, and source evidence.
- Add commands/queries/APIs, administration UI, report filters/drill-down, migration/backfill conflicts, audit, telemetry, and documentation.

### 5. Constraints and preservation rules

- Follow the Shared execution contract; `docs/design.md` screenshot-first workflow applies to new dimension administration UI.
- Never mutate dimension identity on a posted line; later rename/restructure changes current display mappings, not historical facts.
- Ambiguous legacy/provider values become explicit mapping conflicts.

### 6. Acceptance criteria

- Given an account requiring a dimension, when a posting omits or uses an invalid member, then preview/post is blocked with a stable reason.
- Given an allocation template, when applied, then generated lines preserve totals and rounding deterministically.
- Given a dimension report total, when drilled down, then it resolves to the same immutable journal lines.

### 7. Verification

- Lifecycle, effective-date, combination, required-policy, allocation, rounding, hierarchy, mapping-conflict, and concurrency tests.
- Cross-module posting regressions, tenant/authorization, migration, report performance, UI/browser, and no-pending-model checks.

### 8. Definition of done

- Dimensions are governed accounting facts across posting, planning, providers, and reporting rather than optional labels.

---

## Prompt 6 — Recurring journals, accruals, deferrals, and allocation schedules

### 1. Title and outcome

Implement reusable accounting schedules that safely generate recurring, accrual, deferral/prepayment, allocation, and automatic-reversal journals.

### 2. Current context

- Manual journal drafts, approvals, recurring customer invoices, leased workers, account roles, periods, and posting idempotency provide reusable patterns.
- No accounting schedule aggregate owns future journal occurrences, release rules, evidence, regeneration, or reconciliation.

### 3. Dependencies

- P3 Prompt 5 for dimension-aware schedules.

### 4. Implementation requirements

- Add schedule/template, lines, cadence, amount basis, dimensions, evidence, occurrence, generation lease, approval binding, journal link, reversal rule, and exception records.
- Support recurring fixed journals, date/period allocations, accrual with automatic reversal, and prepayment/deferral release with deterministic rounding/residual handling.
- Implement create/preview/submit/approve/activate/pause/resume/end/regenerate-safe workflows and bounded background generation.
- Recheck account/dimension/period/authority/approval/source versions before each post and route through `IAccountingPostingService`.
- Add reconciliation of scheduled amount versus released/remaining amount, close blockers, APIs, UI, audit, telemetry, migration, and runbook.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and reuse patterns, not customer-invoice schedule entities.
- Editing a schedule never mutates posted occurrences; version changes apply prospectively with retained history.
- Worker retries and restarts must not duplicate occurrence journals or reversals.

### 6. Acceptance criteria

- Given an approved schedule, when an occurrence becomes due, then one correctly dimensioned journal posts with retained schedule/version identity.
- Given process death or replay, when generation resumes, then completed occurrences are not duplicated.
- Given schedule totals, when queried, then original, released, reversed, remaining, and exception amounts reconcile.

### 7. Verification

- Cadence, proration, rounding, period boundary, approval invalidation, pause/end, worker lease, replay, reversal, and reconciliation tests.
- SQL concurrency/migration, tenant/authorization, close/reporting, UI/client, and recovery tests.

### 8. Definition of done

- Supported schedules generate and reconcile production journals safely without manual recurring-entry workarounds.

---

## Prompt 7 — Fixed-asset subledger and depreciation

### 1. Title and outcome

Build a fixed-asset subledger covering capitalization through disposal so asset balances, depreciation, evidence, and ledger control accounts reconcile.

### 2. Current context

- The current `FinanceAsset` is a shallow purchased/funding record and is not a depreciation register.
- Supplier bills, source documents, account roles, schedules, dimensions, approvals, journals, reports, and close checks exist.

### 3. Dependencies

- P3 Prompts 5–6.
- Confirm supported accounting/tax depreciation methods and Swedish candidate-pack limitations with qualified review; unsupported tax depreciation remains explicitly outside statutory claims.

### 4. Implementation requirements

- Add asset classes, assets, components, acquisition/capitalization sources, useful life, residual value, book method, dimension/custodian/location, status, evidence, and immutable history.
- Implement capitalization, placed-in-service, depreciation preview/run, partial period, componentization, improvement, transfer, impairment, disposal/sale, gain/loss, and reversal/correction workflows.
- Route every posting through `IAccountingPostingService`, retain exact schedule/run/source identity, and reconcile cost, accumulated depreciation, impairment, and net book value to configured control accounts.
- Add register/detail/reconciliation/report contracts, commands/APIs, approval rules, worker scheduling, audit, telemetry, migration/backfill conflicts, and runbook.
- Replace or explicitly migrate/rename shallow `FinanceAsset` semantics without inventing depreciation history or breaking simulation/source references.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Posted depreciation and disposals are immutable; corrections use linked reversals/adjustments.
- Book accounting support does not imply tax-register compliance unless a reviewed pack explicitly provides it.

### 6. Acceptance criteria

- Given a capitalized asset, when depreciation runs, then period expense and accumulated depreciation reproduce from retained class/asset versions.
- Given disposal, when posted, then cost, accumulated depreciation, proceeds, gain/loss, and status reconcile.
- Given migrated shallow assets without sufficient facts, when processed, then they remain visible conflicts rather than receiving invented schedules.

### 7. Verification

- Method, partial-period, component, improvement, transfer, impairment, disposal, reversal, worker, idempotency, and reconciliation tests.
- SQL migration/upgrade, tenant/authorization, source-document access, report/close, performance, and UI/client tests.

### 8. Definition of done

- A production fixed-asset register reconciles deterministically to the native ledger with complete lifecycle and evidence.

---

## Prompt 8 — Account lifecycle, reporting controls, series, and inventory boundary

### 1. Title and outcome

Complete accounting administration controls so accounts and series evolve safely and inventory/commerce integration has an explicit supported boundary.

### 2. Current context

- Accounts support creation, rename, deactivation, roles, posting restrictions, mappings, and effective dates in parts; code notes that lifecycle/reportability flags are incomplete.
- Voucher and statutory document series exist but are not fully configurable by transaction type, location, fiscal year, and jurisdiction.
- `FinanceAsset` and provider articles do not constitute inventory quantity or COGS accounting.

### 3. Dependencies

- P3 Prompts 1–7.
- Decide whether the initial SME segment requires native inventory/COGS. If not, implement only the stable commerce/inventory accounting boundary and explicit capability state.

### 4. Implementation requirements

- Add complete account lifecycle/reportability flags, effective-dated rename/classification history, replacement workflows, posting/report restrictions, merge prevention, and dependency impact previews.
- Extend document/voucher series policy by source/transaction type, fiscal year, location/dimension where selected, and policy-pack rule; preserve uniqueness, gap evidence, and concurrency.
- If inventory is selected, add stock-accounting subledger sources, valuation/COGS policy, adjustments, close reconciliation, and external quantity boundary. Otherwise add versioned commerce events/contracts and explicit unsupported inventory capability without quantity state in Finance.
- Update setup/admin APIs and UI, provider mappings, reports, migrations/backfill conflicts, audit, telemetry, and documentation.

### 5. Constraints and preservation rules

- Follow the Shared execution contract; screenshot-first workflow applies to material administration UI changes.
- Never delete/merge an account with historical postings or renumber issued documents/vouchers.
- Do not implement a partial inventory quantity system inside the ledger.

### 6. Acceptance criteria

- Given an account with historical use, when retirement is requested, then dependency impact is visible and future posting follows replacement/restriction policy without changing history.
- Given concurrent allocation in a configured series, when documents post, then identities remain unique and gap policy is retained.
- Given an unsupported inventory request, when received, then it is explicitly blocked at the stable integration boundary.

### 7. Verification

- Account lifecycle/dependency, series concurrency/gap, provider mapping, historical reporting, and capability-boundary tests.
- SQL migration/upgrade, tenant/authorization, setup/admin UI/browser, and no-pending-model checks.

### 8. Definition of done

- Accounts and series are safely governable over time and inventory scope is implemented completely or bounded explicitly without an accidental half-ledger.

---

## Prompt 9 — Advanced accounting workspace and drill-down

### 1. Title and outcome

Expose multi-currency, dimensions, schedules, assets, revaluation, and reconciliations through a coherent accountant-grade Web workspace.

### 2. Current context

- Accounting setup, accounts, periods, journals, reconciliation, reports, manual journals, and evidence drill-down pages already exist.
- Prompts 1–8 add advanced states that must not be scattered across technical/admin pages or exposed as storage tokens.

### 3. Dependencies

- P3 Prompts 1–8.

### 4. Implementation requirements

- Extend the Finance accounting information architecture with focused Currency/Rates, Dimensions, Schedules, Assets, and Revaluation workspaces while preserving canonical/compatibility routes.
- Provide list/detail, preview, review/approval, exception, reconciliation, timeline, journal/evidence drill-down, and operator recovery states using authoritative backend read models.
- Add cross-links among source documents, subledger items, rates, dimensions, occurrences, approvals, journals, reports, and close blockers.
- Complete responsive behavior, keyboard/screen-reader support, localization, empty/loading/stale/error states, typed clients, and safe problem mapping.

### 5. Constraints and preservation rules

- Follow the Shared execution contract. The mandatory screenshot-first workflow in `docs/design.md` applies to every new or materially redesigned screen.
- UI must not calculate rates, depreciation, allocations, eligibility, or allowed actions.
- Keep daily work distinct from system/provider administration.

### 6. Acceptance criteria

- Given an accountant investigating a reported amount, when drilling down, then the path reaches immutable journal lines and the originating subledger/rate/dimension evidence.
- Given a blocked/stale operation, when displayed, then the reason and safe next action are plain, localized, and backend-derived.
- Given narrow viewport and keyboard-only use, when completing supported review flows, then no information or control is inaccessible.

### 7. Verification

- Presenter/client/component tests and authenticated English/Swedish desktop/narrow browser UAT against stored reference images.
- Accessibility checks, route compatibility tests, authorization tests, and supported-volume query measurements.

### 8. Definition of done

- Advanced accounting work is discoverable, explainable, and operable without exposing technical implementation state or requiring database access.

---

## Prompt 10 — Advanced ledger release proof and operations

### 1. Title and outcome

Prove advanced-ledger correctness, reconciliation, migration, recovery, and capacity and publish an evidence-backed production decision.

### 2. Current context

- The repository has production test-matrix, accounting integrity, SQL/Docker recovery, capacity, readiness, and release-evidence patterns.
- Prompts 1–9 add high-risk monetary calculations and multiple new subledgers that must reconcile to the same immutable journal truth.

### 3. Dependencies

- P3 Prompts 1–9.
- Qualified review for any jurisdiction-specific currency, asset, tax, or inventory claim.

### 4. Implementation requirements

- Extend accounting readiness and close checks for rate coverage, currency control differences, revaluation, dimensions, schedules, assets, series, and inventory capability.
- Add one deterministic advanced-accounting scenario spanning foreign invoice/bill, settlement gain/loss, bank reconciliation, revaluation/reversal, dimensions/allocation, accrual/prepayment, asset depreciation/disposal, reports, and close.
- Run fresh/upgrade migrations, SQL concurrency/rollback, worker restart, regeneration, large-volume reporting, and coordinated SQL/object recovery.
- Prove every subledger control account and document/functional currency total reconciles; retain golden calculations and checksums.
- Publish supported cases/limits, deployment/rollback/forward-fix, rate outage, schedule recovery, asset correction, and data-retention runbooks plus go/no-go evidence.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Environment-gated or professionally reviewed prerequisites remain explicit release stops when absent.
- Rollback never mutates posted rates, journals, schedules, depreciation, documents, series, or report snapshots.

### 6. Acceptance criteria

- Given the advanced scenario, when run on SQL Server, then all currency, dimension, schedule, asset, and control-account totals reconcile and reproduce after restore.
- Given concurrent/replayed workers, when processing, then no duplicate journal, reversal, depreciation, revaluation, or occurrence exists.
- Given a release candidate, when evidence is incomplete, then the decision is no-go with named remediation rather than inferred success.

### 7. Verification

- Full solution build and complete hermetic, SQL Server, Docker migration/restore, performance, browser, and applicable provider lanes.
- Security/tenant/authorization review, golden calculation review, and recovery checksum comparison.

### 8. Definition of done

- Advanced Ledger and Subledgers has reproducible accounting evidence, production operations, and no unresolved critical/high correctness or reconciliation gap.
