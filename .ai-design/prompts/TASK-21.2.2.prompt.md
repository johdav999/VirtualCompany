# Goal
Implement backlog task **TASK-21.2.2 — Integrate generated records with existing finance workflow, approval, payment, and history services** for story **US-21.2 Generate deterministic finance activity and anomaly scenarios into existing finance workflows**.

The coding agent must modify the existing .NET solution so that deterministic finance generation writes into the **real finance domain/application workflow**, not a mock-only store, and so generated activity/anomalies become visible through existing finance review, approval, anomaly, payment, and history paths for the **active company**.

The implementation must satisfy these outcomes:

- When generation is enabled and a simulated day advances, finance records are created/updated in existing finance tables/services for the active company.
- Generated daily activity includes:
  - invoices
  - bills
  - transactions
  - balances
  - recurring expense instances
  - payment status changes
  - workflow tasks
  - approval requests where required
  - finance history events
- Deterministic runs must include invoice scenarios:
  - pending invoice over approval threshold
  - different approval currency
  - partial payment
  - full or over-payment
  - due soon
  - overdue
  - normal low-risk pending
- Deterministic runs must include policy scenarios:
  - amount just below threshold
  - exactly at threshold
  - just above threshold
  - requiring human approval
  - eligible without escalation
  - already approved or no longer actionable
- Anomalies must be injected **periodically**, not every simulated day, and include:
  - duplicate vendor charge
  - unusually high amount
  - category mismatch
  - missing document
  - suspicious payment timing
  - multiple payments
  - payment before expected state transition
- Generated anomalies/review signals must surface through existing finance review, approval, anomaly, and history workflows.
- Given the same seed/configuration, repeated runs must produce the same records, anomaly days, and workflow outcomes for the same simulated dates.

Work within the existing architecture:
- ASP.NET Core modular monolith
- Application/domain/infrastructure layering
- PostgreSQL-backed transactional model
- Existing workflow/approval/history services where available
- Tenant-scoped behavior using `company_id`

Do **not** build a parallel finance simulation subsystem if existing finance modules/services can be extended.

# Scope
In scope:

- Find the current finance generation/simulation entry points and replace mock-only persistence with integration into existing finance application/domain services.
- Ensure generated records flow through existing business logic for:
  - approvals
  - workflow tasks
  - payment transitions
  - anomaly/review handling
  - finance history/audit events
- Add deterministic scenario planning so seeded runs consistently produce the required scenario coverage over time.
- Add or extend persistence models/mappings only where necessary to support missing scenario metadata or history linkage.
- Add tests proving deterministic behavior and workflow integration.
- Keep all behavior tenant-scoped to the active company.

Out of scope:

- New UI redesigns beyond what is required for existing workflows to surface generated data.
- Replacing the overall workflow engine or approval engine.
- Introducing a new external message broker or major architectural rewrite.
- Building a separate event-sourced finance engine.
- Non-finance domain changes unless required for integration points.

Implementation constraints:

- Prefer existing domain/application services over direct DB writes.
- Preserve clean boundaries: API -> Application -> Domain -> Infrastructure.
- Use deterministic seeded generation logic that is testable and side-effect aware.
- If background workers/schedulers are involved, ensure idempotent day advancement behavior.
- Respect multi-tenancy and existing authorization/policy patterns.

# Files to touch
Touch only the files needed after inspecting the codebase. Expect to work primarily in these areas:

- `src/VirtualCompany.Application/**`
  - finance generation orchestration
  - workflow integration services
  - approval request creation
  - history/audit event application services
  - deterministic scenario planner
- `src/VirtualCompany.Domain/**`
  - finance entities/value objects/enums
  - scenario classification rules
  - payment/approval/anomaly domain logic
- `src/VirtualCompany.Infrastructure/**`
  - EF Core persistence mappings/repositories
  - deterministic seed/config persistence if needed
  - background job integration
- `src/VirtualCompany.Api/**`
  - only if simulation/day-advance endpoints or handlers need wiring changes
- `src/VirtualCompany.Web/**`
  - only if existing views require minimal binding fixes to show generated records already exposed by backend contracts
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- potentially additional test projects if finance application/domain tests already exist

Also inspect these likely anchors first:

- `README.md`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`
- any existing finance-, workflow-, approval-, payment-, anomaly-, history-, simulation-, seed-, or scheduler-related folders/classes
- any migration or schema docs under `docs/`

If schema changes are required, update the appropriate migration mechanism used by this repo rather than inventing a new one.

# Implementation plan
1. **Discover existing finance workflow boundaries**
   - Inspect the solution structure and identify:
     - finance entities and tables
     - invoice/bill/payment/transaction/balance models
     - approval services and policies
     - workflow task creation services
     - anomaly/review services
     - finance history/audit services
     - simulation/day advancement entry points
   - Document the actual integration path in code comments or commit structure before changing behavior.
   - Determine whether current generation writes to a mock store, in-memory store, or isolated tables.

2. **Map generation to existing domain/application services**
   - Replace mock-only persistence with calls into existing finance commands/services/repositories.
   - Ensure generated records are created as first-class finance records for the active `company_id`.
   - Prefer invoking the same application services used by normal finance workflows so downstream approvals/history are naturally triggered.
   - If current services are too UI-centric or incomplete, extract reusable application-layer orchestration methods rather than bypassing domain rules.

3. **Introduce a deterministic scenario planner**
   - Create or extend a deterministic planner component that, given:
     - company id
     - seed
     - simulation config
     - simulated date
   - returns the exact set of finance activities and anomalies for that day.
   - The planner must guarantee scenario coverage across a deterministic run, including all acceptance-criteria invoice and policy scenarios.
   - Use stable identifiers/keys derived from seed + company + date + scenario type so repeated runs update or no-op consistently instead of duplicating records.
   - Separate:
     - daily baseline activity generation
     - periodic anomaly injection schedule
     - workflow/approval outcome planning

4. **Implement idempotent record upsert behavior**
   - For each generated artifact, define a deterministic business key or external/reference key.
   - On repeated runs for the same simulated day and config:
     - update the same records or no-op
     - do not create duplicates unless the scenario explicitly represents duplicates as an anomaly
   - Apply this to:
     - invoices
     - bills
     - transactions
     - balances
     - recurring expense instances
     - payment state changes
     - workflow tasks
     - approval requests
     - history events where deduplication is appropriate

5. **Generate required daily finance activity**
   - Ensure each simulated day can create/update:
     - invoices
     - bills
     - transactions
     - balances
     - recurring expense instances
     - payment status changes
   - Use realistic state transitions through existing services:
     - pending -> partially paid -> paid/overpaid
     - due soon -> overdue
     - approval pending -> approved/rejected/non-actionable
   - If balances are derived rather than stored, trigger the existing recalculation/update path instead of persisting redundant values.

6. **Integrate approval policy scenarios**
   - Ensure deterministic runs include policy edge cases:
     - just below threshold
     - exactly at threshold
     - just above threshold
     - requiring human approval
     - eligible without escalation
     - already approved or no longer actionable
   - Reuse the existing approval policy engine if present.
   - Clarify and encode threshold semantics, especially for:
     - equality at threshold
     - currency differences
     - already-approved entities
     - stale/non-actionable requests
   - If the current policy engine lacks deterministic test seams, add them without weakening production behavior.

7. **Integrate invoice scenario coverage**
   - Ensure the deterministic run includes invoice cases for:
     - pending invoice over approval threshold
     - different approval currency
     - partial payment
     - full or over-payment
     - due soon
     - overdue
     - normal low-risk pending
   - These should appear as real finance records and flow into the same review/approval/history mechanisms as non-generated records.
   - Where one invoice can satisfy multiple dimensions, keep scenario tagging/metadata internal for testability, but do not expose artificial mock-only concepts in user-facing models unless the domain already supports them.

8. **Inject anomalies periodically**
   - Implement anomaly scheduling so anomalies occur on deterministic periodic days, not every day.
   - Include all required anomaly types:
     - duplicate vendor charge
     - unusually high amount
     - category mismatch
     - missing document
     - suspicious payment timing
     - multiple payments
     - payment before expected state transition
   - Route anomalies through existing review/anomaly services and create corresponding workflow tasks/history entries where the current system expects them.
   - Ensure anomaly generation is deterministic by seed/date and tenant-scoped.

9. **Create workflow tasks, approvals, and history events**
   - For generated records that require review or approval:
     - create workflow tasks in the existing task/workflow subsystem
     - create approval requests in the existing approval subsystem
     - create finance history/audit events in the existing history subsystem
   - Ensure linked entity references are correct and tenant-scoped.
   - Avoid duplicating approvals/tasks/history on rerun by using deterministic correlation keys or idempotency checks.

10. **Preserve existing review visibility**
   - Verify generated anomalies and review signals are queryable through existing finance review/approval/anomaly/history APIs or query handlers.
   - If existing queries exclude generated records due to source filters, update them so generated records are treated as normal finance records for the active company.
   - Do not create a separate “simulation-only” read path unless absolutely necessary.

11. **Add tests**
   - Add focused unit tests for deterministic planning:
     - same seed/config/date => same planned outputs
     - different dates => expected periodic anomaly cadence
     - threshold edge cases
     - invoice scenario coverage
   - Add application/integration tests for:
     - simulated day advancement creates/updates real finance records
     - approvals are created when thresholds require them
     - no approval when below threshold
     - history events are written
     - anomalies surface in existing review/anomaly queries
     - rerunning same day is idempotent
   - Prefer integration tests against the real application pipeline over isolated mocks where feasible.

12. **Schema/migration updates if needed**
   - Only add schema changes if required for:
     - deterministic external keys
     - scenario correlation
     - missing history linkage
     - anomaly classification persistence
   - Keep schema changes minimal and aligned with existing naming conventions and tenant scoping.
   - Add indexes/constraints for idempotency if appropriate.

13. **Keep implementation production-safe**
   - Guard generation behind the existing enablement/config flags.
   - Ensure only the active company is affected.
   - Make background execution safe for retries and repeated day advancement.
   - Use structured logging where existing patterns support it, but do not replace business audit/history with technical logs.

# Validation steps
Run these after implementation, adapting to the repo’s actual test layout:

1. **Build**
   - `dotnet build`

2. **Run tests**
   - `dotnet test`

3. **Targeted verification**
   - Execute or add tests covering:
     - deterministic same-seed same-date output
     - idempotent rerun of the same simulated day
     - invoice scenario coverage across a deterministic run
     - policy threshold edge cases
     - periodic anomaly injection rather than daily injection
     - visibility through existing finance review/approval/history queries

4. **Manual code validation checklist**
   - Confirm generation no longer writes only to a mock/in-memory store.
   - Confirm generated records carry the active `company_id`.
   - Confirm approvals are created only when policy requires them.
   - Confirm history events are created for generated actions.
   - Confirm anomalies appear in existing review/anomaly workflows.
   - Confirm repeated runs with same seed/config do not create unintended duplicates.
   - Confirm deterministic keys/correlation IDs are stable and scoped by company/date/scenario.

5. **If there are API endpoints or scheduled jobs for simulation**
   - Trigger a simulated day advance twice with the same seed/config and verify:
     - same finance records
     - same anomaly days
     - same workflow outcomes
     - no duplicate approvals/tasks/history except where explicitly modeled

# Risks and follow-ups
- **Risk: hidden mock-store coupling**
  - The current generator may be deeply coupled to mock DTOs or isolated repositories.
  - Mitigation: introduce an application-layer adapter that translates planned scenarios into existing finance commands.

- **Risk: duplicate side effects on rerun**
  - Approvals, tasks, and history events are especially prone to duplication.
  - Mitigation: add deterministic correlation keys, unique constraints, or idempotent lookup-before-create behavior.

- **Risk: threshold semantics ambiguity**
  - “Exactly at threshold” and currency conversion behavior may be undefined.
  - Mitigation: codify explicit rules in tests and align with existing approval policy behavior.

- **Risk: existing queries filter out generated records**
  - Review/anomaly/history screens may only show records from imported/manual sources.
  - Mitigation: normalize generated records as first-class finance records and update source filtering carefully.

- **Risk: anomaly cadence not deterministic enough**
  - Using random calls directly in orchestration can break repeatability.
  - Mitigation: centralize all randomness behind a seeded deterministic planner.

- **Risk: recurring expense and balance logic may already be derived elsewhere**
  - Direct writes could conflict with existing recal