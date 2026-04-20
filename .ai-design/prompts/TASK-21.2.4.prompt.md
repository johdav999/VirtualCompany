# Goal
Implement backlog task **TASK-21.2.4** by adding **end-to-end automated tests** that validate deterministic finance scenario generation flows through the **real finance domain tables, services, APIs, and existing UI/workflow surfaces** for the active company.

The prompt should direct the coding agent to verify that generated finance activity and anomalies are not isolated in a mock store, but instead appear in the same records and workflows used by the application’s finance pages and approval/review/history experiences.

# Scope
Focus only on test-oriented implementation needed to satisfy the task and acceptance criteria.

In scope:
- Add or extend end-to-end/integration-style tests in the existing `.NET` test projects, preferring the highest-level test harness already present.
- Use the real application composition where practical:
  - API host / test server
  - real application services
  - real persistence layer used by tests
  - existing finance workflow/query endpoints and services
- Cover deterministic seeded generation over one or more simulated day advances.
- Assert generated records are written into existing finance tables/services for the active company.
- Assert generated outputs are visible through existing finance review, approval, anomaly, and history workflows.
- Assert deterministic repeatability for same seed/configuration and simulated dates.
- Assert anomaly cadence is periodic and not daily.
- Assert scenario coverage across deterministic runs for invoice and policy cases listed in acceptance criteria.

Out of scope unless required to make tests pass:
- Large refactors of finance generation logic
- New product UX
- Replacing existing architecture
- Broad schema redesign
- Mock-only tests that do not exercise real persistence/workflows
- Adding a separate fake finance store

If gaps are discovered that block meaningful E2E coverage, make the **smallest production changes necessary** to expose stable test seams, deterministic configuration, or queryability.

# Files to touch
Start by inspecting and then modify only the minimum necessary set. Likely areas:

- `tests/VirtualCompany.Api.Tests/**`
- Any existing test host / fixture / WebApplicationFactory files under `tests/**`
- Finance-related API/controller/query tests under `tests/**`
- Seed/test utility files for company setup, auth, and simulated day advancement
- Potentially minimal production files under:
  - `src/VirtualCompany.Api/**`
  - `src/VirtualCompany.Application/**`
  - `src/VirtualCompany.Infrastructure/**`
  if needed to:
  - expose deterministic seed/configuration in tests
  - trigger simulated day advancement through existing APIs/services
  - query finance workflow/history/anomaly surfaces through supported endpoints/services

Before editing, search for:
- finance generation services
- simulated day advancement
- seed/configuration objects
- approval workflow endpoints/services
- anomaly/review/history endpoints/services
- existing integration/E2E test patterns
- tenant/company-scoped test helpers

# Implementation plan
1. **Discover the existing finance generation and test harness**
   - Search the solution for:
     - finance entities such as invoices, bills, transactions, balances, recurring expenses, approvals, tasks, history, anomalies
     - deterministic generation config / seed handling
     - simulated day advancement scheduler/service/command
     - finance review/approval/history APIs or queries
   - Identify the highest-level existing test style:
     - `WebApplicationFactory`
     - API integration tests
     - application-level integration tests with real DB
   - Reuse existing patterns rather than inventing a new harness.

2. **Map acceptance criteria to concrete assertions**
   Build a test matrix that covers:
   - generation enabled + simulated day advance writes to existing finance records
   - daily activity includes:
     - invoices
     - bills
     - transactions
     - balances
     - recurring expense instances
     - payment status changes
     - workflow tasks
     - approval requests where required
     - finance history events
   - deterministic invoice scenarios across a run:
     - pending invoice over approval threshold
     - different approval currency
     - partial payment
     - full or over-payment
     - due soon
     - overdue
     - normal low-risk pending
   - deterministic policy scenarios across a run:
     - amount just below threshold
     - exactly at threshold
     - just above threshold
     - requiring human approval
     - eligible without escalation
     - already approved or no longer actionable
   - anomaly cadence and types:
     - periodic, not every day
     - duplicate vendor charge
     - unusually high amount
     - category mismatch
     - missing document
     - suspicious payment timing
     - multiple payments
     - payment before expected state transition
   - visibility in existing workflows:
     - finance review
     - approval
     - anomaly
     - history
   - repeatability:
     - same seed/config => same records, anomaly days, workflow outcomes

3. **Add reusable test helpers**
   Create or extend helpers for:
   - provisioning a company/tenant context
   - enabling finance generation with deterministic seed/config
   - advancing simulated day(s)
   - querying finance records and workflow surfaces
   - normalizing record snapshots for deterministic comparison
   Keep helpers tenant-aware and avoid brittle UI-only assertions if API/service assertions already prove the same workflow visibility.

4. **Implement core end-to-end test coverage**
   Add a focused suite, ideally one class per concern, for example:
   - `FinanceScenarioGenerationEndToEndTests`
   - `FinanceAnomalyWorkflowEndToEndTests`
   - `FinanceDeterminismEndToEndTests`

   Recommended test cases:
   - **Generation writes to real finance records**
     - Arrange: active company, generation enabled, deterministic seed
     - Act: advance simulated day
     - Assert: records exist in existing finance tables/services and are retrievable through normal finance queries/endpoints
     - Assert: no mock-only path is being used
   - **Generated daily activity populates existing workflows**
     - Advance enough days to produce representative activity
     - Assert presence of invoices, bills, transactions, balances, recurring expense instances, payment status changes, tasks, approvals, history events
   - **Deterministic invoice scenario coverage**
     - Run a known deterministic window
     - Assert the resulting invoice set contains all required scenario categories
   - **Deterministic policy threshold coverage**
     - Assert records/workflow states cover below/exactly/above threshold and actionable/non-actionable approval states
   - **Anomalies are periodic and visible**
     - Advance multiple days
     - Assert anomaly days are a subset of simulated days, not all days
     - Assert each required anomaly type appears over the run
     - Assert anomalies/review signals are visible through existing anomaly/review/history/approval surfaces
   - **Same seed produces same outcomes**
     - Run twice with same seed/config and same simulated dates, ideally in isolated companies or reset state
     - Compare normalized snapshots of finance records, anomaly days, approval/workflow outcomes, and history events

5. **Prefer stable semantic assertions over fragile exact IDs**
   For deterministic comparisons:
   - compare business-relevant fields, not generated DB IDs or timestamps that are expected to differ
   - normalize/sort by simulated date, document number, vendor/customer, amount, currency, status, anomaly type, workflow outcome
   - if timestamps are deterministic by design, include them; otherwise compare date-level or event-type-level semantics

6. **Ensure tenant isolation in tests**
   - Use at least one assertion that generated records are scoped to the active company
   - If practical, create a second company and assert no leakage into its finance views/workflows

7. **Make minimal production changes only if necessary**
   If tests cannot reliably drive or observe the behavior, add the smallest possible changes such as:
   - exposing deterministic seed/config through existing options/commands
   - adding missing query endpoints already implied by finance pages/workflows
   - ensuring history/anomaly/approval records are persisted through existing services
   Do not add test-only business behavior unless unavoidable.

8. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries
   - Use application/API contracts rather than direct DB mutation in tests where possible
   - It is acceptable to inspect persistence for verification if that is the only reliable way to prove records land in existing tables
   - Maintain company/tenant scoping throughout

# Validation steps
1. Inspect and run the relevant tests locally:
   - `dotnet build`
   - `dotnet test`

2. Ensure new tests:
   - pass consistently
   - are deterministic
   - do not depend on execution order
   - do not rely on wall-clock timing if simulated dates are available

3. Verify each acceptance criterion is covered by at least one explicit assertion.

4. In the final implementation notes, include:
   - which tests were added
   - which acceptance criteria each test covers
   - any minimal production changes made to support deterministic E2E testing
   - any remaining gaps if a required finance workflow surface does not yet exist in the codebase

# Risks and follow-ups
- The repository may not yet contain full finance workflow surfaces or deterministic generation hooks; if so, add only the smallest enabling changes and clearly document them.
- Existing tests may be integration tests rather than true browser E2E; that is acceptable if they validate end-to-end behavior through real APIs/services/persistence and existing workflow endpoints.
- Deterministic comparison can be flaky if timestamps/IDs are not normalized; explicitly normalize snapshots.
- If anomaly or approval visibility is only available through UI composition and not API/query endpoints, prefer adding stable query coverage rather than brittle rendered markup assertions unless the project already has robust UI integration tests.
- If finance generation currently writes to a mock-only store, treat that as a production bug exposed by these tests and update the implementation minimally so tests validate the real persistence path.