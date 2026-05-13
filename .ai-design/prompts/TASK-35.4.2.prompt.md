# Goal

Implement backlog task **TASK-35.4.2**: add an `IRevenueForecastService` that computes tenant-scoped expected revenue forecasts for the next **30 / 60 / 90 days**, and add **daily pipeline risk scoring jobs** for active deals.

The implementation must fit the existing **.NET modular monolith** architecture, preserve **shared-schema multi-tenancy**, and support the broader story **US-35.4 Implement conversion analytics, deal intelligence signals, and revenue forecasting dashboard**.

Deliver production-ready backend functionality that:
- calculates forecast values from tenant production data
- recalculates active-deal pipeline risk scores at least daily
- persists forecast/risk outputs for dashboard and API consumption
- is testable, deterministic, and safe for repeated background execution

# Scope

Implement only what is necessary for this task, while aligning with the acceptance criteria and existing architecture.

In scope:
- Define and implement `IRevenueForecastService`
- Add domain/application models for:
  - forecast windows: 30 / 60 / 90 days
  - expected revenue outputs
  - pipeline risk score outputs
- Add persistence support for stored forecast snapshots and/or deal risk score snapshots if not already present
- Add daily scheduled/background job(s) to recalculate pipeline risk scores for active deals
- Ensure tenant-scoped querying and persistence
- Expose application-layer query/use-case methods needed by dashboard/API consumers
- Add tests for forecast calculations, tenant isolation, and daily risk job behavior
- Wire up DI registrations

Use existing modules and patterns where possible:
- CQRS-lite application layer
- PostgreSQL persistence
- background workers / scheduler
- Redis/distributed locking only if the project already uses it for scheduled jobs
- outbox/audit hooks only if already established nearby

Out of scope unless required by compilation or obvious existing extension points:
- full dashboard UI implementation
- mobile changes
- broad analytics redesign
- LLM-based deal intelligence extraction itself
- A/B variant analytics implementation beyond preserving compatibility with forecast inputs
- introducing microservices or external schedulers if the solution already has an internal worker pattern

If related analytics entities already exist, extend them rather than duplicating concepts.

# Files to touch

Inspect the solution first and then update the actual matching files. Expect to touch files in these areas:

- `src/VirtualCompany.Application/**`
  - service interface definitions
  - forecast/risk DTOs or query models
  - command/query handlers
  - scheduling/job abstractions if present
- `src/VirtualCompany.Domain/**`
  - deal, analytics, forecast, and risk-related domain models/value objects
  - enums/constants for forecast windows or risk bands
- `src/VirtualCompany.Infrastructure/**`
  - EF Core/db access implementations
  - repository/query implementations
  - background job implementations
  - DI registration
  - persistence mappings/configurations
  - migrations support if this repo uses code-first migrations here
- `src/VirtualCompany.Api/**`
  - endpoint/controller wiring only if needed to expose new dashboard/deal-detail data
- `src/VirtualCompany.Shared/**`
  - shared contracts only if this solution uses shared DTOs there
- `tests/**`
  - unit tests for forecast calculations
  - integration tests for tenant-scoped persistence/querying
  - background job tests

Also inspect:
- `README.md`
- any architecture/conventions docs
- existing scheduler/background worker patterns
- existing analytics/deals/dashboard code paths
- existing migration approach, including `docs/postgresql-migrations-archive/README.md`

Do not invent file names prematurely; follow the existing project structure and naming conventions.

# Implementation plan

1. **Discover existing architecture and extension points**
   - Inspect the solution structure for:
     - deals/pipeline domain models
     - analytics/dashboard services
     - background worker scheduler pattern
     - tenant context abstractions
     - repository/query patterns
     - migration workflow
   - Identify whether `IRevenueForecastService` already exists as a stub or needs to be introduced.
   - Identify existing entities/tables for:
     - deals
     - deal stages/status
     - expected value/amount
     - close dates
     - message/conversion analytics
     - intelligence signals
     - risk scores
     - dashboard aggregates

2. **Design the forecast contract**
   - Add or complete `IRevenueForecastService` in the application layer.
   - The interface should support tenant-scoped forecast generation and retrieval.
   - Prefer methods along the lines of:
     - compute forecast for a tenant as-of a timestamp
     - return 30/60/90 day expected revenue windows
     - optionally persist a snapshot for dashboard reads
   - Keep the contract deterministic by accepting an explicit `asOfUtc` where appropriate.

3. **Define forecast calculation rules**
   - Base the forecast on active tenant production deal data.
   - Use existing fields if available:
     - deal amount/value
     - expected close date
     - stage probability or win likelihood
     - pipeline risk score
     - recent conversion/performance signals
   - If no single probability field exists, derive a pragmatic expected revenue formula from available data and document it in code comments.
   - Recommended default approach unless the codebase already defines another:
     - include active/open deals only
     - include a deal in a forecast window if expected close date falls within that window
     - expected revenue contribution = `deal amount * adjusted win probability`
     - adjusted win probability may incorporate risk score as a dampening factor, e.g. `baseProbability * (1 - riskScoreNormalized * weight)`
   - Keep formulas simple, explainable, and testable.
   - Do not hardcode tenant-specific assumptions.

4. **Define risk scoring rules for active deals**
   - Add a reusable risk scoring component/service used by the daily job.
   - Recalculate for active deals at least daily.
   - Use available signals in priority order:
     - stale/ghosting indicators
     - inactivity / no recent reply
     - price resistance signals
     - positive buying signals
     - close date slippage
     - stage aging
     - engagement/conversion history
   - Produce:
     - numeric risk score
     - optional risk band/category
     - calculation timestamp
     - contributing factors summary if the model supports it
   - Keep scoring deterministic and bounded, e.g. 0-100 or 0.0-1.0.
   - Persist the latest score so it can be exposed via dashboard and deal detail API.

5. **Add/extend persistence models**
   - If missing, add tables/entities for one or both of:
     - revenue forecast snapshots
     - deal risk score snapshots or current risk fields on deals
   - Minimum persisted data should include:
     - tenant/company id
     - deal id for risk records
     - forecast window or snapshot date for forecast records
     - computed values
     - calculated at timestamp
   - Ensure indexes support:
     - tenant + calculated_at
     - tenant + active/open deal queries
     - tenant + deal id for latest risk lookup
   - Follow the repo’s migration pattern exactly.

6. **Implement infrastructure service**
   - Implement `IRevenueForecastService` in infrastructure/application as appropriate for the project’s layering.
   - Query only tenant-scoped production data.
   - Avoid loading unnecessary rows into memory; aggregate in SQL where practical, but keep business logic readable.
   - Ensure null-safe handling for missing amounts, dates, or probabilities.
   - Return stable outputs for 30/60/90 windows.

7. **Implement daily pipeline risk scoring job**
   - Add a scheduled/background worker that:
     - enumerates tenants or tenant-scoped active deals safely
     - recalculates risk scores daily
     - persists results idempotently
   - Use distributed locking if the project already uses it for scheduled jobs.
   - Make the job safe for retries and repeated execution on the same day.
   - Log structured execution details with tenant context.
   - If the codebase has an internal scheduler abstraction, register the job there rather than creating a custom loop.

8. **Expose query/read models for dashboard and deal detail**
   - Add application queries or service methods so consumers can retrieve:
     - latest 30/60/90 expected revenue values
     - risk distribution summary if this task’s existing dashboard path expects it
     - latest per-deal risk score for deal detail API
   - Reuse existing dashboard DTOs if present.
   - Keep API changes minimal and backward compatible.

9. **Add tests**
   - Unit tests for forecast calculation:
     - includes only active deals
     - respects 30/60/90 windows
     - handles missing/edge-case data
     - applies risk adjustment correctly
   - Unit tests for risk scoring:
     - ghosting increases risk
     - buying signals reduce risk
     - stale deals increase risk
     - score remains bounded
   - Integration tests:
     - tenant isolation
     - persistence of latest forecast/risk outputs
     - daily job updates active deals only
     - idempotent repeated execution behavior if applicable

10. **Document assumptions in code**
   - Add concise comments where formulas or heuristics are introduced.
   - If acceptance criteria depend on upstream analytics/signal data not yet fully implemented, consume existing fields and leave clear TODOs only where unavoidable.
   - Do not leave placeholder implementations without tests.

# Validation steps

1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are required, generate/apply them using the repository’s established process and verify:
   - new tables/columns/indexes exist
   - tenant-scoped queries still work
   - no destructive schema changes are introduced unintentionally

4. Verify forecast behavior with tests or seed fixtures:
   - active deals closing within 30 days contribute to 30/60/90
   - active deals closing within 60 days contribute to 60/90 only when outside 30
   - active deals closing within 90 days contribute to 90 only when outside 60
   - closed/lost/inactive deals do not contribute
   - risk-adjusted expected revenue decreases as risk increases

5. Verify daily risk job behavior:
   - job processes active deals
   - latest risk score is persisted
   - rerunning the job does not create inconsistent duplicates unless snapshots are intentionally versioned
   - tenant isolation is preserved across job execution

6. Verify read paths:
   - dashboard-facing query returns 30/60/90 expected revenue values
   - deal detail read path returns latest risk score
   - outputs are based on tenant production data only

7. Ensure code quality:
   - no cross-layer violations
   - no direct DB access from controllers/UI
   - DI registrations are complete
   - nullable warnings and analyzer issues are addressed where practical

# Risks and follow-ups

- **Existing data model mismatch:** the repo may not yet have canonical deal probability, expected close date, or intelligence signal fields. If missing, derive from the closest existing fields and document the fallback logic clearly.
- **Scheduler pattern uncertainty:** if no scheduler exists yet, implement the smallest background worker consistent with current architecture, but avoid introducing a parallel job framework.
- **Forecast formula ambiguity:** keep the formula explainable and deterministic; prefer simple weighted expected revenue over opaque heuristics.
- **Snapshot strategy:** decide based on existing patterns whether to store only latest values or historical snapshots. If historical trending is not yet needed, latest-per-day snapshots are a reasonable compromise.
- **Performance:** tenant-wide daily recalculation may become expensive; batch queries and add indexes. Leave clear follow-up notes if optimization is needed after correctness.
- **Acceptance criteria overlap:** this task depends on upstream analytics and intelligence signals. Integrate with existing stored metrics/signals where available, but do not block delivery on unrelated UI or extraction work.
- **Follow-up candidates after this task:**
  - dashboard UI wiring for forecast and risk distribution widgets
  - historical forecast trend charts
  - configurable risk scoring weights per tenant
  - audit events for forecast/risk recalculation runs
  - richer explainability payload for why a deal is high risk