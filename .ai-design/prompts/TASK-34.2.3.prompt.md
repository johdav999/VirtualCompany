# Goal
Implement backlog task **TASK-34.2.3** for story **US-34.2 Build real sales email ingestion, intent detection, and lead/deal linking workflows** by adding a deterministic, idempotent lead-ingestion workflow behind:

- `POST /api/sales/email/process-message`
- `POST /api/sales/email/process-thread`

The implementation must:

- Process **real mailbox messages** from existing Gmail and Microsoft integrations only, with no mock providers.
- Detect supported sales signals from inbound email content.
- Create or update leads using extracted:
  - sender email
  - contact name
  - company name when available
  - intent
  - product/service interest
  - urgency
  - confidence score
- Ignore newsletters, receipts, invoices, and support tickets without upsell intent, while persisting an auditable ignore reason.
- Be **idempotent** for repeated processing of the same message or thread.
- Prevent duplicate leads for the same email thread.
- On successful lead detection:
  - create a `SalesActivity`
  - emit `sales.email.received`
  - emit `sales.lead.detected`
  - persist `SalesEmailLinks` to source message and thread

Use the existing modular monolith / clean architecture conventions in this repo. Prefer deterministic application services, transactional persistence, and outbox-backed event publication.

# Scope
In scope:

- API endpoints for processing a single mailbox message and a mailbox thread
- Application command/handler or service orchestration for sales email ingestion
- Real provider resolution for Gmail and Microsoft mailbox connections already present in the codebase
- Domain persistence for:
  - lead create/update
  - duplicate prevention
  - email-thread linking
  - ignore decisions
  - sales activity logging
  - idempotency guards
- Outbox/event publication for:
  - `sales.email.received`
  - `sales.lead.detected`
- Tests covering:
  - successful lead creation
  - lead update on existing thread/contact
  - ignored-message audit persistence
  - idempotent reprocessing
  - duplicate prevention across same thread

Out of scope unless required by existing patterns:

- New UI pages
- Mock mailbox providers
- Broad CRM/deal pipeline redesign
- LLM-heavy probabilistic orchestration if a deterministic classifier/extractor pattern already exists or can be added simply
- New external infrastructure beyond current PostgreSQL/outbox setup

# Files to touch
Inspect first, then update the minimum coherent set. Likely areas:

- `src/VirtualCompany.Api/**`
  - sales email controller/endpoints
  - request/response contracts
- `src/VirtualCompany.Application/**`
  - sales email processing commands/handlers/services
  - provider abstraction usage
  - idempotency and duplicate-prevention orchestration
  - event/outbox enqueue logic
- `src/VirtualCompany.Domain/**`
  - lead, sales activity, sales email link, ignore audit, and related domain entities/value objects
  - domain enums/statuses/reasons if missing
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories
  - provider adapters for Gmail/Microsoft message retrieval if not already wired
  - outbox persistence
  - migrations or schema updates
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests
- Possibly:
  - `README.md` or module docs if conventions require documentation
  - migration files under the project’s active migration location

Before coding, locate existing implementations or partial models for:

- leads / sales leads
- sales activities
- email integration connections
- Gmail/Microsoft mailbox sync/read services
- outbox/events
- audit events
- idempotency keys / processed message tracking
- thread/message link tables

# Implementation plan
1. **Discover existing sales and integration primitives**
   - Search the solution for:
     - `Lead`, `SalesLead`, `SalesActivity`, `EmailLink`, `Outbox`, `AuditEvent`
     - Gmail/Microsoft connection entities and mailbox client abstractions
     - existing sales email endpoints or placeholders
   - Reuse current naming and module boundaries rather than inventing parallel models.
   - Identify the active persistence approach:
     - EF Core DbContext(s)
     - repository pattern
     - transaction/outbox conventions

2. **Define the ingestion contract and endpoint behavior**
   - Add or complete:
     - `POST /api/sales/email/process-message`
     - `POST /api/sales/email/process-thread`
   - Request DTOs should minimally identify:
     - company/tenant context via existing auth/tenant resolution
     - integration connection id or mailbox account reference
     - provider message id and/or thread id
   - Do not accept raw mock payloads if the task requires processing from existing real connections.
   - Return deterministic result payloads such as:
     - processed
     - ignored
     - idempotent-noop
     - lead-created
     - lead-updated
     - linked-existing

3. **Implement provider-backed message/thread retrieval**
   - Use existing Gmail and Microsoft connection infrastructure to fetch:
     - message metadata
     - sender/from
     - subject
     - body/snippet
     - received timestamp
     - provider message id
     - provider thread/conversation id
   - Normalize provider-specific payloads into a shared internal model, e.g.:
     - `NormalizedSalesEmailMessage`
     - `NormalizedSalesEmailThread`
   - Reject unsupported or disconnected providers with safe validation errors.
   - Ensure tenant/company scoping is enforced on connection lookup.

4. **Add deterministic sales-signal classification and extraction**
   - Implement a deterministic classifier service that decides:
     - supported sales signal
     - ignored category with reason
   - At minimum support ignore reasons for:
     - newsletter
     - receipt
     - invoice
     - support-ticket-no-upsell-intent
     - unsupported / insufficient-signal if needed
   - Extract structured fields from message/thread:
     - sender email
     - contact name
     - company name when inferable
     - intent
     - product/service interest
     - urgency
     - confidence score
   - Prefer explicit heuristics/rules already used in the codebase. If an AI extraction service already exists and is standard for this module, keep the workflow deterministic by:
     - constraining schema
     - persisting confidence
     - making idempotency independent of model output

5. **Design idempotency guards**
   - Add persistence for processed source references if missing.
   - Enforce idempotency on:
     - provider + company + connection + message id
     - provider + company + connection + thread id for thread processing
   - Reprocessing the same message/thread must not:
     - create duplicate leads
     - create duplicate sales activities
     - create duplicate source links
     - emit duplicate business events
   - Prefer database-backed uniqueness constraints plus application checks.
   - If both message-level and thread-level processing can touch the same thread, ensure the dedupe strategy converges on one lead per thread.

6. **Implement duplicate prevention and lead linking**
   - Define the canonical duplicate-prevention rule:
     - one lead per company per normalized email thread/source thread
     - optionally merge with existing lead by sender email if thread link absent and current domain rules support it
   - Persist `SalesEmailLinks` (or equivalent) linking:
     - lead id
     - source provider
     - connection/account id
     - provider message id
     - provider thread id
   - On processing:
     - if thread already linked to a lead, update that lead rather than creating a new one
     - if message already linked, return idempotent result
     - if sender matches an existing lead and no conflicting thread link exists, update existing lead per current domain rules
   - Keep all writes transactional.

7. **Persist ignored messages with auditability**
   - For ignored messages/threads, persist an auditable record including:
     - company id
     - source provider
     - connection id
     - provider message/thread ids
     - ignore reason
     - classification summary / confidence if available
     - timestamps
   - If the system already uses `audit_events`, create a business audit event as well or instead according to existing conventions.
   - Reprocessing an already ignored message/thread should also be idempotent.

8. **Create/update lead records**
   - On positive sales detection, create or update the lead with extracted fields.
   - Preserve existing lead data quality rules:
     - do not overwrite stronger existing values with weaker/null values
     - update last-seen / latest-intent style fields if such patterns exist
   - Ensure tenant scoping on all lead queries and writes.

9. **Create `SalesActivity`**
   - On successful lead detection, create one activity record representing the inbound email signal.
   - Include references to:
     - lead
     - source message/thread
     - activity type
     - summary/rationale
     - confidence
     - timestamps
   - Guard against duplicate activity creation during reprocessing.

10. **Publish workflow/business events via outbox**
    - Emit:
      - `sales.email.received`
      - `sales.lead.detected`
    - Use the project’s outbox pattern rather than inline fire-and-forget dispatch.
    - Ensure event payloads include enough identifiers for downstream workflows:
      - company id
      - lead id
      - sales activity id
      - provider
      - connection id
      - provider message id
      - provider thread id
      - correlation/idempotency key if conventions exist
    - Do not enqueue duplicate events on idempotent reprocessing.

11. **Database schema and constraints**
    - Add/update schema for any missing tables/columns such as:
      - sales email links
      - processed email ingestion records / idempotency keys
      - ignored email audit records
      - unique indexes for message/thread dedupe
    - Prefer explicit unique constraints for:
      - company + provider + connection + provider message id
      - company + provider + connection + provider thread id where appropriate
      - one lead link per canonical thread
    - Add EF configurations and migration(s).

12. **Testing**
    - Add integration-style tests around the API endpoints using the project’s existing test patterns.
    - Cover at least:
      - Gmail message with sales intent creates lead, activity, links, and outbox events
      - Microsoft thread with sales intent updates existing lead on same thread
      - newsletter/receipt/invoice/support-ticket-no-upsell message is ignored with persisted reason
      - same message processed twice returns idempotent result and does not duplicate lead/activity/events
      - same thread processed twice does not create duplicate lead
      - message then thread processing for same underlying thread still results in one lead and one canonical linkage set
      - tenant scoping prevents cross-company connection/message access
    - If provider clients are abstracted, fake the provider client at the application boundary while still exercising the real endpoint and persistence path.

13. **Implementation quality constraints**
    - Keep orchestration in application layer, not controllers.
    - Keep provider-specific logic in infrastructure adapters.
    - Keep domain invariants in domain entities/value objects where practical.
    - Use cancellation tokens, async APIs, and existing logging/correlation patterns.
    - Avoid introducing mock-provider code paths.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify endpoint behavior manually or via tests:
   - `POST /api/sales/email/process-message`
   - `POST /api/sales/email/process-thread`

4. Confirm persistence outcomes in test assertions or local DB:
   - lead created/updated
   - ignored records persisted with reason
   - sales activity created once
   - sales email links persisted
   - outbox records for:
     - `sales.email.received`
     - `sales.lead.detected`

5. Confirm idempotency:
   - process same message twice
   - process same thread twice
   - process message then thread for same conversation
   - verify no duplicate:
     - leads
     - activities
     - links
     - outbox events

6. Confirm provider restrictions:
   - Gmail existing connection works
   - Microsoft existing connection works
   - unsupported/mock provider path is rejected

7. Confirm tenant isolation:
   - connection lookup and resulting records remain company-scoped
   - cross-tenant access fails safely

# Risks and follow-ups
- **Unknown existing domain model names**
  - The repo may already have lead/activity/email-link concepts under different names. Reuse them instead of creating duplicates.

- **Provider API shape differences**
  - Gmail and Microsoft thread/message identifiers differ. Normalize carefully and document the canonical dedupe key.

- **Thread dedupe edge cases**
  - Some messages may lack stable thread ids or have provider-specific conversation semantics. If encountered, use the strongest available canonical key and note any fallback behavior in code comments/tests.

- **Classifier determinism**
  - If current implementation relies on AI extraction, keep idempotency and duplicate prevention independent of model variability. Persist normalized source keys and avoid event duplication regardless of extraction output.

- **Migration placement**
  - Confirm the repo’s active migration workflow before adding schema changes; do not use archived migration locations unless the solution currently does.

- **Outbox contract consistency**
  - Match existing event envelope/schema conventions exactly so downstream dispatchers continue to work.

- **Potential follow-up tasks**
  - richer lead merge rules across sender aliases/domains
  - support-ticket upsell detection refinement
  - observability dashboards for ignored vs detected email classifications
  - replay tooling for failed ingestion records
  - deal creation/linking once lead qualification rules are finalized