# Goal
Implement `TASK-25.1.3` by adding an automated test suite for ranked match generation across supported entity types in the reconciliation matching engine.

The prompt should direct the coding agent to:
- verify ranked suggestion generation for:
  - bank transaction -> candidate payments
  - invoice -> candidate payments
  - bill -> candidate payments
- verify scoring behavior for:
  - exact amount match
  - near amount match
  - date proximity
  - reference similarity
  - counterparty similarity
  - no-match
- verify every suggestion includes:
  - normalized confidence score in `[0.00, 1.00]`
  - per-rule scoring details in the result payload
- verify tenant-configurable amount tolerance and date proximity windows are applied during scoring
- keep implementation aligned with the existing .NET solution structure and current architecture patterns

# Scope
In scope:
- Inspect the existing reconciliation/matching engine implementation and current test conventions
- Add or extend automated tests in the appropriate test project(s)
- Add any minimal test fixtures/builders/helpers needed to make tests readable and maintainable
- Cover acceptance criteria with deterministic unit and/or application-level tests
- Validate ranking order, confidence normalization, and scoring detail payload shape
- Validate tenant-specific configuration inputs affect scoring outcomes

Out of scope:
- Re-architecting the matching engine
- Large production refactors unless required to make the code testable
- UI, mobile, or API contract changes unless existing tests require small adjustments
- Introducing new infrastructure dependencies
- Broad schema or migration work unless the engine already depends on persisted tenant config and tests cannot be written otherwise

# Files to touch
Start by locating the reconciliation matching engine and its related contracts. Likely areas:
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Domain/**`
- `src/VirtualCompany.Infrastructure/**` if configuration or repositories are involved
- `tests/VirtualCompany.Api.Tests/**` for existing test patterns

Expected files to add or update:
- test files under `tests/VirtualCompany.Api.Tests/` in a feature-appropriate folder
- shared test helpers/builders if needed
- only minimal production files if necessary to expose test seams or fix uncovered issues

Before coding, identify:
- the service/class responsible for ranked match generation
- result DTO/domain model containing confidence and scoring details
- tenant settings/config model for amount tolerance and date windows
- existing test framework and assertion libraries in use

# Implementation plan
1. Explore the codebase
   - Search for terms like:
     - `reconciliation`
     - `matching`
     - `ranked`
     - `confidence`
     - `payment`
     - `invoice`
     - `bill`
     - `bank transaction`
   - Determine whether tests should be:
     - pure unit tests against a scorer/ranker service, or
     - higher-level application/API tests if the engine is only reachable through handlers/endpoints
   - Prefer the lowest-level deterministic tests that validate business behavior cleanly.

2. Map the production contracts
   - Identify supported input entity types and candidate payment models.
   - Confirm how scoring details are represented:
     - per-rule breakdown object
     - dictionary/map
     - list of rule results
   - Confirm how tenant configuration is supplied:
     - options/config object
     - tenant settings entity
     - repository lookup
     - request parameter

3. Design the test suite structure
   - Create a focused test class or set of classes, for example:
     - `RankedMatchGenerationTests`
     - `BankTransactionToPaymentMatchingTests`
     - `InvoiceAndBillPaymentLinkSuggestionTests`
   - Organize tests by scenario rather than implementation detail.
   - Use builders/factories for concise setup of:
     - bank transactions
     - invoices
     - bills
     - payment candidates
     - tenant scoring config

4. Implement acceptance-criteria coverage
   - Add tests for bank transaction -> payment ranking:
     - exact amount/date/reference/counterparty strong match ranks highest
     - near amount within tolerance is scored lower than exact but still suggested
     - date-only match produces a lower-confidence suggestion with rule details
     - reference-only match produces a lower-confidence suggestion with rule details
     - counterparty-only match produces a lower-confidence suggestion with rule details
     - no-match scenario returns either empty results or low-confidence non-matches according to current contract; assert the intended behavior from the implementation/acceptance criteria
   - Add tests for invoice -> payment suggestions
   - Add tests for bill -> payment suggestions

5. Validate confidence score behavior
   - For every returned suggestion, assert:
     - confidence exists
     - confidence is `>= 0.00` and `<= 1.00`
   - Where deterministic enough, assert relative ordering:
     - exact match confidence > near match confidence
     - multi-signal match confidence > single-signal match confidence
   - Avoid brittle assertions on exact decimals unless the engine uses stable fixed scoring rules.

6. Validate per-rule scoring details
   - Assert result payload includes rule-level details for relevant rules:
     - amount exact/near
     - date proximity
     - reference similarity
     - counterparty similarity
   - Assert details indicate both matched and non-matched rules where applicable, if supported by the contract.
   - If the payload includes weights/subscores/reasons, assert presence and sensible values rather than overfitting to formatting.

7. Validate tenant configurability
   - Add tests with different tenant configs, e.g.:
     - tolerance allows near amount match in one tenant
     - same candidate falls outside tolerance in another tenant
     - wider date window allows a suggestion that narrower window rejects or scores lower
   - Ensure tests prove config is actually applied during scoring, not just passed through.

8. Keep tests maintainable
   - Introduce reusable builders only if they reduce duplication.
   - Prefer explicit test data with readable values.
   - Name tests in behavior form, e.g.:
     - `Returns_ranked_payment_matches_for_bank_transaction_using_multiple_signals`
     - `Applies_tenant_amount_tolerance_when_scoring_near_matches`
     - `Includes_normalized_confidence_and_rule_breakdown_for_each_suggestion`

9. Make minimal production changes only if required
   - If the engine is hard to test due to hidden dependencies, add small seams such as:
     - injectable clock
     - injectable tenant settings provider
     - public/internal result models with test visibility
   - Do not change business behavior unless tests reveal a real defect; if so, fix it narrowly and document it in the final summary.

# Validation steps
1. Restore/build/test locally:
   - `dotnet build`
   - `dotnet test`

2. Confirm the new tests cover all required scenarios:
   - exact match
   - near amount match
   - date-only match
   - reference-only match
   - counterparty-only match
   - no-match
   - invoice-to-payment
   - bill-to-payment
   - confidence normalization
   - per-rule scoring details
   - tenant-configurable tolerance/window behavior

3. If there are existing reconciliation tests, ensure:
   - no regressions
   - naming and structure remain consistent with the suite

4. In your final implementation summary, include:
   - where the tests were added
   - what scenarios are covered
   - whether any production code was changed
   - any ambiguities discovered in the current contract/behavior

# Risks and follow-ups
- The matching engine may not yet expose a stable contract for per-rule scoring details; if so, add the smallest possible seam and note it.
- The current solution may place business tests in `Api.Tests`; if unit-style tests fit better elsewhere but no project exists, stay consistent with the existing repository pattern unless creating a new test project is clearly justified.
- No-match behavior may be ambiguous:
  - empty result set
  - low-confidence suggestions
  - filtered suggestions below threshold
  Determine current intended contract from code and acceptance criteria, then test that behavior explicitly.
- If tenant configuration is currently hardcoded, tests may expose a gap versus acceptance criteria; implement only the minimum needed to make configuration testable and applied.
- Follow-up recommendation: after this task, consider adding API-level contract tests for reconciliation endpoints if only service-level tests are added here.