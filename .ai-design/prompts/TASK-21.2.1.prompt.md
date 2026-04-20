# Goal
Implement backlog task **TASK-21.2.1**: add a deterministic **finance generation policy** that converts simulated day progression into realistic finance activity for the **active company**, writing into the **existing finance domain tables/services/workflows** rather than any mock-only store.

The implementation must ensure that, when generation is enabled and a simulated day advances, the system deterministically creates or updates:
- invoices
- bills
- transactions
- balances
- recurring expense instances
- payment status changes
- workflow tasks
- approval requests where policy requires
- finance history/audit events
- anomaly/review signals surfaced through existing finance review workflows

The implementation must satisfy deterministic replay behavior:
- same seed + same configuration + same simulated dates => same records, anomaly days, and workflow outcomes

# Scope
In scope:
- Find the existing simulated day advancement entry point and finance workflow/services.
- Add a finance generation policy/service that runs on simulated day advancement for the active company.
- Use deterministic seeded generation, scoped by company and simulated date.
- Persist outputs into existing finance tables/services and approval/history/review workflows.
- Cover scenario generation across a deterministic run for:
  - invoice states and payment outcomes
  - approval threshold edge cases
  - anomaly injection cadence and anomaly types
- Add/update tests proving deterministic behavior and workflow visibility.
- Keep implementation tenant-safe and idempotent for repeated processing of the same company/date.

Out of scope:
- New mock-only storage paths
- Replacing existing finance workflows/UI
- Broad schema redesign unless strictly required
- Non-deterministic/random generation without seeded control
- Mobile/web UX changes beyond what is required for existing workflows to surface generated data

# Files to touch
Inspect and update the actual files that implement these concerns; likely candidates include:

- `src/VirtualCompany.Domain/**`
  - finance entities/value objects/enums
  - simulation/day progression domain contracts
  - approval/anomaly/history domain models if shared
- `src/VirtualCompany.Application/**`
  - simulated day advancement handlers/services
  - finance generation orchestration service
  - deterministic policy interfaces and implementations
  - task/workflow/approval command handlers
  - finance history/audit event writers
- `src/VirtualCompany.Infrastructure/**`
  - persistence/repositories for finance entities
  - EF Core configurations/mappings
  - deterministic seed/config persistence if needed
  - background/job wiring if day advancement is worker-driven
- `src/VirtualCompany.Api/**`
  - DI registration
  - any API endpoints involved in simulation advancement/configuration
- `src/VirtualCompany.Shared/**`
  - shared contracts/DTOs only if already used by finance simulation/config
- `src/VirtualCompany.Web/**`
  - only if existing review/approval/history screens require contract updates to display generated records already supported by backend
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for simulated day advancement and workflow visibility
- `tests/**` or other test projects present in repo
  - unit tests for deterministic generation policy
  - application tests for idempotency and approval/anomaly outcomes

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
for migration/testing conventions if schema changes are necessary.

# Implementation plan
1. **Discover existing finance and simulation architecture**
   - Locate:
     - simulated day progression entry point
     - finance domain services/tables
     - approval workflow integration
     - anomaly/review/history workflow integration
     - any existing mock finance generator or placeholder store
   - Identify the canonical write path for finance records and reuse it.
   - Do not bypass domain/application services with direct DB writes unless the codebase already uses repository-based application writes for these entities.

2. **Define deterministic generation contract**
   - Introduce a clear application/domain contract, e.g.:
     - `IFinanceGenerationPolicy`
     - `IFinanceScenarioPlanner`
     - `IFinanceAnomalyInjector`
     - `IFinanceDeterministicRandom` or equivalent seeded helper
   - Inputs should include at minimum:
     - `companyId`
     - simulated date/day index
     - company finance configuration
     - seed/configuration
     - current finance state needed for progression
   - Outputs should be structured planned actions/records, not ad hoc side effects.

3. **Implement deterministic seeded planner**
   - Seed derivation must be stable and replayable, e.g. based on:
     - global/company seed
     - company id
     - simulated date/day number
     - scenario stream/category
   - Ensure deterministic ordering before generation:
     - sort source records/configs by stable keys
     - avoid iteration over unordered dictionaries/sets
   - The planner should map simulated day progression into realistic outputs for:
     - invoices
     - bills
     - transactions
     - balances
     - recurring expense instances
     - payment status transitions
     - workflow tasks
     - approval requests
     - finance history events

4. **Model scenario coverage across a deterministic run**
   - Ensure the generator produces, across a run, invoice scenarios covering:
     - pending invoice over approval threshold
     - different approval currency
     - partial payment
     - full payment
     - over-payment
     - due soon
     - overdue
     - normal low-risk pending
   - Ensure policy threshold scenarios cover:
     - amount just below threshold
     - exactly at threshold
     - just above threshold
     - requiring human approval
     - eligible without escalation
     - already approved or no longer actionable
   - Implement this as a deterministic scenario schedule/rotation, not probabilistic hope-based coverage.

5. **Implement anomaly cadence and anomaly types**
   - Inject anomalies periodically, not every simulated day.
   - Use a deterministic cadence/schedule derived from seed/config/date.
   - Cover anomaly types:
     - duplicate vendor charge
     - unusually high amount
     - category mismatch
     - missing document
     - suspicious payment timing
     - multiple payments
     - payment before expected state transition
   - Anomalies must be attached to real generated finance records and surfaced through existing review/anomaly/history workflows.

6. **Write through existing finance services/workflows**
   - Replace or bypass any mock-only persistence path.
   - On simulated day advancement, create/update records for the active company in existing finance tables/services.
   - Reuse existing commands/services for:
     - invoice/bill creation
     - transaction posting
     - balance updates
     - recurring expense instantiation
     - approval request creation
     - workflow task creation
     - history/audit event creation
   - If no reusable service exists for a required record type, add one in the application layer consistent with current architecture.

7. **Ensure idempotency for same company/date**
   - Reprocessing the same simulated day must not duplicate records unexpectedly.
   - Introduce stable external/business keys or generation keys where needed, e.g. based on:
     - company id
     - simulated date
     - scenario id
     - record type
   - Prefer upsert/update semantics for generated records.
   - Preserve deterministic workflow outcomes when rerun.

8. **Integrate approval logic**
   - For generated records crossing policy thresholds, create approval requests using the existing approval module.
   - Support:
     - over-threshold pending approval
     - different approval currency
     - already approved / no longer actionable scenarios
   - Ensure generated approval requests are visible in existing approval inbox/workflows.

9. **Integrate finance history and review visibility**
   - Every generated or transitioned finance event should create appropriate history/audit entries through existing mechanisms.
   - Generated anomalies/review signals must appear in existing:
     - finance review workflows
     - approval workflows
     - anomaly workflows/views
     - history/audit trails
   - Avoid hidden internal-only flags if the acceptance criteria require visibility through existing workflows.

10. **Configuration and feature gating**
   - Respect existing “generation enabled” configuration.
   - If configuration is missing, add minimal strongly typed config/options for:
     - enabled flag
     - seed
     - anomaly cadence/frequency
     - thresholds or scenario schedule settings if needed
   - Keep defaults deterministic and conservative.

11. **Testing**
   - Add unit tests for:
     - deterministic seed behavior
     - scenario scheduling
     - anomaly periodicity
     - threshold edge cases
   - Add integration/application tests for:
     - simulated day advancement writes to real finance persistence path
     - approval request creation
     - anomaly/review/history visibility
     - idempotent rerun for same company/date
     - same seed/config produces same outputs across repeated runs
   - Prefer assertions on persisted business records and workflow artifacts, not internal implementation details.

12. **Migrations only if required**
   - If existing schema lacks a safe idempotency key or generation metadata, add the smallest possible migration.
   - Keep schema changes backward-compatible and aligned with project migration conventions.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add/execute targeted tests proving:
   - when generation is enabled and simulated day advances, finance records are created/updated in existing finance persistence
   - generated daily activity includes invoices, bills, transactions, balances, recurring expense instances, payment status changes, workflow tasks, approval requests where required, and finance history events
   - across a deterministic run, invoice scenarios include:
     - pending over threshold
     - different approval currency
     - partial payment
     - full payment
     - over-payment
     - due soon
     - overdue
     - normal low-risk pending
   - across a deterministic run, policy scenarios include:
     - just below threshold
     - exactly at threshold
     - just above threshold
     - requiring human approval
     - eligible without escalation
     - already approved or no longer actionable
   - anomalies occur periodically rather than daily
   - anomaly types include:
     - duplicate vendor charge
     - unusually high amount
     - category mismatch
     - missing document
     - suspicious payment timing
     - multiple payments
     - payment before expected state transition
   - anomalies and review signals are visible through existing finance review/approval/anomaly/history workflows
   - repeated runs with same seed/config/date produce identical outputs and workflow outcomes

4. If there is an API or command for day advancement, manually verify with a seeded company:
   - advance several simulated days
   - inspect persisted finance entities
   - inspect approval requests
   - inspect anomaly/review/history records
   - rerun with same seed/config and confirm stable results

# Risks and follow-ups
- **Risk: existing finance domain may be incomplete**
  - If some finance entities/workflows are not yet implemented, wire generation only into the real existing paths and document any acceptance-criteria gaps explicitly.

- **Risk: hidden mock store path**
  - Be careful not to leave generation writing to a parallel/mock repository. Remove or deprecate mock-only writes if they conflict.

- **Risk: non-determinism from unordered collections or timestamps**
  - Normalize ordering and avoid `DateTime.UtcNow` in generation logic; derive timestamps from simulated date/time rules.

- **Risk: duplicate records on rerun**
  - Use stable generation keys and idempotent upserts.

- **Risk: approval/anomaly visibility depends on downstream projections**
  - If existing review screens rely on projections/read models, ensure those are updated by the same events/paths.

- **Risk: threshold semantics at exact boundary**
  - Be explicit in code/tests whether “exactly at threshold” requires approval or not, based on existing policy rules. If ambiguous, preserve current policy behavior and document it.

Follow-ups after implementation if needed:
- add richer finance generation configuration per company
- add observability/metrics for generated scenario coverage
- add admin tooling to inspect deterministic generation plans for a date range
- expand seeded scenario catalogs once broader finance workflows exist