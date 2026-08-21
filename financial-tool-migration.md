# Financial Tool Migration

## Purpose

This document defines the approach for moving a company's authoritative accounting between Virtual Company's built-in accounting application and an external accounting provider such as Fortnox. It also covers switching between two external providers.

The objective is to make the process guided and understandable without weakening accounting integrity. Laura, the Finance Manager agent, may assess, explain, recommend, coordinate, and monitor the migration. Deterministic Finance policies, persisted evidence, approval workflows, and reconciliation decide whether a cutover may advance.

The supported directions are:

- Virtual Company to an external provider.
- An external provider to Virtual Company.
- One external provider to another external provider.

## Current foundation

Virtual Company already persists accounting authority by accounting period and supports `internal_ledger`, `external_provider`, and bounded `migration` states. The authority policy permits only the operation appropriate to the authoritative system:

- Virtual Company can create authoritative postings during an internal-ledger period.
- The selected provider can create authoritative postings during an external-provider period.
- Internal committed journals may be exported downstream while Virtual Company remains authoritative.
- External data may be imported as a read projection while the provider is authoritative.
- Normal posting and exports are paused during an authority cutover, while reconciliation operations remain available.

The existing accounting connections workspace provides an authority timeline, a change preview, provider connection status, export reconciliation, and a manually maintained cutover-readiness checklist. This is the correct safety foundation, but it is not yet a complete migration experience.

## Core invariants

The following rules apply in every direction:

1. For every company, accounting date, and business event, exactly one accounting system may create the authoritative posting.
2. A migration takes effect only at the start of an existing monthly accounting period.
3. The source remains authoritative while assessment, mapping, rehearsal, and plan approval are underway.
4. Authority does not change until final extraction, reconciliation, and activation approval have completed.
5. Provider data is normalized before it reaches Finance domain behavior. Core entities do not contain provider-specific schemas.
6. Imported provider records enter staging first. They do not become committed ledger records directly.
7. External writes use durable background execution, stable idempotency, bounded retries, and explicit reconciliation of ambiguous outcomes.
8. Committed history is not rewritten or deleted to implement a migration or rollback.
9. AI recommendations never replace accounting policy, authorization, approval, or reconciliation evidence.
10. Every material decision and transition is tenant-scoped, authorized, versioned, and audited.

## Default migration strategy

The recommended default is **opening balances plus open items at the next monthly boundary**.

Under this strategy:

- The source remains the authoritative archive for earlier periods.
- The target receives verified opening balances.
- Unpaid customer and supplier invoices, credit notes, payment allocations, and required master data are transferred.
- Provider identities and source references are preserved for traceability and duplicate prevention.
- Historical transaction detail is transferred only when required.

The user may instead choose:

### Current fiscal-year detail

Transfer journals and supporting evidence from the beginning of the current fiscal year. Every transferred period must independently reconcile. This option requires stronger duplicate detection, voucher-number mapping, tax validation, and evidence coverage.

### Full history

Transfer all available accounting periods and evidence. This should be used only when legal, contractual, reporting, or archive requirements justify the additional work and risk.

The selected strategy changes which data gaps are blocking. For example, a missing historical attachment may be an accepted exception when the source remains an accessible archive, but it may block a full-history replacement.

## Durable migration workflow

The provider switch must be a persisted workflow rather than chat state or Blazor component state.

```text
Draft
  -> Assessing
  -> Gaps found / Ready for planning
  -> Plan awaiting approval
  -> Preparing target
  -> Rehearsal passed
  -> Scheduled
  -> Final source freeze
  -> Reconciling
  -> Activation awaiting approval
  -> Target authoritative
  -> Monitoring
  -> Closed
```

Any active stage may move to `Blocked`, with a stable reason code, responsible party, recovery action, and supporting evidence. Before activation, the migration may be cancelled and the source remains authoritative. Once authoritative target postings exist, returning to the source requires another controlled cutover rather than a database toggle.

## Common end-to-end process

### 1. Capture intent

Collect and persist:

- Source and target systems.
- Requested effective accounting period.
- Business reason.
- Migration strategy and historical depth.
- Reporting, tax, archive, and evidence requirements.
- Required participants and approvers.
- Expected posting-freeze window.
- Post-activation monitoring period.

The user may begin this process in the accounting connections workspace or by asking Laura. In both cases, a durable migration record is created.

### 2. Connect and inspect both systems

The user completes external-provider authorization interactively. Laura must not ask for, read, store, or transmit credentials.

The system verifies:

- Connection health.
- Granted scopes.
- Provider tenant or company identity.
- Available accounting periods.
- Provider capabilities and API limitations.
- Last successful synchronization.
- Pending, failed, or ambiguous external operations.

### 3. Build a migration inventory

The assessment inventories, as applicable:

- Fiscal years, periods, and lock state.
- Chart of accounts and account roles.
- Tax codes, tax rules, and tax balances.
- Voucher series and numbering constraints.
- Customers and suppliers.
- Customer invoices, supplier invoices, and credit notes.
- Open receivables and payables.
- Payments, allocations, and unapplied cash.
- Bank accounts, ledger balances, and reconciliation state.
- Currencies and exchange rates.
- Projects, cost centres, and other dimensions.
- Journals, corrections, and source relationships.
- Attachments and supporting documents.
- Provider references and synchronization state.
- Features present in the source but unsupported by the target.

The inventory distinguishes between data confirmed not to exist and data that the provider could not supply.

### 4. Produce a gap report

Every gap contains:

- Stable reason code.
- Category and severity.
- Blocking or non-blocking status.
- Affected dataset and record identifiers.
- Expected and observed values.
- Evidence source and extraction timestamp.
- Proposed resolution.
- Responsible person or agent.
- Resolution status and approval, when applicable.

Gap severity is deterministic. Laura may explain severity but does not decide whether a policy-defined blocking condition can be ignored.

### 5. Prepare mappings and transformations

Provider data is converted into neutral Finance contracts. Mapping decisions cover accounts, tax codes, dimensions, counterparties, currencies, numbering, document relationships, and payment allocations.

Every staged item receives one explicit disposition:

- Ready to import.
- Mapped.
- Transformed.
- Represented in opening balance.
- Duplicate.
- Excluded with approval.
- Missing from source.
- Unsupported by target.
- Conflicting.
- Awaiting evidence.
- Blocked.

AI uncertainty must never be converted into an invented account, tax treatment, exchange rate, allocation, or balancing amount.

### 6. Run a rehearsal

Run a non-authoritative migration into a provider sandbox, provider preview API, or persisted local staging simulation. A rehearsal produces:

- Records inspected, accepted, rejected, mapped, and excluded.
- Expected opening balances.
- Trial balance comparisons by account and currency.
- Receivable and payable control-account comparisons.
- Open-item comparisons.
- Tax-balance comparisons.
- Bank and reconciliation comparisons.
- Missing evidence.
- Proposed adjustments.
- Provider validation failures.
- Estimated final cutover duration.

Blocking gaps must be resolved before the plan can be approved.

### 7. Approve and schedule

The approval package shows:

- Source, target, and effective period.
- Migration strategy.
- Dataset counts and financial totals.
- Mapping decisions.
- Resolved gaps and accepted exceptions.
- Rehearsal results.
- Freeze window.
- Recovery and rollback boundary.
- Responsible participants.
- Laura's recommendation and its evidence.

An accounting administrator approves the plan. Higher-risk migrations may require a second approver or external accountant. Laura cannot approve her own recommendation.

### 8. Execute the final cutover

At the approved boundary:

1. Confirm connection and synchronization health.
2. Freeze authoritative creation for the target period.
3. Capture a final source snapshot.
4. Read or calculate the final delta since rehearsal.
5. Verify that no unaccounted source activity occurred during extraction.
6. Transfer the approved datasets using durable, idempotent jobs.
7. Persist provider acknowledgements and external identifiers.
8. Route ambiguous outcomes to reconciliation instead of retrying blindly.
9. Recalculate all financial controls.
10. Request final activation approval.

### 9. Reconcile and activate

Activation is blocked until deterministic reconciliation succeeds and required approvals are present. The authority-period change and final migration state transition are committed atomically.

### 10. Monitor and close

For a configurable period, normally 7 to 30 days, monitor:

- Failed or delayed provider operations.
- Differences between target data and internal projections.
- Duplicate or missing invoices.
- Unmapped accounts or counterparties.
- Changed provider permissions.
- Bank reconciliation discrepancies.
- Unexpected posting attempts in the former authority.
- Tax, currency, or control-account variances.

Laura provides concise daily summaries and immediately escalates blocking discrepancies. The migration closes only when monitoring succeeds or an authorized user accepts documented non-blocking exceptions.

## Virtual Company to an external provider

Example: Virtual Company remains authoritative through 31 August and Fortnox becomes authoritative on 1 September.

The target normally receives:

- Account and tax mappings.
- Required customers and suppliers.
- Opening balances.
- Open receivables and payables.
- Unapplied payments and credits.
- Required dimensions.
- Source documents supported by the provider.
- Optional historical journals according to the selected strategy.

The preview must block a boundary where committed local journals already exist unless they are explicitly included and reconciled. Pending provider exports and unknown outcomes must be completed or reconciled. Historical records without provider references must be reported and dispositioned.

After activation:

- The external provider creates authoritative records for the new period.
- Virtual Company blocks native authoritative posting for that period.
- Provider activity may be imported into Virtual Company as a read projection.
- Earlier Virtual Company periods remain authoritative and immutable.
- Duplicate downstream export from the old local ledger is prohibited.

## External provider to Virtual Company

Example: Fortnox remains authoritative through 31 August and Virtual Company becomes authoritative on 1 September.

### Internal readiness

Before the migration can proceed, Virtual Company must have:

- A configured fiscal year and monthly periods.
- A base currency and permitted transaction currencies.
- A chart of accounts with required account roles.
- Tax configuration and posting rules.
- Voucher series and numbering policy.
- Customer and supplier control accounts.
- Bank and payment accounts.
- Required dimensions.
- A validated accounting policy pack, or a clear disclosure that country-specific compliance is not configured.

An external connection alone is not sufficient to make the internal ledger ready.

### Inbound staging

External data is read into normalized staging. It must not be copied directly into posted ledger entities. Staging retains:

- Provider identity and external record ID.
- Provider version or modification timestamp.
- Extraction batch and timestamp.
- Normalized business data.
- Payload hash or equivalent integrity evidence.
- Mapping and validation status.
- Supporting document references.
- Final import disposition.

Approved opening-balance or historical journals are created through the governed native posting boundary. They must satisfy balance, period, account, tax, currency, evidence, authorization, and idempotency rules just like any other native accounting record.

After activation:

- Virtual Company creates authoritative postings for the new period.
- Provider-authoritative writes are blocked for that period.
- The external provider remains a historical archive or optional downstream destination.
- Imported external records cannot replace or overwrite committed internal records.

## External provider to external provider

Provider-to-provider migrations always pass through normalized Virtual Company contracts:

```text
Source provider
  -> normalized staging
  -> mapping and reconciliation
  -> target provider adapter
  -> target confirmation and reconciliation
```

A source-provider payload must not be translated directly into a target-provider payload. The normalized intermediate representation supplies consistent validation, auditability, idempotency, and provider independence.

Virtual Company does not become authoritative merely because it performs the transfer. The source remains authoritative until activation, after which the target becomes authoritative.

## Financial data gaps and resolution

| Gap category | Example | Required handling |
|---|---|---|
| Aggregate mismatch | Source trial balance is higher than the target result | Blocking. Reconcile before activation. |
| Account mapping | A provider account has no internal or target equivalent | Laura proposes a mapping; an authorized reviewer approves material or ambiguous mappings. |
| Tax treatment | A source tax code has no validated target rule | Blocking until a validated treatment is selected. |
| Open-item mismatch | The payables control balance includes invoices absent from the provider response | Blocking. Retrieve, reconstruct from evidence, or use an explicitly approved opening-balance exception. |
| Payment allocation | A payment exists without its invoice allocation | Import as unapplied cash or resolve the allocation before activation. |
| Currency gap | A required historical exchange rate is unavailable | Retrieve an authoritative rate or approve a documented conversion adjustment. |
| Dimension gap | A source project cannot be represented in the target | Map, preserve as metadata, or approve omission according to reporting requirements. |
| Document gap | A journal has no invoice or receipt | Create an evidence task; blocking status depends on policy and migration strategy. |
| Numbering conflict | An imported voucher number already exists | Preserve the source number as an external reference and allocate a valid target identity. |
| Duplicate | A record exists through both synchronization and migration | Match using provider identity, source identity, version, financial values, and hashes; never import twice. |
| Unknown outcome | A provider request timed out after transmission | Reconcile with the provider before retrying or proceeding. |
| Timing gap | A source posting was added after final extraction | Run a final delta and block activation until the source snapshot is stable. |
| Locked-period difference | A source period is locked while the target is open | Apply an equivalent target restriction or require an approved exception. |
| Unsupported capability | The target does not support a source accounting feature | Retain an accessible archive, integrate a specialist system, narrow the migration scope, or block replacement. |

## Required reconciliation evidence

For every transferred period or opening-balance cutover, the system calculates and persists:

- Total debits equal total credits.
- Trial balance agrees by account and currency.
- Receivables control accounts agree with open customer items.
- Payables control accounts agree with open supplier items.
- Tax control accounts agree with tax detail.
- Bank ledger balances agree with imported reconciliation state.
- Opening equity and retained earnings are accounted for.
- Every imported journal has a stable source identity.
- Every in-scope source record has exactly one migration disposition.
- No duplicate source identities exist in the target.
- No business event is authoritative in both systems.
- No external write has an unresolved outcome.
- Final extraction occurred after the last included source change.
- Required evidence is present or covered by an explicitly approved exception.

Manual confirmation is permitted only where a check cannot be calculated. It requires an authorized actor, explanation, evidence reference, timestamp, and audit event. A free-form checkbox is not sufficient evidence for a material reconciliation assertion.

## Laura-supported and steered experience

Laura coordinates the migration through structured workflow checkpoints. Chat is an interface to the workflow, not its system of record.

### Laura can autonomously read

- Provider connection and capability status.
- Accounting authority and available cutover periods.
- Migration inventory and gap reports.
- Trial balances and open-item totals.
- Mapping status.
- Rehearsal and reconciliation results.
- Provider execution, retry, and reconciliation state.
- Audit and approval status relevant to the migration.

### Laura can recommend

- A safe effective accounting period.
- An appropriate migration strategy.
- Account, tax, counterparty, and dimension mappings.
- Resolutions for identified conflicts.
- Required evidence and responsible participants.
- A cutover plan, freeze window, and monitoring duration.
- Whether the migration is ready for plan or activation approval.

Recommendations show confidence, evidence, materiality, and the deterministic policy result. They remain reviewable proposals.

### Laura can execute only through governed tools

With applicable backend authorization and approval, Laura may:

- Start a read-only assessment.
- Queue a rehearsal.
- Apply approved mappings.
- Queue approved migration batches.
- Create tasks for unresolved gaps.
- Request plan approval.
- Request activation approval.
- Start an approved freeze or final extraction.
- Record system-produced reconciliation evidence.
- Start approved monitoring or recovery operations.

Laura may not:

- Handle provider credentials.
- Approve her own recommendation.
- Override a blocking policy result.
- Mark a reconciliation successful without evidence.
- Change accounting authority directly.
- Invent missing financial data.
- Retry an ambiguous provider outcome blindly.
- Create authoritative postings in both systems for the same event and period.
- Delete committed history to implement rollback.

### Guided conversation checkpoints

Laura's conversation follows the persisted workflow:

1. **Intent:** confirm target, effective period, purpose, and migration scope.
2. **Assessment:** explain available data, provider limitations, and material gaps.
3. **Strategy:** recommend opening balances, current-year detail, or full history.
4. **Mappings:** present proposed mappings and route ambiguous decisions for review.
5. **Rehearsal:** summarize counts, balances, exceptions, and readiness.
6. **Plan approval:** present the immutable approval package.
7. **Cutover:** report progress without claiming success before provider confirmation.
8. **Activation approval:** show final reconciliation evidence and remaining exceptions.
9. **Monitoring:** provide daily summaries and escalate anomalies.

Each agent action is bound to the company, migration ID, workflow version, actor, permissions, correlation ID, and applicable preview or approval version. Stale recommendations or approvals must be rejected and regenerated.

## Approval model

At minimum, require:

- Accounting-administrator authorization to create and manage a migration.
- Human approval of the migration strategy and cutover plan.
- Human approval of material or ambiguous mappings and exceptions.
- Separate final approval to activate the target authority.

Configuration may require separation of duties, a second approver, or an external accountant based on materiality, migration scope, tax risk, or company policy. Approval is rechecked immediately before any consequential external write or authority activation.

## Provider capability model

Each external provider adapter exposes a capability profile for migration planning, including support for:

- Read and write access by dataset.
- Incremental extraction and modification timestamps.
- Sandboxes or preview validation.
- Opening-balance import.
- Historical journals.
- Customers, suppliers, invoices, credits, and payments.
- Tax codes and dimensions.
- Currencies and exchange rates.
- Attachments.
- Period locks.
- Stable external identifiers and idempotency.
- Webhooks, polling, rate limits, and reconciliation lookup.

Unsupported or partially supported capabilities become explicit migration gaps. Provider adapters own provider authentication, endpoints, schemas, payload mapping, and error translation.

## Persistence and service boundaries

Preserve `AccountingAuthorityPeriod` as the authoritative period timeline. Add a separate durable switch aggregate, conceptually `AccountingProviderSwitch`, with:

- Source and target endpoint.
- Effective fiscal period.
- Migration strategy.
- Workflow status and version.
- Assessment and plan versions.
- Freeze, activation, monitoring, cancellation, and completion timestamps.
- Plan and activation approval references.
- Rehearsal and final execution references.
- Responsible users and agent.
- Recovery state and safe failure summary.

Direction is derived from generic endpoints:

```text
Source: internal | external(provider key)
Target: internal | external(provider key)
```

Supporting persisted concepts may include:

- Migration dataset and extraction.
- Gap and resolution.
- Mapping decision.
- Snapshot and checkpoint.
- Reconciliation result and evidence.
- Accepted exception.

Reuse the existing accounting migration conflicts, cutover reports, provider exports, external references, approval infrastructure, task workflow, audit infrastructure, and durable execution mechanisms wherever their semantics match. Do not create parallel implementations of the ledger, approval system, outbox, agent orchestration, or provider connection lifecycle.

Application contracts and orchestration belong to Finance. Provider-specific behavior belongs to Finance provider adapters. The shared agent orchestration system exposes narrow, structured, permissioned tools that invoke Finance application contracts. Laura does not query EF Core or call provider APIs directly.

## Rollback and recovery

Rollback behavior depends on the activation point:

### Before activation

Cancel the migration, stop queued migration work, and discard or retain staged records according to retention policy. The source remains authoritative and no accounting history changes.

### After transfer but before authoritative target activity

An approved recovery action may restore the source authority using the persisted pre-cutover snapshot, provided the system proves that no authoritative target posting was accepted.

### After authoritative target activity

Do not toggle authority back or delete the new activity. Reconcile all target activity and schedule a new controlled cutover at a valid accounting-period boundary.

Recovery must distinguish validation failures, authorization failures, stale credentials, missing scopes, provider rejection, rate limiting, transport failure, unknown provider outcome, and provider-success/local-persistence failure. Only retryable outcomes receive bounded automatic retries.

## User experience

Extend the accounting connections workspace into a migration workspace with:

- Current and planned authority timeline.
- Source and target connection health.
- Migration strategy and effective period.
- Step-by-step progress.
- Gap counts by severity and responsible party.
- Mapping review.
- Rehearsal comparison.
- Approval status.
- Cutover activity and provider acknowledgements.
- Reconciliation evidence.
- Monitoring and recovery state.
- A contextual Laura panel tied to the active workflow step.

User-facing language must describe business consequences. It should not expose storage values, raw provider errors, internal workflow names, or technical identifiers.

## Delivery approach

### Phase 1: Durable switch workflow

- Add the provider-switch aggregate and explicit state policy.
- Connect it to accounting authority, approvals, tasks, audit, and authorization.
- Support cancellation, blocking reasons, concurrency, and recovery state.
- Preserve existing authority-period behavior and routes during rollout.

### Phase 2: Assessment and capability contracts

- Add provider capability profiles.
- Add normalized inventory contracts.
- Implement deterministic gap detection and severity.
- Cover internal-to-external, external-to-internal, and external-to-external assessment.

### Phase 3: Mapping and staging

- Add versioned mapping decisions and inbound staging.
- Add duplicate detection and stable source identities.
- Add strategy-dependent dataset requirements.
- Ensure imported records use governed Finance commands.

### Phase 4: Rehearsal and reconciliation

- Add rehearsal execution and immutable snapshots.
- Calculate financial controls and evidence-backed readiness.
- Replace manual readiness assertions where the result can be calculated.
- Retain manual exceptions only through authorized, evidenced decisions.

### Phase 5: Final transfer and activation

- Add final-delta extraction, freeze handling, durable transfer, and atomic activation.
- Add provider ambiguity reconciliation and safe operator recovery.
- Prove that dual-authoritative posting cannot occur.

### Phase 6: Laura-guided workflow

- Add read and recommendation tools first.
- Add workflow-aware conversations and task creation.
- Add approval-backed execution tools only after deterministic services exist.
- Persist rationale, evidence references, tool executions, and outcomes.

### Phase 7: Monitoring and operational hardening

- Add post-activation comparisons and alerts.
- Add rollback eligibility and corrective-cutover workflows.
- Add telemetry, operator views, recovery documentation, and release evidence.

## Verification expectations

Verification must cover:

- Internal-to-external, external-to-internal, and external-to-external migrations.
- Opening-balance, current-year, and full-history strategies.
- Tenant isolation and cross-company denial.
- Accounting-administrator and approval enforcement.
- Separation of plan and activation approvals.
- Authority enforcement before, during, and after cutover.
- Concurrent and stale workflow commands.
- Duplicate delivery and idempotent retry.
- Missing scope, stale credential, rate limit, rejection, timeout, and ambiguous provider outcomes.
- Provider-success/local-persistence failure.
- Account, tax, currency, dimension, open-item, evidence, and aggregate mismatches.
- Source activity arriving during final extraction.
- Cancellation before activation and recovery after partial transfer.
- Prohibition of destructive rollback after authoritative target activity.
- Agent permission, grounding, approval, and stale-context guardrails.
- Audit completeness and safe logging.
- SQL Server migrations and equivalent local and Docker restore/run paths for schema changes.
- Focused Finance tests, API integration tests, Web tests, and browser verification of the guided workspace.

## Definition of success

A migration is complete only when:

- Exactly one system is authoritative for every affected accounting period.
- All in-scope source records have a recorded disposition.
- Financial totals reconcile according to the approved migration strategy.
- Blocking gaps and unknown provider outcomes are resolved.
- Required evidence and approvals are persisted.
- The target can continue accounting independently from the activation boundary.
- The source history remains accessible according to the approved archive strategy.
- Failures and accepted exceptions are visible and recoverable.
- Laura's statements and recommendations are grounded in current workflow evidence.
- No scaffolding, mock production data, silent fallback, duplicate authoritative posting, or unresolved in-scope critical gap remains.
