# Goal
Implement backlog task **TASK-34.2.1** for story **US-34.2 Build real sales email ingestion, intent detection, and lead/deal linking workflows** by replacing any mock email ingestion path with a real `ISalesEmailIngestionService` that reads inbound messages from existing **Gmail** and **Microsoft** OAuth mailbox connections, supports both **single-message** and **thread-aware** processing, performs **sales-signal detection**, creates or updates leads idempotently, records ignored-message audit reasons, creates `SalesActivity`, emits required domain/integration events, and persists `SalesEmailLinks` to source message and thread.

# Scope
In scope:
- Implement `ISalesEmailIngestionService` in the application/infrastructure layers using existing mailbox OAuth connections.
- Support:
  - `POST /api/sales/email/process-message`
  - `POST /api/sales/email/process-thread`
- Retrieve real messages from Gmail and Microsoft providers using existing connection/token infrastructure.
- Normalize provider message/thread payloads into a common internal contract.
- Process inbound email content for supported sales signals.
- Ignore newsletters, receipts, invoices, and support tickets without upsell intent, while persisting an auditable ignore reason.
- Ensure idempotent reprocessing of the same message/thread and prevent duplicate leads for the same email thread.
- On successful lead detection:
  - create/update lead
  - create `SalesActivity`
  - emit `sales.email.received`
  - emit `sales.lead.detected`
  - persist `SalesEmailLinks`

Out of scope unless already partially implemented and required to complete the task:
- New OAuth connection UX
- New mailbox sync engine beyond direct retrieval for requested message/thread
- Full deal-stage automation beyond lead/deal linking already present in domain
- LLM-heavy classification redesign if deterministic/domain heuristics already exist and can be extended
- Frontend UX changes beyond API compatibility
- New external providers beyond Gmail and Microsoft

# Files to touch
Inspect first, then update the minimum necessary set in these areas:

- `src/VirtualCompany.Api/**`
  - Sales email processing controllers/endpoints for `/api/sales/email/process-message` and `/api/sales/email/process-thread`
  - Request/response DTOs if needed
- `src/VirtualCompany.Application/**`
  - `ISalesEmailIngestionService`
  - command/query handlers or application services for sales email processing
  - provider-agnostic mailbox message/thread models
  - sales signal detection orchestration
  - idempotency handling
  - event publishing/outbox integration
- `src/VirtualCompany.Domain/**`
  - lead, sales activity, sales email link, ignore-reason, and event entities/value objects if missing
  - domain events for `sales.email.received` and `sales.lead.detected` if represented explicitly
- `src/VirtualCompany.Infrastructure/**`
  - Gmail mailbox retrieval adapter using existing OAuth connection/token storage
  - Microsoft mailbox retrieval adapter using existing OAuth connection/token storage
  - implementation of `ISalesEmailIngestionService`
  - repositories for lead lookup/upsert, sales activity, sales email links, ignored-email audit records
  - EF Core configurations/mappings
  - outbox/event dispatcher integration
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests for process-message and process-thread
- Potentially:
  - `tests/**Application**`
  - `tests/**Infrastructure**`
  - migration files if new persistence objects/constraints are required

Also inspect for existing relevant types before creating new ones:
- mailbox integration abstractions
- OAuth connection entities
- lead/deal domain models
- sales activity models
- audit/outbox/event infrastructure
- idempotency or deduplication helpers
- provider clients for Gmail Graph/Microsoft Graph

# Implementation plan
1. **Discover existing architecture before coding**
   - Find current implementations or stubs for:
     - `ISalesEmailIngestionService`
     - sales lead/deal services
     - mailbox OAuth connection storage
     - Gmail/Microsoft integration clients
     - outbox/event publishing
     - audit event persistence
   - Trace the current `/api/sales/email/process-message` and `/api/sales/email/process-thread` endpoints.
   - Identify whether there is already a normalized mailbox abstraction; reuse it if present.

2. **Define the ingestion contract**
   - Ensure `ISalesEmailIngestionService` exposes explicit methods for:
     - processing a single message by provider connection + provider message id
     - processing a thread by provider connection + provider thread/conversation id
   - Return a structured result including:
     - processed/ignored/already-processed status
     - lead id if detected
     - thread id/message id linkage
     - ignore reason if ignored
     - idempotency outcome

3. **Implement provider-agnostic mailbox normalization**
   - Create or extend a normalized model for inbound email retrieval with fields such as:
     - company/tenant id
     - mailbox connection id
     - provider type
     - external message id
     - external thread id / conversation id
     - internet message id
     - subject
     - plain text body
     - html body if available
     - sender email
     - sender display name
     - recipients
     - received timestamp
     - references/in-reply-to headers if available
   - Gmail:
     - use existing Gmail OAuth connection and API client
     - retrieve message and thread details from real Gmail APIs
   - Microsoft:
     - use existing Microsoft OAuth connection and Graph client
     - retrieve message and conversation/thread details from real Microsoft APIs
   - Prefer thread-aware retrieval for thread processing and include all relevant inbound messages in chronological order.

4. **Implement message classification and ignore logic**
   - Reuse existing sales intent detection if available; otherwise implement a pragmatic deterministic classifier first.
   - Supported positive signals should extract when possible:
     - sender email
     - contact name
     - company name
     - intent
     - product/service interest
     - urgency
     - confidence score
   - Explicitly ignore and audit:
     - newsletters
     - receipts
     - invoices
     - support tickets without upsell intent
   - Persist a machine-readable ignore reason and a human-readable rationale summary.
   - Keep classification deterministic/testable; do not bury core logic inside controllers.

5. **Implement lead create/update and thread deduplication**
   - Deduplicate by tenant + source thread identity, with fallback strategy if provider thread id is absent.
   - Reprocessing the same message or thread must be idempotent:
     - do not create duplicate leads
     - do not create duplicate `SalesEmailLinks`
     - do not create duplicate `SalesActivity` for the same detection event
   - Link new qualifying messages in the same thread to the existing lead when appropriate.
   - If a lead already exists for the thread, update it with newly extracted information only when it improves completeness/confidence.

6. **Persist source linkage and auditability**
   - Ensure successful processing stores `SalesEmailLinks` for:
     - source message
     - source thread
     - provider
     - mailbox connection
   - Ensure ignored messages/threads are also auditable with:
     - provider ids
     - ignore reason
     - processed timestamp
     - actor/system source
   - If no dedicated table exists, add one consistent with current domain patterns.

7. **Create `SalesActivity` and emit events**
   - On successful lead detection:
     - create `SalesActivity`
     - emit/persist outbox events for:
       - `sales.email.received`
       - `sales.lead.detected`
   - Ensure event payloads include tenant/company id, lead id, source message/thread identifiers, provider, and confidence/intent metadata as appropriate.
   - Follow existing outbox/event conventions rather than introducing ad hoc publishing.

8. **Wire endpoints to real processing**
   - Update `POST /api/sales/email/process-message` to call the real ingestion service.
   - Update `POST /api/sales/email/process-thread` to call the real ingestion service.
   - Remove or bypass any mock provider path for these endpoints.
   - Preserve authorization, tenant scoping, and safe error handling.
   - Return clear API responses for:
     - processed with lead detected
     - ignored with reason
     - already processed/idempotent no-op
     - invalid provider/message/thread/connection

9. **Add persistence constraints for idempotency**
   - Add DB constraints/indexes where needed, likely unique indexes on combinations such as:
     - tenant/company + provider + external message id
     - tenant/company + provider + external thread id
     - tenant/company + lead/thread linkage
   - Prefer database-enforced uniqueness plus application-level graceful handling.
   - Add EF migrations if schema changes are required.

10. **Testing**
   - Add unit tests for:
     - positive sales detection
     - ignored newsletter/receipt/invoice/support-ticket cases
     - idempotent reprocessing of same message
     - idempotent reprocessing of same thread
     - lead update vs duplicate lead creation
   - Add integration/API tests for both endpoints using mocked provider clients but real application flow.
   - Verify emitted events and persisted `SalesEmailLinks`/`SalesActivity`.
   - Verify tenant scoping and forbidden cross-tenant access if endpoint tests already cover auth patterns.

# Validation steps
1. Restore/build:
   - `dotnet build`
2. Run tests:
   - `dotnet test`
3. If migrations were added, verify they apply cleanly in the project’s normal migration flow.
4. Validate endpoint behavior with automated tests covering:
   - `POST /api/sales/email/process-message`
   - `POST /api/sales/email/process-thread`
5. Confirm acceptance criteria explicitly:
   - real Gmail connection path used
   - real Microsoft connection path used
   - no mock provider dependency in production code path
   - qualifying inbound email creates or updates a lead
   - extracted fields include sender/contact/company/intent/product-or-service-interest/urgency/confidence where available
   - ignored categories are persisted with auditable ignore reason
   - reprocessing same message/thread is idempotent
   - successful detection creates `SalesActivity`
   - `sales.email.received` emitted
   - `sales.lead.detected` emitted
   - `SalesEmailLinks` persisted to source message and thread
6. If possible, add one end-to-end service test per provider adapter using mocked external HTTP responses shaped like Gmail and Microsoft APIs to verify normalization and thread retrieval logic.

# Risks and follow-ups
- **Unknown existing models:** The repo may already contain partial sales, lead, or mailbox abstractions. Reuse them instead of creating parallel types.
- **Thread identity mismatch:** Gmail and Microsoft thread/conversation semantics differ. Normalize carefully and document fallback dedupe behavior.
- **Token refresh complexity:** Existing OAuth connection infrastructure may require refresh-token handling; do not duplicate auth logic.
- **Body parsing quality:** HTML-only emails and forwarded chains may reduce extraction quality. Prefer plain text extraction with safe HTML fallback.
- **False positives/negatives:** Deterministic ignore/signal rules should be conservative and auditable. If confidence is low, consider recording as ignored/non-actionable rather than creating noisy leads.
- **Idempotency race conditions:** Use DB uniqueness and transactional handling to avoid duplicate lead/activity creation under concurrent requests.
- **Event duplication:** Ensure outbox records are also idempotent or tied to the same processing transaction.
- **Schema additions:** If new tables are required for ignored-email audit or sales email links, add migrations and indexes carefully.
- **Follow-up candidates after this task:**
  - background mailbox sync/webhook ingestion
  - richer NLP/LLM-assisted extraction behind deterministic guardrails
  - deal auto-linking and stage progression
  - UI for ignored-email audit review
  - provider retry/observability dashboards