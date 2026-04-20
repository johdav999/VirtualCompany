# Goal
Implement backlog task **TASK-21.2.3 — Build deterministic anomaly injection scheduler and scenario factory using seeded generation rules** for story **US-21.2 Generate deterministic finance activity and anomaly scenarios into existing finance workflows**.

The coding agent must add a deterministic, seed-driven finance generation subsystem that:
- writes generated activity into the **existing finance domain tables/services**, not a mock-only store,
- produces realistic daily finance activity and policy/approval scenarios,
- injects anomalies on a **periodic schedule** rather than every simulated day,
- routes generated records through existing review, approval, anomaly, workflow, and history mechanisms,
- guarantees that the same **seed + config + simulated dates** produce the same records, anomaly days, and workflow outcomes.

# Scope
In scope:
- Add or extend a deterministic finance generation scheduler/background workflow for simulated day advancement.
- Add a seeded scenario factory/rule engine that deterministically emits:
  - invoices,
  - bills,
  - transactions,
  - balances,
  - recurring expense instances,
  - payment status changes,
  - workflow tasks,
  - approval requests where required,
  - finance history events.
- Add deterministic scenario coverage for invoice and policy cases required by acceptance criteria.
- Add periodic anomaly injection logic for:
  - duplicate vendor charge,
  - unusually high amount,
  - category mismatch,
  - missing document,
  - suspicious payment timing,
  - multiple payments,
  - payment before expected state transition.
- Ensure generated outputs are persisted through existing finance services/repositories/workflows for the active company.
- Ensure deterministic replay behavior for same seed/config/date range.
- Add tests covering determinism, scenario coverage, anomaly cadence, and workflow visibility.

Out of scope unless required by existing architecture:
- New UI beyond minimal plumbing needed for existing review/approval/history surfaces to display generated data.
- Replacing existing finance workflows or approval engines.
- Introducing non-deterministic randomness sources.
- Building a separate mock finance store.

Implementation constraints:
- Prefer modular monolith boundaries already present in the solution.
- Keep tenant/company scoping explicit.
- Use typed application/domain services rather than direct DB writes where finance workflows already exist.
- Preserve idempotency for repeated simulated-day processing.

# Files to touch
Touch only the files needed after inspecting the solution structure. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - finance entities/value objects/enums
  - deterministic generation configuration models
  - anomaly/scenario definitions
- `src/VirtualCompany.Application/**`
  - finance generation orchestration services
  - scenario factory interfaces/implementations
  - simulated day advancement handlers
  - approval/review/history integration commands
- `src/VirtualCompany.Infrastructure/**`
  - persistence mappings/repositories
  - background job scheduling
  - seeded deterministic random provider implementation
  - outbox/event dispatch integration if needed
- `src/VirtualCompany.Api/**`
  - DI registration
  - any endpoints/hosted services needed for simulation triggers if already exposed here
- `src/VirtualCompany.Shared/**`
  - shared contracts only if existing architecture uses shared DTOs/configs
- `tests/**`
  - unit tests for seeded generation and anomaly scheduling
  - integration tests for persistence into existing finance workflows/services
  - determinism/replay tests

Also inspect:
- `README.md`
- any finance module folders under `src/VirtualCompany.Application`, `src/VirtualCompany.Domain`, `src/VirtualCompany.Infrastructure`
- existing worker/scheduler implementations
- existing approval/workflow/history services
- existing migrations or schema configuration if new persistence fields are required

Do not invent file paths blindly; first discover the actual finance-related modules and align with existing naming conventions.

# Implementation plan
1. **Inspect the current finance generation and workflow architecture**
   - Find existing finance domain models, services, repositories, workflow handlers, approval logic, anomaly/review/history pipelines, and simulated-day advancement code.
   - Identify whether there is already a mock generator or placeholder seed logic.
   - Map the current write path for finance records so generation can use the same path.

2. **Design deterministic generation primitives**
   - Introduce a deterministic seeded generation abstraction, e.g.:
     - `IFinanceDeterministicGenerator`
     - `IFinanceScenarioFactory`
     - `ISeededRandom` or equivalent deterministic sequence provider
   - Seed derivation must be stable and based on immutable inputs such as:
     - company ID,
     - configured base seed,
     - simulated date,
     - scenario category/type,
     - sequence index.
   - Avoid `Random.Shared`, current time, GUID-based randomness, unordered dictionary iteration, or DB-return-order dependence.

3. **Implement a scenario factory with explicit rule buckets**
   - Build deterministic scenario generation rules that guarantee coverage across a run.
   - Include invoice scenarios:
     - pending invoice over approval threshold,
     - different approval currency,
     - partial payment,
     - full or over-payment,
     - due soon,
     - overdue,
     - normal low-risk pending.
   - Include policy scenarios:
     - amount just below threshold,
     - exactly at threshold,
     - just above threshold,
     - requiring human approval,
     - eligible without escalation,
     - already approved or no longer actionable.
   - Use deterministic rotation/allocation logic so these scenarios appear predictably across a configured run window.

4. **Implement periodic anomaly scheduling**
   - Add anomaly cadence logic so anomalies are injected on deterministic intervals or selected deterministic days, not every day.
   - The anomaly scheduler must deterministically choose:
     - whether a day is an anomaly day,
     - which anomaly types appear,
     - which generated records are targeted.
   - Ensure support for all required anomaly types:
     - duplicate vendor charge,
     - unusually high amount,
     - category mismatch,
     - missing document,
     - suspicious payment timing,
     - multiple payments,
     - payment before expected state transition.
   - Make anomaly cadence configurable but deterministic.

5. **Integrate with simulated day advancement**
   - Hook generation into the existing simulated-day advancement workflow.
   - For each active company/day when generation is enabled:
     - create or update finance records in existing finance tables/services,
     - create related tasks/approval requests/history events as required,
     - avoid duplicate inserts on rerun for the same company/date.
   - Prefer an idempotent upsert strategy keyed by deterministic external/reference IDs.

6. **Persist through existing finance services**
   - Do not write to a mock-only store.
   - Route generated invoices, bills, transactions, balances, recurring expenses, payment updates, approvals, tasks, and history through the same application/domain services used by normal finance workflows.
   - If current services are too UI/request-centric, extract reusable application services rather than bypassing business rules.

7. **Ensure workflow visibility**
   - Verify generated anomalies and review signals surface in existing:
     - finance review workflows,
     - approval workflows,
     - anomaly workflows,
     - finance history/audit views.
   - If needed, emit the same domain events/history records as real finance actions.
   - Preserve tenant/company scoping on all generated artifacts.

8. **Add deterministic identifiers and idempotency**
   - For generated records, derive stable business keys/reference IDs from seed inputs.
   - Reprocessing the same simulated date should update or no-op rather than duplicate.
   - Ensure workflow outcomes are deterministic for same seed/config/date range.

9. **Add tests**
   - Unit tests:
     - same seed/config/date => same scenario outputs,
     - different seed => different but valid outputs,
     - anomaly cadence is periodic and not daily,
     - threshold edge cases for below/exactly/above threshold,
     - invoice scenario coverage across deterministic run.
   - Integration tests:
     - simulated day advancement persists records into existing finance persistence,
     - approval requests created when threshold/currency rules require,
     - anomalies visible in existing review/history workflows,
     - rerun of same day is idempotent,
     - multi-day replay yields same records and outcomes.
   - Prefer stable assertions on business keys/types/statuses rather than fragile timestamps if timestamps are system-generated.

10. **Wire up DI and configuration**
   - Register new generator/scheduler/factory services in the existing composition root.
   - Add configuration for:
     - generation enabled flag,
     - base seed,
     - anomaly cadence,
     - scenario density/volume if needed.
   - Keep defaults deterministic and safe for tests/dev.

11. **Document assumptions in code comments where necessary**
   - Especially around seed derivation, deterministic ordering, and idempotent keys.
   - Keep comments concise and operational.

Implementation notes:
- If the codebase already has finance modules with different naming, adapt to them rather than forcing new abstractions.
- If schema changes are required, keep them minimal and aligned with existing migration patterns.
- If there is no existing finance module yet, still implement within Domain/Application/Infrastructure boundaries and persist to the real transactional store, not an in-memory substitute.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add and run focused tests for this task, covering:
   - deterministic replay with same seed/config/date range,
   - invoice scenario coverage across a deterministic run,
   - policy threshold edge cases,
   - anomaly periodicity,
   - persistence into existing finance services/tables,
   - visibility in approval/review/history workflows,
   - idempotent rerun of same simulated day.

4. Manually validate in code or integration tests that:
   - generation only runs when enabled,
   - advancing a simulated day creates/updates records for the active company,
   - anomalies are not injected every day,
   - approval requests are created for threshold/currency-required cases,
   - generated history events are linked to the same company and entities,
   - repeated runs with same seed produce identical business outcomes.

5. If there are existing API or worker entry points for simulation:
   - execute a short deterministic run for a fixed company and seed,
   - capture generated record references for days N..N+K,
   - rerun and confirm exact match.

# Risks and follow-ups
- **Risk: hidden non-determinism**
  - Sources include unordered LINQ over sets/dictionaries, DB default ordering, `DateTime.UtcNow`, random GUIDs, and async race-dependent sequencing.
  - Mitigation: enforce explicit ordering and deterministic key derivation.

- **Risk: duplicate records on replay**
  - If existing finance services assume one-way inserts, reruns may duplicate data.
  - Mitigation: add deterministic external keys/upsert semantics or replay guards.

- **Risk: bypassing business workflows**
  - Direct repository writes may skip approvals/history/anomaly visibility.
  - Mitigation: use existing application services/domain events wherever possible.

- **Risk: acceptance criteria require scenario coverage “across a deterministic run”**
  - A purely probabilistic generator may miss required cases.
  - Mitigation: use explicit scheduled scenario allocation, not probability-only generation.

- **Risk: anomaly cadence may conflict with low-volume runs**
  - Short runs may not naturally include all anomaly types.
  - Mitigation: define deterministic rotation windows or configurable cadence/test fixtures that guarantee coverage over a known run length.

- **Follow-up**
  - If useful, add a developer-facing diagnostic/report endpoint or test helper that summarizes generated scenarios by day for easier verification, but only if it fits existing architecture and does not expand scope unnecessarily.