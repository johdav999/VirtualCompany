# Goal
Implement deterministic automated regression tests for `TASK-28.4.2` that prove seeded scenario generation and event execution are replayable and financially correct.

The coding agent should add tests that verify:
- invoice issuance increases receivables
- invoice payment increases cash and reduces receivables
- bill receipt increases payables
- bill payment decreases cash and reduces payables
- overdue invoice and bill states emerge from due dates plus unpaid balances after time progression
- recurring costs and asset purchases create the expected payable or cash effects
- the same seed, profile, and start date always produce identical event timelines and derived finance summaries

Keep the implementation aligned with the existing .NET modular-monolith structure and prefer test coverage over production refactors unless a small seam is required to make deterministic behavior testable.

# Scope
In scope:
- Inspect existing scenario generation, seeded randomness, event execution, and finance summary logic
- Add or extend automated tests in the appropriate test project(s)
- Add minimal production changes only if needed to expose deterministic seams or stable comparison surfaces
- Verify deterministic replay at both:
  - event timeline level
  - derived finance summary level
- Verify core financial cause-effect flows from generated/executed events

Out of scope:
- New product features or UI work
- Broad architecture refactors
- Rewriting finance engine behavior unless tests expose a real defect
- Non-deterministic snapshot/golden-file infrastructure unless already present and clearly appropriate

# Files to touch
Start by inspecting these areas and then update the exact files you find relevant:

- `tests/VirtualCompany.Api.Tests/` for existing integration or scenario tests
- Any test projects covering application/domain finance simulation logic
- `src/VirtualCompany.Application/` for scenario generation, orchestration, or finance summary services
- `src/VirtualCompany.Domain/` for financial event/domain models and deterministic rules
- `src/VirtualCompany.Infrastructure/` only if seeded randomness or clock abstractions live there

Likely file categories to touch:
- existing seeded scenario generation tests
- existing finance event execution tests
- shared test fixtures/builders for seeded profiles/start dates
- minimal production files exposing stable ordering/comparison helpers if current outputs are hard to assert deterministically

Do not invent new layers if existing test infrastructure already supports this.

# Implementation plan
1. **Discover current deterministic simulation surface**
   - Find the code responsible for:
     - seeded scenario generation
     - event timeline creation
     - event execution/application to financial state
     - finance summary derivation
   - Identify current abstractions for:
     - random/seed handling
     - time progression/start date
     - profile inputs
   - Identify whether there are already tests for replayability or finance cause-effect flows.

2. **Map acceptance criteria to concrete test cases**
   Create a compact but explicit test matrix covering:
   - invoice issued -> receivables increases
   - invoice paid -> cash increases and receivables decreases
   - bill received -> payables increases
   - bill paid -> cash decreases and payables decreases
   - unpaid invoice past due date after time progression -> overdue invoice state
   - unpaid bill past due date after time progression -> overdue bill state
   - recurring cost -> expected payable or cash impact based on current domain behavior
   - asset purchase -> expected payable or cash impact based on current domain behavior
   - same seed + same profile + same start date -> identical event timeline
   - same seed + same profile + same start date -> identical derived finance summary

3. **Prefer deterministic assertions over brittle snapshots**
   - Assert on stable, semantic fields rather than transient/internal metadata.
   - If event ordering is expected, compare normalized ordered projections of events.
   - If summaries contain timestamps/IDs generated at runtime, normalize or exclude those fields in comparisons.
   - If necessary, introduce a small helper in tests to project events/summaries into deterministic comparable records.

4. **Add replay tests**
   Implement tests that run the same scenario twice with identical:
   - seed
   - profile
   - start date

   Then compare:
   - generated event timeline contents and order
   - derived finance summary outputs

   Also consider adding one guard test that different seeds produce different outputs if the domain already guarantees meaningful variation, but only if this is already stable and low-risk. Do not make this required if it would be flaky.

5. **Add finance cause-effect execution tests**
   For each core event type, execute the event(s) against the financial state and assert deltas:
   - invoice issuance:
     - receivables delta > 0 by expected amount
     - cash unchanged unless domain says otherwise
   - invoice payment:
     - cash delta > 0 by expected amount
     - receivables delta < 0 by expected amount
   - bill receipt:
     - payables delta > 0 by expected amount
   - bill payment:
     - cash delta < 0 by expected amount
     - payables delta < 0 by expected amount

   Use exact expected amounts where possible, not just sign checks.

6. **Add overdue-state progression tests**
   - Build scenarios with due dates before and after the simulated current date.
   - Progress time explicitly using the domain’s existing clock/date mechanism.
   - Assert overdue states emerge only when:
     - due date has passed
     - unpaid balance remains
   - Assert non-overdue behavior when either condition is not met.

7. **Cover recurring costs and asset purchases**
   - Inspect current domain rules for how these events affect finances.
   - Add tests that assert the expected accounting effect according to implemented business rules:
     - payable increase vs immediate cash decrease
     - any related summary/category impact if already modeled
   - Do not assume accounting treatment beyond what the codebase currently defines; align assertions to the intended domain model.

8. **Stabilize ordering/comparison only if needed**
   If replay tests fail due to unstable ordering rather than true nondeterminism:
   - identify the source, such as unordered collection enumeration
   - apply the smallest safe fix in production code, e.g. explicit ordering by date/type/sequence
   - keep the fix narrowly scoped and covered by tests

9. **Keep tests readable and domain-oriented**
   - Use descriptive test names tied to business outcomes
   - Reuse builders/fixtures for seed, profile, and start date setup
   - Group tests by:
     - replay determinism
     - event execution financial effects
     - overdue progression

10. **Document assumptions in code comments where needed**
   - If a test depends on a domain-specific accounting rule, add a short comment
   - If normalization is required for deterministic comparison, explain why that field is excluded

# Validation steps
1. Inspect and run the relevant tests first:
   - `dotnet test`

2. After implementation, run targeted tests if practical, then full suite:
   - `dotnet test`
   - optionally `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

3. Confirm the new tests cover all acceptance criteria:
   - invoice issuance -> receivables increase
   - invoice payment -> cash increase and receivables decrease
   - bill receipt -> payables increase
   - bill payment -> cash decrease and payables decrease
   - overdue invoice/bill after time progression and unpaid balance
   - recurring costs and asset purchases effects
   - identical seed/profile/start date -> identical timeline and finance summary

4. If deterministic replay required a production fix, verify:
   - no flaky ordering remains
   - comparisons are based on stable fields
   - repeated local test runs stay green

5. Ensure code quality expectations:
   - no unnecessary new dependencies
   - no broad refactors
   - tests are isolated and deterministic
   - production changes are minimal and justified

# Risks and follow-ups
- **Risk: hidden nondeterminism**
  - Unordered collections, ambient clocks, GUID generation, or implicit locale/timezone behavior may cause replay mismatches.
  - Mitigation: normalize comparisons and introduce explicit ordering/clock seams only where necessary.

- **Risk: unclear accounting semantics**
  - Recurring costs and asset purchases may have domain-specific treatment not obvious from the backlog text.
  - Mitigation: inspect existing implementation and align tests to actual intended behavior rather than generic accounting assumptions.

- **Risk: assertions too coupled to internal models**
  - Comparing full objects may make tests brittle if non-business fields change.
  - Mitigation: compare projected business-relevant fields only.

- **Risk: tests placed in wrong layer**
  - API-level tests may be slower or harder to control deterministically than application/domain tests.
  - Mitigation: prefer the lowest layer that exercises seeded generation and event execution faithfully.

Follow-ups if gaps are discovered:
- add a shared deterministic test fixture/builder for seed/profile/start-date scenarios
- introduce a domain-level comparable projection for event timelines/summaries if replay testing becomes common
- consider adding explicit clock/random abstractions where ambient behavior currently leaks into simulation logic