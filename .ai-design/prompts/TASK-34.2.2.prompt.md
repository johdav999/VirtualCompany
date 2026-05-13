# Goal
Implement backlog task **TASK-34.2.2** for story **US-34.2 Build real sales email ingestion, intent detection, and lead/deal linking workflows**.

Deliver a production-ready, tenant-scoped sales email processing flow that:
- Processes real mailbox messages from existing **Gmail** and **Microsoft** connections via:
  - `POST /api/sales/email/process-message`
  - `POST /api/sales/email/process-thread`
- Uses **structured LLM extraction** to detect sales intent and extract:
  - sender email
  - contact name
  - company name when available
  - intent
  - product/service interest
  - urgency
  - confidence score
- Validates LLM output server-side
- Classifies non-sales emails as ignored with an **auditable ignore reason**
- Uses a **fail-safe fallback** when LLM output is invalid, unavailable, or low-confidence
- Is **idempotent** for repeated processing of the same message/thread
- On successful lead detection:
  - creates or updates a lead
  - creates a `SalesActivity`
  - emits `sales.email.received` and `sales.lead.detected` events
  - stores `SalesEmailLinks` to source message and thread

Use existing architecture and conventions in the repo. Prefer modular monolith boundaries, CQRS-lite application services, outbox-backed events, tenant isolation, and auditable business records.

# Scope
In scope:
- Add or complete API endpoints for processing a single mailbox message and a thread
- Integrate with existing Gmail/Microsoft mailbox connection infrastructure only; do not add mock providers
- Add application-layer orchestration for:
  - mailbox message retrieval/normalization
  - structured LLM classification/extraction
  - validation
  - fallback heuristics
  - ignore classification
  - lead upsert/linking
  - sales activity creation
  - event emission
  - idempotency
- Add/extend domain models and persistence for:
  - sales email processing result
  - ignore reason auditability
  - source message/thread links
  - confidence score
- Add tests covering supported sales signals, ignored messages, invalid LLM output, fallback behavior, and idempotent reprocessing

Out of scope:
- UI work unless required for compile/runtime wiring
- New mailbox provider implementations
- Full CRM/deal pipeline redesign beyond what is required to create/update leads and activities
- General-purpose workflow engine changes unrelated to this task
- Mobile changes

# Files to touch
Inspect the solution first, then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:
- API controllers/endpoints for sales email processing
- Application commands/handlers/services for process-message and process-thread
- Domain entities/value objects/enums for:
  - sales intent
  - ignore reason
  - confidence
  - sales email link
  - processing status/result
- Infrastructure adapters for:
  - Gmail/Microsoft message fetch/normalization
  - LLM structured output invocation
  - outbox/event publishing
  - persistence mappings/repositories
- EF Core configurations and migrations
- Test fixtures and integration/unit tests

If present, prefer updating existing sales/email/integration modules rather than creating parallel abstractions.

# Implementation plan
1. **Discover existing sales, email, integration, and eventing code**
   - Search for:
     - sales lead/deal entities and services
     - Gmail/Microsoft connection adapters
     - mailbox sync/message models
     - outbox/event infrastructure
     - audit/event tables
     - existing idempotency patterns
   - Reuse existing abstractions and naming conventions.
   - Identify whether message/thread IDs are already normalized across providers.

2. **Define the processing contract**
   - Add request models for:
     - process single message by provider connection + external message ID
     - process thread by provider connection + external thread ID
   - Add response models that clearly indicate:
     - processed/ignored/no-op
     - lead ID if detected
     - activity ID if created
     - ignore reason if ignored
     - confidence score
     - idempotent replay status
   - Keep API tenant-scoped and authorization-consistent with the rest of the app.

3. **Normalize mailbox messages from real providers**
   - Implement or extend a provider-agnostic normalized email model containing at least:
     - provider
     - connection/account ID
     - external message ID
     - external thread ID
     - internet message ID if available
     - from address/name
     - subject
     - body text and/or HTML-to-text
     - received timestamp
     - participants if available
   - Ensure `process-message` and `process-thread` fetch from existing Gmail/Microsoft integrations only.
   - Do not use fake/mock provider paths in production code.

4. **Design structured LLM extraction schema**
   - Create a strict structured output contract for the LLM, for example:
     - `classification`: `sales_lead | ignore | uncertain`
     - `intent`
     - `contactName`
     - `companyName`
     - `productOrServiceInterest`
     - `urgency`
     - `confidence`
     - `ignoreReason`
     - `reasonSummary`
   - Include supported ignore categories such as:
     - newsletter
     - receipt
     - invoice
     - support_ticket
     - non_sales_operational
     - insufficient_signal
   - Add server-side validation rules:
     - required fields by classification
     - confidence range bounds
     - enum validation
     - non-empty ignore reason when ignored
     - non-empty intent for sales lead classification

5. **Implement LLM extraction service**
   - Add an application/infrastructure service that:
     - builds a deterministic prompt for sales intent extraction
     - requests structured JSON output from the configured LLM provider
     - parses and validates the result
   - Keep prompt assembly outside controllers.
   - Log technical failures safely; do not persist chain-of-thought.
   - Persist concise rationale/summary only if that pattern already exists.

6. **Add fail-safe fallback classification**
   - If LLM call fails, times out, returns malformed output, or validation fails:
     - run a deterministic fallback classifier using heuristics from subject/body/sender patterns
   - Fallback should:
     - detect obvious ignore cases: newsletters, receipts, invoices, support tickets
     - detect obvious sales signals: demo request, pricing inquiry, quote request, interested in service/product, urgent follow-up
     - otherwise classify as ignored or uncertain with auditable reason, per existing domain conventions
   - If confidence is too low, do not create a lead; record auditable ignore/no-action reason instead.
   - Make fallback conservative to support reliable production lead detection.

7. **Implement idempotent processing**
   - Ensure reprocessing the same message/thread does not create duplicate leads or duplicate activities/links.
   - Use stable idempotency keys based on tenant + provider + connection + external message/thread identifiers.
   - For thread processing:
     - avoid duplicate lead creation when multiple messages in the same thread are processed repeatedly
     - prefer one lead per qualifying thread unless existing domain rules dictate otherwise
   - If an existing `SalesEmailLink` or equivalent exists, return a no-op/idempotent result while still surfacing prior linked entities.

8. **Lead create/update logic**
   - On supported sales signal detection:
     - create or update a lead using extracted sender/contact/company/intent/product-interest/urgency/confidence
   - Match existing leads using the strongest available identifiers, likely:
     - tenant + normalized sender email
     - existing thread link
     - existing lead/contact linkage if present
   - Do not create duplicates for the same email thread.
   - Preserve source attribution to the originating message/thread.

9. **Create SalesActivity and source links**
   - On successful lead detection:
     - create a `SalesActivity`
     - create/store `SalesEmailLinks` for source message and thread
   - Ensure links include enough metadata for audit and future drill-down:
     - provider
     - connection/account
     - external message ID
     - external thread ID
     - processed timestamp
     - classification outcome
   - If ignored, still persist an auditable processing record or ignore record per current architecture.

10. **Emit domain/integration events**
    - Emit:
      - `sales.email.received`
      - `sales.lead.detected`
    - Use the existing outbox/event dispatcher pattern.
    - Ensure events are emitted once for successful first-time processing, not duplicated on idempotent replay unless the existing eventing model explicitly supports replay semantics.
    - Include tenant and correlation identifiers.

11. **Persistence and migration updates**
    - Add/extend tables/entities for:
      - sales email links
      - processing outcome
      - ignore reason
      - confidence score
      - provider message/thread identifiers
    - Add EF mappings and migration(s).
    - Keep schema tenant-aware and auditable.

12. **Auditability**
    - Record ignored messages with explicit ignore reason.
    - Record successful detections with concise rationale summary and source references if supported by current audit model.
    - Keep business audit separate from technical logs.

13. **Tests**
    - Add tests for:
      - Gmail message with clear sales inquiry creates/updates lead
      - Microsoft thread with sales intent creates one lead and links thread
      - newsletter ignored with auditable ignore reason
      - receipt/invoice ignored with auditable ignore reason
      - support ticket without upsell intent ignored
      - malformed/invalid LLM output triggers fallback
      - LLM timeout/failure triggers fallback
      - low-confidence result does not create lead
      - reprocessing same message is idempotent
      - reprocessing same thread is idempotent
      - successful detection creates `SalesActivity`, emits both events, and stores source links
    - Prefer integration-style tests around API + application wiring where feasible.

14. **Implementation quality constraints**
    - Keep code async and cancellation-token aware.
    - Respect tenant isolation in all queries and writes.
    - Avoid direct DB access from controllers.
    - Use typed contracts between modules.
    - Keep logic deterministic and testable, especially validation and fallback heuristics.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify API behavior manually or via tests for:
   - `POST /api/sales/email/process-message`
   - `POST /api/sales/email/process-thread`

4. Confirm acceptance criteria explicitly:
   - Real Gmail/Microsoft connections are used, not mock providers
   - Supported sales emails create or update a lead with extracted fields:
     - sender
     - contact name
     - company name when available
     - intent
     - product/service interest
     - urgency
     - confidence score
   - Newsletters, receipts, invoices, and support tickets without upsell intent are ignored with auditable ignore reason
   - Reprocessing same message/thread is idempotent and does not create duplicate leads for same thread
   - Successful lead detection creates `SalesActivity`
   - Successful lead detection emits:
     - `sales.email.received`
     - `sales.lead.detected`
   - `SalesEmailLinks` are stored for source message and thread

5. Validate edge cases:
   - Invalid LLM JSON
   - Missing required extracted fields
   - Confidence out of range
   - Provider fetch failure
   - Empty/HTML-heavy body normalization
   - Duplicate processing under retry conditions

6. If migrations were added, verify they apply cleanly and the app still builds/tests successfully.

# Risks and follow-ups
- The repo may already contain partial sales/email ingestion models; avoid duplicating concepts and instead extend existing ones.
- Existing lead matching rules may be weak; if sender-email-only matching is insufficient, document follow-up improvements.
- Provider APIs may expose different thread/message identifiers; normalize carefully to preserve idempotency across Gmail and Microsoft.
- LLM structured output reliability may vary; keep validation strict and fallback conservative.
- If no existing audit entity cleanly fits ignored-email recording, add the smallest auditable persistence needed and document it.
- Event naming/payload conventions may already exist; align with current outbox/event schema rather than inventing a new pattern.
- If thread-level lead uniqueness rules are ambiguous, implement the safest interpretation for acceptance criteria and note any domain assumptions in code comments or PR notes.
- Follow-up candidates after this task:
  - richer lead/contact/company matching
  - configurable confidence thresholds per tenant
  - replay/reconciliation tooling for mailbox backfills
  - analytics on ignored-email categories and detection precision