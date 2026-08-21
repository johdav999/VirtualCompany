# Financial Migration Implementation Prompts

This prompt pack implements the bidirectional accounting-system migration described in `financial-tool-migration.md`. Execute the prompts in order. Each prompt delivers a bounded production capability and preserves the existing accounting-authority, approval, provider-integration, audit, and agent-orchestration foundations.

`architecture-inst.md` is not currently present in the repository. Every prompt therefore requires `/docs/architecture-rules.md` directly and also requires `architecture-inst.md` if that file exists when the prompt is executed.

## Prompt 1 — Durable accounting-system switch lifecycle

### 1. Title and outcome

Implement a durable, company-scoped accounting-system switch workflow that represents a planned move between Virtual Company and an external provider, or between two external providers, without changing accounting authority prematurely.

The delivered value is a safe system of record for migration intent, state, ownership, concurrency, cancellation, and audit history. At the end of this prompt, an accounting administrator can create, read, list, update the draft plan, and cancel a switch while the existing source remains authoritative.

### 2. Current context

- Read and follow `production-implementation.md`, `financial-tool-migration.md`, and `/docs/architecture-rules.md` before implementation. If `architecture-inst.md` exists, read and follow it too.
- `AccountingAuthorityPeriod` in `src/VirtualCompany.Domain/Entities/AccountingAuthorityPeriod.cs` currently owns the period authority timeline and stores the bounded `migration` state plus manually maintained reconciliation fields.
- `AccountingAuthorityService` and `AccountingAuthorityPolicy` in `src/VirtualCompany.Infrastructure.Finance/Finance/` preview, start, validate, and complete authority changes and enforce one authoritative system per accounting period.
- `AccountingAuthorityContracts.cs` exposes the current commands, queries, DTOs, reason codes, and service interfaces.
- `InternalFinanceController.AccountingAuthority.cs`, `FinanceApiClient.AccountingAuthority.cs`, and `AccountingConnectionsPage.razor(.cs)` expose the existing API and Web flow.
- `VirtualCompanyDbContext` already contains `AccountingAuthorityPeriods`, `AccountingProviderExports`, `AccountingMigrationRuns`, `AccountingMigrationConflicts`, and `AccountingCutoverReports`.
- Existing tests include `AccountingAuthorityPolicyTests`, `AccountingOperationsTests`, `AccountingOperationsApiIntegrationTests`, and `AccountingAuthoritySurfaceTests`.
- The current implementation has no separate durable aggregate for preparation before the authority period enters migration.

### 3. Dependencies

None. This is the foundation for all later prompts.

### 4. Implementation requirements

- Add a Finance-owned domain aggregate, with a name such as `AccountingProviderSwitch`, representing source endpoint, target endpoint, effective fiscal period, migration strategy, reason, responsible user, optional responsible agent, status, version, timestamps, cancellation state, correlation ID, and safe failure summary.
- Represent endpoints generically as internal or external with a provider key. Derive direction from source and target; do not persist separate behavior-specific implementations.
- Support the strategies `opening_balances_and_open_items`, `current_fiscal_year`, and `full_history` using typed domain values and validation.
- Add explicit workflow states sufficient for draft, assessment, planning, preparation, rehearsal, scheduling, freeze, reconciliation, activation approval, active monitoring, completion, blocked, cancelled, and recovery. Enforce legal transitions in domain/application policy rather than controllers or UI.
- Keep `AccountingAuthorityPeriod` as the authority timeline. Creating or editing a draft switch must not end an authority period, set configuration to migration, pause posting, or invoke a provider.
- Prevent more than one non-terminal switch for the same company. Use SQL Server-enforceable persistence rules where possible and transactional/concurrency checks where a filtered uniqueness constraint is unsuitable.
- Add company-scoped Application commands, queries, DTOs, stable reason codes, and an owning Finance application service. Commands must accept expected versions and reject stale writes.
- Add transport-only endpoints under the matching internal Finance controller partial for create, get, list, update-plan, cancel, and allowed-actions/readiness. Apply server-side company membership and `CompanyPolicies.AccountingAdmin` authorization to mutations.
- Persist audit events for creation, plan changes, status changes, blocking, cancellation, and rejected stale or illegal transitions. Include source, target, effective period, strategy, actor, before/after state, and correlation ID without credentials or sensitive provider payloads.
- Add EF configurations, `DbSet` registrations, an EF migration in `VirtualCompany.Persistence.Migrations`, and update the model snapshot.
- Preserve all existing accounting-authority and historical-migration records and API behavior. Do not repurpose `AccountingMigrationRun`; it currently represents historical native-accounting recovery and reports rather than the provider-switch lifecycle.
- Register new Finance services only through `AddFinanceInfrastructure` in `FinanceModuleRegistration.cs`.
- Update relevant documentation where the persisted lifecycle or operator behavior requires explanation.

### 5. Constraints and preservation rules

- Follow `production-implementation.md`; deliver production code, real endpoints, real authorization, and a real migration with no mock production data.
- Follow `/docs/architecture-rules.md` and `architecture-inst.md` if present. Domain owns deterministic transitions, Application owns contracts, Finance Infrastructure owns implementation, Persistence owns EF configuration, API remains transport-only, and Web is not part of this prompt.
- Preserve tenant isolation even when using `IgnoreQueryFilters`; every such query must explicitly reapply `CompanyId`.
- Preserve the existing single-authority invariant and all current Fortnox connection, sync, export, and write behavior.
- Do not introduce provider schemas into the new aggregate or JSON-only storage for queryable workflow state.
- Do not create a new generic workflow engine or a second approval, audit, outbox, or agent stack.
- Keep local SQL Server and Docker SQL Server restore/run compatibility. Document any required operational step in both paths.

### 6. Acceptance criteria

- Given a company with internal authority, when an accounting administrator creates an internal-to-Fortnox switch for a later monthly period, then one durable draft is created and current accounting authority remains unchanged.
- Given Fortnox authority, when a switch targeting Virtual Company is created, then the source and target endpoints and inbound direction are represented without a second direction-specific aggregate.
- Given one external source and a different external target, when a switch is created, then both provider keys are preserved and Virtual Company is not incorrectly marked authoritative.
- Given a duplicate active switch, cross-company identifier, invalid endpoint, same source and target, invalid strategy, non-existent period, or non-future boundary, when submitted, then the backend rejects it with a stable plain-English policy result.
- Given a stale expected version or illegal transition, when a command is submitted, then no state changes and the client receives a conflict response.
- Given cancellation before activation, when approved by an accounting administrator, then the switch becomes terminal and authority remains unchanged.
- Given historical authority and migration records, when the migration is applied, then all existing records remain readable and behavior remains compatible.

### 7. Verification

- Add focused domain tests for every allowed and forbidden state transition, endpoint validation, strategy normalization, and version behavior.
- Add Finance tests for single-active-switch enforcement, period validation, source-authority validation, audit writes, and tenant isolation.
- Add API integration tests for owner/accounting-admin success, employee denial, cross-company denial, stale versions, cancellation, and compatible error mapping.
- Generate and inspect the EF migration. Validate upgrade behavior against SQL Server, including indexes, foreign keys, row-version/concurrency behavior, and model snapshot consistency.
- Verify both local SQL Server and Docker SQL Server migration/restore instructions or scripts remain compatible.
- Run focused tests, then build the affected solution projects and run the existing accounting-authority and operations regression suites.

### 8. Definition of done

The prompt is complete when the provider-switch lifecycle is a production-grade, tenant-safe, versioned, audited system of record; draft work cannot alter authority; existing authority and migration behavior is preserved; the database has a forward-compatible EF migration for local and Docker SQL Server; all endpoints enforce backend authorization; and all affected tests and builds pass with no scaffolding, mock production data, silent failures, deferred in-scope TODOs, or unhandled workflow states.

## Prompt 2 — Provider capabilities, inventory, and deterministic gap assessment

### 1. Title and outcome

Implement read-only source/target assessment for accounting-system switches, including provider capability discovery, normalized financial inventory, durable extraction evidence, and deterministic migration-gap classification.

The delivered value is an evidence-backed answer to what can be transferred, what is missing, and what blocks the selected migration strategy, without changing either accounting system.

### 2. Current context

- Read and follow `production-implementation.md`, `financial-tool-migration.md`, and `/docs/architecture-rules.md`. If `architecture-inst.md` exists, read and follow it.
- Prompt 1 has added the durable accounting-system switch lifecycle and its API/application contracts.
- `IFinanceIntegrationProvider` currently exposes provider metadata, OAuth, synchronization, write commands, and mapping. `FinanceIntegrationProviderRegistry` resolves registered providers; Fortnox is currently the implemented provider.
- `FinanceIntegrationConnection` contains connection status, scopes, provider tenant identity, and synchronization health.
- `FortnoxApiClient`, `FortnoxSyncService`, `FortnoxMappingService`, and the Fortnox integration adapters own current Fortnox-specific API and mapping behavior.
- `AccountingHistoricalMigrationService` already inventories historical native data and produces conflicts and cutover reports for its separate recovery use case. Reuse compatible concepts, but do not conflate its runs with a provider switch.
- Current authority preview only reports committed journals, pending exports, and unmapped historical journals. It does not assess tax, dimensions, open items, currencies, documents, period locks, or provider capability gaps.

### 3. Dependencies

- Prompt 1 completed, including its EF migration and service registration.
- Real provider credentials are not required for deterministic unit tests, but real connected-provider behavior must use the production adapter and may be covered by categorized external integration tests.

### 4. Implementation requirements

- Add Finance Application contracts for a provider migration capability profile and read-only inventory extraction. Cover accounts, tax, fiscal periods and locks, voucher numbering, customers, suppliers, invoices, credits, payments, allocations, bank and reconciliation state, currencies, exchange rates, dimensions, journals, attachments, stable identifiers, incremental extraction, sandbox/preview support, rate limits, and reconciliation lookup.
- Add a provider-switch adapter abstraction separate from the remote provider's general `IFinanceIntegrationProvider` concerns. Implement the external side for Fortnox inside Finance Infrastructure and an internal-ledger inventory source using existing Finance queries/services.
- Do not claim a capability merely because the conceptual model supports it. Persist `supported`, `partial`, `unsupported`, or `unknown`, plus a safe explanation and required scope.
- Add durable company-scoped switch extraction and dataset records with extraction time, source version or cursor, record counts, financial totals, integrity hash, capability result, and safe failure state. Store important queryable state relationally; use bounded JSON only for flexible evidence details.
- Add deterministic gap rules for aggregate mismatches, account and tax mapping, open items, payment allocation, currency, dimensions, documents, numbering, duplicates, unknown provider outcomes, timing, locked periods, missing configuration, missing provider scope, and unsupported target capability.
- Make blocking severity depend on migration strategy. Preserve why a gap is blocking and the evidence used.
- Distinguish `confirmed_absent` from `not_returned`, `not_authorized`, `unsupported`, and `unknown`.
- Run assessment through a durable background job with leases, bounded batches, idempotency, retry classification, safe errors, and operator-visible progress. Reuse established hosted-worker patterns rather than executing a long provider scan in the HTTP request.
- Add commands/queries and API endpoints to start or replay an assessment and read inventory, capability, dataset, gap, progress, and allowed-next-action results.
- Keep the switch in a preparation state where the current source remains authoritative. Assessment must perform no provider writes and no local ledger commits.
- Audit assessment request, completion, blocking failures, and material gap changes. Add telemetry for duration, counts, failures, and blocking-gap totals without provider secrets.
- Add EF entities/configurations/migration/snapshot updates as required and retain Docker/local SQL Server compatibility.

### 5. Constraints and preservation rules

- Follow production and architecture instructions. Provider authentication, endpoints, schemas, and error translation remain in provider adapters; controllers contain no EF or gap rules.
- Every extraction, dataset, and gap is company-scoped and switch-scoped. Cross-company records may never be read or attached to a switch.
- Do not call LLMs to decide capability or gap severity. This prompt is deterministic and read-only.
- Do not use fake Fortnox responses in production. Test doubles are allowed only in tests behind the adapter contract.
- Avoid broad unbounded provider reads. Use cursors or bounded pages and persist resumable progress.
- Preserve current sync and OAuth behavior and never log tokens or provider payloads containing sensitive data.

### 6. Acceptance criteria

- Given an internal-to-Fortnox switch, when assessment completes, then internal datasets and target Fortnox capabilities are persisted with counts, totals, timestamps, and explicit limitations.
- Given a Fortnox-to-internal switch, when required scopes are missing, then the affected datasets are marked `not_authorized`, actionable gaps are created, and no incomplete data is presented as absent.
- Given an external-to-external switch, when source and target capabilities differ, then the gap report identifies unsupported target features while Virtual Company remains non-authoritative.
- Given opening-balances strategy, when an old historical attachment is unavailable but the source archive remains accessible, then policy may classify it as non-blocking; given full-history strategy, the same gap is classified according to the stricter evidence requirement.
- Given duplicate assessment delivery or worker restart, when processing resumes, then records and counts are not duplicated and progress continues safely.
- Given a provider timeout after a read, when the outcome is retryable, then bounded retry is scheduled; permanent authorization or validation failures stop with a safe operator action.

### 7. Verification

- Add pure policy tests for every gap category and strategy-dependent severity.
- Add Finance tests for internal inventory, capability resolution, durable batching, restart, idempotency, and company isolation.
- Add Fortnox adapter tests using its established HTTP test factory for scopes, pagination, missing fields, rate limiting, stale credentials, validation failures, and safe error translation.
- Add API tests for authorized assessment start/read, employee denial, cross-company denial, stale switch version, and status/result contracts.
- Add SQL Server migration/model tests where provider-specific constraints or concurrency cannot be proven with SQLite.
- Run current Fortnox OAuth, sync, mapping, external-reference, authority, and accounting-operations regression tests plus a focused build.

### 8. Definition of done

The prompt is complete when every switch direction has a real, read-only, resumable assessment; provider capabilities and data absence are represented honestly; strategy-dependent financial gaps are deterministic, persisted, tenant-safe, auditable, and actionable; no authority or financial record is mutated; and all focused and regression verification passes without mock production behavior, silent partial results, or deferred in-scope TODOs.

## Prompt 3 — Normalized staging, mappings, and source dispositions

### 1. Title and outcome

Implement normalized migration staging and versioned mapping decisions so every in-scope source record has a traceable, reviewable disposition before it can be transferred or committed.

The delivered value is a provider-independent bridge that supports all migration directions without translating one provider payload directly into another provider payload.

### 2. Current context

- Read and follow `production-implementation.md`, `financial-tool-migration.md`, and `/docs/architecture-rules.md`. If `architecture-inst.md` exists, read and follow it.
- Prompts 1 and 2 have delivered a durable switch, provider capabilities, inventory extraction, and deterministic gap records.
- Core accounting accounts, policy packs, fiscal periods, journals, invoices, bills, payments, external references, documents, and provider mapping foundations already exist in Finance.
- `FortnoxMappingService` and provider adapters contain Fortnox-specific mappings; core Finance entities must remain provider-neutral.
- `AccountingMigrationConflict` demonstrates persisted reason codes, evidence, operator action, and resolution, but it belongs to historical native-data recovery and must not become the switch aggregate.
- The current system does not have switch-scoped normalized staging, versioned mapping approval, or one disposition per source record.

### 3. Dependencies

- Prompt 1 and Prompt 2 completed, including their database migrations.

### 4. Implementation requirements

- Add normalized staging contracts and entities for accounts, tax treatments, counterparties, documents, journals and lines, invoices and credits, payments and allocations, bank state, currencies/rates, dimensions, open items, and opening-balance candidates where those datasets are supported.
- Preserve provider source identity, provider version or modification time, extraction batch, source hash, normalized hash, evidence references, mapping version, and disposition.
- Define explicit dispositions matching `financial-tool-migration.md`: ready, mapped, transformed, opening-balance representation, duplicate, excluded with approval, missing, unsupported, conflicting, awaiting evidence, and blocked.
- Enforce a stable uniqueness boundary derived from company, switch, source endpoint, dataset, external/source identity, and source version. Replays must update or reuse the same staged identity rather than duplicate it.
- Add versioned mapping sets and decisions for accounts, tax codes, dimensions, counterparties, currencies, numbering, and payment allocations. Separate provider mapping from domain semantics.
- Add deterministic mapping suggestions based on exact identifiers, existing approved external references, account roles, known tax semantics, and previously approved company mappings. Persist confidence and evidence, but do not automatically approve ambiguous or material mappings.
- Route material mappings, exclusions, transformations, and manual exceptions through existing approval infrastructure. Bind approval to switch ID, mapping/version, affected records, financial totals, and evidence so later source changes invalidate stale approval.
- Add commands and queries to list staged records, filter by dataset/disposition, preview proposed mapping, approve or reject a mapping through the existing approval flow, resolve a disposition, and report completeness.
- Update switch allowed-actions policy so preparation cannot advance until every in-scope source record has exactly one valid disposition and every blocking mapping gap is resolved.
- Add audit events for automated suggestions, human decisions, exclusions, transformations, duplicate matches, and stale decision rejection.
- Add EF configuration/migration/snapshot updates and bounded indexes for switch, dataset, disposition, source identity, and unresolved work.

### 5. Constraints and preservation rules

- Follow all production and architecture instructions. Do not persist raw provider schemas as core business state, and do not add provider-specific fields to neutral entities.
- Staging is non-authoritative. No staged record may create a ledger entry, invoice, bill, payment, or provider write in this prompt.
- An AI confidence score cannot authorize a financial mapping. Backend policy decides whether deterministic auto-acceptance is allowed.
- Never create balancing lines, tax codes, exchange rates, accounts, or allocations to hide missing data.
- Preserve source payload privacy. Store only sanitized bounded evidence required for audit and reconciliation.
- Maintain tenant isolation, optimistic concurrency, auditability, and SQL Server/Docker compatibility.

### 6. Acceptance criteria

- Given a repeated provider extraction of the same source version, when staging runs again, then the same staged identity is reused and completeness counts do not increase.
- Given an existing approved external reference, when a matching record is staged, then a deterministic mapping is proposed with the reference as evidence.
- Given ambiguous account or tax semantics, when mapping is evaluated, then the record remains blocked or awaiting review and no value is invented.
- Given a material mapping approved for version N, when source or normalized content changes to version N+1, then the approval becomes stale and transfer is blocked pending review.
- Given a source record intentionally excluded, when the exclusion is allowed by strategy, then an approval and explanation are required and the disposition remains visible in completeness totals.
- Given all in-scope records, when completeness is calculated, then each has exactly one disposition and totals reconcile to the assessed dataset.

### 7. Verification

- Add domain tests for disposition transitions, uniqueness, mapping versioning, stale approvals, and illegal combinations.
- Add Finance tests for deterministic suggestions, duplicates, completeness, strategy-sensitive exclusions, concurrency, and tenant isolation.
- Add approval integration tests proving affected versions/totals are bound to the decision and rechecked before use.
- Add API integration tests for list/filter/preview/decision paths, forbidden users, cross-company access, and plain-English errors.
- Inspect and validate the SQL Server migration, indexes, foreign keys, and snapshot; verify local and Docker restore paths remain compatible.
- Run existing provider mapping, approval, accounting migration, and authority tests plus a focused solution build.

### 8. Definition of done

The prompt is complete when all supported source data is normalized into provider-independent, versioned, non-authoritative staging; every record has a single traceable disposition; ambiguous and material mappings are approval-bound; duplicates and stale data cannot progress; no provider or ledger write occurs; and all tests, migrations, and builds are green without scaffolding, invented finance data, or deferred in-scope TODOs.

## Prompt 4 — Rehearsal, reconciliation evidence, and approved cutover plan

### 1. Title and outcome

Implement non-authoritative migration rehearsal, deterministic financial reconciliation, immutable cutover-plan snapshots, and separate plan approval.

The delivered value is a reliable proof that the proposed transfer is financially coherent before the source is frozen or any authoritative target data is created.

### 2. Current context

- Read and follow `production-implementation.md`, `financial-tool-migration.md`, and `/docs/architecture-rules.md`. If `architecture-inst.md` exists, read and follow it.
- Prompts 1–3 have delivered switch lifecycle, assessment, gaps, normalized staging, mappings, dispositions, and mapping approvals.
- `AccountingHistoricalMigrationService` already creates `AccountingCutoverReport` records with period financial summaries for historical recovery.
- `AccountingAuthorityPeriod.RecordCutoverValidation` currently accepts manually supplied booleans and a conflict count. This cannot be the primary evidence for the new switch workflow.
- `IApprovalRequestService` and approval chains already support required roles/users, steps, decisions, and affected-entity/rationale presentation.
- Provider capabilities from Prompt 2 identify whether a real provider preview or sandbox is available.

### 3. Dependencies

- Prompts 1, 2, and 3 completed.
- Required mapping or exception approvals for the rehearsal version are complete.

### 4. Implementation requirements

- Add durable rehearsal runs, immutable input snapshots, per-dataset results, reconciliation checks, evidence references, and safe failure state. Rehearsals must be resumable, idempotent, and executed in bounded background work.
- Support a real provider sandbox/preview adapter when available. When unavailable, perform a clearly identified local target simulation using production mapping and validation logic; do not present it as provider acceptance.
- Calculate debit/credit equality, trial balance by account and currency, receivable/open-customer-item agreement, payable/open-supplier-item agreement, tax control/detail agreement, bank and reconciliation agreement, opening equity treatment, source-disposition completeness, duplicate identities, unresolved provider outcomes, evidence coverage, and source-snapshot freshness.
- Persist expected values, observed values, tolerance, result, reason code, data sources, calculation version, and timestamps for every check.
- Derive readiness from persisted checks and open blocking gaps. Do not allow manually entered booleans to override calculated failures.
- Permit manual evidence only for checks that cannot be calculated. Require authorized actor, explanation, document or evidence reference, timestamp, expiry or applicable version, and audit event.
- Produce an immutable cutover-plan snapshot containing source/target, effective period, strategy, dataset counts and totals, mapping versions, accepted exceptions, rehearsal result, freeze window, recovery boundary, participants, and source snapshot hash.
- Create a plan approval through the existing approval service and bind it to the immutable plan version/hash. Recalculate allowed action immediately before recognizing approval.
- Add commands/queries/API endpoints to start/replay rehearsal, read progress/results, generate a plan, request approval, and read plan approval/readiness.
- Do not start the authority migration state, freeze posting, commit native ledger records, or perform non-preview provider writes in this prompt.
- Audit every rehearsal, calculation, manual evidence decision, plan generation, approval request, approval outcome, and stale-plan rejection.

### 5. Constraints and preservation rules

- Follow production and architecture instructions. Reconciliation is deterministic Finance policy, not controller, UI, or LLM logic.
- Reuse existing reporting and ledger query contracts where semantically correct; do not duplicate the accounting engine.
- Do not weaken `AccountingAuthorityPeriod` readiness behavior for legacy callers. Introduce the switch evidence boundary compatibly, then route later activation through it.
- Financial tolerances must be currency/policy-aware and explicit; never use a broad tolerance to conceal differences.
- Plan approval and later activation approval are separate. Laura cannot approve either.
- Preserve tenant isolation, version checks, safe logging, and SQL Server/Docker compatibility.

### 6. Acceptance criteria

- Given a balanced opening-balance migration with complete dispositions, when rehearsal runs, then all calculated controls and their evidence are persisted and the plan can be requested for approval.
- Given a trial-balance, tax, open-item, duplicate, unknown-outcome, or freshness mismatch, when reconciliation runs, then the relevant stable check fails and plan readiness remains blocked.
- Given a provider without preview support, when rehearsal succeeds locally, then the result clearly states that target-provider acceptance is not yet proven.
- Given a manually verifiable requirement, when a user records evidence without an attachment/reference or authorization, then it is rejected.
- Given an approved plan whose staging, mapping, totals, strategy, or source snapshot changes, when the plan is used, then it is rejected as stale.
- Given approval of a current immutable plan, when queried, then the switch becomes eligible for preparation but authority remains unchanged.

### 7. Verification

- Add pure reconciliation tests for every financial control, tolerance, currency, mismatch, and completeness condition.
- Add worker tests for batching, lease recovery, replay, failure classification, and idempotency.
- Add approval tests for immutable plan binding, multi-step decisions, rejection, expiration, stale versions, and separation from activation approval.
- Add tenant-isolation and authorization API tests for rehearsal, manual evidence, plan generation, and approval status.
- Add provider preview adapter tests where Fortnox supports validation; otherwise test the explicit unsupported capability and local-simulation disclosure.
- Validate new EF migration/schema changes against SQL Server and Docker-compatible restore/run paths.
- Run existing reporting, accounting operations, authority, approval-chain, provider export, and Fortnox regression suites, followed by a focused build.

### 8. Definition of done

The prompt is complete when a switch can produce a resumable, non-authoritative rehearsal; all material financial controls are calculated and evidenced; manual assertions are constrained and auditable; the approved plan is immutable and stale-safe; authority and real target books remain unchanged; and all tests/builds pass with no fake provider acceptance, silent mismatch, or deferred in-scope TODO.

## Prompt 5 — External-provider to Virtual Company preparation

### 1. Title and outcome

Implement production preparation for moving authoritative accounting from an external provider such as Fortnox into Virtual Company's accounting application.

The delivered value is a complete, approved, idempotent set of native accounting candidates—opening balances and open items by default, with supported current-year or full-history detail—that is ready for atomic activation but remains non-authoritative until Prompt 7.

### 2. Current context

- Read and follow `production-implementation.md`, `financial-tool-migration.md`, and `/docs/architecture-rules.md`. If `architecture-inst.md` exists, read and follow it.
- Prompts 1–4 have delivered the switch, inventory, gaps, staging, mappings, reconciliation, and approved plan.
- `AccountingConfigurationService` and `AccountingAdministrationService` establish internal accounting configuration, chart, voucher series, fiscal years, and monthly periods.
- `IAccountingPostingService` and `AccountingPostingService` are the governed native ledger boundary. `IManualJournalService` owns approval-backed manual journal drafts and evidence.
- Native customer invoice, supplier bill, payment, cash settlement, and bank reconciliation services already exist and must remain the owning boundaries for their records.
- `AccountingAuthorityPolicy` currently permits provider imports as read projections while external authority is active and blocks normal native authoritative posting.
- Current Fortnox synchronization is not a complete provider-switch import and must not be treated as one.

### 3. Dependencies

- Prompts 1–4 completed.
- A current approved cutover plan targeting Virtual Company exists.
- Internal accounting configuration is complete or the switch has explicit blocking readiness gaps.

### 4. Implementation requirements

- Add a deterministic internal-target readiness policy covering accounting configuration, fiscal periods, chart roles, tax rules, voucher series, base currency, control accounts, bank/payment accounts, dimensions, and policy-pack compliance disclosure.
- Convert approved normalized staging into switch-scoped native candidates without posting them. Use domain-appropriate candidates for opening journals, historical journals, customers, suppliers, invoices/credits, payments/allocations, bank state, documents, and external references.
- For the default strategy, create verified opening-balance and open-item candidates while keeping earlier provider periods as the historical archive. For current-year/full-history strategies, preserve source dates, voucher references, corrections, currencies, tax evidence, and period attribution.
- Do not bypass existing native accounting policies. Candidate validation must invoke or share the same deterministic rules used by the owning posting/document services.
- Preserve stable source identities and idempotency so retries cannot create duplicate customers, invoices, bills, payments, or journals.
- Represent unsupported internal capabilities as blocking gaps or explicitly approved archive dependencies. Do not silently drop fixed-asset, specialist-tax, payroll, document, dimension, or other source data outside internal scope.
- Create provider-source external references and immutable source hashes for all accepted candidates.
- Add bounded background preparation with progress, lease recovery, retry classification, and operator-visible failure state.
- Add commands/queries/API endpoints to start/replay preparation and read candidate totals, validation results, unresolved gaps, and activation readiness.
- Persist audit evidence for every transformation, created candidate, rejected candidate, archive dependency, and validation outcome.
- Keep all candidates non-authoritative. Do not write posted ledger entries or enable internal authority until Prompt 7 performs the approved activation.

### 5. Constraints and preservation rules

- Follow production and architecture instructions. Use Finance Application contracts and owning Finance services; do not place EF or posting rules in controllers.
- Never use direct SQL or detached entities to bypass posting, approval, document, or tenant boundaries.
- Do not create balancing entries, exchange rates, tax values, or allocations merely to make totals agree.
- Imported source documents retain evidence hashes and access scope. Missing evidence follows approved gap policy.
- Earlier provider-authoritative periods remain immutable as authority history. Internal candidates may not overwrite existing committed local records.
- Maintain local/Docker SQL Server compatibility for schema changes.

### 6. Acceptance criteria

- Given Fortnox authority and an approved opening-balances plan, when preparation completes, then balanced opening-journal and open-item candidates exist with source references and no posted native ledger entry exists yet.
- Given current-year strategy, when historical provider journals are prepared, then dates, currencies, source vouchers, tax evidence, corrections, and periods are preserved or explicitly blocked.
- Given a source record already represented by a stable external reference, when preparation replays, then no duplicate candidate or business record is created.
- Given missing internal tax configuration or control accounts, when readiness is evaluated, then activation is blocked with an actionable gap.
- Given unsupported source capability, when archive retention satisfies the approved strategy, then the dependency is explicit and auditable; otherwise preparation remains blocked.
- Given cross-company source or target IDs, when preparation runs, then access is denied and no candidate is written.

### 7. Verification

- Add focused Finance tests for internal readiness, opening balances, historical candidates, open AR/AP, payments/allocations, currencies, tax, evidence, duplicates, and unsupported capabilities.
- Add policy parity tests proving candidates pass the same validations as normal native posting/document paths.
- Add worker restart, concurrency, idempotency, and safe failure tests.
- Add API authorization, tenant-isolation, stale-plan, and read-model tests.
- Add SQL Server integration tests for transactional candidate persistence and uniqueness constraints where SQLite is insufficient.
- Run native ledger, manual journal, customer invoice, supplier bill, payment, reconciliation, historical migration, and authority regression suites plus the affected solution build.

### 8. Definition of done

The prompt is complete when an approved external-to-internal switch can prepare every in-scope native candidate through governed Finance rules, preserve source identity/evidence, surface unsupported or missing data, replay safely, and prove activation readiness without committing authoritative records; all affected tests and builds pass with no bypass, mock production data, invented accounting values, or deferred in-scope TODOs.

## Prompt 6 — Virtual Company to provider and provider-to-provider preparation

### 1. Title and outcome

Implement production target preparation and durable transfer plans for Virtual Company-to-external and external-to-external migrations, using normalized Finance data and target-provider adapters.

The delivered value is an approved, target-validatable, idempotent transfer package ready for final cutover execution without direct source-provider-to-target-provider translation.

### 2. Current context

- Read and follow `production-implementation.md`, `financial-tool-migration.md`, and `/docs/architecture-rules.md`. If `architecture-inst.md` exists, read and follow it.
- Prompts 1–4 provide approved plans, normalized staging, mappings, capability profiles, and rehearsal evidence.
- Prompt 5 covers external-provider to Virtual Company and is not required for an internal-to-external switch, but its identity and validation patterns should remain consistent.
- `AccountingProviderExportService`, `IAccountingProviderExportAdapter`, `FinanceIntegrationWriteApprovalService`, Fortnox outbound execution, `AccountingExportBackgroundService`, and provider external references already implement approval-backed durable writes for committed local journals.
- `IFinanceIntegrationProvider` and the Fortnox adapters own provider-specific behavior.
- Existing provider exports are journal-oriented and do not yet represent a complete migration package containing master data, opening balances, open items, documents, and dimensions.

### 3. Dependencies

- Prompts 1–4 completed.
- A current approved cutover plan targeting an external provider exists.

### 4. Implementation requirements

- Add a target-preparation adapter contract for migration datasets, implemented for Fortnox without leaking Fortnox schemas into Application or Domain.
- Build transfer batches only from approved normalized staging and mapping versions. For external-to-external switches, always extract to neutral staging and map through the target adapter; never translate source payloads directly.
- Support master data, opening balances, open receivables/payables, credits, payments/allocations, dimensions, documents, and optional historical journals according to provider capability and selected strategy.
- Reuse existing provider write-command approval, outbox/background execution, stable write identity, external references, failure classification, and reconciliation tracking. Extend them compatibly where dataset-specific migration identity is needed.
- Derive idempotency from company, switch, plan version, target provider, dataset, source identity/version, and action. A retry or duplicate delivery must not create a second provider object.
- Separate target preparation that is safe before the boundary from final authoritative transfer. Record whether each operation is preview-only, preparatory/non-posting, or final/authoritative according to provider capability.
- Require explicit approval for preparatory provider writes and bind it to the immutable plan/batch version. Do not reuse plan approval as execution approval unless policy explicitly creates the corresponding approval step.
- Persist transfer batches, items, attempts, provider acknowledgements, external IDs, safe summaries, and reconciliation-needed state.
- Treat timeouts, provider success/local failure, and other ambiguous outcomes as reconciliation work; never retry them blindly.
- Add commands/queries/API endpoints to prepare/replay target batches and read readiness, item state, failures, and reconciliation actions.
- Do not change accounting authority or execute final authoritative writes in this prompt. Final execution is orchestrated by Prompt 7.

### 5. Constraints and preservation rules

- Follow production and architecture instructions. External effects run through durable background execution, not HTTP request handlers.
- Existing journal export and Fortnox sales/supplier behaviors must remain compatible. Do not create a second generic outbound engine when the current infrastructure can be extended safely.
- Provider adapters own authentication, endpoints, payloads, scopes, limitations, and error translation.
- Do not mark provider success without provider evidence, and never expose sensitive payloads or credentials in logs, audits, or UI contracts.
- Preserve one authority per period and prevent migration preparation from being mistaken for an authoritative posting.
- Maintain tenant isolation, approval recheck, concurrency, and local/Docker SQL Server compatibility.

### 6. Acceptance criteria

- Given an approved Virtual Company-to-Fortnox opening-balance plan, when preparation runs, then versioned target batches are created from committed local data and approved staging without changing authority.
- Given provider A-to-provider B, when target batches are built, then the records pass through normalized staging and no source-provider schema appears in the target adapter contract.
- Given a duplicate worker delivery, when the same batch executes or prepares again, then stable identity prevents duplicate target objects.
- Given a missing scope or unsupported target capability, when preparation runs, then the batch stops with an actionable gap rather than silently omitting records.
- Given a timeout or provider-success/local-persistence failure, when execution state is evaluated, then it enters reconciliation-required and is not blindly retried.
- Given stale plan, mapping, or approval versions, when a target operation is attempted, then the backend rejects it before the external action.

### 7. Verification

- Add adapter tests for every supported dataset, deterministic payload mapping, idempotency identity, scopes, provider limitations, and sanitized error handling.
- Add durable worker tests for concurrent claims, duplicate delivery, retryable/permanent/ambiguous failure categories, and provider-success/local-failure recovery.
- Add approval tests proving immediate recheck and stale-plan rejection before provider writes.
- Add API authorization and tenant-isolation tests for preparation and reconciliation.
- Extend Fortnox API test coverage with production request shapes without calling the real API in normal test runs; keep real API tests explicitly categorized.
- Run current provider export, Fortnox write approval, outbound execution, mapping, sync, source-document, invoice, supplier, payment, authority, and external-reference regression suites plus a focused build.

### 8. Definition of done

The prompt is complete when internal-to-external and provider-to-provider switches can build real, provider-validatable, approval-backed, durable transfer packages through normalized Finance contracts; retries and ambiguity are safe; existing exports remain compatible; authority is unchanged; and all tests/builds pass with no direct provider-to-provider mapping, mock production integration, silent omissions, or deferred in-scope TODOs.

## Prompt 7 — Final freeze, transfer, atomic activation, and recovery

### 1. Title and outcome

Implement the final cutover coordinator that freezes the affected boundary, captures the final delta, executes approved transfer work, reconciles results, obtains separate activation approval, and atomically changes accounting authority.

The delivered value is a complete bidirectional switchover that cannot produce dual-authoritative posting and can recover safely from partial or ambiguous execution.

### 2. Current context

- Read and follow `production-implementation.md`, `financial-tool-migration.md`, and `/docs/architecture-rules.md`. If `architecture-inst.md` exists, read and follow it.
- Prompts 1–6 have delivered a durable switch, assessment, staging/mapping, rehearsal/plan approval, internal candidates, and external transfer packages.
- `AccountingAuthorityService.StartChangeAsync` currently ends the source authority period and creates a migration authority period at the selected boundary. `CompleteCutoverAsync` changes it to the target when readiness booleans pass.
- `AccountingAuthorityPolicy` blocks native/provider authoritative posting and exports during migration while allowing reconciliation.
- The existing authority preview uses a signed preview token and expected version to prevent stale changes.
- Existing approval, audit, provider-write/outbox, and background-worker infrastructure must be reused.

### 3. Dependencies

- Prompts 1–6 completed for the applicable direction.
- A current approved plan, successful rehearsal, complete dispositions, approved mappings/exceptions, healthy required connections, and prepared target package or native candidates exist.

### 4. Implementation requirements

- Add a Finance-owned cutover coordinator and explicit policy for schedule, freeze, final extraction, final delta, transfer, reconciliation, activation request, activation, cancellation, pre-activation recovery, and corrective-cutover requirement.
- Keep the source authoritative until the effective boundary and final freeze. Recheck current authority, period, plan version, approvals, provider connection/scopes, open gaps, pending writes, and source freshness immediately before freeze.
- Enter the existing accounting authority `migration` state only for the affected period. Preserve the prior period's authority and history.
- Capture an immutable final source snapshot and calculate the delta from the approved rehearsal. If source activity appears during extraction, repeat the bounded delta or block; never ignore it.
- For external-to-internal, atomically materialize approved native candidates through governed Finance boundaries and activate internal authority only after final reconciliation and activation approval. Design the transaction/outbox boundary so a crash cannot leave untracked partial authority.
- For internal-to-external or provider-to-provider, execute only approved final provider batches, persist acknowledgements, reconcile ambiguous outcomes, and activate the target only after target confirmation and financial reconciliation.
- Create a separate activation approval bound to final snapshot, final totals, provider acknowledgements, exceptions, and switch version. Recheck it immediately before activation. Plan approval is insufficient.
- Derive `AccountingAuthorityPeriod` readiness from the persisted switch reconciliation evidence. Retain compatible legacy endpoints/read models but do not accept unsupported manual success claims for a switch-backed cutover.
- Commit authority-period completion, accounting configuration authority, switch activation, and required internal finalization atomically where they share the database. Use outbox/saga-style recovery for unavoidable provider/database boundaries.
- Implement cancellation before freeze, safe recovery after partial transfer but before target authoritative activity, and a mandatory corrective cutover after target authoritative activity. Never delete committed history or flip authority back blindly.
- Add status/readiness/recovery commands, API endpoints, audit events, and telemetry. Operator-visible state must identify the failed step, whether retry is safe, whether provider reconciliation is required, and the next action.

### 5. Constraints and preservation rules

- Follow production and architecture instructions. Backend policy is authoritative; UI and agents consume allowed actions.
- No controller, Blazor component, or LLM may orchestrate transactional cutover steps directly.
- Approval, authority, and connection state are checked at execution time, not only when queued.
- External side effects must be idempotent and durable. Ambiguous outcomes block activation until reconciled.
- Maintain tenant isolation, optimistic concurrency, audit completeness, safe logging, and provider-secret protection.
- Preserve historical authority periods and existing external references. Do not rewrite or redate committed records.
- Preserve local SQL Server and Docker SQL Server migration/restore/run compatibility.

### 6. Acceptance criteria

- Given a successful Fortnox-to-internal cutover, when final activation completes, then Fortnox is authoritative through the prior day, Virtual Company is authoritative from the selected monthly boundary, native candidates are committed once, and provider writes for the new period are blocked.
- Given a successful internal-to-Fortnox cutover, when activation completes, then local native posting for the new period is blocked, Fortnox authoritative writes are allowed, and earlier local periods remain unchanged.
- Given provider A-to-provider B, when cutover completes, then the authority period identifies B, A cannot create authoritative writes for that period through Virtual Company, and Virtual Company was never marked authoritative merely for transferring data.
- Given new source activity after the approved rehearsal or during final extraction, when cutover checks run, then activation is blocked or the bounded delta is processed and reconciled.
- Given an ambiguous provider outcome, stale approval, connection loss, unresolved financial check, or concurrent command, when activation is attempted, then authority does not change and an actionable recovery state is persisted.
- Given failure before authoritative target activity, when approved recovery runs, then the source remains/restores authority without deleting history; given target authoritative activity, rollback is refused and a corrective cutover is required.

### 7. Verification

- Add end-to-end Finance tests for all three directions and all migration strategies at monthly boundaries.
- Add failure-injection tests at every transition: before/after freeze, extraction, local commit, provider acceptance, local persistence, approval recheck, authority update, and worker restart.
- Add concurrency and idempotency tests proving exactly-once business effects under duplicate delivery.
- Add authority-policy tests before, during, and after cutover for native posting, provider writes, downstream exports, imports, and reconciliation.
- Add approval and tenant-isolation API tests, including cross-company IDs and stale activation snapshots.
- Add SQL Server integration tests for transactions, uniqueness, concurrency, migration schema, and recovery queries.
- Run current accounting authority, posting, manual journal, invoices, supplier bills, payments, provider exports, Fortnox, approval, audit, and historical migration regression suites; then run a full solution build.

### 8. Definition of done

The prompt is complete when all supported directions can execute an approved boundary cutover without dual authority; final deltas, provider ambiguity, crashes, concurrency, and approval changes are safely handled; recovery never destroys accounting history; authority and target records are consistent and auditable; and all tests/builds pass with no scaffolding, unsafe direct side effects, silent intermediate state, or deferred in-scope TODOs.

## Prompt 8 — Laura's migration tools and steered Finance-agent flow

### 1. Title and outcome

Give Laura, the Finance Manager agent, structured read, recommendation, and approval-backed execution tools that guide users through an existing accounting-system switch without allowing the agent to override financial controls.

The delivered value is an evidence-first conversational and task-driven migration assistant whose guidance always reflects current persisted workflow state.

### 2. Current context

- Read and follow `production-implementation.md`, `financial-tool-migration.md`, and `/docs/architecture-rules.md`. If `architecture-inst.md` exists, read and follow it.
- Prompts 1–7 have delivered the deterministic switch workflow and all Finance commands/queries.
- Shared agent orchestration contracts live under `VirtualCompany.Application/Agents`; `CompanyAgentToolExecutionService`, `PolicyGuardrailEngine`, `StaticCompanyToolRegistry`, and `InternalCompanyToolContract` implement current tool execution and policy boundaries.
- `LauraFinanceAgentSeedData` defines Laura as a guided Finance Manager with conservative objectives, finance data scopes, execute approval requirements, and financial-data-integrity escalation.
- Existing Finance tools include cash, transaction, invoice, categorization, approval recommendation, and paid-supplier-expense actions. There are no provider-switch tools.
- Architecture rules require named agents to use the shared orchestration stack, structured company-scoped tools, guardrails, approval, rationale, and audit evidence.

### 3. Dependencies

- Prompts 1–7 completed and their Finance application contracts stable.

### 4. Implementation requirements

- Add narrowly scoped Finance migration tool definitions with explicit JSON schemas, versions, action types, data scopes, sensitive-action classification, and bounded outputs.
- Provide read tools for switch status, provider capabilities, inventory, gaps, mappings, rehearsal, reconciliation, approvals, transfer progress, monitoring, and audit evidence.
- Provide recommendation tools for effective period, migration strategy, mapping proposals, gap resolution, required evidence, cutover plan, freeze window, monitoring period, and readiness explanation.
- Provide execute tools only for safe workflow commands such as starting assessment/rehearsal/preparation, applying already approved mappings, creating follow-up tasks through permitted contracts, requesting approvals, starting an already approved freeze/final extraction, recording system-produced evidence, and starting approved monitoring/recovery.
- Do not expose a tool that directly changes authority, marks reconciliation successful from free text, approves Laura's own request, supplies credentials, invents financial data, or retries ambiguous provider outcomes.
- Route every tool through the shared tool registry, `CompanyAgentToolExecutionService`, policy guardrails, membership resolution, and Finance Application contracts. Do not call EF Core, provider APIs, or LLM providers directly from the tool contract.
- Bind execution to company, switch ID, switch version, actor/agent, permission scope, correlation ID, and relevant plan/approval/snapshot version. Reject stale context.
- Add a structured migration briefing/read model for Laura containing plain-English current step, why it matters, blockers, evidence, allowed actions, responsible party, and next checkpoint. Keep raw storage values and sensitive provider errors out of agent-facing language.
- Update Laura's seeded capabilities conservatively and compatibly for existing companies. Use the established runtime profile resolution/backfill mechanism rather than relying only on new-company seed data.
- Persist tool request, guardrail result, Finance result, rationale summary, data sources, and audit evidence. Ensure denial and failure explanations are safe and actionable.
- Add task/escalation integration for financial integrity gaps without letting chat become the task or workflow system of record.

### 5. Constraints and preservation rules

- Follow production and architecture instructions. Use one shared AI orchestration system; do not create a migration chatbot or Finance-only LLM stack.
- Deterministic services decide readiness, severity, authorization, approval, and authority. Laura explains and recommends.
- Sensitive execute tools always require applicable backend approval regardless of prompt wording, autonomy configuration, or agent output.
- Do not broaden Laura to unrelated tools or data scopes. Preserve existing Finance tools and policy behavior.
- Never include credentials, raw provider payloads, hidden system instructions, or cross-company context in prompts, tool results, memory, logs, or audit.

### 6. Acceptance criteria

- Given a user asks Laura to move from Fortnox to Virtual Company, when no switch exists, then Laura can recommend intent fields and direct the user to create/review a draft but cannot silently activate authority.
- Given an active switch with blocking tax gaps, when Laura assesses readiness, then she cites current persisted evidence, explains the blockers, proposes next actions, and cannot mark them resolved.
- Given a stale switch or plan version, when Laura invokes an execute tool, then the guardrail/application service rejects it and returns a current-state recovery message.
- Given an ambiguous provider outcome, when Laura is asked to retry, then she directs the workflow to provider reconciliation instead of executing a blind retry.
- Given an approved plan but no activation approval, when Laura starts permitted preparation, then preparation may proceed but authority cannot change.
- Given a cross-company switch ID or a non-Laura agent without the tool permission, when a tool is invoked, then it is denied and audited without leaking record existence.

### 7. Verification

- Add registry/schema tests for every new tool name, version, action type, sensitive flag, input, and output.
- Add guardrail tests for read/recommend/execute distinctions, autonomy, approval, stale versions, company membership, and explicit denial.
- Add tool execution tests for successful Finance contract routing and all prohibited operations.
- Add agent briefing tests proving evidence grounding, plain-English output, bounded context, and no raw enums/secrets.
- Add seed/runtime-profile compatibility tests for new and existing Laura agents.
- Run existing agent execution policy, tool registry, Laura Finance, approval, audit, and accounting switch suites plus a focused solution build.

### 8. Definition of done

The prompt is complete when Laura can accurately guide and coordinate every switch stage through shared, structured, company-scoped tools; recommendations are evidence-backed; executions are stale-safe and approval-bound; prohibited powers are technically unavailable; existing agent behavior remains compatible; and all tests/builds pass with no prompt-only controls, separate orchestration stack, or deferred in-scope TODOs.

## Prompt 9 — Guided accounting migration workspace

### 1. Title and outcome

Build a production guided migration workspace in the Finance accounting connections experience, showing the authority timeline, switch progress, gaps, mappings, rehearsal, approvals, final cutover, reconciliation, monitoring, and contextual guidance from Laura.

The delivered value is a calm, action-oriented workflow that tells the user what is happening, what needs attention, and what to do next in every state.

### 2. Current context

- Read and follow `production-implementation.md`, `financial-tool-migration.md`, `/docs/architecture-rules.md`, `ui-instructions.md`, and `/docs/design.md`. If `architecture-inst.md` exists, read and follow it.
- Prompts 1–8 have delivered the complete backend switch workflow and Laura tool/read models.
- `AccountingConnectionsPage.razor`, `.razor.cs`, and `.razor.css` currently show accounting authority, an authority-change preview, provider cards, exports, a manual cutover checklist, and a static Laura card.
- `FinanceApiClient.AccountingAuthority.cs` owns existing authority endpoints. Add switch endpoint knowledge to the narrow matching typed client partial rather than a universal client.
- The existing Finance page layout, navigation, status badges, cards, spacing, typography, and design tokens must be reused.
- `AccountingAuthoritySurfaceTests` currently validates the authority route and core explanatory content.
- A reference image already exists for the older authority connections page, but this prompt is a significant redesign and requires a new screenshot-first reference.

### 3. Dependencies

- Prompts 1–8 completed.
- Backend API/read models expose allowed actions and plain-English policy explanations so the UI does not duplicate business decisions.

### 4. Implementation requirements

- Before UI code, explicitly write the reference screenshot prompt, generate a new reference image using the approved image model, and save it under `/docs/design/references/` with a descriptive name such as `accounting-migration-workspace-reference.png`.
- Inspect the generated image, implement against it, render the real page, compare the implementation with the reference, and iterate until layout, hierarchy, spacing, typography, states, and responsive behavior are visually close. Do not ship the reference image as UI content.
- Extend the existing accounting connections route or add a clearly linked Finance migration detail route without disrupting current compatibility routes.
- Present a step-based migration workflow with current/planned authority timeline, source/target health, strategy and boundary, progress, gap severity and ownership, mapping review, rehearsal comparison, approval state, cutover activity, reconciliation evidence, monitoring, and recovery.
- Use plain-English status labels. Never expose raw workflow states, reason codes, provider payloads, IDs, policy types, or technical exception text.
- Show backend-provided allowed actions and disable or hide actions consistently, but rely on backend authorization/policy for enforcement.
- Replace switch-backed manual reconciliation checkboxes with evidence-backed check results. Retain compatible legacy presentation only for authority changes that do not have a switch record.
- Provide list/detail behavior for gaps and mappings, including what the issue means, evidence used, financial impact, responsible person, resolution/approval status, and next action.
- Make plan approval and activation approval visibly distinct. Link to the existing Work/Approvals experience and show rejection, expiration, and ask-for-changes states.
- Add a contextual Laura panel tied to the persisted workflow step, showing her recommendation, why, data used, confidence where relevant, blockers, and safe next actions. Messaging Laura must not mutate workflow state by itself.
- Add loading, empty, blocked, stale, connection-lost, partial-provider-failure, ambiguous-outcome, cancellation, recovery, completed, and no-permission states.
- Use `ICompanyApiTransport` through typed Finance clients, preserve company/correlation context, cancellation, auth forwarding, not-found behavior, and safe error mapping.
- Add accessible semantics, keyboard navigation, focus behavior, labels, error summaries, contrast, and responsive layouts.
- Update localization resources for all new user-facing language, including Swedish where the existing Finance surface is localized.

### 5. Constraints and preservation rules

- Follow `ui-instructions.md` and `/docs/design.md`, including the mandatory screenshot-first workflow. Existing implemented design tokens/components win over older plans.
- Preserve the current app information architecture; do not add a retired or redundant primary navigation destination.
- Reuse existing components and patterns where they fit. Do not add a UI framework or a new visual style.
- Keep language calm, operational, and explicit about accounting consequences. Laura is a named Finance Manager, not a generic chatbot.
- UI contains no EF queries, authority decisions, gap severity logic, approval bypass, provider calls, or mock production data.
- Preserve current authority, export reconciliation, and provider-management paths.

### 6. Acceptance criteria

- Given no active switch, when an accounting administrator opens connections, then current authority and providers are clear and starting a guided migration is the primary relevant action.
- Given an active assessment with gaps, when the workspace loads, then the current step, blocker count, owners, evidence, and next action are visible without exposing raw reason codes.
- Given a Fortnox-to-internal switch, when internal readiness is incomplete, then the UI explains the missing configuration and links directly to the appropriate setup action.
- Given a plan awaiting approval, when viewed, then plan approval is clearly distinct from final activation approval and links to the correct approval detail.
- Given reconciliation-required provider outcome, when viewed, then the UI explains that success is uncertain and offers only backend-allowed reconciliation/recovery actions.
- Given Laura's panel, when the switch state changes or becomes stale, then guidance refreshes from current persisted evidence and does not claim an action completed prematurely.
- Given mobile or narrow viewport, keyboard use, empty data, errors, and denied permissions, when rendered, then the workflow remains usable, understandable, and accessible.

### 7. Verification

- Save and inspect the required reference screenshot before implementation.
- Add/extend Blazor component and presentation tests for every major state and user action.
- Add typed-client transport tests for routes, verbs, company headers, correlation, serialization, conflict/not-found/error mapping, and cancellation.
- Update `AccountingAuthoritySurfaceTests` or add a focused migration workspace test without brittle assertions that merely search arbitrary text.
- Run localization/resource validation and accessibility-oriented component checks.
- Build `VirtualCompany.Web` and relevant API projects.
- Follow the repository Local Web Verification rules. Reuse an existing repository host if appropriate; otherwise prefer static/build verification unless browser runtime validation is necessary. When runtime verification is used, launch and stop only the recorded process and capture screenshots of the real page for visual comparison.
- Compare the implemented page to `accounting-migration-workspace-reference.png` and refine material differences.

### 8. Definition of done

The prompt is complete when users can understand and act on every migration stage from a polished, accessible, responsive Finance workspace; Laura's guidance is contextual and evidence-backed; approvals, failures, reconciliation, and recovery are explicit; existing connection/export behavior remains available; screenshot-first and visual QA are complete; and all tests/builds pass with no mock UI data, raw internal language, silent state, or deferred in-scope TODOs.

## Prompt 10 — Monitoring, recovery, operations, and release proof

### 1. Title and outcome

Finish the accounting-system migration capability with post-activation monitoring, operational recovery, backup/restore compatibility, observability, documentation, and end-to-end release evidence for all supported directions.

The delivered value is an operable production capability that can detect and recover from real provider and accounting failures after activation rather than stopping at a successful status transition.

### 2. Current context

- Read and follow `production-implementation.md`, `financial-tool-migration.md`, and `/docs/architecture-rules.md`. If `architecture-inst.md` exists, read and follow it. For any UI corrections, also read and follow `ui-instructions.md` and `/docs/design.md`, including screenshot-first requirements when the change is significant.
- Prompts 1–9 have delivered the durable switch, assessment, staging/mapping, rehearsal, both transfer directions, activation/recovery, Laura tools, and guided Web workspace.
- Existing accounting operations include readiness, historical migration, cutover reports, telemetry, recovery verification, background workers, provider reconciliation, audit, and SQL Server migrations.
- Repository database rules require equivalent local SQL Server and Docker SQL Server restore/run paths.
- Existing authority, Fortnox, posting, reporting, approval, agent, audit, and Web behavior must remain compatible.

### 3. Dependencies

- Prompts 1–9 completed with no unresolved critical implementation gaps.
- External real-provider verification may require credentials and must remain separately categorized; lack of credentials does not justify mock production behavior or skipping all adapter contract verification.

### 4. Implementation requirements

- Add durable post-activation monitoring with a configurable 7–30 day window, scheduled checks, current status, last-success time, next run, failures, assigned owner, and closure decision.
- Monitor provider write/sync health, internal projection differences, missing/duplicate invoices, unmapped records, connection/scopes, bank reconciliation, unexpected former-authority posting attempts, tax/currency/control-account variances, unresolved external outcomes, and archive availability required by accepted exceptions.
- Reuse background scheduling/worker patterns with bounded batches, leases, duplicate safety, retry classification, and operator-visible failure state.
- Create Finance tasks/escalations and Laura briefings for material discrepancies. Do not treat notification delivery as the workflow system of record.
- Implement explicit closure policy and approval where required. A switch closes only after the monitoring window and required checks pass or authorized users accept documented non-blocking exceptions.
- Complete recovery tools for safe retry, provider reconciliation, credential reconnection, resume after worker failure, cancel before activation, restore source before target activity, and create a corrective cutover after target activity.
- Add operational dashboards/read models for stuck workflows, expired approvals, stale freezes, exhausted retries, ambiguous outcomes, unreconciled totals, and cross-component health.
- Add metrics and structured logs for stage duration, queue age, attempts, gap counts, reconciliation failures, activation outcomes, monitoring violations, and recovery. Keep business audit history separate from technical logs and never log secrets.
- Verify database migration ordering, model snapshot, indexes, concurrency, data retention, and cleanup/archival behavior. Do not delete accounting evidence required for audit or rollback decisions.
- Update operational documentation for connection requirements, cutover runbook, failure classes, reconciliation, recovery, provider archive retention, support escalation, and safe rollback boundaries.
- Verify backup and restore includes switch state, staged metadata, mappings, approvals, audit, source documents, external references, and required object content. Keep `restore-local-sql-db.ps1` and `restore-virtualcompany-db.ps1` compatible.
- Close defects found during end-to-end verification. Do not stop after only reporting failures that are in scope to fix.

### 5. Constraints and preservation rules

- Follow production and architecture instructions. No new microservice, orchestration stack, approval system, outbox, or accounting ledger.
- Monitoring is tenant-scoped and read-oriented; corrective actions still require policy and approval.
- Do not automatically reverse authority after activation. Once target-authoritative activity exists, require reconciliation and a new corrective cutover.
- Do not hide unsupported provider behavior, unavailable archive data, or accepted financial exceptions.
- Preserve existing data, APIs, routes, provider connections, historical authority, and Docker/local SQL Server compatibility.
- Real external integration tests must be opt-in and safe; normal tests must remain deterministic without depending on live provider credentials.

### 6. Acceptance criteria

- Given an activated switch, when the monitoring scheduler runs repeatedly, then checks are idempotent, progress is persisted, and no duplicate tasks or alerts are created.
- Given a provider permission change, reconciliation variance, unknown outcome, or former-authority posting attempt, when detected, then a safe visible incident and required task/escalation are created and closure is blocked when policy requires it.
- Given all monitoring checks pass through the configured window, when closure is requested, then the switch closes with an auditable result.
- Given non-blocking exceptions remain, when closure is approved, then each exception retains actor, evidence, explanation, scope, and financial impact.
- Given a backup taken after an active or completed migration, when restored locally and through the Docker SQL Server path, then authority, switch state, approvals, mappings, evidence, external references, and documents remain coherent.
- Given internal-to-Fortnox, Fortnox-to-internal, and external-to-external scenarios, when run end to end with injected failures, then no scenario permits dual-authoritative posting, duplicate business effects, silent data loss, or destructive rollback.

### 7. Verification

- Add monitoring scheduler/worker tests for leases, duplicate delivery, retries, escalation deduplication, closure, and recovery.
- Add operator read-model/API tests for stuck, expired, ambiguous, and unreconciled states with authorization and tenant isolation.
- Run end-to-end automated scenarios for all directions, strategies, cancellation points, failure categories, and corrective cutover behavior.
- Run existing Finance, API, Web, agent, approval, audit, provider, reporting, ledger, payment, bill, invoice, accounting operations, migration, and authority test suites.
- Run SQL Server migration validation and a full solution build.
- Prove both local SQL Server and Docker SQL Server backup/restore flows using documented commands and verify restored accounting/document consistency.
- Run opt-in Fortnox integration verification when credentials are available; otherwise record the exact external verification still required without weakening deterministic adapter coverage.
- Perform final browser verification of the migration workspace and operator-visible failure/recovery states according to repository process rules.

### 8. Definition of done

The prompt and implementation sequence are complete only when all migration directions are production-operable; post-activation drift and failures are visible and recoverable; authority, totals, evidence, approvals, agent actions, and external references remain coherent across backup/restore; local and Docker SQL Server paths are proven; all in-scope critical/high defects are fixed; and all required tests/builds pass. No scaffolding, mock production data, silent fallback, unhandled intermediate state, unresolved in-scope critical gap, destructive rollback, or deferred in-scope TODO remains.
