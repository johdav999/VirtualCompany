# Goal
Implement backlog task **TASK-35.4.3** for story **US-35.4** by adding a **reply signal detection pipeline** that identifies and persists **ghosting**, **price resistance**, and **buying intent** signals from inbound replies and conversation history, including **confidence** and **explanation** fields, in a **tenant-safe**, **production-ready**, **.NET modular monolith** style consistent with the existing architecture.

This work must support the acceptance criterion:

- “The system detects and stores deal intelligence signals for ghosting, price resistance, and buying signals from inbound replies and conversation history with confidence scores.”

The implementation should fit cleanly into the existing modules, preserve CQRS-lite boundaries, and be designed so downstream dashboard/API work can consume the persisted signals without re-running inference on every request.

# Scope
In scope for this task:

- Add domain and persistence support for **deal intelligence signals** derived from inbound communication.
- Support at minimum these signal types:
  - `ghosting`
  - `price_resistance`
  - `buying_intent`
- Persist, per detected signal:
  - tenant/company scope
  - related conversation / message / deal / sequence context where available
  - signal type
  - status or polarity if needed
  - confidence score
  - explanation / rationale summary
  - timestamps
  - source metadata sufficient for audit/debugging
- Implement an application/service pipeline that:
  - triggers on inbound reply processing and/or conversation analysis jobs
  - evaluates recent conversation history
  - produces structured signal results
  - stores/upserts results idempotently
- Add background-job-friendly orchestration so signal recalculation can be invoked safely outside request paths.
- Expose the persisted signal data through internal application contracts/repositories needed for future dashboard and deal detail API consumption.
- Add tests for persistence, tenant isolation, and signal pipeline behavior.

Out of scope unless required by existing code coupling:

- Full revenue dashboard implementation
- Full risk score recalculation logic
- A/B variant reporting
- New UI beyond minimal plumbing if absolutely necessary
- Broad LLM prompt framework refactors unrelated to this task

If existing code already has partial analytics/deal intelligence structures, extend them rather than duplicating concepts.

# Files to touch
Inspect the solution first and then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add or modify:

## Domain
- Deal intelligence signal entity/value objects, e.g.:
  - `src/VirtualCompany.Domain/.../DealIntelligenceSignal.cs`
  - `src/VirtualCompany.Domain/.../SignalType.cs`
- Related enums/constants for supported signal kinds and source types
- If deals/conversations/messages already exist in domain, add navigation/association points only where appropriate

## Application
- Command/query contracts and handlers for signal analysis/persistence, e.g.:
  - analyze inbound reply
  - analyze conversation history
- Service interfaces such as:
  - `IReplySignalDetectionService`
  - `IDealIntelligenceSignalRepository`
- DTOs/models for structured detection results:
  - signal type
  - confidence
  - explanation
  - evidence references
- Background job entrypoint or orchestration service for re-analysis

## Infrastructure
- EF Core entity configuration / DbContext updates
- Repository implementation
- Migration(s) for new tables/columns
- Optional JSONB mapping for evidence/explanation metadata
- LLM/rule-based detector implementation if AI orchestration is already present
- Inbox/background worker integration point

## API
- If there is already an inbound message webhook/controller/processor path, wire the pipeline there
- If there is already a deal detail endpoint contract, include persisted signals only if low-risk and already aligned with current API shape
- Avoid inventing broad new public endpoints unless necessary for validation

## Tests
- Domain tests for signal creation/validation
- Application tests for detection pipeline behavior
- Integration/API tests for:
  - tenant scoping
  - persistence
  - idempotent processing
  - expected structured fields

Also review:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

to align migration and project conventions.

# Implementation plan
1. **Inspect existing analytics, CRM, inbox, and conversation models**
   - Find current entities for:
     - deals
     - conversations
     - messages
     - inbound replies
     - sequences/campaigns if present
     - analytics/risk/signal concepts if already started
   - Reuse existing naming and module boundaries.
   - Identify where inbound replies are normalized and persisted today.
   - Identify whether there is already an AI orchestration abstraction suitable for structured classification.

2. **Design the persistence model for deal intelligence signals**
   - Add a dedicated tenant-scoped table/entity for persisted signals rather than burying this in message JSON.
   - Recommended fields:
     - `id`
     - `company_id`
     - `deal_id` nullable
     - `conversation_id` nullable
     - `message_id` nullable
     - `sequence_id` nullable if available
     - `sequence_step_id` nullable if available
     - `signal_type` text
     - `signal_state` or `direction` if useful
     - `confidence_score` numeric
     - `explanation` text
     - `evidence_json` jsonb nullable
     - `detected_at`
     - `source_window_started_at` nullable
     - `source_window_ended_at` nullable
     - `created_at`
     - `updated_at`
   - Add uniqueness/idempotency constraints appropriate to the processing model, for example:
     - one signal type per message
     - or one latest signal type per conversation/deal snapshot
   - Prefer explicit indexes on:
     - `company_id`
     - `deal_id`
     - `conversation_id`
     - `message_id`
     - `signal_type`
     - `detected_at`

3. **Add domain model and validation**
   - Create a domain entity or aggregate-friendly record for persisted signals.
   - Add enum/string constants for supported signal types:
     - ghosting
     - price_resistance
     - buying_intent
   - Enforce:
     - confidence range validation
     - required explanation for persisted positive detections
     - tenant ownership consistency where applicable

4. **Implement structured detection result contracts**
   - Add application-layer models for detector output, e.g.:
     - `DetectedReplySignal`
     - `ReplySignalDetectionResult`
   - Include:
     - signal type
     - confidence
     - explanation
     - evidence references
     - whether signal is detected / not detected
   - Keep these contracts deterministic and serializable.

5. **Implement the reply signal detection service**
   - Create `IReplySignalDetectionService` in Application.
   - Implement in Infrastructure.
   - Prefer a layered approach:
     - first normalize inbound message + recent conversation history
     - then classify for the three required signal types
   - If the codebase already has LLM orchestration for structured outputs, use it with a strict schema.
   - If not, implement a safe deterministic baseline classifier with extensibility for future LLM enhancement.
   - The detector must return structured outputs only, not free-form text blobs.

6. **Use conversation history, not just single-message text**
   - Build a small context window from recent inbound/outbound messages for the same conversation/deal.
   - Include enough history to detect:
     - ghosting: prolonged lack of reply after prior engagement or repeated unanswered outbound attempts
     - price resistance: objections around cost/budget/discount/pricing
     - buying intent: explicit interest, next steps, timeline, procurement, demo, contract, approval, etc.
   - Keep the context assembly in application/infrastructure services, not controllers.

7. **Persist results idempotently**
   - After detection, upsert signal records.
   - Avoid duplicate rows on retries or webhook replays.
   - If the same message/conversation is reprocessed:
     - update confidence/explanation if the pipeline is designed as latest-wins
     - or no-op if identical
   - Record enough metadata for audit/debugging without storing chain-of-thought.

8. **Wire into inbound reply processing**
   - Find the existing inbound email/inbox/webhook/message ingestion path.
   - After inbound message persistence succeeds, invoke the signal analysis pipeline asynchronously if possible.
   - If there is a background worker/outbox pattern already in place, prefer:
     - emit internal event / outbox record
     - process in background worker
   - Keep request latency low and retries safe.

9. **Support conversation-level reanalysis**
   - Add an internal command/job to analyze a conversation or deal thread on demand.
   - This supports future daily recalculation and backfill scenarios.
   - Keep this as an application command/worker entrypoint even if not yet exposed publicly.

10. **Add repository/query support for downstream consumers**
   - Implement repository/query methods to fetch latest signals by:
     - deal
     - conversation
     - company
     - signal type
   - This should support future dashboard and deal detail API work without schema changes.

11. **Add migration and EF configuration**
   - Create the PostgreSQL migration for the new signal table and indexes.
   - Follow repository conventions from the project.
   - Use proper numeric precision for confidence and JSONB for evidence metadata if needed.

12. **Add tests**
   - Unit tests:
     - confidence validation
     - supported signal type handling
     - explanation required rules
   - Application tests:
     - buying intent detected from clear positive reply
     - price resistance detected from pricing objection
     - ghosting detected from conversation pattern if supported by available data
   - Integration tests:
     - inbound reply processing persists signals
     - tenant A cannot read tenant B signals
     - replay/idempotent processing does not duplicate records

13. **Document assumptions in code comments or task notes**
   - Especially for ghosting detection, define the initial heuristic clearly if full business rules are not already present.
   - Keep implementation extensible for future richer scoring/risk models.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration generation/application flow follows repo conventions.
   - If migrations are committed from this repo, add the new migration and ensure it builds cleanly.

4. Validate persistence manually or via integration test:
   - create or simulate an inbound reply tied to a tenant conversation/deal
   - run the reply signal pipeline
   - confirm a persisted signal row exists with:
     - correct `company_id`
     - expected `signal_type`
     - non-null `confidence_score`
     - non-null `explanation`

5. Validate idempotency:
   - process the same inbound reply twice
   - confirm no duplicate signal rows are created beyond the intended uniqueness model

6. Validate tenant isolation:
   - query signals under a different tenant context
   - confirm forbidden/not found/no cross-tenant leakage according to existing patterns

7. Validate representative scenarios:
   - buying intent example:
     - “This looks good, can we schedule a demo next week?”
   - price resistance example:
     - “It’s too expensive for our budget right now.”
   - ghosting example:
     - conversation with prior engagement followed by configured no-response window / repeated unanswered outbound attempts
   - confirm persisted confidence and explanation are sensible and structured

# Risks and follow-ups
- **Ghosting detection ambiguity:** ghosting is often temporal and sequence-aware, not purely message-text-based. If current data does not yet model unanswered outbound cadence cleanly, implement a documented heuristic now and flag a follow-up for stronger sequence/deal-timeline logic.
- **LLM consistency risk:** if using an LLM for classification, require strict structured output, bounded prompt context, and safe fallback behavior. Do not persist raw reasoning.
- **Schema overlap risk:** there may already be analytics or signal tables in progress. Reuse/extend existing structures instead of creating parallel concepts.
- **Performance risk:** avoid reprocessing full conversation history on every request. Prefer bounded windows and background execution.
- **Idempotency risk:** inbound webhooks and workers may retry. Ensure unique constraints/upsert semantics are explicit.
- **Follow-up likely needed:** expose these persisted signals in the deal detail API and dashboard query layer once the broader US-35.4 analytics surfaces are implemented.
- **Follow-up likely needed:** add backfill job to analyze historical conversations for existing tenants after deployment.
- **Follow-up likely needed:** unify these signals with daily pipeline risk recalculation once TASKs for risk scoring are implemented.