# Goal
Implement backlog task **TASK-5.3.3 — Add fixture-based tests for summary generation across representative event types** for **US-5.3 Human-readable activity summaries and transparency metadata**.

The coding agent should add deterministic, fixture-based automated tests that verify human-readable summary generation for representative audit/activity event types, while preserving the contract that each activity event returned to clients includes both:
- the **raw technical payload**, and
- the **normalized summary payload**

The implementation should focus on test coverage first, using the existing summary-generation path if already present. If minor production changes are required to make the behavior testable and deterministic, keep them minimal and scoped.

# Scope
In scope:
- Discover the current activity/audit event summary generation implementation in the .NET solution.
- Add or expand **fixture-based tests** covering **at least 20 predefined supported event types**.
- Verify summaries are deterministic and match expected fixture output exactly.
- Verify summaries include available fields such as **actor**, **action**, **target**, and **outcome**, and omit missing fields cleanly without rendering `"undefined"`, `"null"`, empty labels, or equivalent placeholder text.
- Verify API-facing activity event models expose both raw payload and normalized summary payload.
- Add benchmark-style or lightweight performance validation only if an existing benchmark/test harness already exists nearby; otherwise do not introduce a large new benchmarking framework for this task.

Out of scope:
- Large redesign of audit/event architecture.
- Introducing LLM-based summary generation.
- Broad API contract refactors unrelated to summary payloads.
- Building a full performance benchmarking subsystem from scratch unless the repository already has one.

# Files to touch
Start by inspecting these likely areas, then update the exact files you find:

- `tests/VirtualCompany.Api.Tests/**/*`
- Any existing test project files related to activity feed, audit trail, or explainability
- API contract/DTO files under:
  - `src/VirtualCompany.Api/**/*`
  - `src/VirtualCompany.Application/**/*`
  - `src/VirtualCompany.Shared/**/*`
- Audit/activity domain and formatter code under:
  - `src/VirtualCompany.Domain/**/*`
  - `src/VirtualCompany.Application/**/*`
  - `src/VirtualCompany.Infrastructure/**/*`

Likely artifacts to add:
- A new fixture file directory, for example:
  - `tests/VirtualCompany.Api.Tests/Fixtures/ActivitySummaries/*.json`
  - or `tests/VirtualCompany.Api.Tests/Fixtures/ActivitySummaries/*.yaml`
- One or more test classes, for example:
  - `tests/VirtualCompany.Api.Tests/ActivityFeed/SummaryGenerationFixtureTests.cs`
  - `tests/VirtualCompany.Api.Tests/Audit/SummaryGenerationFixtureTests.cs`

If needed, minimally touch production files to expose deterministic formatting behavior, such as:
- summary formatter classes
- event normalization mappers
- API response DTOs for activity events

# Implementation plan
1. **Discover the existing implementation**
   - Search the solution for:
     - `audit_events`
     - `summary`
     - `rationale_summary`
     - `activity`
     - `explainability`
     - `Audit`
     - `Feed`
   - Identify:
     - where supported event types are defined
     - where human-readable summaries are generated
     - what API model returns activity events to clients
     - whether raw payload and normalized summary payload already exist

2. **Map supported event types**
   - Build an inventory of current event/action types already supported by the formatter.
   - Select **at least 20 representative predefined event types** from real supported types in the codebase.
   - Prefer a mix such as:
     - task lifecycle events
     - workflow events
     - approval events
     - tool execution events
     - agent events
     - system/audit events
   - Do not invent unsupported event types just to satisfy the count.

3. **Design fixture format**
   - Create a fixture schema that is easy to extend and review in PRs.
   - Recommended fixture shape:
     - `eventType`
     - `input` or `rawPayload`
     - optional top-level fields like `actorType`, `actorId`, `action`, `targetType`, `targetId`, `outcome`
     - `expectedSummary`
     - optional `expectedNormalizedPayload` assertions if the API exposes structured summary fields
   - Keep fixtures deterministic and free of timestamps/random IDs unless those values are part of the expected output and fixed.

4. **Add fixture-based tests**
   - Implement parameterized tests that load all fixtures from disk and validate:
     - the formatter returns the exact expected summary text
     - no placeholder text such as `"undefined"` or `"null"` appears
     - missing optional fields are omitted gracefully
   - Add explicit assertions for the acceptance criteria:
     - actor/action/target/outcome are included when present
     - absent fields do not produce malformed text like double spaces, dangling punctuation, or empty labels
   - If the normalized summary payload is structured, assert those fields too.

5. **Verify API contract behavior**
   - Add API-level tests if there are existing endpoint/integration tests for the activity feed or audit history.
   - Assert each returned activity event includes:
     - raw technical payload
     - normalized summary payload
   - If there is no existing API test pattern, add a focused test at the application/mapper layer instead of building a large new integration harness.

6. **Minimally harden production code if needed**
   - Only if tests reveal nondeterminism or missing contract fields:
     - centralize summary formatting in a deterministic formatter/template layer
     - ensure null/missing values are omitted rather than stringified
     - ensure API DTOs expose both raw and normalized payloads
   - Keep production changes small and directly tied to enabling the tests.

7. **Handle performance criterion pragmatically**
   - Check whether the repo already contains:
     - benchmark tests
     - performance test helpers
     - feed endpoint timing tests
   - If yes, extend the nearest existing test to confirm summary generation does not materially regress response time and remains under the stated threshold.
   - If no such harness exists, do not add heavyweight benchmarking infrastructure in this task; instead:
     - document the gap in comments/notes
     - keep summary generation test-only changes lightweight
     - mention follow-up work in the final notes

8. **Keep implementation clean**
   - Reuse existing serialization/test helpers.
   - Prefer strongly typed fixture deserialization over ad hoc dynamic parsing.
   - Keep fixture names descriptive and stable.
   - Ensure tests are readable enough for product review of expected summary wording.

# Validation steps
Run and report the results of the smallest relevant set first, then broader validation if needed:

1. Build:
   - `dotnet build`

2. Run targeted tests for the new fixture suite:
   - `dotnet test --filter "SummaryGeneration|Activity|Audit"`

3. Run the full relevant test project:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

4. If production code was touched beyond tests, run the full solution tests if feasible:
   - `dotnet test`

Validation expectations:
- At least **20 fixture cases** pass.
- Fixture outputs match expected summary text exactly.
- No summaries contain `"undefined"`, `"null"`, or equivalent placeholder artifacts.
- API/application contract tests confirm both raw payload and normalized summary payload are present.
- If an existing performance harness is available, include its result and confirm no regression beyond the acceptance threshold.

# Risks and follow-ups
- **Risk: event types may not yet be centrally enumerated.**
  - Mitigation: derive the supported set from the existing formatter/switch/mapping logic and document what is covered.

- **Risk: summary generation may currently be embedded in API mapping code and hard to test directly.**
  - Mitigation: extract only the minimal formatter interface/helper needed for deterministic unit testing.

- **Risk: API contract names may differ from the backlog wording.**
  - Mitigation: preserve existing naming if semantically equivalent, but ensure tests clearly verify both raw and normalized summary representations.

- **Risk: no existing performance benchmark harness exists.**
  - Mitigation: avoid overbuilding; note this as a follow-up if acceptance cannot be fully automated in the current repo.

- **Follow-up recommendation:**
  - Add a dedicated benchmark or integration performance test for the activity feed once the feed endpoint and representative seeded data are stable.
  - Consider snapshot approval testing if summary wording becomes a product-reviewed surface area across many event types.