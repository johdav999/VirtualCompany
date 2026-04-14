# Goal
Implement `TASK-5.3.2` for **US-5.3 Human-readable activity summaries and transparency metadata** by adding a **normalized summary payload** to activity/feed and correlation responses while preserving the existing raw technical payload.

The implementation must ensure:
- Supported audit/activity event types are transformed into deterministic, human-readable summaries.
- Summary payloads expose structured fields such as `actor`, `action`, `target`, and `outcome` when available.
- Missing source fields are omitted cleanly with no placeholder text like `"undefined"` or `"null"`.
- API responses include both:
  - the original/raw event payload
  - the normalized summary payload
- Automated tests cover at least **20 predefined event types** with expected summary fixtures.
- Summary generation does not materially regress feed performance and remains within the benchmark target of **median < 300 ms**.

# Scope
In scope:
- Identify the activity/audit/feed response models and correlation response models currently returned by the API.
- Add a normalized summary contract to those response DTOs.
- Implement deterministic summary generation for supported event types.
- Ensure summary generation can run either:
  - at ingestion/persistence time if the architecture already supports enrichment, or
  - at read-time enrichment if that is lower risk for this task
- Add tests for:
  - summary field generation
  - omission of missing fields
  - fixture-based expected text for at least 20 event types
  - API response shape including raw + normalized payloads
- Add or extend lightweight performance/benchmark coverage if a benchmark harness already exists; otherwise add a focused test or documented measurement path.

Out of scope:
- LLM-generated summaries
- redesigning the audit domain model beyond what is needed for normalized summaries
- changing unrelated feed filtering/pagination behavior
- broad UI work unless a shared contract change requires minimal updates
- introducing non-deterministic formatting logic

# Files to touch
Inspect first, then update the actual files that match the existing implementation. Likely areas:

- `src/VirtualCompany.Domain/**`
  - audit/activity event entities, enums, value objects, event type definitions
- `src/VirtualCompany.Application/**`
  - feed query handlers
  - correlation query handlers
  - DTOs/view models/contracts
  - mapping/enrichment services
- `src/VirtualCompany.Infrastructure/**`
  - persistence mappings if enrichment is stored
  - repository/query projections if summary fields are projected from DB
- `src/VirtualCompany.Api/**`
  - response contracts/endpoints if API-specific DTO mapping exists
- `src/VirtualCompany.Shared/**`
  - shared contracts if feed/correlation payloads are shared with web/mobile
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/contract tests
- potentially add:
  - `tests/VirtualCompany.Application.Tests/**` if present in solution structure, otherwise place unit tests in the nearest existing test project
  - fixture files for expected summaries, e.g. `tests/**/Fixtures/ActivitySummaries/*.json`

Also review:
- `README.md`
- any existing docs for audit/feed/correlation APIs
- any benchmark/perf test project if already present

# Implementation plan
1. **Discover current activity/feed/correlation flow**
   - Find the API endpoints and handlers that return:
     - recent activity feed
     - correlation/grouped activity responses
   - Trace the path from persistence model -> application query -> DTO -> API response.
   - Identify the canonical event type field and the raw payload shape currently returned.

2. **Define normalized summary contract**
   Add a deterministic normalized payload model, preferably in the application/shared contract layer, for example:
   - `SummaryText`
   - `Actor`
   - `Action`
   - `Target`
   - `Outcome`
   - `EventType`
   - optional `Metadata` only if already consistent with current contracts

   Requirements:
   - `SummaryText` must be deterministic.
   - `Actor`, `Action`, `Target`, `Outcome` should be nullable/optional and omitted when unavailable.
   - Do not serialize nulls if the API is already configured for null omission; otherwise align with existing API conventions.
   - Preserve the existing raw payload unchanged.

3. **Implement deterministic summary formatter service**
   Create a dedicated formatter/enricher service, e.g.:
   - `IActivitySummaryFormatter`
   - `ActivitySummaryFormatter`

   Design expectations:
   - Use explicit per-event-type templates/formatters, not reflection-heavy or ad hoc string concatenation spread across handlers.
   - Prefer a registry/dictionary/switch expression keyed by event type.
   - Each supported event type should map source payload fields into normalized fields and final summary text.
   - Missing values must be skipped cleanly.

   Example behavior pattern:
   - If actor + action + target + outcome exist: `"Alice approved invoice INV-1001 successfully."`
   - If outcome missing: `"Alice approved invoice INV-1001."`
   - If actor missing: `"Invoice INV-1001 was approved."`
   - Never emit `"null"`, `"undefined"`, extra double spaces, dangling punctuation, or empty labels.

4. **Support at least 20 predefined event types**
   Identify at least 20 event types already present in the system or seed definitions. Implement deterministic mappings for them.
   
   Prefer event types aligned with the backlog and architecture, such as:
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
   - tool execution allowed
   - tool execution denied
   - agent created/hired
   - agent updated
   - agent paused/restricted/archived
   - document uploaded
   - document processed
   - memory created
   - briefing generated
   - notification sent
   
   Use the actual event type constants/enums found in the codebase rather than inventing new canonical names unless necessary.

5. **Choose enrichment point**
   Prefer the lowest-risk option based on the current architecture:
   - **Read-time enrichment** if feed responses are already projection-based and raw payloads are available.
   - **Ingestion-time enrichment** only if audit events are already normalized on write and adding persisted summary fields is straightforward.

   Decision guidance:
   - If adding DB columns/migrations would increase risk, use read-time enrichment first.
   - If feed queries are already expensive, consider projecting precomputed summary fields only if there is an existing pattern.

6. **Attach normalized summary payload to responses**
   Update feed and correlation response DTOs so each event includes:
   - existing raw payload
   - new normalized summary payload

   Ensure backward compatibility:
   - do not remove or rename existing fields unless required by compile errors and clearly justified
   - additive contract change only

   If correlation responses aggregate multiple events:
   - attach normalized summary per event item
   - if there is a correlation-level summary already, leave it unchanged unless this task explicitly requires extending it

7. **Handle serialization cleanly**
   Ensure null/optional fields are omitted according to existing JSON settings.
   If current serializer includes nulls by default, either:
   - annotate the normalized summary DTO appropriately, or
   - configure only the relevant contract serialization behavior in a way consistent with the rest of the API

   Validate that missing fields do not appear as placeholder strings.

8. **Add fixture-based tests for 20+ event types**
   Create deterministic tests that verify exact expected summary text for at least 20 event types.
   Recommended structure:
   - one unit test per event type, or
   - parameterized theory using fixture data
   - fixture includes:
     - event type
     - raw payload
     - expected normalized fields
     - expected summary text

   Also add tests for:
   - missing actor
   - missing target
   - missing outcome
   - unknown/unsupported event type fallback behavior

   Fallback behavior should be deterministic and safe, for example:
   - generic summary using available fields, or
   - no normalized summary for unsupported types
   Choose one approach and test it.

9. **Add API contract tests**
   Add endpoint tests verifying feed/correlation responses now include:
   - raw payload
   - normalized summary payload
   - no `"null"`/`"undefined"` text in generated summaries

10. **Validate performance**
   If a benchmark/perf harness exists:
   - add/extend a benchmark around feed response generation with summary enrichment enabled

   If no harness exists:
   - add a focused test or measurement script/documented validation path that exercises representative feed volume and records median latency
   - keep implementation allocation-conscious:
     - avoid repeated JSON parsing where possible
     - avoid per-item service resolution
     - avoid unnecessary DB round trips
     - cache static formatter definitions/templates

11. **Document implementation notes**
   Add a concise note in code comments or docs if needed:
   - where summary generation occurs
   - how to add support for new event types
   - expectations for deterministic formatting and fixture updates

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Specifically verify:
   - formatter unit tests pass for at least 20 predefined event types
   - feed endpoint tests confirm normalized summary payload is present
   - correlation endpoint tests confirm normalized summary payload is present
   - missing fields are omitted cleanly
   - no summary text contains `"null"` or `"undefined"`

4. If benchmarks/perf tests exist, run them and confirm:
   - median feed response time remains below `300 ms`

5. Manually inspect one or two serialized API responses to confirm the final JSON shape is additive and client-safe.

# Risks and follow-ups
- **Unknown existing contract shape**: feed/correlation models may already have partial summary fields. Reuse and extend rather than duplicating concepts.
- **Event type sprawl**: actual event payloads may be inconsistent across modules. Normalize only supported types explicitly and add safe fallback behavior.
- **Performance risk**: read-time enrichment could become expensive if it requires repeated payload parsing. Mitigate by parsing once per event and using lightweight deterministic formatters.
- **Serialization mismatch**: null omission behavior may differ across projects. Verify actual API JSON output, not just DTO values.
- **Fixture brittleness**: exact text fixtures are valuable but can become noisy if formatting changes. Keep templates stable and intentional.
- **Potential follow-up**:
  - persist precomputed normalized summaries at ingestion if feed scale grows
  - expose summary schema in shared/mobile contracts if needed
  - add admin tooling or docs for registering new event type formatters