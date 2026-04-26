# Goal
Implement backlog task **TASK-30.1.3 — Build manual inbox scan endpoint and orchestration job limited to the last 30 days** for story **US-30.1 Implement tenant-scoped mailbox connection and manual bill scan initiation**.

Deliver a production-ready vertical slice in the existing .NET solution that:
- exposes a tenant-scoped, user-triggered API endpoint for **manual inbox bill scan**
- starts an application/background orchestration job that scans **only the last 30 days**
- limits evaluation to configured folders/labels and the required bill-related keywords
- persists an **EmailIngestionRun** audit record with the required metrics and failure details
- does **not** create any payment action or approval proposal as part of connection or scan initiation

Keep the implementation aligned with the modular monolith, CQRS-lite, tenant isolation, and auditability requirements in the architecture.

# Scope
Implement only what is necessary for this task and acceptance criteria.

In scope:
- Mailbox scan initiation endpoint for the authenticated user within the active tenant/company context
- Application command/service/job orchestration for manual scan execution
- Domain/infrastructure support for:
  - mailbox connection lookup by tenant + user
  - provider-specific scan execution abstraction for Gmail and Microsoft 365
  - 30-day lookback enforcement
  - configured folder/label filtering
  - keyword filtering using:
    - `invoice`
    - `bill`
    - `faktura`
    - `payment due`
    - `amount due`
    - `OCR`
    - `IBAN`
    - `bankgiro`
    - `plusgiro`
- Persistence of `MailboxConnection` status/tokens if needed to support the scan path
- Persistence of `EmailIngestionRun` audit data:
  - start time
  - end time
  - provider
  - scanned message count
  - detected candidate count
  - failure details
- Safe API response contract for scan initiation and/or current run result
- Tests covering tenant scoping, 30-day limit, keyword/folder filtering behavior, and “no downstream payment/approval creation”

Out of scope:
- Full OAuth connection flow UI unless already partially present and only minimal backend additions are needed
- Automatic scheduled scans
- Full email ingestion parsing pipeline beyond identifying bill candidates
- Creating payment actions, approval requests, or approval proposals
- Expanding scopes beyond minimal read scopes required for message and attachment retrieval
- Broad inbox sync/history import beyond the last 30 days for this manual action

# Files to touch
Inspect the solution structure first and then update the appropriate files in these projects.

Likely areas:
- `src/VirtualCompany.Api`
  - mailbox/integration controller or minimal API endpoint registration
  - request/response DTOs
  - auth/tenant enforcement wiring if needed
- `src/VirtualCompany.Application`
  - command + handler for manual inbox scan initiation
  - orchestration service / background job abstraction
  - provider-agnostic mailbox scan interfaces
  - validation and policy checks
- `src/VirtualCompany.Domain`
  - `MailboxConnection` entity/value objects/enums if missing or incomplete
  - `EmailIngestionRun` entity
  - provider/status enums
  - domain rules for scan constraints
- `src/VirtualCompany.Infrastructure`
  - EF Core configurations/mappings
  - repository implementations
  - Gmail/Microsoft 365 provider adapters
  - token encryption usage/integration
  - background job execution implementation
- `tests/VirtualCompany.Api.Tests`
  - endpoint tests
  - integration-style tests for tenant/user scoping and command behavior

Also inspect:
- existing migrations strategy and where schema changes are added
- any current integration module patterns
- any existing outbox/background worker/job abstractions
- any current audit entity patterns

If schema changes are required, add them in the project’s current migration style rather than inventing a new one.

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect how the solution currently handles:
     - tenant/company resolution
     - authenticated user resolution
     - CQRS commands/handlers
     - EF Core entities/configurations
     - background jobs/workers
     - encrypted secrets/tokens
     - audit records
   - Reuse naming and module boundaries already present in the codebase.

2. **Model or complete the mailbox domain**
   - Ensure there is a `MailboxConnection` model stored per **tenant/company** and per **user**.
   - Ensure it includes at minimum:
     - id
     - company/tenant id
     - user id
     - provider
     - encrypted access/refresh tokens or token payload
     - connection status
     - configured folders/labels
     - created/updated timestamps
   - Add/complete `EmailIngestionRun` with fields required by acceptance criteria.
   - Prefer explicit enums for:
     - provider (`Gmail`, `Microsoft365`)
     - connection status
     - run status if useful
   - Keep tokens encrypted at rest using existing infrastructure patterns.

3. **Add the manual scan application command**
   - Create a command such as `StartManualInboxBillScanCommand`.
   - Inputs should be derived from authenticated context where possible, not trusted from client payload:
     - company/tenant id
     - user id
   - Handler responsibilities:
     - resolve the caller’s mailbox connection for the active tenant
     - verify connection exists and is in a usable/connected state
     - create an `EmailIngestionRun` record with start metadata
     - enqueue or invoke the scan orchestration job
     - return a safe result (run id/status)
   - Enforce that the scan is manual and tenant-scoped.

4. **Expose the API endpoint**
   - Add a secure endpoint such as `POST /api/mailbox/scan` or follow existing route conventions.
   - Require authenticated user and active tenant/company context.
   - Do not accept arbitrary tenant/user ids from the client unless existing API conventions require them and they are server-validated.
   - Return:
     - accepted/started status
     - ingestion run id
     - possibly provider and started timestamp
   - Map missing connection, forbidden access, and invalid state to appropriate HTTP responses.

5. **Implement the scan orchestration service/job**
   - Create a provider-agnostic orchestration service, e.g. `IManualInboxBillScanOrchestrator`.
   - The orchestrator must:
     - load the `MailboxConnection`
     - compute `fromDate = UtcNow - 30 days`
     - call the provider adapter with the strict lookback window
     - restrict scanning to configured folders/labels only
     - evaluate messages using the required keyword set
     - count scanned messages
     - count detected bill candidates
     - capture failures without creating payment or approval artifacts
     - update `EmailIngestionRun` end time and failure details
   - If the app already has a background worker queue, enqueue the job there.
   - If not, implement the smallest consistent async execution mechanism already used by the solution.

6. **Implement provider adapter contracts**
   - Define an abstraction similar to:
     - `IMailProviderClient`
     - `IMailProviderClientFactory`
   - Provider methods should support:
     - listing messages since a date
     - limiting to folders/labels
     - retrieving enough message/attachment metadata for candidate detection
   - Gmail adapter:
     - use minimal read scopes for messages/attachments retrieval only
   - Microsoft 365 adapter:
     - use minimal read scopes for messages/attachments retrieval only
   - Do not broaden permissions beyond acceptance criteria.
   - If OAuth connection flow already exists, ensure scopes align; if not, at least codify the required scopes in provider configuration/constants.

7. **Implement bill-candidate filtering**
   - Apply filtering only within configured folders/labels.
   - Evaluate bill-related keywords case-insensitively across appropriate searchable fields, such as:
     - subject
     - snippet/body preview
     - attachment names if already available cheaply
   - Required keywords:
     - invoice
     - bill
     - faktura
     - payment due
     - amount due
     - OCR
     - IBAN
     - bankgiro
     - plusgiro
   - Keep the matching logic deterministic and testable.
   - Avoid overreaching into full document extraction unless already present.

8. **Persist audit/run outcomes**
   - `EmailIngestionRun` must be persisted for every manual scan attempt.
   - Record:
     - start time
     - end time
     - provider
     - scanned message count
     - detected candidate count
     - failure details
   - On failure:
     - persist failure details safely
     - do not lose the run record
   - If partial success is possible, still finalize the run with the best available counts and error summary.

9. **Explicitly prevent downstream actions**
   - Verify the scan path does **not** create:
     - payment actions
     - approval requests/proposals
   - If there are existing downstream hooks/events from email ingestion, either:
     - do not invoke them in this task, or
     - gate them behind a later-stage explicit action not triggered here
   - Add tests asserting no such records/messages are created.

10. **Data access and tenant isolation**
    - Ensure all queries and updates are scoped by company/tenant id.
    - Mailbox connections must be resolved by both tenant and user.
    - Do not allow one user to trigger scans against another user’s connection unless an explicit existing authorization rule already supports it, which this task does not require.
    - Follow existing repository/query patterns.

11. **Schema and persistence**
    - Add/adjust EF configurations and migrations for:
      - `MailboxConnection`
      - `EmailIngestionRun`
      - any enums/value conversions
      - encrypted token columns
      - configured folders/labels storage
    - Keep schema names and timestamps consistent with the rest of the project.
    - If JSON storage is already used for flexible config, it is acceptable for folder/label configuration and failure details.

12. **Testing**
    - Add tests for:
      - authenticated tenant-scoped user can start a scan for their own connection
      - scan is rejected when no mailbox connection exists
      - scan uses only the last 30 days
      - scan evaluates only configured folders/labels
      - keyword matching includes all required terms
      - `EmailIngestionRun` is persisted with required fields
      - failures persist failure details
      - no payment action or approval proposal/request is created
    - Prefer fast tests with provider clients mocked/faked.
    - Add at least one API-level test for endpoint behavior.

# Validation steps
Run the relevant validation locally and include any necessary notes in the final implementation summary.

Minimum:
1. Restore/build:
   - `dotnet build`
2. Run tests:
   - `dotnet test`

Also validate manually or via tests:
- endpoint requires authentication
- endpoint is tenant-scoped
- endpoint starts a run only when a valid mailbox connection exists for the current user in the current tenant
- provider selection is based on the stored mailbox connection
- scan window is hard-limited to the last 30 days
- only configured folders/labels are scanned
- keyword filter includes all required terms
- `EmailIngestionRun` is created and finalized with counts and timestamps
- failure details are persisted on provider/API errors
- no payment/approval entities are created by this flow

If migrations are added, ensure they are included and the app still builds/tests cleanly.

# Risks and follow-ups
- **Existing OAuth/mail integration may be incomplete**  
  If connection flow or token storage is not yet implemented, keep this task focused on the scan endpoint/job and add only the minimum supporting domain/infrastructure needed.

- **Background job infrastructure may vary**  
  Reuse the project’s existing worker/queue pattern. Do not introduce a heavy new dependency unless already standard in the solution.

- **Provider API differences**  
  Gmail labels/folders and Microsoft 365 folders differ. Normalize them behind the provider abstraction, but keep behavior explicit and test-covered.

- **Keyword matching may be simplistic**  
  For this task, deterministic keyword matching is acceptable. More advanced classification can be a later backlog item.

- **Failure detail sensitivity**  
  Persist useful operational failure details without storing raw secrets or excessive provider payloads.

- **No downstream automation**  
  Be careful not to accidentally trigger later-stage ingestion/payment workflows through shared events or handlers. If necessary, isolate this manual scan path from those side effects.

- **Potential follow-up tasks**
  - OAuth connection endpoints/UI completion for Gmail and Microsoft 365
  - mailbox folder/label configuration management UI
  - scheduled recurring scans
  - candidate review UI
  - richer attachment/content extraction
  - deduplication/idempotency across repeated manual scans