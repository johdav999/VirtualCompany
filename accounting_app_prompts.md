# Bare-Bones Accounting Application Implementation Prompts

## Purpose and Execution Order

This prompt pack implements the country-agnostic, native accounting application described in `accounting_app.md`. It is intended for micro-companies that do not already use an accounting product. Virtual Company's internal ledger is the normal accounting authority; Fortnox and future accounting products remain optional external adapters.

Execute the prompts in order. Each prompt delivers a production-usable increment and establishes invariants required by later prompts:

1. Country-agnostic accounting configuration and policy-pack foundation.
2. Native ledger kernel, voucher sequencing, posting, and reversal.
3. Micro-company accounting setup, chart of accounts, and period administration.
4. Manual journal preparation, approval, posting, and correction.
5. Native customer invoice and credit-note accounting.
6. Native supplier bill and credit-note accounting.
7. Bank import, payment allocation, reconciliation, and suspense accounting.
8. General ledger, trial balance, statements, tax summary, close, and export.
9. Optional external-provider authority, export, synchronization, and cutover.
10. Historical migration, observability, recovery, and production release gate.

Do not stop the sequence at an intermediate checkpoint. If a prompt exposes an in-scope build or test failure, diagnose and fix it before continuing. Stop only for a genuine blocker described in `AGENTS.md`.

## Instructions for Every Prompt

- Read and follow `AGENTS.md`, `production-implementation.md`, `accounting_app.md`, and `docs/architecture-rules.MD` before implementation. `architecture-inst.md` is currently absent; if it exists at execution time, read and follow it.
- For any UI work, also read and follow `ui-instructions.md` and `docs/design.md`. Complete their mandatory screenshot-first workflow before implementing a new page, major component, modal, dashboard, or significant redesign. Save references under `docs/design/references/` and do not ship the reference image as a UI asset.
- Treat the current repository as authoritative. Reinspect the named files and nearby implementation before editing because paths and behavior may have changed since this pack was written.
- Implement production behavior as required by `production-implementation.md`: no scaffolding, mock production data, fake provider behavior, placeholder UI, silent fallback, or deferred in-scope TODOs.
- Keep accounting inside the existing Finance capability. Do not implement the native accounting engine as `IFinanceIntegrationProvider`, a microservice, a second DbContext, a parallel ledger, or a sibling accounting stack.
- Use the existing modular-monolith boundaries: Domain for deterministic rules, Application/Finance for contracts and policies, Persistence for EF configuration, Persistence.Migrations for SQL Server migrations, Infrastructure.Finance for implementations, API for transport, and Web for presentation.
- Preserve tenant isolation. Every company-owned read, write, background operation, cache entry, audit event, document link, approval, and external reference must be company-scoped and authorized server-side.
- Posted journal lines are the source of financial truth. Invoice, bill, payment, bank, provider, snapshot, and simulation records may supply workflow or projection data but must not become a competing accounting balance source.
- Deterministic backend policy owns posting eligibility, balance, period state, tax calculation, approval, authority, and success. AI may extract or recommend, but may not establish financial truth or bypass policy.
- Schema changes require an EF Core migration in `VirtualCompany.Persistence.Migrations`, an updated model snapshot, pending-model verification, and compatible local SQL Server and Docker SQL Server restore/run paths. Do not use startup DDL, `EnsureCreated`, destructive recreation, or SQLite-only verification.
- External side effects use approval where applicable, a durable outbox/background worker, stable business idempotency, bounded retry, safe failure classification, and reconciliation for ambiguous outcomes. Do not call external providers directly from request handlers.
- Reuse existing Finance contracts, pages, components, authorization patterns, audit infrastructure, document infrastructure, approval workflows, and background execution where they already fit. Preserve existing Fortnox behavior unless a prompt explicitly changes its boundary.
- Keep user-facing language plain English. Do not expose raw enums, policy object names, provider payloads, internal identifiers, tenant terminology, or workflow implementation names.
- Add tests to the narrowest appropriate project: `VirtualCompany.Finance.Tests` for pure Finance policies, `VirtualCompany.Api.Tests` for composed backend/API and persistence behavior, `VirtualCompany.Web.Tests` for components and clients, and SQL Server-backed tests for provider-specific database behavior.

---

## Prompt 1: Establish Country-Agnostic Accounting Configuration and Policy Packs

### 1. Title and outcome

Implement the native accounting configuration and versioned policy-pack foundation so a micro-company can use a country-neutral internal ledger without Fortnox and can optionally select locally validated accounting defaults later.

The delivered behavior is a persistent, tenant-owned accounting configuration with internal-ledger authority by default, a country-neutral setup mode, a versioned policy-pack contract and resolver, safe pack selection/upgrade rules, and API-visible setup status. This prompt must not implement journal posting or full setup UI.

### 2. Current context

- `FinanceAccount`, `FiscalPeriod`, `LedgerEntry`, `LedgerEntryLine`, and reporting entities already exist under `src/VirtualCompany.Domain/Entities/`.
- Finance contracts are split under `src/VirtualCompany.Application/Finance/Contracts` plus focused Finance contract files.
- `VirtualCompanyDbContext` and one-entity-oriented configurations live in `VirtualCompany.Persistence`.
- `FinanceModuleRegistration` owns Finance registrations.
- `IFinanceIntegrationProvider` in `FinanceIntegrationProviderContracts.cs` is explicitly shaped around OAuth, sync, mapping, and remote writes. It is not the accounting-policy abstraction.
- The repository has company settings, tenant authorization, audit, initialization/seeding, and migration infrastructure that should be reused.
- There is no native accounting configuration or versioned country-policy-pack model yet.

### 3. Dependencies

None. This is the first accounting implementation prompt.

Required project instructions: `AGENTS.md`, `production-implementation.md`, `accounting_app.md`, `docs/architecture-rules.MD`, and `architecture-inst.md` if it exists.

### 4. Implementation requirements

- Introduce a tenant-owned accounting configuration aggregate containing at minimum base currency, fiscal-year defaults, accounting authority, setup state, effective policy-pack key/version, rounding settings, and references to default/control-account roles without hard-coding account codes.
- Make internal-ledger authority the production default for a newly configured company. Do not require an integration connection.
- Define typed Application contracts for retrieving setup status, creating initial configuration, previewing a policy-pack selection, applying a pack/version, and validating configuration.
- Define a versioned accounting-policy-pack abstraction separate from `IFinanceIntegrationProvider`. Cover chart templates, account roles, tax rules, invoice rules, reporting mappings, terminology, retention/lock policy, and supported exports without forcing every pack to implement remote integration behavior.
- Prefer declarative, immutable pack definitions loaded through Finance-owned registration/resources. Add a resolver that rejects duplicate keys/versions and never silently substitutes another pack.
- Provide a country-neutral base configuration that supports bookkeeping but is explicitly marked as not supplying country-specific statutory compliance. Its account codes and labels belong to the pack/template, not the kernel.
- Persist the selected pack key/version and effective date. Pack upgrades must expose an impact preview and may affect only future activity; historical postings must retain the version/facts used when created.
- Model stable reason codes for incomplete configuration, unsupported pack/version, invalid upgrade, missing required account role, and country-specific capability unavailable.
- Add company-scoped API endpoints in the matching internal Finance controller partial and typed Web API-client partial. Keep controllers transport-only.
- Persist business audit evidence for initial configuration, pack selection, and pack upgrade.
- Register services once in `FinanceModuleRegistration` with lifetimes appropriate to EF-backed services and immutable pack definitions.
- Add EF configurations and a migration. Preserve existing company upgrade paths and Docker/local SQL Server compatibility.
- Update `accounting_app.md` only if implementation discoveries require the architecture description to change; do not weaken its country-agnostic boundary.

### 5. Constraints and preservation rules

- Do not register the internal accounting application or a policy pack as `IFinanceIntegrationProvider`.
- Do not hard-code Sweden, BAS, SEK, VAT, GST, sales tax, a fixed account code, or Fortnox into the kernel or default authority decision.
- Do not claim the country-neutral mode is tax/statutory compliant.
- Keep important queryable configuration fields relational. Flexible immutable pack metadata may use JSON only when it is not the sole store for authoritative queryable state.
- A company can have only one effective accounting configuration and one effective pack version at a time; enforce concurrency and uniqueness in SQL Server.
- Existing Finance simulation and Fortnox integration behavior must remain available and must not silently become the native accounting authority.
- Follow the mandatory production, architecture, multi-tenancy, database, audit, and testing instructions at the top of this file.

### 6. Acceptance criteria

- **Given** a company with no accounting provider connection, **when** authorized setup creates a country-neutral configuration, **then** the persisted authority is the internal ledger and setup status is returned without requiring OAuth or sync.
- **Given** two companies, **when** either reads or changes accounting configuration, **then** it cannot observe or mutate the other company's configuration or pack selection.
- **Given** an unknown pack or version, **when** selection is requested, **then** the backend rejects it with a stable safe reason and changes nothing.
- **Given** a pack upgrade with future effect, **when** it is applied, **then** the prior version remains identifiable for historical activity and the change is audited.
- **Given** country-neutral mode, **when** a country-specific report capability is requested, **then** the response says it is unavailable rather than guessing rules.
- **Given** duplicate pack registrations, **when** Finance composition starts, **then** startup fails clearly instead of choosing one nondeterministically.

### 7. Verification

- Run focused Domain/Application policy tests for configuration, normalization, version selection, upgrade rules, and duplicate resolver registration.
- Run persistence tests for company uniqueness, concurrency, effective-version storage, and cross-company foreign-key protection.
- Run API authorization and tenant-isolation tests for every new endpoint.
- Create and inspect the EF migration and model snapshot.
- Run `dotnet ef migrations has-pending-model-changes` with the repository's Persistence.Migrations/API project arrangement.
- Validate migration application against SQL Server and the repository's Docker restore/run path.
- Run focused API and Finance project builds and tests, followed by the broader build required by changed dependencies.

### 8. Definition of done

The prompt is complete when a new company can persist and retrieve internal-ledger accounting configuration without an external provider, policy packs are typed/versioned/resolved independently of integrations, country-neutral limitations are explicit, tenant and concurrency rules are enforced, the migration is valid for local and Docker SQL Server, audits exist, and all focused verification is green. No UI scaffolding, fake compliance, mock production pack, parallel accounting stack, or in-scope TODO remains.

---

## Prompt 2: Implement the Native Ledger Kernel and Governed Posting Boundary

### 1. Title and outcome

Harden the existing ledger into the authoritative native accounting kernel with concurrency-safe voucher sequencing, whole-entry validation, immutable posting, source-version idempotency, and reversal/correction support.

The delivered behavior is a central Finance posting service through which all future accounting sources can preview, post, retrieve, and reverse balanced journals atomically. This prompt establishes accounting truth but does not yet build manual-journal, invoice, bill, or banking UI.

### 2. Current context

- `FinanceAccount` currently stores code, name, free-form account type, currency, and opening balance.
- `LedgerReportingEntities.cs` contains `FiscalPeriod`, `LedgerEntry`, `LedgerEntrySourceMapping`, `LedgerEntryLine`, statement snapshots, and trial-balance snapshots.
- Ledger lines reject negative values, zero-only values, and simultaneous debit/credit values, but the aggregate does not provide one complete governed posting boundary for all sources.
- `LedgerEntryConfiguration` has tenant-scoped entry/source indexes, while current entry numbers are often source-derived identifiers.
- `CompanyBankTransactionService` and `CompanyCashSettlementPostingService` construct and persist posted entries independently.
- `CompanyReportingPeriodCloseService` detects unbalanced entries and other close blockers.
- Existing reports and snapshots already read ledger data and must keep working.

### 3. Dependencies

- Prompt 1 completed: accounting configuration, internal authority, policy-pack contracts, and migration are present.

Required project instructions: `AGENTS.md`, `production-implementation.md`, `accounting_app.md`, `docs/architecture-rules.MD`, and `architecture-inst.md` if present.

### 4. Implementation requirements

- Add explicit account class, normal balance, active/effective dates, posting-enabled state, control-account role, and optional manual-post restriction to `FinanceAccount` or focused related entities. Preserve existing records through a safe backfill.
- Introduce voucher series and a company/fiscal-year/series sequence with an atomic SQL Server allocation mechanism. Allocate a human voucher number only inside the posting transaction; never reuse a committed number.
- Extend ledger data with the minimum authoritative fields required by `accounting_app.md`: voucher series/sequence, document and posting dates, base currency, posting type, source type/id/version, stable idempotency key, policy-pack version/facts, actor, approval reference, reversal/original link, correction reason, and concurrency token.
- Keep new domain entities in focused files and EF configurations normally one per entity. Do not expand a catch-all file merely for convenience.
- Implement typed proposed-entry, preview, post, and reversal commands/results behind a central Application Finance boundary. The implementation belongs in Infrastructure.Finance.
- Validate the entire journal before persistence: same company, open period, valid posting date, active posting-enabled accounts, supported currency/precision, at least two nonzero lines, debit equals credit at the configured precision, valid tax/dimension facts, source version, approval, and authority.
- Commit voucher allocation, journal header, lines, source mapping, source-version/idempotency record, reversal links, and audit evidence atomically.
- Make posted entries immutable in domain behavior and persistence interception/policy. Drafts may change; posted entries may only be corrected through a linked reversing or adjusting entry in an open period.
- Replaying the same company/action/source/source-version command must return the existing posting. A changed payload with the same idempotency identity must fail as a conflict.
- Add read contracts for journal list/detail and source-to-journal lookup so later prompts do not query EF from controllers or UI.
- Add stable reason codes and safe plain-English explanations for every rejected invariant.
- Do not yet reroute bank/cash services; add characterization tests and a documented internal migration seam for Prompt 7.
- Add migration/backfill behavior that identifies ambiguous historical records rather than inventing account classes, source versions, or authority.

### 5. Constraints and preservation rules

- Posted journal lines become the sole source of accounting balances. Do not create a second ledger or use provider transactions as the native book.
- No controller, Razor component, AI prompt, or provider adapter may implement balance or posting rules.
- Voucher allocation must be correct under concurrent SQL Server transactions and safe on retry.
- Existing reporting, period-close, source-mapping, Fortnox sync, simulation, and cash-posting records must remain readable during rollout.
- Do not modify or delete existing migration history. Use additive/expand-backfill-contract migration techniques.
- A closed or reporting-locked period rejects posting and correction. Reopening remains a separate authorized operation.
- Follow all shared production, architecture, tenant, database, audit, and test constraints.

### 6. Acceptance criteria

- **Given** an unbalanced proposed entry, **when** preview or posting runs, **then** it is rejected and no voucher number or journal state is persisted.
- **Given** two concurrent postings in one company/year/series, **when** they commit, **then** each receives a unique monotonic voucher number.
- **Given** a failure before commit, **when** the transaction rolls back, **then** no partial journal, source link, audit, or consumed committed voucher exists.
- **Given** the same source version and payload twice, **when** both requests execute concurrently or sequentially, **then** one journal exists and both resolve to it.
- **Given** the same idempotency identity with a changed payload, **when** posting is retried, **then** it fails with a deterministic conflict.
- **Given** a posted journal, **when** update or deletion is attempted through any supported path, **then** it is rejected.
- **Given** a posted journal in a locked period, **when** correction is requested, **then** the original remains unchanged and any allowed correction is posted only to a valid open period.
- **Given** a cross-company account, period, approval, or source identifier, **when** posting is attempted, **then** no data is disclosed or changed.

### 7. Verification

- Add Domain/Finance tests for all ledger and account invariants, rounding, immutable transitions, reversal construction, and stable errors.
- Add SQL Server integration tests for voucher concurrency, idempotency uniqueness, rollback, stale row versions, composite company foreign keys, and immutable-posting enforcement.
- Add cross-tenant and authorization tests for posting/read contracts and API endpoints.
- Test migrations from a representative existing Finance database, including ambiguous-backfill reporting.
- Verify the model snapshot and pending-model state, then validate local and Docker SQL Server upgrade/restore compatibility.
- Run current reporting-period, financial-statement, bank-transaction, and cash-settlement regression suites to prove preservation.
- Run focused builds, affected test projects, and the broader backend build.

### 8. Definition of done

The prompt is complete when the repository has one production-grade native posting boundary, voucher allocation is concurrency-safe, every committed entry is balanced and tenant-correct, posted history is immutable and correctable, source retries are idempotent, historical data has a safe migration path, existing reporting/cash behavior remains green, and no posting invariant is duplicated in transport or presentation code.

---

## Prompt 3: Deliver Micro-Company Accounting Setup, Accounts, and Period Administration

### 1. Title and outcome

Build the complete default-first setup experience that lets a micro-company with no external accounting software initialize the native ledger, choose country-neutral or validated pack defaults, review its chart of accounts, and administer fiscal periods safely.

The delivered behavior includes real backend commands/queries and production Blazor pages for accounting setup, chart-of-accounts administration, fiscal periods, and setup validation. It does not yet include the manual-journal editor.

### 2. Current context

- Prompts 1 and 2 provide accounting configuration, policy packs, account semantics, voucher series, and the native ledger kernel.
- Existing Finance pages live under `src/VirtualCompany.Web/Pages/Finance` and use typed Finance API clients.
- Existing Finance navigation, settings, balances, statements, and provider-management surfaces must be reused rather than replaced.
- `ui-instructions.md` requires a screenshot-first workflow and a calm, action-oriented Finance experience with Laura visible as the Finance Manager.
- The established design uses the current app shell, Finance secondary navigation, cards, list/detail patterns, grouped setup cards, and plain-English statuses.

### 3. Dependencies

- Prompt 1 completed.
- Prompt 2 completed.

Required instructions: read and follow `AGENTS.md`, `production-implementation.md`, `accounting_app.md`, `docs/architecture-rules.MD`, `ui-instructions.md`, `docs/design.md`, and `architecture-inst.md` if present, in addition to the shared instructions in this pack.

### 4. Implementation requirements

- Before UI implementation, explicitly write image prompts and generate reference screenshots for the accounting setup and chart/period administration surfaces. Save descriptive PNG references under `docs/design/references/`.
- Implement a short setup flow with progressive defaults: internal ledger, base currency, fiscal year, country-neutral or available validated policy pack, chart template, control-account roles, voucher series, tax behavior, and review/confirm.
- Do not silently choose legally important values. Show why a required decision matters and clearly label country-neutral mode as basic bookkeeping without country-specific compliance.
- Add setup preview and validation so users can inspect accounts, roles, taxes, periods, and warnings before applying changes.
- Apply initial setup atomically and idempotently. Retrying after a lost response must not duplicate periods, accounts, series, tax codes, or configuration.
- Implement chart-of-accounts list/search/detail and permitted create/rename/deactivate actions. Prevent deletion/deactivation or incompatible role changes when posted history or control-account policy forbids them.
- Implement fiscal-year/period list and detail, plus permitted creation and validation. Closing, locking, and reopening remain in Prompt 8; this prompt must still show current closed/locked state accurately.
- Add company-scoped API endpoints in focused controller partials, typed Web-client methods, and presenter/view-model logic. Keep business decisions in backend services.
- Add accounting-specific permissions for view and setup administration using existing authorization conventions. UI visibility must not replace server authorization.
- Add navigation from Finance to Setup, Accounts, Periods, and later Journal surfaces without disrupting existing Finance routes.
- Include loading, empty, validation, conflict, unauthorized, retryable failure, and completed states; provide direct next actions.
- Persist audit events for configuration completion, account administration, period creation, and policy-pack changes.
- Localize all new user-facing strings consistently with existing resource patterns.

### 5. Constraints and preservation rules

- Follow the screenshot-first and design-token rules exactly; do not introduce a new visual language or UI framework.
- Do not expose pack storage keys, enum values, account-role internals, tenant identifiers, or provider concepts in the setup UI.
- Do not add mock accounts or sample transactions as production behavior. Versioned template data applied by explicit setup is production configuration, not simulation data.
- Setup must work with no Fortnox connection and must not redirect users to provider management.
- Existing Finance pages, simulation tools, localization, mobile behavior, and provider configuration must remain intact.
- Every mutation is company-scoped, authorized, concurrency-aware, audited, and safe on retry.

### 6. Acceptance criteria

- **Given** a new company without an accounting provider, **when** setup is completed, **then** it has one valid internal-ledger configuration, chart, voucher series, and fiscal periods without duplicate records.
- **Given** a retry of the same completed setup request, **when** it runs, **then** the original result is returned and no setup records are duplicated.
- **Given** country-neutral mode, **when** setup completes, **then** the UI clearly states which country-specific capabilities are unavailable.
- **Given** an account referenced by a posted journal, **when** deletion or prohibited deactivation is attempted, **then** the backend rejects it with an actionable explanation.
- **Given** a user without accounting-administration permission, **when** a setup/account/period mutation is attempted directly, **then** it is denied server-side.
- **Given** a small viewport, **when** the pages render, **then** navigation, forms, tables/list-detail content, and primary actions remain usable and match the approved reference hierarchy.

### 7. Verification

- Add service and API tests for setup idempotency, template application, invalid configurations, concurrency, authorization, and tenant isolation.
- Add component tests for setup steps, previews, validation, account actions, period states, empty/loading/error states, navigation, and localization.
- Compare implemented pages with the saved reference screenshots on desktop and responsive widths.
- Run accessibility checks for form labels, keyboard navigation, focus, validation summaries, dialogs, and contrast.
- Run focused Web/API tests and builds, then the broader relevant regression suite.
- If schema changes are introduced beyond Prompts 1-2, add and verify the required EF migration and Docker/local SQL Server paths.

### 8. Definition of done

The prompt is complete when a real micro-company can initialize and administer native accounting through polished production UI without an external provider, backend policies protect accounts and periods, setup is idempotent and audited, references and responsive states are verified, tests/builds are green, and no placeholder setup behavior or mock production data remains.

---

## Prompt 4: Implement Manual Journals, Approval, Posting, and Correction

### 1. Title and outcome

Deliver a safe manual-journal workflow that lets authorized users prepare, preview, approve, post, inspect, reverse, and adjust native journals without ever rewriting posted history.

This produces the first complete end-user accounting workflow over the governed posting boundary and proves that internal accounting operates independently of Fortnox.

### 2. Current context

- Prompt 2 provides proposed-entry preview/post/reversal services, voucher sequencing, and immutable journals.
- Prompt 3 provides setup, accounts, periods, permissions, navigation, and design references/patterns.
- Existing approval and audit infrastructure already supports policy-controlled Finance actions.
- Existing statement drill-down exposes journal lines, but there is no complete manual-journal preparation workbench.
- Document/evidence infrastructure exists and should be linked rather than recreated.

### 3. Dependencies

- Prompts 1-3 completed.

Required instructions: read and follow `AGENTS.md`, `production-implementation.md`, `accounting_app.md`, `docs/architecture-rules.MD`, `ui-instructions.md`, `docs/design.md`, and `architecture-inst.md` if present, in addition to the shared instructions and screenshot-first workflow in this pack.

### 4. Implementation requirements

- Generate and save reference screenshots for the journal list/detail and manual-journal workbench before UI implementation.
- Add persistent draft commands for create, update, discard, preview, submit for approval, post approved, reverse, and create adjusting entry.
- A draft includes posting/document dates, period, voucher series, explanation, source/evidence links, currency, two or more lines, accounts, debit/credit amounts, tax code/facts where supported, and allowed dimensions.
- Use optimistic concurrency and stable request idempotency. Stale edits must return a conflict with the current version; duplicate submit/post requests must not duplicate approval or journal state.
- Implement a deterministic manual-journal policy for required permissions, evidence, restricted/control accounts, amount thresholds, tax behavior, and approval. Client flags cannot assert approval or autonomous authority.
- Bind approval to the exact draft/source version and payload hash. Editing an approval-bound draft invalidates or supersedes the approval.
- Recheck configuration, authority, permission, approval, accounts, pack version, and open period immediately before posting.
- Persist draft-to-posted linkage, actor, approval, evidence, voucher number, and audit atomically through the central posting boundary.
- Implement journal list/detail read models with filters, totals, source, status, correction chain, evidence, approval, actor, and audit timeline.
- Implement `Create correction` rather than edit/delete for posted entries. Support full reversal and a separately balanced adjusting journal with explicit reason and original link.
- Add focused API endpoints/client methods and polished Web pages matching the reference and existing Finance design.
- Show live debit, credit, and difference calculations for usability, while repeating all validation on the server.
- Add loading, empty, invalid, stale, awaiting approval, rejected, posted, reversed, blocked-period, unauthorized, and recoverable failure states.

### 5. Constraints and preservation rules

- Posted entries are immutable. Never implement update/delete endpoints for posted accounting history.
- UI totals are advisory; only the backend posting service establishes validity.
- Do not bypass approval for Laura or another AI agent. Agent tools must use the same permission, policy, approval, idempotency, and audit boundary.
- Do not perform evidence storage or audit as a best-effort afterthought when it belongs to the local accounting transaction.
- A correction may not mutate a locked period. It must post to a permitted open period and preserve the original.
- Reuse existing document, approval, audit, localization, design, and authorization infrastructure.

### 6. Acceptance criteria

- **Given** a balanced authorized draft in an open period, **when** posting completes, **then** exactly one voucher and its evidence/audit links are committed.
- **Given** an unbalanced or zero-only draft, **when** preview or post is requested, **then** it is rejected without consuming a committed voucher number.
- **Given** an approval for draft version N, **when** the draft changes to N+1, **then** posting is blocked until the current version is approved.
- **Given** duplicate or concurrent post requests, **when** they execute, **then** one journal exists and callers resolve to the same result.
- **Given** a posted journal, **when** correction is requested, **then** the original remains unchanged and linked reversal/adjustment entries explain the correction.
- **Given** a closed/locked period or restricted account, **when** a manual posting is attempted, **then** it is denied with a stable actionable reason.
- **Given** a cross-company draft, account, evidence, approval, or journal identifier, **when** used by another tenant, **then** no data is disclosed or changed.

### 7. Verification

- Add Finance policy tests for balance, restricted accounts, thresholds, evidence, approval versioning, reversal, and adjustment behavior.
- Add persistence/API integration tests for draft concurrency, idempotency, atomic posting, stale approval, rollback, tenant isolation, and authorization.
- Add Web component tests for entry editing, totals, previews, approval states, conflicts, correction chains, filters, and error/empty/loading states.
- Perform screenshot comparison and responsive browser verification against saved references.
- Run accessibility checks for the editable line grid, errors, keyboard navigation, dialogs, and focus restoration.
- Run focused Finance/API/Web tests and builds plus current ledger/reporting regression tests.

### 8. Definition of done

The prompt is complete when authorized users and governed agents can perform the entire manual-journal lifecycle through production UI/API, approval is version-bound and rechecked, posted history cannot be altered, corrections are traceable, tenant/idempotency/concurrency failures are safe, evidence and audit are durable, and all verification is green with no mock or placeholder behavior.

---

## Prompt 5: Make Customer Invoices and Credit Notes Native-Ledger Accounting Sources

### 1. Title and outcome

Connect customer invoices and credit notes to the native accounting kernel so approved sales documents create exactly one authoritative receivable/revenue/tax posting and can be corrected without depending on Fortnox.

This prompt delivers native accounts-receivable accounting and visible document-to-journal traceability. Invoice delivery remains a separate governed external side effect.

### 2. Current context

- Finance already has invoice entities, invoice queries/pages, payments/allocations, review workflows, and Fortnox-related actions.
- Prompt 2 provides central posting and source-version idempotency.
- Prompt 4 proves approval-bound posting and correction UI patterns.
- Country-policy packs provide account roles, tax rules, invoice requirements, and terminology.
- Existing operational invoice status and accounting status must remain distinguishable.

### 3. Dependencies

- Prompts 1-4 completed.

Required instructions: read and follow `AGENTS.md`, `production-implementation.md`, `accounting_app.md`, `docs/architecture-rules.MD`, `ui-instructions.md`, `docs/design.md`, and `architecture-inst.md` if present, in addition to the shared instructions in this pack. UI rules apply to changed invoice/list/detail surfaces.

### 4. Implementation requirements

- Define versioned native posting templates for customer invoice and customer credit note using policy-pack roles and tax rules rather than fixed account codes.
- Persist invoice accounting facts required for historical reproducibility: net/gross/tax amounts, inclusive/exclusive method, currency/base-currency conversion facts where supported, policy-pack version, source version, and posting status/link.
- Add deterministic readiness policy covering company, counterparty, required invoice fields, totals, tax treatment, accounts, period, authority, approval, and duplicate document number.
- Add preview, submit/approve where required, post, retrieve posting, and create credit-note/correction commands. Bind approval to the exact invoice/source version.
- Commit the invoice accounting transition and journal/source link atomically. Do not mark a document posted if the journal transaction fails.
- Customer invoice posting must use receivable, revenue, and configured tax lines appropriate to the policy pack. Non-taxable, inclusive-tax, rounding, and multi-line cases must be explicit and tested.
- Credit notes link to the original document and post the appropriate inverse/delta through the central boundary. They never delete or rewrite the original invoice/journal.
- Keep sending/delivery separate from accounting posting. If invoice sending exists, retain its own approval/outbox/idempotency/reconciliation behavior.
- Update invoice list/detail UI to show plain-English accounting readiness, approval, posted voucher, blocked reason, source evidence, and correction/credit status with direct journal drill-down.
- Remove any requirement that a native invoice needs a Fortnox connection. Preserve optional Fortnox export as downstream integration state, not native posting state.
- Add receivables/control-account reconciliation queries used by later reporting and close prompts.

### 5. Constraints and preservation rules

- Operational invoice state, delivery state, native accounting state, and provider export state are separate dimensions; do not collapse them into one status.
- Posted journal lines, not invoice totals alone, drive financial statements.
- Do not duplicate tax logic in invoice services or UI; use the selected policy pack and central posting validation.
- No external API call may occur inside the local invoice-posting transaction.
- Preserve existing invoice routes/contracts where possible; add versioned/focused contracts rather than breaking unrelated Finance clients.
- Existing Fortnox-only companies must not be silently switched to internal authority.

### 6. Acceptance criteria

- **Given** an approved taxable invoice under an exclusive-tax policy, **when** it posts, **then** receivable equals gross and revenue plus configured payable tax equals gross.
- **Given** a policy with inclusive or exempt tax treatment, **when** the same business document posts, **then** the policy-specific journal is balanced and historical tax facts are retained.
- **Given** a posting failure, **when** the transaction rolls back, **then** neither invoice accounting state nor partial journal/source state reports success.
- **Given** duplicate/concurrent post requests for one invoice version, **when** they run, **then** exactly one journal is linked.
- **Given** an invoice changed after approval, **when** posting is attempted, **then** stale approval is rejected.
- **Given** a posted invoice, **when** a credit note is completed, **then** it creates a linked correcting journal while preserving the original.
- **Given** no Fortnox connection, **when** a valid internal-authority invoice is posted, **then** native accounting succeeds.

### 7. Verification

- Add policy/golden tests for taxable, inclusive, exempt, rounding, credit, multi-line, and unsupported-tax scenarios using at least two contrasting synthetic policy packs.
- Add atomicity, concurrency, idempotency, stale-version, tenant, and authorization integration tests.
- Add control-account reconciliation tests proving posted invoice balances agree with receivable journal lines and allocations.
- Add component/API-client tests for invoice accounting states, direct journal navigation, approval, correction, and provider-export separation.
- Run screenshot-first workflow if the invoice surface changes materially; otherwise visually verify consistency with existing Finance references.
- Run focused Invoice/Finance/API/Web tests and broader accounting/report regressions.

### 8. Definition of done

The prompt is complete when customer invoices and credit notes can be accounted for entirely inside Virtual Company, postings are pack-driven, balanced, atomic, idempotent, approval/version-safe, and traceable, receivables reconcile to journals, Fortnox is optional downstream state, UI/API are production-ready, and all affected tests/builds are green.

---

## Prompt 6: Make Supplier Bills and Credit Notes Native-Ledger Accounting Sources

### 1. Title and outcome

Connect supplier bills and supplier credit notes to the native accounting kernel so reviewed and approved purchases create exactly one authoritative expense/asset, recoverable/non-recoverable tax, and payable posting without requiring Fortnox.

This prompt converts the existing strong supplier-bill intake/review experience into native accounts-payable accounting while preserving optional Fortnox export.

### 2. Current context

- The repository already has bill entities/pages, bill inbox/detail, document extraction, duplicate detection, source-document attachment, enrichment, correction, approval automation, payment proposals, and Fortnox registration/expense-posting paths.
- Some supplier services default to `FinanceIntegrationProviderKeys.Fortnox` and must not define native accounting authority.
- Prompt 2 provides the central native posting boundary.
- Prompt 4 provides version-bound approval/correction patterns.
- Prompt 5 establishes native document accounting-state separation and journal drill-down.

### 3. Dependencies

- Prompts 1-5 completed.

Required instructions: read and follow `AGENTS.md`, `production-implementation.md`, `accounting_app.md`, `docs/architecture-rules.MD`, `ui-instructions.md`, `docs/design.md`, and `architecture-inst.md` if present, in addition to the shared instructions in this pack. UI rules apply to materially changed bill-inbox/detail surfaces.

### 4. Implementation requirements

- Define versioned native supplier-bill and supplier-credit posting templates driven by account roles, selected expense/asset account, tax rule, and policy-pack version.
- Preserve extracted and reviewed facts separately. AI/document extraction may propose supplier, dates, totals, account, and tax; deterministic review/posting policy validates the accepted version.
- Support recoverable tax, non-recoverable tax included in cost, exempt/no-tax, rounding, asset classification, and explicit unsupported scenarios without assuming VAT or a fixed account code.
- Add native accounting readiness, preview, approval, post, correction/credit, journal lookup, and reconciliation contracts.
- Bind approval to exact bill facts/source document hash/version. Any relevant edit invalidates stale approval.
- Atomically commit bill accounting transition, journal lines, source/document links, approval evidence, and audit.
- Retain duplicate supplier/document-number/amount/currency/date checks and surface their evidence before posting.
- Refactor current native-eligible paths that assume Fortnox so `ProviderKey` or connection state is required only for optional provider actions, never for internal posting.
- Preserve existing Fortnox registration, enrichment, correction, and payment-export behavior as explicit downstream integration actions with separate statuses.
- Update bill inbox/detail/list UI with native readiness, blocked reasons, selected account/tax evidence, approval, posted voucher, correction chain, and journal drill-down. Clearly separate `Posted in Virtual Company` from `Exported to Fortnox`.
- Add payables/control-account reconciliation queries used by close/reporting.

### 5. Constraints and preservation rules

- Do not replace deterministic duplicate, eligibility, account-resolution, or approval policies with AI output.
- Do not silently fall back to a generic expense account in production. Missing/ambiguous account selection creates review work.
- Native accounting may not require an OAuth token, Fortnox scope, connection, or external write.
- External provider success/failure cannot alter whether the native journal transaction committed; provider export is separately durable and reconcilable.
- Preserve existing bill documents, extraction provenance, supplier mappings, approvals, and optional provider workflows.
- Do not modify paid-supplier-bill eligibility by duplicating its rules in UI or projections; converge authority deliberately where native posting supersedes an old path.

### 6. Acceptance criteria

- **Given** an approved bill with recoverable tax, **when** it posts, **then** expense/asset plus recoverable tax equals payable and the source document is linked.
- **Given** a non-recoverable-tax policy, **when** the bill posts, **then** tax is included in the configured cost treatment and no false recoverable-tax balance is created.
- **Given** an ambiguous or missing expense account, **when** posting is requested, **then** it is blocked for review rather than using a silent fallback.
- **Given** changed extracted/reviewed facts after approval, **when** posting is attempted, **then** stale approval is rejected.
- **Given** duplicate or concurrent post requests, **when** they execute, **then** one native journal is committed and linked.
- **Given** no Fortnox connection, **when** a valid internal-authority bill posts, **then** native accounting succeeds and provider export remains separately unavailable/pending.
- **Given** a supplier credit note, **when** it posts, **then** the original remains intact and the linked correcting journal is balanced.

### 7. Verification

- Add golden policy tests for recoverable, non-recoverable, exempt, rounding, asset, credit, and multi-line bills across contrasting synthetic packs.
- Add extraction/review-version, document-hash, approval, duplicate, atomicity, idempotency, concurrency, authorization, and tenant tests.
- Add payables reconciliation tests proving bill/accounting/payment totals agree with control-account journal lines.
- Run existing supplier invoice enrichment, draft action, correction, document attachment, payment proposal, approval automation, and Fortnox regression suites.
- Add Web tests for native/provider state separation, review blockers, voucher links, correction, loading/empty/error states, and localization.
- Complete screenshot/browser verification when the bill surfaces change materially.

### 8. Definition of done

The prompt is complete when supplier bills and credits post natively without Fortnox, accepted evidence and policy drive deterministic balanced journals, ambiguity creates visible review rather than fallback, approvals are version-bound, payables reconcile, provider actions remain optional and separate, existing supplier workflows are preserved, and all focused/broader verification is green.

---

## Prompt 7: Converge Banking, Payments, Reconciliation, and Suspense on the Native Ledger

### 1. Title and outcome

Make bank imports, payment allocation, reconciliation, and cash settlement use the central native accounting boundary with safe duplicate handling, explicit suspense accounting, and complete receivable/payable traceability.

The delivered behavior replaces independent ledger construction and generic offset fallbacks with governed, pack-configured cash postings while preserving existing bank and payment workflows.

### 2. Current context

- `CompanyBankTransactionService` imports/reconciles transactions and currently creates ledger entries directly.
- `CompanyCashSettlementPostingService` creates cash settlement journals directly and has source-based duplicate handling.
- `BankTransactionPaymentLink`, `BankTransactionCashLedgerLink`, `BankTransactionPostingStateRecord`, and payment cash-ledger links already provide traceability.
- Current reconciliation can select generic offset accounts such as `1100`/`2000` or another fallback; `accounting_app.md` explicitly rejects that as production accounting behavior.
- Prompts 5 and 6 provide native receivable/payable accounting and control-account reconciliation.
- Prompt 2 provides the required central posting boundary and migration seam.

### 3. Dependencies

- Prompts 1-6 completed.

Required instructions: read and follow `AGENTS.md`, `production-implementation.md`, `accounting_app.md`, `docs/architecture-rules.MD`, `ui-instructions.md`, `docs/design.md`, and `architecture-inst.md` if present, in addition to the shared instructions in this pack. UI rules apply to reconciliation surfaces.

### 4. Implementation requirements

- Route bank reconciliation and cash settlement journal creation through the central posting service. Remove independent balance, period, voucher, account, idempotency, and audit rules after behavior is safely migrated.
- Preserve existing public/application contracts where practical, but ensure all new posting results link to the governed journal and voucher identity.
- Use company-configured bank, receivable, payable, fee, rounding, exchange, and suspense account roles from the policy/configuration boundary. Never select an arbitrary first account.
- Match incoming/outgoing transactions to completed payments and allocations with direction, currency, amount, company, and source-version validation.
- Support partial allocations without exceeding either bank transaction or target payment/document amounts. Represent unmatched remainder explicitly.
- For an unmatched but valid bank transaction, post only through an explicit user-reviewed categorization or configured suspense workflow. Create visible follow-up work with reason/evidence; do not silently treat suspense as resolved.
- Handle fees, discounts, rounding, and exchange differences with explicit additional lines and supported policy-pack rules.
- Make statement/import identity stable across repeated files, overlapping imports, polling, and retries. Persist content/row identities and reject conflicting duplicates.
- Commit reconciliation links, allocations, posting state, journal/source links, and local audit atomically where they share the database transaction.
- Preserve idempotent recovery under concurrent requests and duplicate worker delivery.
- Add reconciliation read models and UI states for unmatched, partial, matched, posted, suspense, conflict, and correction with direct source/payment/journal drill-down.
- Add correction/reclassification workflow that reverses or adjusts suspense/accounting in an open period rather than editing posted entries.
- Backfill or map existing cash ledger links to the central posting identity in a resumable, conflict-reporting process.

### 5. Constraints and preservation rules

- Do not break existing bank transaction, payment allocation, cash analytics, or reporting contracts without an explicit compatible migration path.
- Never exceed source/payment/document amounts or mix currencies without an explicit supported conversion fact.
- Duplicate imports or retries must not duplicate transactions, links, allocations, journals, or audits.
- Ambiguous historical links become operator-visible conflicts; do not fabricate matches.
- Bank/provider ingestion and local accounting commits have separate failure boundaries. External ambiguity must not cause duplicate local posting.
- Preserve cross-company composite relationships and authorization on all read/write paths.

### 6. Acceptance criteria

- **Given** an incoming payment matched to a receivable, **when** reconciliation posts, **then** bank is debited, receivable is credited, and allocation/source/journal links commit exactly once.
- **Given** an outgoing payment matched to a payable, **when** reconciliation posts, **then** payable is debited, bank is credited, and links commit exactly once.
- **Given** repeated or overlapping import input, **when** it is processed, **then** no bank row or posting is duplicated and conflicting content is reported.
- **Given** a partial match, **when** it is saved, **then** allocations do not exceed either side and the remaining amount stays visible.
- **Given** an unmatched transaction, **when** no reviewed categorization exists, **then** production code does not select a generic offset account; it uses an explicit suspense workflow or remains unposted.
- **Given** a suspense item later reclassified, **when** correction posts, **then** the original journal remains immutable and the adjustment is linked and balanced.
- **Given** two concurrent reconciliation requests, **when** both run, **then** one set of local links and one governed journal exist.

### 7. Verification

- Extend existing bank transaction, cash settlement, payment allocation, traceability backfill, and analytics tests.
- Add SQL Server tests for concurrent import/reconciliation, duplicate rows, unique identities, transaction rollback, partial allocations, and backfill conflicts.
- Add tenant/authorization tests and wrong-direction/currency/amount tests.
- Add suspense, fee, rounding, exchange, reclassification, and correction policy tests using pack-configured roles.
- Add Web component/client tests for every reconciliation state and direct drill-down.
- Perform screenshot-first/browser verification for material reconciliation UI work.
- Run Finance/API/Web focused suites plus cash metrics, statements, and period-close regressions.

### 8. Definition of done

The prompt is complete when every new bank/cash accounting effect uses the native posting boundary, arbitrary offset fallback is gone from production behavior, imports and reconciliation are idempotent/concurrency-safe, suspense is explicit and correctable, AR/AP/bank balances reconcile, historical links have a safe backfill path, UI exposes actionable states, and all affected regression suites are green.

---

## Prompt 8: Deliver Authoritative Reports, Tax Summaries, Period Close, and Accountant Export

### 1. Title and outcome

Complete the accountant-facing output of the native ledger: general ledger, trial balance, profit and loss, balance sheet, configurable tax summary, control-account reconciliation, period close/lock/reopen, and a documented country-neutral export.

The delivered reports must derive from posted journal lines, drill down to immutable evidence, remain reproducible for locked periods, and clearly distinguish country-neutral output from policy-pack-validated local output.

### 2. Current context

- The repository already has `TrialBalanceSnapshot`, financial-statement snapshots/lines, statement mappings, reporting contracts, `CompanyFinanceReadService.Reporting`, statement drill-down, and `CompanyReportingPeriodCloseService`.
- Existing close checks include unposted source documents, unbalanced entries, and missing statement mappings.
- Prompts 5-7 establish native AR, AP, bank, payment, and reconciliation sources.
- Policy packs provide tax rules/report mappings and optional export formats.
- Existing Finance monthly summary, balances, statements, and close-related routes/pages should be extended, not replaced by a parallel reporting stack.

### 3. Dependencies

- Prompts 1-7 completed.

Required instructions: read and follow `AGENTS.md`, `production-implementation.md`, `accounting_app.md`, `docs/architecture-rules.MD`, `ui-instructions.md`, `docs/design.md`, and `architecture-inst.md` if present, in addition to the shared instructions and screenshot-first workflow in this pack.

### 4. Implementation requirements

- Make posted native journal lines the common source for general ledger, account ledger, trial balance, P&L, balance sheet, configured tax summary, and control-account reconciliation.
- Preserve or evolve existing statement mappings and snapshot behavior. Closed/locked period reports must use reproducible versioned snapshots/checksums with drill-down to source journal lines and documents.
- Add general-ledger and trial-balance queries with opening, debit, credit, closing, account class, currency, period, and drill-down data.
- Implement configurable tax-summary calculation from persisted historical tax facts and selected pack reporting mappings. Do not recalculate historical tax using the currently installed pack.
- Clearly label country-neutral tax summaries as bookkeeping information, not a statutory return. Only packs with explicit validated capability may expose country-specific report/export labels.
- Extend close validation to include unresolved suspense/reconciliation conflicts, AR/AP/bank control-account differences, incomplete configured tax review, unposted in-period sources, invalid mappings, and stale/regeneration failures.
- Provide actionable close issues with stable codes, record links, amounts, evidence, and remediation. Do not return opaque failure strings.
- Implement authorized close, reporting lock, and exceptional reopen workflows with current-policy rechecks and durable audit. Reopen must not rewrite snapshots/history silently.
- Implement a documented country-neutral machine-readable export containing companies, configuration/pack versions, accounts, periods, vouchers, lines, parties, tax facts, currencies, source references, corrections, and an attachment manifest with hashes.
- Generate exports as durable background work when size/retry/recovery matters. Persist status, checksum, safe failure, retention, and authorized download metadata.
- Before UI implementation, create reference screenshots for the reports/close workspace. Build responsive Blazor surfaces using existing Finance design and typed clients.
- Include journal/evidence drill-down, close checklist, lock history, export status, loading/empty/error/conflict states, and plain-English explanations.

### 5. Constraints and preservation rules

- Do not calculate authoritative statements from invoice, bill, payment, provider, simulation, or cached dashboard totals independently of posted journals.
- Snapshot generation must be deterministic for a fixed ledger/configuration version and safe on retry.
- Locked-period reports and evidence must remain reproducible after a policy-pack upgrade.
- Reopening is privileged, explicit, audited, and cannot mutate existing vouchers. Later corrections post in an allowed period.
- Exports must not leak another tenant's data or expose secrets/provider tokens.
- Preserve existing reporting routes/contracts where compatible and maintain current drill-down behavior during migration.

### 6. Acceptance criteria

- **Given** a set of posted journals, **when** trial balance runs, **then** total debits equal total credits and every balance drills down to its journal lines.
- **Given** P&L and balance-sheet reports for the same period, **when** totals are compared with the trial balance and mappings, **then** they reconcile exactly within configured precision.
- **Given** a historical posting under policy-pack version N, **when** version N+1 is installed, **then** the historical tax summary retains N's persisted facts/mapping version.
- **Given** unresolved suspense or a control-account difference, **when** close validation runs, **then** close is blocked with linked actionable evidence.
- **Given** all configured blockers resolved, **when** an authorized close/lock completes, **then** snapshots/checksums are reproducible and later normal posting is rejected.
- **Given** an authorized export request, **when** generation completes or retries, **then** one checksum-identified export exists with complete manifest and tenant-correct content.
- **Given** country-neutral mode, **when** reports render, **then** no statutory compliance claim or guessed authority format appears.

### 7. Verification

- Add golden accounting datasets spanning manual journals, invoices, bills, credits, payments, fees, suspense, corrections, tax variants, and period boundaries.
- Verify journal, trial balance, P&L, balance sheet, tax, AR/AP, and bank totals reconcile for each dataset.
- Add snapshot determinism, checksum, regeneration, pack-upgrade history, close blocker, close concurrency, lock, reopen, and authorization tests.
- Add export schema/manifest, tenant isolation, checksum, retry, failure, and attachment-reference tests.
- Run existing period reporting, close, statement mapping, snapshot drill-down, variance, and analytics suites.
- Add Web component tests and screenshot/responsive/accessibility verification for reports, drill-down, close, and export states.
- Run focused and broad API/Finance/Web builds/tests and SQL Server verification.

### 8. Definition of done

The prompt is complete when all accountant-facing reports share posted journals as truth, totals reconcile and drill down, tax history is pack-version safe, period close blocks real inconsistencies, locked output is reproducible, exports are durable and tenant-safe, the UI is production-quality, and all golden/regression/build verification is green without compliance overclaiming.

---

## Prompt 9: Preserve Optional External Providers Through Explicit Authority and Durable Adapters

### 1. Title and outcome

Integrate the completed native accounting capability with Fortnox and future external accounting systems without treating the internal engine as a provider or allowing dual accounting authority.

The delivered behavior explicitly records accounting authority per company/period, preserves internal-ledger default operation, supports provider export/sync through durable adapters, and provides a safe migration/cutover workflow for companies that adopt or already use an external system.

### 2. Current context

- `IFinanceIntegrationProvider` currently exposes OAuth, sync, writes, and mapping, with Fortnox registered through `FinanceIntegrationProviderRegistry`.
- Fortnox connections, sync states, external references, approval-backed write commands, outbound execution, error translation, and provider-management UI already exist.
- Several supplier and sales workflows explicitly use `FinanceIntegrationProviderKeys.Fortnox`.
- Prompts 1-8 make the internal ledger authoritative and fully usable without a provider.
- `accounting_app.md` requires one authority per company/accounting period and forbids independent dual posting.

### 3. Dependencies

- Prompts 1-8 completed.

Required instructions: read and follow `AGENTS.md`, `production-implementation.md`, `accounting_app.md`, `docs/architecture-rules.MD`, `ui-instructions.md`, `docs/design.md`, and `architecture-inst.md` if present, in addition to the shared external-side-effect and integration-boundary rules in this pack. UI rules apply to authority/provider management changes.

### 4. Implementation requirements

- Add a tenant-owned, effective-period accounting-authority model or equivalent policy that distinguishes internal ledger, external provider, and bounded migration/cutover state. Use typed storage values and plain-English presentation.
- Keep internal ledger as the default for new micro-companies. Authority changes require explicit permission, impact preview, effective period boundary, validation, audit, and concurrency control.
- Reject native authoritative posting for periods owned by an external provider, except explicitly permitted migration/reconciliation operations. Reject provider-authoritative writes for internally owned periods where they would duplicate native accounting.
- Do not implement the internal engine or policy packs as `IFinanceIntegrationProvider`. Preserve the provider registry for real external systems only.
- Define Finance-owned export/synchronization events/contracts that map committed native vouchers/documents to provider commands without leaking provider schemas into Domain or core entities.
- Use the existing approval-backed durable outbound infrastructure for Fortnox writes. Derive stable idempotency from company, authority period, business source/version, action, and provider.
- Persist external references and export/sync state separately from native accounting state. A failed or ambiguous provider action must not undo or repost a committed internal journal.
- Implement reconciliation for provider success/local failure, timeout/unknown outcome, duplicate callbacks, stale OAuth, rate limits, validation failure, and changed provider scopes.
- Add authority/cutover read models and provider-management UI explaining where the books are authoritative, which periods are affected, pending exports, failures, and reconciliation actions.
- Preserve existing Fortnox import/read projection mode for external-authority periods and existing downstream actions where they remain valid.
- Ensure sales/support/agent workflows request Finance accounting actions through Application contracts rather than assuming Fortnox is always the accounting destination.
- Add safe operator-visible failure and audit evidence for authority changes, exports, provider references, reconciliation, and cutover completion.

### 5. Constraints and preservation rules

- One company/accounting period has one authoritative book. Do not add a generic dual-write mode.
- Provider schemas, tokens, scopes, endpoints, and error payloads remain inside adapters/integration entities.
- External writes never run in request handlers and never share a false distributed transaction with SQL Server.
- Ambiguous outcomes enter reconciliation; they are not marked successful or blindly retried.
- Preserve current Fortnox OAuth/sync behavior and existing external references through compatible migrations.
- Do not broaden provider-specific assumptions into the country-agnostic kernel or reporting model.

### 6. Acceptance criteria

- **Given** a new company without integrations, **when** accounting setup completes, **then** internal authority is active and no provider connection is required.
- **Given** an internal-authority period, **when** a provider attempts an independent authoritative posting for the same source, **then** it is rejected or treated as downstream export according to explicit policy.
- **Given** an external-authority period, **when** a normal native authoritative post is attempted, **then** it is blocked with a clear authority explanation.
- **Given** a committed internal voucher and a provider timeout, **when** export execution loses the outcome, **then** the voucher remains committed and export enters reconciliation without reposting locally.
- **Given** duplicate export delivery, **when** workers execute concurrently, **then** at most one provider business action is issued for the stable identity or the duplicate is safely reconciled.
- **Given** an authority cutover, **when** opening/trial balances or source mappings do not reconcile, **then** completion is blocked and conflicts are listed.
- **Given** a successful cutover at an effective period boundary, **when** later actions run, **then** each period follows exactly one authority and the transition is audited.

### 7. Verification

- Extend Fortnox integration foundation, OAuth, sync, write approval, outbound execution, mapping, error translation, and external-reference tests.
- Add authority policy tests across internal, external, and migration states and period boundaries.
- Add failure-injection tests for timeout, provider success/local failure, duplicate delivery, stale credentials, scope errors, rate limiting, permanent validation, and reconciliation.
- Add cross-tenant, authorization, approval, idempotency, and concurrency tests for authority and export operations.
- Add UI/API-client tests for authority explanation, cutover preview, pending export, failure, and reconciliation states.
- Run existing sales-to-Fortnox and supplier Fortnox regression suites to ensure preserved behavior.
- Run focused Finance/API/Web builds/tests and broader integration regressions.

### 8. Definition of done

The prompt is complete when native accounting remains provider-independent, external providers stay true adapters, authority is explicit and period-safe, no dual-authoritative posting path exists, Fortnox export/sync is durable/idempotent/reconcilable, existing integrations are preserved, UI makes authority and failures understandable, and all failure-path and regression verification is green.

---

## Prompt 10: Complete Historical Migration, Operations, Recovery, and Production Release Evidence

### 1. Title and outcome

Finish the bare-bones accounting application as an operable production capability by migrating existing Finance data safely, reconciling balances, exposing health and failure signals, proving backup/restore, and producing release evidence with no unresolved in-scope accounting gaps.

This is an implementation and production-hardening prompt, not a planning-only checkpoint. It must close defects discovered across the full path and leave the native accounting application ready for controlled release.

### 2. Current context

- Prompts 1-9 provide configuration, policy packs, native posting, setup, journals, AR, AP, banking, reports, close, exports, provider authority, and migrations.
- The repository may contain historical local Finance, simulation, seed, and Fortnox-derived accounts, transactions, invoices, bills, payments, ledger entries, source mappings, periods, and snapshots.
- Existing restore scripts must keep local SQL Server and Docker SQL Server compatible.
- Accounting evidence includes SQL Server records plus linked source documents/object storage.
- The platform already has audit, logs, metrics, background execution, and health concepts that must be extended rather than duplicated.

### 3. Dependencies

- Prompts 1-9 completed with their migrations and focused tests green.

Required instructions: read and follow `AGENTS.md`, `production-implementation.md`, `accounting_app.md`, `docs/architecture-rules.MD`, and `architecture-inst.md` if present, in addition to the shared database compatibility, background execution, audit, observability, and production rules in this pack.

### 4. Implementation requirements

- Inventory actual existing Finance data shapes and implement bounded, resumable, company-scoped migration/backfill jobs for account semantics, authority, pack versions, voucher series/numbers, source versions, idempotency, journal links, tax facts where known, and reconciliation state.
- Never invent ambiguous historical accounting facts. Persist conflict records with safe reason codes, evidence, status, operator actions, and audit.
- Produce company/period cutover reports comparing opening balances, journal totals, trial balances, AR, AP, bank, tax facts where available, provider references, documents, and snapshots.
- Make backfills idempotent, restart-safe, concurrency-safe, leased/claimed where distributed execution applies, and observable by progress/failure counts.
- Add accounting health/readiness signals for migration conflicts, posting failures, stale approvals, duplicate/idempotent replays, suspense balance, reconciliation backlog, close blockers, export/reconciliation backlog, snapshot failures, and pack/configuration validity.
- Add structured logs/metrics with company, journal/source, background execution, provider, and correlation identifiers while excluding secrets and unnecessary document contents.
- Create or update operator runbooks covering accounting initialization, migration conflicts, posting incidents, voucher-sequence issues, reconciliation/suspense, close failures, policy-pack upgrade, provider ambiguity, export recovery, and restore verification.
- Prove coordinated backup/restore of SQL Server and accounting source-document/object storage. Validate hashes, links, audit references, voucher uniqueness, totals, snapshots, and provider references after restore.
- Verify all migrations from an empty database and at least one representative restored database in both supported local and Docker SQL Server flows.
- Run end-to-end scenarios for a new micro-company: setup, manual journal, invoice, bill, customer/supplier payment, bank reconciliation, correction, tax summary, statements, close/lock, export, and restore.
- Run failure-injection scenarios at the highest-risk boundaries: duplicate commands, concurrent voucher allocation, process death, SQL transient failure, provider timeout, provider success/local failure, stale approval, cross-tenant identifier, locked period, and policy-pack upgrade.
- Resolve in-scope critical/high findings uncovered by this verification. Do not merely list them as future work.
- Produce a repository-backed release evidence document covering scope, migrations, configuration, authorization, external side effects, exact build/test results, failure injection, observability, deployment order, rollback/forward-fix, recovery, residual risks, and runbook links.

### 5. Constraints and preservation rules

- Migration and repair are additive, resumable, and evidence-preserving. Do not drop/recreate databases or overwrite ambiguous history.
- New micro-companies with no legacy data must not be forced through provider migration.
- Simulation/seed data must remain clearly non-production and must not contaminate authoritative internal-ledger companies.
- Do not mark background work successful while conflicts, partial state, or ambiguous external outcomes remain unexplained.
- Release evidence must report exact commands/results; do not claim unperformed verification.
- Residual legal/country compliance risk must be explicit for country-neutral mode and for any unvalidated policy pack.

### 6. Acceptance criteria

- **Given** an existing company with unambiguous Finance data, **when** migration runs repeatedly or resumes after interruption, **then** it converges to one identical native accounting result without duplication.
- **Given** ambiguous historical account/source/tax/provider data, **when** migration runs, **then** it creates an operator-visible conflict and does not fabricate an authoritative posting.
- **Given** a new company without legacy/provider data, **when** onboarding runs, **then** it enters internal-ledger operation without migration blockers.
- **Given** the complete micro-company scenario, **when** reports and close run, **then** journal, trial balance, AR, AP, bank, tax, P&L, and balance-sheet totals reconcile.
- **Given** SQL Server and document storage are restored, **when** integrity verification runs, **then** vouchers, lines, source links, evidence hashes, audits, snapshots, and provider references remain valid.
- **Given** injected failures or duplicate/concurrent execution, **when** recovery completes, **then** each business action ends in confirmed success, confirmed safe failure, or explicit reconciliation—not an unexplained state.
- **Given** the release evidence, **when** a reviewer evaluates readiness, **then** exact verification, deployment, recovery, known risks, and operator actions are available without reconstructing chat history.

### 7. Verification

- Run every focused suite added by Prompts 1-9 and the existing affected Finance/API/Web regression suites.
- Run full relevant Release builds with exact commands and capture warnings/errors.
- Run migration creation/discovery/pending-model validation and empty/restored SQL Server upgrade tests.
- Run local SQL Server and Docker restore/startup/smoke/backup/restore validation.
- Run deterministic golden accounting scenarios under at least two contrasting synthetic packs and locally reviewed fixtures for any advertised country pack.
- Run tenant-isolation and authorization matrices for all accounting permissions and background paths.
- Run failure-injection, concurrency, idempotency, approval-version, provider ambiguity, and restore-integrity tests.
- Run screenshot/responsive/accessibility checks for all new accounting UI surfaces and confirm references exist.
- Review the complete diff for unrelated changes, secrets, mock production data, debug settings, silent fallbacks, and in-scope TODOs.

### 8. Definition of done

The prompt and implementation sequence are complete only when the native country-agnostic accounting application can safely serve a micro-company without Fortnox, existing data has an evidence-preserving migration path, all authoritative totals reconcile, operational failures are visible and recoverable, SQL Server plus documents can be restored coherently, optional providers remain adapters, exact release evidence is checked in, and all required builds/tests/failure scenarios are green. No scaffolding, mock production data, silent simulation fallback, unhandled intermediate state, unresolved in-scope critical/high defect, or deferred in-scope TODO remains.
