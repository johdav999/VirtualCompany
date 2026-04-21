# Goal
Implement backlog task **TASK-27.4.1 — Implement finance insight aggregation services and narrative-ready DTOs** for story **US-27.4 Add finance insight services and migration-safe rollout verification**.

Deliver a production-ready, tenant-scoped finance insight capability in the existing .NET modular monolith that:
- computes finance insights from current finance data,
- exposes a structured API response suitable for downstream agent/narrative consumption,
- safely supports optional snapshot/materialization persistence and background refresh,
- hardens migration safety and startup validation,
- and adds an idempotent admin bootstrap/backfill rerun path for existing companies.

Work within the current architecture and code conventions already present in the repository. Prefer extending existing modules, patterns, and naming over inventing parallel structures.

# Scope
In scope:
- Add or extend finance insight domain/application services to compute:
  - top expenses,
  - revenue trend,
  - burn rate,
  - overdue customer risk,
  - payable pressure.
- Ensure all calculations are **tenant/company scoped**.
- Add narrative-ready DTOs/view models for agent consumption, including:
  - structured metrics,
  - concise narrative fields,
  - `generatedAt` timestamp.
- Add or extend an API endpoint/query handler returning the finance insight payload.
- If snapshot/materialization/cache tables are needed:
  - create EF/Core or SQL migrations,
  - enforce uniqueness for same tenant + snapshot key,
  - add background refresh/update job with idempotent behavior.
- Add automated migration tests covering:
  - clean database migration,
  - migration from current mock finance schema,
  - migration of partially seeded company state.
- Add startup validation in **dev and test only** that fails fast when pending migrations exist.
- Add a safe admin-triggered bootstrap/backfill rerun operation for an existing company that:
  - reruns planning and approval backfills,
  - does not duplicate seeded records,
  - is explicitly idempotent.

Out of scope unless required by existing patterns:
- New UI/dashboard work beyond API contract exposure.
- Mobile changes.
- Broad finance schema redesign unrelated to insight computation.
- Replacing existing bootstrap/seeding architecture if it can be extended safely.

# Files to touch
Inspect the solution first, then update the most relevant existing files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - finance entities/value objects/specifications if needed
- `src/VirtualCompany.Application/**`
  - finance insight query/service interfaces and implementations
  - DTOs/contracts for API and agent consumption
  - admin/bootstrap command handlers
  - background job abstractions
- `src/VirtualCompany.Infrastructure/**`
  - EF Core DbContext mappings
  - repositories/query services
  - migrations
  - hosted/background services
  - startup migration validation wiring
- `src/VirtualCompany.Api/**`
  - finance insight controller/endpoints
  - DI registration
  - environment-specific startup validation
  - admin bootstrap endpoint if API-hosted
- `tests/VirtualCompany.Api.Tests/**`
  - API tests
  - migration/integration tests
  - bootstrap idempotency tests

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If the repo already has finance, seeding, migration, or startup validation code, modify those files instead of creating duplicate infrastructure.

# Implementation plan
1. **Discover existing finance and migration patterns**
   - Find current finance schema/entities/tables and any mock finance data model.
   - Find existing CQRS patterns, API endpoint style, background job style, seeding/bootstrap logic, and migration test setup.
   - Identify whether finance insights should live under an existing finance/analytics/cockpit module.
   - Identify current company/tenant resolution pattern and enforce it consistently.

2. **Define the finance insight contract**
   - Create a response DTO that is narrative-ready and stable for agent consumption.
   - Include:
     - `generatedAt`
     - tenant/company identifier only if consistent with existing API conventions
     - metric sections for:
       - top expenses
       - revenue trend
       - burn rate
       - overdue customer risk
       - payable pressure
     - narrative-ready fields such as:
       - headline/summary
       - trend labels
       - risk labels
       - short explanatory text
       - optional confidence/data coverage notes if supported by available data
   - Keep fields structured and deterministic; avoid free-form LLM generation here.

3. **Implement aggregation logic**
   - Add an application service/query service that computes insights from current finance data.
   - Use current persisted finance records only; do not fabricate missing data.
   - Handle sparse/partial tenant data gracefully:
     - return empty collections or nullables where appropriate,
     - include narrative text that reflects insufficient data without failing.
   - Suggested computation expectations:
     - **Top expenses**: highest expense categories/vendors/entries over a recent window supported by schema.
     - **Revenue trend**: recent period totals and direction vs prior comparable period.
     - **Burn rate**: expense outflow rate and runway-style interpretation only if cash/revenue basis exists; otherwise compute a clearly defined burn metric from available data.
     - **Overdue customer risk**: overdue receivables concentration, aging severity, count/value of overdue invoices/customers.
     - **Payable pressure**: upcoming/overdue payables burden, near-term obligations, concentration or urgency indicators.
   - Keep formulas explicit in code comments where non-obvious.

4. **Add persistence for snapshots only if justified**
   - Prefer on-demand computation unless the existing architecture or performance profile clearly benefits from materialization.
   - If adding snapshot/materialization/cache tables:
     - model a finance insight snapshot entity,
     - include company/tenant id,
     - include snapshot key/window,
     - include generated timestamp,
     - include serialized metrics payload or normalized columns per existing style,
     - add a unique constraint/index preventing duplicate rows for same tenant + snapshot key.
   - Ensure refresh logic performs upsert/idempotent replace semantics, not blind inserts.

5. **Add background refresh job if snapshots are introduced**
   - Implement a hosted/background job or existing scheduler-integrated worker.
   - Scope execution per tenant/company.
   - Prevent duplicate rows and duplicate work using:
     - unique constraints,
     - upsert semantics,
     - and existing distributed locking/job coordination if available.
   - Keep job safe to rerun.

6. **Expose the insight API**
   - Add or extend a finance insights endpoint using existing API conventions.
   - Ensure authorization and tenant scoping are enforced.
   - Return the structured DTO with `generatedAt`.
   - Keep controller thin; delegate to application layer.

7. **Add startup migration validation**
   - In development and test environments only, fail application startup if pending migrations exist.
   - Reuse existing startup/host initialization patterns if present.
   - Do not enable fail-fast pending-migration behavior in production unless already established by repo conventions.
   - Make failure message actionable.

8. **Implement safe admin-triggered bootstrap/backfill rerun**
   - Find current planning and approval backfill/bootstrap logic.
   - Add an admin-only operation to rerun it for an existing company.
   - Make it idempotent:
     - detect existing seeded/planned/approval records,
     - update or skip rather than duplicate,
     - preserve referential integrity and auditability.
   - Prefer explicit command handler/service over embedding logic in controller.
   - If needed, add idempotency keys or natural uniqueness checks aligned with existing seed data semantics.

9. **Add migration coverage**
   - Create automated tests for:
     - clean database migration to latest,
     - migration from current mock finance schema state,
     - migration of a partially seeded company.
   - Verify resulting schema and data invariants, especially:
     - no duplicate snapshot rows for same tenant + snapshot key,
     - bootstrap rerun does not duplicate seeded records,
     - finance insight-related tables/indexes/constraints exist as expected.

10. **Add application/API tests**
   - Test finance insight calculations against representative seeded finance data.
   - Test sparse-data behavior.
   - Test tenant isolation.
   - Test `generatedAt` presence and response shape.
   - If snapshot refresh exists, test idempotent refresh behavior.

11. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries.
   - Keep business logic in application/domain layers, not controllers.
   - Use typed contracts and repository/query abstractions rather than direct ad hoc SQL in controllers.
   - Preserve auditability and deterministic outputs.

# Validation steps
Run and verify at minimum:

1. Build:
   - `dotnet build`

2. Full tests:
   - `dotnet test`

3. Specifically validate:
   - finance insight API returns all required sections,
   - `generatedAt` is populated,
   - narrative-ready fields are present and deterministic,
   - tenant A cannot access tenant B insights,
   - migration tests pass for:
     - clean DB,
     - current mock finance schema migration,
     - partially seeded company migration,
   - startup fails fast in dev/test when migrations are pending,
   - admin bootstrap rerun is safe and non-duplicating,
   - if snapshot tables exist:
     - migration creates them,
     - refresh job updates snapshots,
     - duplicate rows for same tenant + snapshot key are prevented.

4. If integration tests use a real PostgreSQL test fixture, prefer that over in-memory providers for migration verification.

5. In the final implementation notes/PR summary, include:
   - formulas/assumptions for each finance insight,
   - whether snapshots were added or on-demand computation was used,
   - uniqueness/idempotency strategy,
   - any known limitations due to current mock finance schema.

# Risks and follow-ups
- **Ambiguous finance schema**: current mock finance data may not fully support ideal burn/runway formulas. Use the best defensible calculation from available fields and document assumptions in code/tests.
- **Migration fragility**: migration-from-mock-schema may require careful handling of legacy table names/data shapes. Prefer additive, reversible-safe migration steps.
- **Duplicate seed/backfill records**: do not rely only on “check then insert” in memory; enforce durable uniqueness where possible.
- **Environment-specific startup checks**: ensure fail-fast migration validation is limited to dev/test and does not break production deployment flow.
- **Snapshot complexity**: do not add materialization tables unless needed. If added, ensure upsert semantics and uniqueness constraints are tested.
- **Narrative drift**: narrative-ready fields must be deterministic summaries of computed metrics, not generated prose.
- **Follow-up candidates**:
  - dashboard/UI consumption of the new insight API,
  - caching/performance tuning,
  - richer finance trend windows and forecasting,
  - audit events for admin bootstrap reruns and snapshot refreshes.