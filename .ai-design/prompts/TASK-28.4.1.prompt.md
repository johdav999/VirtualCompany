# Goal
Implement deterministic integration/regression tests for core financial cause-effect flows under `TASK-28.4.1` so the system verifies end-to-end finance progression behavior for:

- invoice issuance, payment, and overdue progression
- bill receipt, payment, and overdue progression
- recurring costs and asset purchases
- deterministic replayability from identical seed/profile/start-date inputs

The prompt should direct the coding agent to add tests only, using the existing .NET test stack and current architecture, while minimizing production-code changes unless required to expose stable test seams.

# Scope
Focus on automated integration tests in the existing .NET solution, most likely under `tests/VirtualCompany.Api.Tests`, covering the acceptance criteria exactly.

In scope:
- Discover the current finance domain/API/application entry points already used for:
  - invoice creation/issuance
  - invoice payment
  - bill creation/receipt
  - bill payment
  - recurring cost generation/execution
  - asset purchase handling
  - time progression / scheduled jobs / overdue evaluation
  - seeded simulation/profile/start-date replay
- Add integration tests that verify financial state transitions and derived summaries, not just HTTP status codes.
- Reuse existing test infrastructure, fixtures, factories, seeded DB setup, worker/scheduler hooks, and deterministic clock abstractions if present.
- If necessary, make minimal production changes to support deterministic testing:
  - injectable clock/time provider
  - stable seed/profile/start-date entry points
  - test-safe scheduler execution hook
  - query/read model access for receivables, payables, cash, overdue state, event timeline, and finance summaries

Out of scope:
- Broad refactors of finance modules
- New product features beyond what is needed to test existing behavior
- UI tests
- Mobile changes
- Rewriting unrelated tests

# Files to touch
Prefer touching only the smallest necessary set after repo inspection. Expected areas:

- `tests/VirtualCompany.Api.Tests/...`
  - add new integration test class(es) for finance progression flows
  - add/extend shared test fixtures, builders, helpers, or assertions if needed
- Potentially existing test infrastructure files in:
  - `tests/VirtualCompany.Api.Tests/Fixtures/...`
  - `tests/VirtualCompany.Api.Tests/Infrastructure/...`
  - `tests/VirtualCompany.Api.Tests/Helpers/...`

Only if required for deterministic integration testing, minimally touch:
- `src/VirtualCompany.Api/...`
- `src/VirtualCompany.Application/...`
- `src/VirtualCompany.Infrastructure/...`

Likely production seams to inspect before changing anything:
- clock/time abstraction
- background worker or scheduler invocation path
- finance summary query handlers/read models
- simulation/replay seed handling
- event timeline persistence/query path

Do not invent file paths blindly; inspect the solution and update the actual relevant files.

# Implementation plan
1. Inspect the solution structure and identify the existing finance workflow surface area.
   - Find current entities, commands, endpoints, handlers, and tests related to invoices, bills, payments, cash, receivables, payables, recurring costs, asset purchases, overdue logic, and simulation/replay.
   - Determine whether integration tests are API-level, application-level with real infrastructure, or WebApplicationFactory-based.

2. Identify the canonical assertions for each acceptance criterion.
   - Invoice issuance increases receivables.
   - Invoice payment increases cash and reduces receivables.
   - Bill receipt increases payables.
   - Bill payment decreases cash and reduces payables.
   - Overdue invoice/bill states emerge only after due date passes with unpaid balance.
   - Recurring costs and asset purchases create expected payable or cash effects.
   - Same seed/profile/start date yields identical event timelines and derived finance summaries.

3. Reuse existing deterministic test infrastructure where available.
   - Prefer existing fake clock / `TimeProvider` / injectable date service.
   - Prefer existing seeded database fixture and transaction reset approach.
   - Prefer existing scheduler/background job execution helper over manual DB mutation.
   - Prefer existing simulation bootstrap APIs over direct repository writes.

4. Add integration tests for the core finance cause-effect flows.
   Create one or more test classes with clear scenario names, for example:
   - `Invoice_Issuance_Increases_Receivables`
   - `Invoice_Payment_Increases_Cash_And_Reduces_Receivables`
   - `Bill_Receipt_Increases_Payables`
   - `Bill_Payment_Decreases_Cash_And_Reduces_Payables`
   - `Unpaid_Invoice_Becomes_Overdue_After_Due_Date`
   - `Unpaid_Bill_Becomes_Overdue_After_Due_Date`
   - `Recurring_Costs_Produce_Expected_Payable_Or_Cash_Effects`
   - `Asset_Purchases_Produce_Expected_Payable_Or_Cash_Effects`
   - `Same_Seed_Profile_And_StartDate_Produce_Identical_Timeline_And_Finance_Summary`

5. Make assertions against persisted/derived finance state, not implementation details.
   Prefer validating through the same read/query surfaces the app uses, such as:
   - finance summary endpoints/queries
   - account balance snapshots
   - receivables/payables totals
   - invoice/bill status fields
   - event timeline records
   - derived summary DTOs
   Avoid brittle assertions on internal private methods or incidental log output.

6. For overdue progression tests, drive time forward through supported mechanisms.
   - Set initial due dates relative to a controlled start date.
   - Advance the fake clock or invoke the time progression/scheduler path.
   - Assert pre-due state is not overdue.
   - Assert post-due unpaid state is overdue.
   - If partial payments are supported, ensure overdue depends on unpaid balance, not merely due date.

7. For recurring costs and asset purchases, test the actual domain effect path.
   - If recurring costs create bills/payables, assert payable increase.
   - If they are immediate cash expenses, assert cash decrease.
   - If asset purchases can be cash or payable-backed, assert the expected configured effect for the scenario used.
   - Use deterministic fixture data and explicit amounts.

8. For replayability, verify strict determinism.
   - Run the same seed/profile/start-date scenario twice in isolated test contexts.
   - Capture normalized event timeline output and derived finance summary output.
   - Assert equality on stable fields and ordering.
   - If IDs/timestamps are generated differently but semantically equivalent, normalize only if the product contract treats them as non-deterministic; otherwise prefer full equality.
   - If determinism currently fails because of uncontrolled time/randomness, add the smallest possible seam to inject deterministic providers.

9. Keep production changes minimal and justified.
   If tests reveal missing seams:
   - introduce or wire an existing clock abstraction consistently
   - expose a test-safe scheduler trigger
   - ensure seeded simulation uses injected RNG/seed source
   - avoid changing business behavior unless fixing a real determinism bug

10. Maintain test quality.
   - Use Arrange/Act/Assert structure.
   - Keep each test independent and isolated.
   - Use descriptive names tied to acceptance criteria.
   - Avoid sleeps, real wall-clock time, and flaky async timing.
   - Prefer explicit amounts/dates over generated fuzz data.

11. Add concise comments only where behavior is non-obvious.
   Do not over-comment straightforward test setup.

# Validation steps
1. Restore/build and inspect baseline:
   - `dotnet build`
2. Run the targeted test project during development:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`
3. Run the full suite before finishing:
   - `dotnet test`
4. Confirm the new tests cover all acceptance criteria:
   - invoice issuance -> receivables up
   - invoice payment -> cash up, receivables down
   - bill receipt -> payables up
   - bill payment -> cash down, payables down
   - overdue progression after time advancement and unpaid balance
   - recurring costs and asset purchases -> expected payable/cash effects
   - same seed/profile/start date -> identical timeline and finance summary
5. If deterministic replay tests required production seam changes, rerun the same replay test multiple times locally to ensure no flakiness.
6. Ensure formatting and analyzer warnings remain clean if the repo enforces them.

# Risks and follow-ups
- The finance domain surface may not yet expose stable read models for receivables/payables/cash summaries; minimal query/test seam additions may be required.
- Overdue progression may currently depend on real time or background workers, which can cause flaky tests unless a controllable clock/scheduler seam exists.
- Replay determinism may fail due to uncontrolled randomness, generated IDs, unordered queries, or ambient timestamps; fix only the smallest root causes needed for stable regression coverage.
- If recurring costs and asset purchases have multiple accounting modes, choose scenarios that match current business rules and document assumptions in test names.
- If there is no existing integration-test host/fixture, create one consistent with current test conventions rather than inventing a parallel pattern.
- Follow-up work may be needed later to expand coverage to partial payments, cancellations, refunds, write-offs, and multi-tenant isolation of finance simulations.