# Goal
Implement **TASK-5.3.1 — Create event-to-summary formatter library for supported activity types** for **US-5.3 Human-readable activity summaries and transparency metadata**.

Build a deterministic, reusable formatter library in the .NET backend that converts supported audit/activity events into a normalized, human-readable summary payload while preserving the raw technical payload. The implementation must support at least **20 predefined event types**, omit missing fields cleanly, and be safe for use during ingestion or read-time enrichment without materially degrading feed performance.

# Scope
Include:
- A formatter library/service in the backend for transforming activity/audit events into normalized summary data.
- Deterministic templates/formatters for at least 20 supported event types.
- A normalized summary payload shape that includes, when available:
  - actor
  - action
  - target
  - outcome
  - summary text
  - event type / formatter key
- Logic that omits missing fields without rendering placeholder text like `undefined`, `null`, empty parentheses, dangling separators, or double spaces.
- API/domain/application integration so each returned activity event includes:
  - raw technical payload
  - normalized summary payload
- Automated tests with fixture-style assertions for at least 20 event types.
- A lightweight benchmark or performance-focused test/assertion path validating summary generation does not push median feed response time above 300 ms.

Do not include:
- UI redesign of audit/activity feed.
- LLM-generated summaries.
- Non-deterministic or localized natural language generation unless already supported by existing patterns.
- Broad refactors outside the audit/activity feed path unless required to integrate the formatter cleanly.

# Files to touch
Inspect the solution first and then update the most appropriate files. Expected likely areas:

- `src/VirtualCompany.Domain/`
  - Add activity event / summary value objects or contracts if domain-owned.
- `src/VirtualCompany.Application/`
  - Add formatter interfaces, implementations, enrichment service, query DTOs, and mapping logic.
- `src/VirtualCompany.Infrastructure/`
  - Add persistence/read-model mapping if raw audit payloads are stored here and enrichment happens here.
- `src/VirtualCompany.Api/`
  - Update response contracts/serializers/endpoints if API DTOs need to expose both raw and normalized payloads.
- `src/VirtualCompany.Shared/`
  - Add shared DTOs only if this repo already uses Shared for API-facing contracts.
- `tests/VirtualCompany.Api.Tests/`
  - Add API contract/integration tests if feed endpoints are covered here.
- Potentially add or update:
  - `tests/VirtualCompany.Application.Tests/` or equivalent if present for formatter unit tests
  - benchmark/perf test project if one already exists
  - fixture files under a `Fixtures/ActivitySummaries/` or similar folder

Before coding, locate:
- Existing audit event entities/DTOs
- Activity feed query handlers/endpoints
- Existing event type enums/constants
- Existing test conventions and fixture organization

# Implementation plan
1. **Discover current audit/activity model**
   - Find where audit/activity events are stored and returned.
   - Identify current event type identifiers, payload shape, and feed query path.
   - Determine whether enrichment should happen:
     - at ingestion time,
     - at read time,
     - or via a shared formatter usable in both paths.
   - Prefer a design that keeps formatting deterministic and side-effect free.

2. **Define normalized summary contract**
   - Introduce a normalized summary DTO/value object with fields such as:
     - `EventType`
     - `Actor`
     - `Action`
     - `Target`
     - `Outcome`
     - `Text`
     - optional `Metadata` only if clearly needed
   - Ensure the API event response includes both:
     - `RawPayload`
     - `Summary` (normalized payload)
   - Keep naming consistent with existing API conventions.

3. **Create formatter abstraction**
   - Add an interface such as:
     - `IActivityEventSummaryFormatter`
     - or registry-based `IActivitySummaryFormatterRegistry`
   - The abstraction should:
     - accept event type + raw payload/domain event
     - return normalized summary payload
     - be deterministic
     - fail safely for unsupported or malformed payloads
   - Prefer explicit formatter registration per event type over reflection-heavy magic.

4. **Implement deterministic formatting helpers**
   - Add shared helper methods for:
     - extracting optional actor/action/target/outcome fields
     - joining text fragments without placeholder leakage
     - handling null/empty/whitespace values
     - formatting IDs/names consistently
   - Ensure missing fields are omitted cleanly:
     - good: `Alice approved invoice INV-1001`
     - good: `Approved invoice INV-1001`
     - bad: `Alice approved invoice INV-1001 with outcome null`
     - bad: `undefined approved target`

5. **Support at least 20 predefined event types**
   - Implement formatters/templates for at least 20 concrete event types already present or aligned with backlog domains, for example:
     - task created
     - task assigned
     - task completed
     - task failed
     - workflow started
     - workflow step completed
     - workflow failed
     - approval requested
     - approval approved
     - approval rejected
     - agent hired
     - agent updated
     - agent paused
     - agent archived
     - tool execution allowed
     - tool execution denied
     - document uploaded
     - document processed
     - memory item created
     - conversation message sent
   - Use actual repo event type constants if they already exist; do not invent conflicting identifiers.
   - If fewer than 20 currently exist, add support for the existing set plus the next most relevant planned event types only if they fit current architecture cleanly.

6. **Add formatter registry/resolution**
   - Implement a registry mapping event type to formatter/template.
   - Add a fallback formatter for unknown event types that still returns safe normalized output without placeholders.
   - Unknown events should not break feed rendering.

7. **Integrate into ingestion or read-time enrichment**
   - Wire summary generation into the existing activity feed pipeline.
   - If ingestion-time summary persistence already fits the model, ensure raw payload is still preserved.
   - If read-time enrichment is simpler/safer, enrich in query handlers or response mappers.
   - Keep implementation efficient:
     - avoid repeated JSON parsing
     - avoid unnecessary allocations
     - avoid network/database calls during formatting

8. **Update API response shape**
   - Ensure each activity event returned to clients includes:
     - raw technical payload
     - normalized summary payload
   - Preserve backward compatibility where possible; if changing contracts, update tests and any documented response examples.
   - Do not remove existing raw fields.

9. **Add automated tests with fixtures**
   - Create fixture-style tests for at least 20 event types.
   - For each event type, verify:
     - exact summary text
     - actor/action/target/outcome field extraction
     - omission of missing fields
     - no `null`/`undefined`/empty placeholder artifacts
   - Add edge-case tests:
     - missing actor
     - missing target
     - missing outcome
     - unknown event type
     - malformed payload
   - Prefer table-driven tests to keep coverage maintainable.

10. **Add performance validation**
   - Add a benchmark, perf-oriented integration test, or measurable timing harness around feed enrichment.
   - Validate median feed response time remains under 300 ms in the chosen test scenario.
   - If full end-to-end benchmarking is not practical in current test infrastructure, add:
     - a focused formatter throughput benchmark, and/or
     - a feed query perf test with representative event volume
   - Document assumptions clearly.

11. **Register dependencies and keep architecture clean**
   - Add DI registrations in the appropriate composition root.
   - Keep HTTP concerns in API, orchestration in Application, and persistence in Infrastructure.
   - Avoid leaking JSON parsing or endpoint-specific logic into Domain unless already patterned that way.

12. **Document implementation notes inline**
   - Add concise comments only where formatter behavior or fallback rules are non-obvious.
   - Keep templates easy to extend for future event types.

# Validation steps
Run and report results for the relevant commands:

1. Restore/build:
   - `dotnet build`

2. Run automated tests:
   - `dotnet test`

3. If a targeted test filter is useful, run formatter-related tests separately and include the command used.

4. Validate acceptance criteria explicitly:
   - Confirm each supported event type returns deterministic summary output.
   - Confirm missing fields are omitted without placeholder text.
   - Confirm API responses include both raw payload and normalized summary payload.
   - Confirm at least 20 predefined event types are covered by automated tests.
   - Confirm benchmark/perf validation for median feed response time under 300 ms, or clearly document the closest enforceable automated validation added.

5. Include in your final implementation notes:
   - list of supported event types
   - where summary generation occurs
   - response contract shape
   - any fallback behavior for unknown events

# Risks and follow-ups
- **Event taxonomy mismatch:** Existing event type names may differ from backlog wording. Reuse repo constants/enums rather than inventing a parallel taxonomy.
- **Contract compatibility risk:** Adding normalized summary payload may affect existing clients/tests. Preserve existing raw payload fields and extend contracts carefully.
- **Performance risk:** Read-time enrichment could add JSON parsing overhead on large feeds. Cache parsed structures or enrich once per event in the query pipeline.
- **Fixture brittleness:** Exact text fixtures can become noisy if templates change. Keep wording stable and intentional.
- **Unknown/malformed payloads:** Feed rendering must remain resilient; implement safe fallback summaries.
- **Follow-up suggestion:** If this task lands with read-time enrichment, consider a later optimization to persist normalized summaries at ingestion for high-volume feeds.
- **Follow-up suggestion:** Consider future localization/i18n support, but do not introduce it in this task unless already established in the codebase.