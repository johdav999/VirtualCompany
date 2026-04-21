# Goal
Implement automated tests for `TASK-26.1.4` covering settlement posting and duplicate replay scenarios for story `US-26.1 Implement ledger posting for incoming and outgoing cash events with source traceability`.

The coding agent should add or update tests so the system proves that:

- settled customer payments create exactly one balanced journal entry:
  - debit cash
  - credit accounts receivable
  - amount equals settled amount
- settled supplier payments create exactly one balanced journal entry:
  - debit accounts payable
  - credit cash
  - amount equals settled amount
- each posting stores traceable source metadata:
  - source type
  - source id
  - company id
  - posting timestamp
- replaying/reprocessing the same settled payment or bank event is idempotent:
  - no duplicate journal entries are created
  - existing posting result is returned
- automated tests explicitly cover:
  - incoming payment posting
  - outgoing payment posting
  - partial settlement handling
  - idempotent replay behavior

# Scope
Focus only on test implementation and any minimal test-support refactoring required to make the tests reliable and readable.

In scope:

- locate the ledger posting application/service/domain flow for payment settlement and bank event posting
- identify existing test projects, fixtures, builders, and helpers
- add automated tests at the most appropriate level already used by the codebase:
  - unit tests if posting logic is isolated there
  - integration/application tests if persistence/idempotency/source-reference behavior requires database-backed verification
- verify balanced journal entry creation and line composition
- verify source traceability fields on created journal entries/postings
- verify idempotent replay returns existing result and does not insert duplicates
- verify partial settlement behavior using settled amount, not original invoice/payment total

Out of scope unless absolutely necessary for tests to compile or reflect current behavior:

- redesigning posting architecture
- changing production business rules beyond minimal bug fixes exposed by the tests
- adding unrelated accounting features
- broad schema changes not required by current acceptance criteria

If production code defects are discovered while writing tests, make the smallest targeted fix necessary and keep it clearly tied to the failing test.

# Files to touch
Start by inspecting these likely locations and adjust based on actual repository structure:

- `tests/VirtualCompany.Api.Tests/**/*`
- `src/VirtualCompany.Application/**/*`
- `src/VirtualCompany.Domain/**/*`
- `src/VirtualCompany.Infrastructure/**/*`

Likely test targets to find:

- settlement/payment posting handlers or services
- journal entry creation services
- idempotency/replay guards
- source reference/value object/entity definitions
- payment settlement event handlers
- bank event posting handlers

Prefer touching:

- existing test classes for finance/accounting/ledger posting if present
- shared test fixtures/builders for companies, payments, journal entries, and accounts
- only the minimal production files required if tests reveal missing behavior or inaccessible seams

Avoid creating parallel test patterns if the repository already has an established style.

# Implementation plan
1. **Discover the posting flow**
   - Search for terms like:
     - `JournalEntry`
     - `Settlement`
     - `Settled`
     - `Payment`
     - `AccountsReceivable`
     - `AccountsPayable`
     - `Cash`
     - `SourceReference`
     - `Idempotent`
     - `Replay`
     - `BankEvent`
   - Identify:
     - command/handler/service responsible for posting settled incoming payments
     - command/handler/service responsible for posting settled outgoing payments
     - persistence model for journal entries and lines
     - where source metadata is stored
     - how duplicate replay is prevented

2. **Choose the right test level**
   - If journal posting logic is pure and source metadata is exposed in domain objects, add focused unit tests.
   - If idempotency depends on persistence constraints or repository lookups, add integration tests using the project’s existing test infrastructure.
   - Prefer the lowest level that still verifies:
     - exact single-entry creation
     - balanced lines
     - persisted source reference
     - replay behavior

3. **Add test coverage for incoming settlement posting**
   - Create a test for a settled customer payment.
   - Assert:
     - exactly one journal entry is created
     - exactly two expected lines exist unless the domain uses a different balanced representation
     - debit cash equals settled amount
     - credit accounts receivable equals settled amount
     - total debits equal total credits
     - source reference contains source type, source id, company id, posting timestamp

4. **Add test coverage for outgoing settlement posting**
   - Create a test for a settled supplier payment.
   - Assert:
     - exactly one journal entry is created
     - debit accounts payable equals settled amount
     - credit cash equals settled amount
     - total debits equal total credits
     - source reference fields are populated correctly

5. **Add partial settlement test**
   - Create a scenario where payment/invoice total differs from settled amount.
   - Mark only part of the payment as settled.
   - Assert posting uses the settled amount only.
   - Assert only one journal entry is created for that settlement event.
   - If the model supports multiple settlements over time, keep the test aligned to the acceptance criterion and current implementation rather than inventing new behavior.

6. **Add idempotent replay tests**
   - Reprocess the same settled payment or bank event twice.
   - Assert:
     - second processing does not create a new journal entry
     - returned result references the original posting
     - persisted journal entry count remains `1` for that source
   - If both payment and bank event replay paths exist, cover both; otherwise cover the implemented replay path and note any uncovered path in follow-ups.

7. **Assert source traceability explicitly**
   - Do not stop at “journal entry exists”.
   - Verify the created posting stores:
     - source type
     - source id
     - company id
     - posting timestamp
   - If timestamp precision makes exact equality brittle, assert non-default value and expected temporal bounds.

8. **Keep tests deterministic**
   - Use fixed IDs, fixed timestamps, and explicit amounts where possible.
   - Avoid assertions that depend on unordered collections unless sorted first.
   - Reuse existing builders/fixtures instead of ad hoc setup if available.

9. **Minimal production fixes only if needed**
   - If tests expose missing idempotency or missing source metadata persistence, implement the smallest correction.
   - Preserve existing architecture boundaries:
     - application orchestration in Application
     - domain rules in Domain
     - persistence in Infrastructure

10. **Document intent in test names**
   - Use descriptive names such as:
     - `SettledCustomerPayment_CreatesSingleBalancedCashToReceivablesJournalEntry`
     - `SettledSupplierPayment_CreatesSingleBalancedPayablesToCashJournalEntry`
     - `PartialSettlement_PostsSettledAmountOnly`
     - `ReprocessingSameSettledPayment_DoesNotCreateDuplicateJournalEntry`
     - `ReprocessingSameBankEvent_ReturnsExistingPostingResult`

# Validation steps
Run the narrowest relevant tests first, then the broader suite.

Suggested workflow:

1. restore/build
   - `dotnet build`

2. run targeted tests for the affected project/class
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --filter FullyQualifiedName~Settlement`
   - if naming differs, use filters for `Journal`, `Ledger`, `Payment`, or the specific test class names

3. run the full test project
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

4. if production code was touched, run the full solution tests if feasible
   - `dotnet test`

Validation checklist:

- tests fail before the fix and pass after, if a production bug was found
- no duplicate journal entries are created on replay
- journal entries are balanced in all covered scenarios
- source traceability assertions are explicit and passing
- partial settlement test proves settled amount drives posting amount
- test code follows existing repository conventions and compiles cleanly

# Risks and follow-ups
- The repository may not yet have a dedicated finance/accounting test area; if so, place tests near the application/API flow that currently owns posting behavior and keep structure easy to extract later.
- Idempotency may currently be enforced implicitly by database state rather than an explicit contract; tests should verify observable behavior, not overfit implementation details.
- If bank event replay is mentioned in acceptance criteria but no bank event posting path exists yet, cover the implemented replay path and note the missing bank-event-specific test as a follow-up only if truly absent.
- Timestamp assertions can be flaky if the code uses `DateTime.UtcNow` directly; prefer existing clock abstractions if available.
- If account identifiers are configurable rather than hardcoded, assert semantic account role/category rather than brittle raw IDs where possible.
- Follow-up recommendation: if not already present, add reusable finance test builders/fixtures for payments, settlements, and journal entries to reduce duplication in future ledger-posting tasks.