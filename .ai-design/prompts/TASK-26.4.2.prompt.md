# Goal
Implement backlog task **TASK-26.4.2 — Add summary table migrations and aggregate backfill jobs if query performance requires pre-aggregation** for **US-26.4 Add agent query support and performance strategy for cash analytics**.

Deliver deterministic, tenant-scoped cash analytics support for these agent queries:

- **“what should I pay this week”**
- **“which customers are overdue”**
- **“why is cash down this month”**

Ensure each supported response includes the exact underlying **record ids** and/or **metric components** used to produce the answer.

If live-query performance is not sufficient on seeded multi-company datasets, introduce **summary/projection tables**, **database migrations**, and **background backfill/rebuild jobs** that can recompute aggregates for an existing company. Also update documentation to clearly state which metrics are computed live vs pre-aggregated and the refresh behavior.

Work within the existing **.NET modular monolith** architecture, preserving:
- strict **tenant scoping**
- deterministic query behavior
- CQRS-lite boundaries
- background worker patterns
- PostgreSQL as source of truth
- Redis only if already used for cache/coordination, not as source of truth

# Scope
In scope:

1. Identify the existing cash analytics and agent query flow in:
   - API endpoints/controllers
   - application query handlers
   - infrastructure repositories/EF/Dapper/SQL
   - background job infrastructure
   - docs and tests

2. Implement or refine deterministic handlers for the three supported agent cash queries so that:
   - outputs are stable for the same tenant/data/time context
   - all data access is tenant-scoped by `company_id`
   - each answer includes traceable source ids / metric breakdowns

3. Measure performance on seeded multi-company datasets.
   - Prefer **live computation first**
   - Only introduce pre-aggregation if needed to meet service thresholds already defined in code/docs/tests, or establish explicit thresholds in tests/docs if missing

4. If pre-aggregation is required:
   - add schema/migrations for summary/projection tables
   - implement aggregate rebuild/backfill jobs for an existing company
   - make rebuild idempotent and tenant-scoped
   - document refresh semantics

5. Add/update:
   - automated tests
   - performance tests/benchmarks
   - operational/documentation notes

Out of scope unless required by existing code structure:
- broad redesign of unrelated analytics modules
- new unsupported natural-language cash questions
- UI redesign beyond minimal exposure needed for tests/docs
- introducing new infrastructure platforms/services

# Files to touch
Start by inspecting and then updating the relevant files you actually find. Likely areas include:

- `src/VirtualCompany.Api/**`
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Domain/**`
- `src/VirtualCompany.Infrastructure/**`
- `tests/VirtualCompany.Api.Tests/**`
- `README.md`
- `docs/**`
- migration-related files under the infrastructure project
- any existing background worker/job registration files
- any seed-data or test-fixture files used for multi-company datasets

Also review:
- `docs/postgresql-migrations-archive/README.md`
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`

If present, expect to touch files such as:
- cash analytics query handlers
- agent orchestration/query intent handlers
- finance repositories
- SQL projections/materialized summary definitions
- hosted services / job runners / command handlers for backfill
- test fixtures for seeded finance data
- docs describing analytics computation strategy

# Implementation plan
1. **Discover current implementation**
   - Locate all code related to:
     - agent query handling
     - finance/cash analytics
     - dashboard cash metrics
     - overdue receivables/payables
     - monthly cash movement explanations
   - Identify whether the project uses EF Core, Dapper, raw SQL, or mixed access.
   - Find existing migration strategy and background job patterns.
   - Find any existing seeded multi-company performance tests or fixtures.

2. **Map the three supported queries to deterministic contracts**
   - Define or refine explicit application-layer query models/DTOs for:
     - `WhatShouldIPayThisWeek`
     - `WhichCustomersAreOverdue`
     - `WhyIsCashDownThisMonth`
   - Ensure outputs are structured and deterministic:
     - sorted consistently
     - tie-broken consistently
     - date-window logic explicit
     - timezone/company context explicit
   - Include provenance fields such as:
     - `recordIds`
     - `invoiceIds`
     - `billIds`
     - `paymentIds`
     - `metricComponents`
     - `calculationWindow`
     - `companyId`

3. **Enforce tenant scoping**
   - Verify every query path filters by `company_id`.
   - Add tests proving cross-tenant data is excluded.
   - Ensure any summary/projection tables also include `company_id` and appropriate indexes/constraints.

4. **Implement live-query path first**
   - Prefer direct transactional queries if they meet thresholds.
   - Optimize with:
     - targeted SQL
     - indexes
     - reduced joins
     - precomputed date boundaries in application code
     - explicit ordering and pagination/limits where appropriate
   - Keep explainability payloads attached to results.

5. **Add performance measurement**
   - Create or extend seeded multi-company test data representing realistic finance volume.
   - Add performance-oriented tests/benchmarks for:
     - dashboard cash queries
     - the three agent cash queries
   - Capture elapsed time and assert against agreed thresholds if they exist.
   - If thresholds are missing, infer a pragmatic testable threshold from existing conventions and document it clearly in code comments/docs.

6. **Decide whether pre-aggregation is required**
   - Only introduce summary/projection tables if live queries fail thresholds or are clearly non-viable.
   - If live queries pass, still document that metrics are computed live and no summary tables were required.
   - If pre-aggregation is required, continue with steps 7–10.

7. **Design summary/projection tables**
   - Create minimal tables needed for the slow paths only.
   - Candidate projections may include:
     - company/day cash movement summary
     - overdue receivables summary by customer
     - upcoming payable obligations by due week
     - monthly cash variance component summary
   - Keep them explainable:
     - include source linkage strategy
     - preserve enough detail to return underlying ids or metric components
   - Add indexes for:
     - `company_id`
     - date/month buckets
     - query-specific filters

8. **Add migrations**
   - Create database migrations in the project’s established style.
   - Include:
     - table creation
     - indexes
     - constraints
     - any rebuild metadata/state tables if needed
   - Make migration names explicit and traceable to TASK-26.4.2 if project conventions allow.

9. **Implement backfill/rebuild jobs**
   - Add a background job/command that rebuilds aggregates for an existing company.
   - Requirements:
     - tenant-scoped
     - idempotent
     - safe to rerun
     - chunked/batched if data volume warrants
     - observable via logs
   - Prefer a command/service shape like:
     - rebuild all summaries for one company
     - rebuild a date range/month range for one company
   - If the system has job coordination/locking patterns, use them.
   - Ensure failures are retryable and do not corrupt aggregate state.

10. **Wire query handlers to live or summarized sources**
   - Keep a clean abstraction so handlers do not care whether data is live or pre-aggregated.
   - If mixed mode is needed, document which metrics use which path.
   - Preserve deterministic output and provenance in both modes.

11. **Document computation strategy**
   - Update docs with:
     - supported cash agent queries
     - deterministic response rules
     - source id / metric component explainability
     - which metrics are live vs pre-aggregated
     - refresh/backfill behavior
     - operational guidance for rebuilding aggregates for an existing company

12. **Add/expand tests**
   - Unit tests:
     - date window logic
     - deterministic ordering
     - metric component calculations
   - Integration tests:
     - tenant scoping
     - query outputs include source ids/components
     - backfill job rebuilds expected summaries
     - existing company rebuild scenario
   - Performance tests:
     - seeded multi-company dashboard and agent query timings

13. **Implementation quality bar**
   - Keep changes cohesive and minimal.
   - Do not expose chain-of-thought.
   - Return concise rationale/explainability fields only.
   - Follow existing naming, DI registration, and project conventions.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects or filters for finance/analytics/agent queries, run them as well.

4. Validate acceptance criteria explicitly:
   - Supported agent query handlers return deterministic responses for:
     - “what should I pay this week”
     - “which customers are overdue”
     - “why is cash down this month”
   - Responses include underlying record ids or metric components.
   - If summary/projection tables were introduced:
     - migrations apply successfully
     - backfill/rebuild job can rebuild aggregates for an existing company
   - Documentation clearly states:
     - live vs pre-aggregated metrics
     - refresh behavior
   - Performance tests pass on seeded multi-company datasets within thresholds.

5. If migrations were added, verify:
   - migration generation/application is clean
   - schema includes tenant keys and indexes
   - rebuild job works from an empty summary state and from a rerun state

6. Include in your final implementation notes:
   - whether pre-aggregation was necessary
   - which queries remain live
   - which tables/jobs were added
   - what thresholds were validated
   - any assumptions made where the repo lacked explicit thresholds

# Risks and follow-ups
- **Risk: unclear existing finance schema**
  - Mitigation: inspect actual entities/tables first and align to current model rather than inventing a parallel one.

- **Risk: nondeterministic outputs due to time/date handling**
  - Mitigation: make company timezone and “current date” handling explicit and testable.

- **Risk: performance tests are flaky**
  - Mitigation: use seeded deterministic datasets, warm-up where appropriate, and assert realistic thresholds.

- **Risk: summary tables lose explainability**
  - Mitigation: preserve source linkage or enough metric-component detail to satisfy acceptance criteria.

- **Risk: backfill jobs are expensive or unsafe**
  - Mitigation: make them idempotent, tenant-scoped, batched, and retry-safe.

Follow-ups to note if not completed in this task:
- admin/ops endpoint or CLI for manual aggregate rebuilds
- incremental refresh strategy beyond full rebuild
- richer observability around aggregate freshness and lag
- additional cash analytics queries once the first three are stable and performant