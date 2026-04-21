# Goal

Implement backlog task **TASK-26.4.3 — Document live versus pre-aggregated cash metrics and validate query latency on seeded datasets** for story **US-26.4 Add agent query support and performance strategy for cash analytics**.

The coding agent should:

- document which cash analytics metrics are computed live versus pre-aggregated
- document refresh and rebuild behavior for any cache, summary, or projection data
- add or update performance tests proving dashboard and agent cash queries meet agreed thresholds on seeded multi-company datasets
- if summary/projection tables already exist or are required to satisfy thresholds, ensure migrations and backfill/rebuild paths are implemented and documented
- preserve deterministic, tenant-scoped behavior for supported finance agent queries and ensure explainability/source ids remain available

This task is not just docs-only. It must verify the implementation strategy against actual seeded datasets and leave the repo with executable evidence.

# Scope

In scope:

- Inspect current cash analytics implementation for:
  - dashboard cash queries
  - agent query handlers for:
    - “what should I pay this week”
    - “which customers are overdue”
    - “why is cash down this month”
- Identify for each metric/query component whether it is:
  - computed live from transactional tables
  - served from cache
  - served from summary/projection/aggregate tables
- Add/update system documentation in the repo.
- Add/update seeded performance tests for multi-company datasets.
- Add/update seed helpers/fixtures needed to generate realistic finance data volumes.
- If needed to meet thresholds:
  - add migrations for summary/projection tables
  - add background backfill/rebuild job(s)
  - add tests proving rebuild works for an existing company
- Ensure supported agent responses still include underlying record ids or metric components used.

Out of scope unless required to satisfy acceptance criteria:

- broad redesign of finance domain
- UI redesign
- unrelated agent orchestration changes
- introducing external infra beyond current architecture
- speculative optimization without measurement

# Files to touch

Start by inspecting and then update the most relevant files in these areas.

## Documentation

- `README.md`
- `docs/**` for architecture/system documentation
- `docs/postgresql-migrations-archive/README.md`
- add a focused doc if missing, e.g.:
  - `docs/finance/cash-metrics.md`
  - `docs/performance/cash-query-latency.md`

## Application/API/Infrastructure

Search for and update files related to:

- finance analytics query handlers
- dashboard KPI/cash query services
- agent query handlers / orchestration tools for finance questions
- tenant-scoped repositories and read models
- caching or Redis-backed query paths
- background jobs / hosted services / workers
- EF Core or SQL migration definitions
- seed data generators

Likely projects:

- `src/VirtualCompany.Api/**`
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Infrastructure/**`
- `src/VirtualCompany.Domain/**`

## Tests

- `tests/VirtualCompany.Api.Tests/**`
- any application/integration/performance test projects already present
- add a new test file/class if needed for:
  - seeded multi-company performance validation
  - aggregate rebuild/backfill validation
  - deterministic explainable finance query assertions

# Implementation plan

1. **Discover the current cash analytics paths**
   - Locate all handlers/services for:
     - dashboard cash metrics
     - “what should I pay this week”
     - “which customers are overdue”
     - “why is cash down this month”
   - Trace each query to its data source:
     - live transactional tables
     - cached values
     - summary/projection tables
   - Record current explainability behavior:
     - record ids returned
     - metric component breakdowns returned
   - Record current tenant scoping enforcement.

2. **Define a metric inventory**
   - Create a concise inventory table in docs with columns like:
     - metric/query
     - consumer surface
     - source tables
     - live vs pre-aggregated
     - cache layer if any
     - refresh trigger/frequency
     - rebuild/backfill mechanism
     - explainability payload
   - Include the three supported agent queries explicitly.
   - Include dashboard cash metrics that overlap with those queries.

3. **Measure before changing**
   - Identify existing seeded dataset support.
   - If insufficient, add deterministic seed generation for:
     - multiple companies
     - invoices / receivables
     - bills / payables
     - payments
     - monthly trend history
     - overdue customers
   - Ensure seed generation is deterministic by fixed clock/reference date and stable ids.
   - Add a baseline performance harness/test around the relevant query handlers, not just repository methods.

4. **Implement or confirm performance strategy**
   - If current live queries meet thresholds on seeded datasets:
     - keep implementation as-is
     - document that these metrics are live
     - document why pre-aggregation is not currently required
   - If thresholds are not met:
     - introduce minimal summary/projection tables only where needed
     - prefer tenant-scoped aggregate tables in PostgreSQL
     - implement background backfill/rebuild job(s) for an existing company
     - ensure refresh semantics are explicit:
       - event-driven update
       - scheduled refresh
       - on-demand rebuild
   - Keep CQRS-lite boundaries clean and avoid leaking raw SQL into unrelated layers.

5. **Preserve deterministic agent responses**
   - For each supported agent query, ensure the response remains deterministic on seeded data.
   - Ensure response payload includes:
     - underlying record ids, or
     - metric components used to produce the answer
   - If using pre-aggregates, preserve drill-through/source attribution by storing or reconstructing source references.

6. **Add migrations/backfill only if introduced**
   - If new summary/projection tables are added:
     - create migration(s)
     - add indexes needed for tenant-scoped query performance
     - implement backfill/rebuild command/job for an existing company
     - add tests proving rebuild correctness and idempotency
   - Document operational usage:
     - how to run rebuild
     - expected runtime characteristics
     - any locking or consistency caveats

7. **Add performance tests with explicit thresholds**
   - Use realistic but CI-safe thresholds.
   - Validate:
     - dashboard cash queries
     - each supported agent cash query
   - Tests should run on seeded multi-company datasets and assert:
     - tenant scoping correctness
     - deterministic outputs
     - latency threshold compliance
   - If exact timing assertions are flaky in CI, structure tests to:
     - warm up once
     - run multiple iterations
     - assert median/p95-like bounded values where practical
     - keep environment-sensitive thresholds documented

8. **Write final documentation**
   - Add a doc section that clearly answers:
     - which metrics are live?
     - which are pre-aggregated?
     - what refreshes when?
     - how stale can data be?
     - how can aggregates be rebuilt for an existing company?
     - what performance evidence exists and where are the tests?
   - Link docs from `README.md` if appropriate.

# Validation steps

1. **Code search and review**
   - Search for finance/cash query handlers and confirm all three supported agent queries are covered.
   - Search for any existing aggregate/summary/projection tables and cache usage.

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run full tests:
     - `dotnet test`
   - Run targeted tests for:
     - finance agent query determinism
     - explainability/source ids
     - aggregate rebuild/backfill if added
     - performance/latency validation on seeded datasets

4. **Performance verification**
   - Execute the new/updated performance tests locally.
   - Capture the measured timings in docs or test output comments.
   - Confirm thresholds are stated in code and documentation.

5. **Tenant isolation verification**
   - Confirm seeded multi-company tests prove no cross-company leakage.
   - Ensure all query paths filter by `company_id`.

6. **Documentation review**
   - Verify docs explicitly distinguish:
     - live metrics
     - cached metrics
     - pre-aggregated metrics
     - refresh behavior
     - rebuild behavior
   - Verify docs mention the supported agent queries and their explainability/source references.

7. **If migrations were added**
   - Apply migrations in test/dev flow.
   - Validate backfill/rebuild for an existing company.
   - Re-run query tests after rebuild to confirm correctness.

# Risks and follow-ups

- **Timing test flakiness:** CI environments vary. Prefer deterministic seeded data, warm-up runs, and conservative thresholds.
- **Hidden N+1/query-shape issues:** Performance may look acceptable on small data but fail on seeded multi-company volumes. Inspect generated SQL/index usage if thresholds fail.
- **Explainability regression:** Pre-aggregation can obscure source records. Preserve record ids or metric component lineage explicitly.
- **Tenant leakage risk:** Shared-schema multi-tenancy requires strict `company_id` filtering in every query and aggregate rebuild path.
- **Over-aggregation risk:** Do not introduce summary tables unless measurement shows they are needed.
- **Operational rebuild complexity:** If adding aggregates, ensure rebuild jobs are idempotent, tenant-scoped, and documented for existing companies.
- **Follow-up suggestion:** if no dedicated performance test project exists, consider a future task to separate benchmark-style tests from standard unit/integration suites while keeping one CI-safe latency gate in the main test pipeline.