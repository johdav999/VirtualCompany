# Goal
Implement backlog task **TASK-26.4.1 — Implement finance agent query resolvers for cash planning, overdue customers, and cash movement explanations** for story **US-26.4 Add agent query support and performance strategy for cash analytics**.

Deliver deterministic, tenant-scoped finance agent query handling in the .NET modular monolith for these exact user intents:

- **"what should I pay this week"**
- **"which customers are overdue"**
- **"why is cash down this month"**

Each supported query must:

- resolve against tenant-scoped finance data only
- return deterministic structured results, not free-form LLM-derived logic
- include the underlying **record ids** and/or **metric components** used to produce the answer
- support explainability and auditability
- meet performance expectations on seeded multi-company datasets

If current live-query performance is insufficient, introduce summary/projection tables plus:

- EF/database migrations
- background backfill/rebuild jobs for existing companies
- documentation of live vs pre-aggregated metrics and refresh behavior
- performance tests proving service thresholds

Use existing architecture patterns: CQRS-lite, tenant-scoped application services, background workers, audit/explainability-friendly outputs, and no direct model/database shortcuts.

# Scope
In scope:

- Discover the existing finance/cash analytics domain, query pipeline, agent query handling, and dashboard query paths
- Add or extend deterministic finance query resolvers in the application/backend
- Support the 3 required finance agent queries with stable intent matching or explicit resolver routing
- Ensure outputs include:
  - recommended payable items for this week with source record ids and calculation basis
  - overdue customers with source record ids and aging basis
  - cash-down explanation with metric components and source ids where applicable
- Enforce company/tenant scoping end-to-end
- Add/extend DTOs, handlers, services, repository queries, and API/tool contracts as needed
- Add summary/projection persistence only if needed for performance or acceptable architecture
- Add migrations and backfill/rebuild job if summary/projection tables are introduced
- Add/update system documentation for:
  - live-computed vs pre-aggregated cash metrics
  - refresh cadence/behavior
  - rebuild/backfill behavior
- Add automated tests:
  - unit tests for deterministic resolver behavior
  - integration tests for tenant scoping and explainability payloads
  - performance-oriented tests/benchmarks on seeded multi-company datasets where the repo’s test patterns allow

Out of scope unless required by existing patterns:

- broad natural-language intent platform redesign
- unrelated finance dashboard redesign
- mobile UI work
- speculative new agent personas
- replacing existing orchestration architecture
- introducing external analytics infrastructure unless absolutely necessary

# Files to touch
Start by inspecting and then update the relevant files you find. Likely areas include:

- `src/VirtualCompany.Api/**`
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Domain/**`
- `src/VirtualCompany.Infrastructure/**`
- `tests/VirtualCompany.Api.Tests/**`
- `README.md`
- `docs/**`

Potential concrete targets, depending on actual repo structure:

- agent query controller/endpoints
- finance analytics query handlers
- application contracts/DTOs for agent responses
- finance repositories / EF Core query services
- tenant context enforcement code
- background worker/job registration
- migration files and DbContext mappings
- seeded dataset/performance test fixtures
- architecture or operations docs for cash analytics refresh strategy

If projection tables are needed, also touch:

- infrastructure persistence mappings
- migration scripts / EF migrations
- background backfill job implementation
- job scheduling/worker registration
- docs describing refresh and rebuild semantics

Do not invent new top-level architectural layers if existing modules already provide the right extension points.

# Implementation plan
1. **Inspect current finance and agent-query architecture**
   - Find existing agent query entry points, orchestration tool contracts, finance analytics services, and dashboard query handlers.
   - Identify where deterministic query resolution belongs:
     - API layer
     - application query handlers
     - internal tool/service used by agent orchestration
   - Identify current finance entities/tables for:
     - payables / bills / invoices to pay
     - receivables / customer invoices
     - cash balances / cash transactions / monthly metrics
   - Identify current tenant scoping conventions and reuse them.

2. **Map the 3 supported intents to explicit deterministic resolvers**
   - Implement explicit resolver routing for the exact supported queries, avoiding non-deterministic interpretation.
   - Prefer a stable enum/command/query model such as:
     - `WhatShouldIPayThisWeek`
     - `WhichCustomersAreOverdue`
     - `WhyIsCashDownThisMonth`
   - If there is already an agent tool or query abstraction, extend it rather than creating a parallel path.
   - Ensure unsupported phrasing fails safely or routes only through existing supported intent logic.

3. **Design structured response contracts with explainability**
   - Define response DTOs that include:
     - human-readable summary
     - deterministic result items
     - source record ids
     - metric components / calculation inputs
     - as-of date / period boundaries
   - Example expectations:
     - **Pay this week**:
       - payable id
       - vendor/supplier id if available
       - due date
       - amount
       - priority/reason fields
       - calculation window used
     - **Customers overdue**:
       - receivable/invoice id
       - customer id
       - due date
       - days overdue
       - outstanding amount
       - aging bucket
     - **Why is cash down this month**:
       - current month vs prior comparison
       - inflow/outflow components
       - top contributing categories or movements
       - source transaction ids and/or aggregate component ids
       - explanation assembled from deterministic metric deltas
   - Keep outputs concise but machine-verifiable.

4. **Implement tenant-scoped finance query services**
   - Add or extend application services/handlers that query finance data by `company_id`.
   - Ensure all repository/database access is tenant-filtered before aggregation.
   - Avoid any cross-tenant joins or cache leakage.
   - Reuse CQRS-lite patterns already present in the solution.

5. **Implement resolver: “what should I pay this week”**
   - Define deterministic business logic based on available finance data.
   - Prefer a documented rule set, for example:
     - unpaid/open payables due within the current tenant-local week
     - optionally overdue unpaid payables first
     - sorted by due date then amount or configured priority
   - Return:
     - selected payable records
     - source ids
     - date window used
     - any ranking/prioritization components
   - Document assumptions in code comments and docs if domain rules are inferred from existing schema.

6. **Implement resolver: “which customers are overdue”**
   - Query open receivables with due date before tenant-local today/as-of date.
   - Compute deterministic aging fields:
     - days overdue
     - aging bucket
     - outstanding amount
   - Return customer and invoice/receivable identifiers used to produce the answer.
   - Ensure ordering is deterministic, e.g. by days overdue desc then amount desc then id.

7. **Implement resolver: “why is cash down this month”**
   - Build a deterministic month-over-month explanation from finance metrics.
   - Compare current month-to-date vs prior comparable period or prior full month based on existing dashboard semantics; use the same definition everywhere and document it.
   - Compute component deltas such as:
     - cash inflows down
     - collections down
     - expenses/payments up
     - payroll/tax/rent/other categories up
     - one-off large movements
   - Return:
     - summary explanation generated from deterministic ranked components
     - metric component list with values and deltas
     - source transaction ids or aggregate source ids
   - If current data model lacks efficient support, introduce a summary/projection model.

8. **Add summary/projection tables only if justified**
   - First assess whether live queries can satisfy thresholds with proper indexing/query shaping.
   - If not, introduce focused summary tables for cash analytics, such as monthly/company/category aggregates or aging snapshots.
   - Keep projections minimal and rebuildable from source-of-truth transactional tables.
   - Add:
     - schema mappings
     - migration(s)
     - rebuild/backfill job for an existing company
     - idempotent refresh logic
     - documentation of refresh timing and staleness behavior

9. **Implement background backfill/rebuild job if projections are added**
   - Add a background worker/job entry point that can rebuild aggregates for an existing company.
   - Ensure:
     - tenant-scoped execution
     - idempotency
     - safe retry behavior
     - observability/logging
   - If the codebase has outbox/job conventions, follow them.
   - Add tests for rebuild correctness.

10. **Document live vs pre-aggregated metrics**
    - Update docs to clearly state:
      - which cash metrics are computed live
      - which are pre-aggregated
      - refresh triggers/cadence
      - expected staleness
      - how to rebuild for an existing company
    - Keep documentation aligned with actual implementation, not aspirational.

11. **Add automated tests**
    - Unit tests:
      - intent routing/resolver selection
      - deterministic ordering and calculations
      - metric component generation
    - Integration tests:
      - tenant isolation
      - exact response payload includes source ids/components
      - backfill/rebuild behavior if projections exist
    - Performance tests:
      - seed multiple companies with realistic finance data volume
      - measure dashboard and agent cash query completion times
      - assert agreed thresholds if already defined in repo/docs; if not, encode the nearest existing benchmark pattern and document assumptions

12. **Preserve explainability and auditability**
    - Ensure the response contracts are suitable for downstream audit/explainability views.
    - If existing audit source-reference persistence exists, integrate with it rather than duplicating logic.
    - Do not expose chain-of-thought; only operational rationale and source references.

13. **Keep implementation incremental and reviewable**
    - Prefer small cohesive commits:
      1. contracts + routing
      2. pay-this-week resolver
      3. overdue-customers resolver
      4. cash-down explanation
      5. projections/migrations/jobs if needed
      6. docs + tests

# Validation steps
1. **Build and test baseline**
   - Run:
     - `dotnet build`
     - `dotnet test`

2. **Verify deterministic resolver behavior**
   - Add/execute tests proving the same seeded data returns the same ordered results for:
     - pay this week
     - overdue customers
     - cash down this month

3. **Verify tenant isolation**
   - Seed at least 2 companies with overlapping-looking finance records.
   - Confirm each query only returns the active company’s data and source ids.

4. **Verify explainability payloads**
   - Confirm each supported query response includes:
     - source record ids and/or metric components
     - as-of date or comparison period
     - deterministic summary fields

5. **Verify projection behavior if introduced**
   - Apply migrations.
   - Run backfill/rebuild for an existing company.
   - Confirm aggregates match source transactional data.
   - Re-run rebuild to verify idempotent behavior.

6. **Verify documentation**
   - Ensure docs explicitly identify:
     - live metrics
     - pre-aggregated metrics
     - refresh behavior
     - rebuild/backfill instructions

7. **Verify performance**
   - Run the added performance/benchmark/integration tests on seeded multi-company datasets.
   - Confirm dashboard and agent cash queries meet the repo’s agreed thresholds.
   - If thresholds are not yet codified, document the measured results and any assumptions in the test/docs.

8. **Final regression**
   - Re-run:
     - `dotnet build`
     - `dotnet test`
   - Include a concise summary of:
     - files changed
     - whether projections were introduced
     - migration names
     - test coverage added
     - measured performance results

# Risks and follow-ups
- **Ambiguous finance schema**: The repo may not yet have complete payables/receivables/cash movement models. If so, infer minimally from existing structures and document assumptions clearly.
- **Intent matching drift**: Free-form NLP matching can break determinism. Prefer explicit supported-intent routing and exact/normalized phrase handling.
- **Time-period ambiguity**: “this week” and “this month” must use tenant/company timezone and documented period boundaries.
- **Cash explanation semantics**: Month-over-month comparison logic can vary. Reuse existing dashboard semantics if present; otherwise define one canonical rule and document it.
- **Performance trade-offs**: Live queries may be acceptable with indexes/query shaping; do not add projections prematurely.
- **Projection staleness**: If summary tables are added, refresh timing and stale-read behavior must be explicit in docs and tests.
- **Backfill cost**: Rebuild jobs on large tenants may need batching and idempotent checkpoints.
- **Test realism**: Performance tests should use realistic seeded multi-company data, not tiny fixtures that hide query issues.
- **Follow-up candidate**: If exact service thresholds are missing from the repo, propose a follow-up task to codify SLOs/thresholds in docs and automated tests.