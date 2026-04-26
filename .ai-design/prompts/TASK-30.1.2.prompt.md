# Goal
Implement `TASK-30.1.2` for story `US-30.1` by adding tenant-scoped Gmail and Microsoft 365 mailbox OAuth connection flows, secure mailbox connection persistence, and a manual inbox scan initiation flow that scans only the last 30 days using minimal read scopes and bill-oriented filtering.

The implementation prompt should instruct the coding agent to:
- add provider-specific OAuth connect/callback flows for Gmail and Microsoft 365
- persist mailbox connections per tenant and per user in a `MailboxConnection` record
- encrypt access/refresh tokens at rest
- support a manual `"Scan inbox for bills"` action
- ensure scans are constrained to:
  - last 30 days only
  - configured folders/labels only
  - bill-related keywords only
- persist an `EmailIngestionRun` audit record for each scan
- explicitly avoid creating any payment action or approval proposal from connection or scan initiation alone

# Scope
Implement only the backend and any minimal web/API surface needed for this task in the existing .NET modular monolith.

Include:
- Domain model additions for mailbox connections and ingestion runs
- PostgreSQL persistence and EF Core mappings/migrations
- OAuth provider abstractions and implementations for:
  - Gmail
  - Microsoft 365
- Application commands/services for:
  - starting OAuth connection
  - handling OAuth callback
  - triggering manual scan
- Tenant/user-scoped authorization and data access
- Background-safe or synchronous scan orchestration as appropriate for current architecture
- Audit persistence via `EmailIngestionRun`
- Minimal configuration model for allowed folders/labels and keyword filtering
- Tests covering happy path and guardrails

Do not include:
- automatic scheduled scans unless required as a small reusable internal abstraction
- payment creation
- approval creation
- invoice extraction/classification beyond candidate detection
- full UI polish beyond minimal endpoint/page wiring
- broad email ingestion pipeline unrelated to this task

# Files to touch
The coding agent should inspect the solution structure first, then update the most appropriate files in these projects:

- `src/VirtualCompany.Domain`
  - add entities/value objects/enums for mailbox integration
- `src/VirtualCompany.Application`
  - add commands, handlers, DTOs, interfaces, validation
- `src/VirtualCompany.Infrastructure`
  - add EF Core configurations, repositories, encryption/token storage, OAuth provider clients, mailbox scan adapters
- `src/VirtualCompany.Api`
  - add controller/endpoints for connect/callback/manual scan
- `src/VirtualCompany.Web`
  - only if needed for minimal trigger UI or callback handling
- `tests/VirtualCompany.Api.Tests`
  - add integration/API tests
- migration location used by the repo’s current EF/PostgreSQL setup
- app configuration files for OAuth settings/secrets placeholders if the repo already uses typed options

Likely new artifacts:
- `MailboxConnection` entity
- `EmailIngestionRun` entity
- provider enum/status enum
- token encryption service interface/implementation
- OAuth state/nonce handling component
- Gmail mailbox provider adapter
- Microsoft 365 mailbox provider adapter
- manual scan command/handler
- migration for new tables/indexes

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect how the solution currently models:
     - tenant-scoped entities
     - user identity and membership
     - EF Core entity configurations
     - command/query handlers
     - API controllers
     - encryption/secrets/options
     - audit records and background jobs
   - Reuse established conventions rather than inventing a parallel pattern.

2. **Add domain model for mailbox connections**
   - Create a `MailboxConnection` aggregate/entity with tenant and user ownership.
   - Required fields should include at minimum:
     - `Id`
     - `CompanyId` or equivalent tenant key used in repo
     - `UserId`
     - `Provider` (`Gmail`, `Microsoft365`)
     - `EmailAddress`
     - `ConnectionStatus` (`Pending`, `Connected`, `Failed`, `Revoked`, optionally `Expired`)
     - encrypted access token
     - encrypted refresh token
     - token expiry timestamp
     - granted scopes
     - provider account identifier
     - configured folders/labels JSON or normalized child table based on repo conventions
     - created/updated timestamps
     - last successful scan timestamp nullable
     - last failure summary nullable
   - Ensure uniqueness rules prevent duplicate active connections for the same tenant/user/provider/account as appropriate.

3. **Add domain model for ingestion audit**
   - Create `EmailIngestionRun` with fields aligned to acceptance criteria:
     - `Id`
     - tenant key
     - `MailboxConnectionId`
     - `UserId` if useful for audit
     - `Provider`
     - `StartedAt`
     - `EndedAt`
     - `ScannedMessageCount`
     - `DetectedCandidateCount`
     - `FailureDetails`
     - optional status/result field
     - optional scan window start/end for traceability
   - Keep this as a business audit record, not just technical logging.

4. **Persist with EF Core/PostgreSQL**
   - Add entity configurations and migration.
   - Add indexes for:
     - tenant + user
     - tenant + provider
     - mailbox connection foreign key on ingestion runs
     - created/started timestamps for audit queries
   - Store granted scopes and configured folders/labels in JSONB if that matches repo style.
   - Ensure all tenant-owned tables include the tenant/company key.

5. **Implement token encryption**
   - Do not store raw OAuth tokens in plaintext.
   - Reuse any existing data protection/encryption abstraction in the repo; otherwise add a focused infrastructure service such as:
     - `ITokenEncryptionService`
     - `Protect(string plaintext)`
     - `Unprotect(string ciphertext)`
   - Use ASP.NET Core Data Protection or an existing KMS-backed abstraction if already present.
   - Keep encryption concerns in infrastructure, not controllers.

6. **Add provider abstractions**
   - Define an application-facing interface like `IMailboxOAuthProvider` / `IMailboxProviderClient` with methods for:
     - building authorization URL
     - exchanging auth code for tokens
     - refreshing tokens if needed
     - listing configured folders/labels
     - listing messages in a date window and folder/label scope
     - retrieving attachment/message metadata needed for candidate detection
   - Implement two providers:
     - Gmail
     - Microsoft 365

7. **Use minimal OAuth scopes**
   - Gmail: request only the minimum scopes needed to read messages/attachments and basic profile/email identity needed to bind the account. Prefer the narrowest Gmail read scope that supports message and attachment retrieval.
   - Microsoft 365: request only the minimum delegated read scopes needed to read mail and attachments and identify the signed-in mailbox.
   - The coding agent must document in code comments or options which exact scopes were chosen and why.
   - Do not request send, modify, delete, offline scopes beyond what is necessary for refresh token support.

8. **Implement secure OAuth state handling**
   - Add a state/nonce mechanism to prevent CSRF and bind callback to:
     - tenant
     - user
     - provider
   - State should be signed/protected and time-limited.
   - Callback must validate state before token exchange.

9. **Add application commands**
   - Implement commands/handlers such as:
     - `StartMailboxOAuthConnectionCommand`
     - `CompleteMailboxOAuthConnectionCommand`
     - `TriggerManualMailboxScanCommand`
   - `Start...` returns provider authorization URL.
   - `Complete...` exchanges code, fetches mailbox identity, stores encrypted tokens, and marks connection status.
   - `TriggerManualMailboxScan...` validates caller access to the tenant/user-owned connection and initiates a scan.

10. **Manual scan behavior**
    - The scan must only evaluate messages from the last 30 days.
    - It must only inspect configured folders/labels.
    - It must only evaluate messages against bill-related keywords:
      - `invoice`
      - `bill`
      - `faktura`
      - `payment due`
      - `amount due`
      - `OCR`
      - `IBAN`
      - `bankgiro`
      - `plusgiro`
    - Implement keyword matching case-insensitively.
    - Restrict evaluation to message fields that make sense, e.g. subject/snippet/body preview/attachment names if available with minimal cost.
    - Count scanned messages and detected candidates.
    - Do not create payment actions, tasks for approval, or approval proposals from this flow alone.

11. **Folder/label configuration**
    - Support storing configured folders/labels on the connection record or a related config object.
    - If no configuration exists yet, choose a conservative default based on provider conventions only if already implied by product behavior; otherwise require explicit configuration or use inbox-only with clear code comments.
    - Keep implementation minimal and acceptance-focused.

12. **Persist `EmailIngestionRun` for every manual scan**
    - On scan start, create a run record with `StartedAt`.
    - On completion, set `EndedAt`, `ScannedMessageCount`, `DetectedCandidateCount`.
    - On failure, persist `FailureDetails` and end timestamp.
    - Ensure failures still produce an audit record.

13. **API surface**
    - Add minimal authenticated tenant-scoped endpoints, for example:
      - `POST /api/mailbox-connections/{provider}/start`
      - `GET /api/mailbox-connections/{provider}/callback`
      - `POST /api/mailbox-connections/{id}/scan`
   - Match existing API routing conventions in the repo.
   - Authorization must ensure the acting user can only connect/scan within their tenant context and their own mailbox connection unless product conventions explicitly allow admins.

14. **Provider-specific implementation notes**
   - Gmail:
     - use Google OAuth authorization code flow
     - use Gmail API for message listing and attachment/message metadata retrieval
     - filter by labels where possible server-side
   - Microsoft 365:
     - use Microsoft identity platform authorization code flow
     - use Microsoft Graph mail endpoints
     - filter folders server-side where possible
   - Prefer server-side filtering by date and folder/label first, then keyword filtering in application code.

15. **Observability and audit**
   - Add structured logs with correlation ID, tenant, provider, connection ID, and ingestion run ID where available.
   - Keep technical logs separate from business audit records.
   - Avoid logging raw tokens or sensitive message content.

16. **Validation and error handling**
   - Handle:
     - invalid/expired OAuth state
     - denied consent
     - token exchange failure
     - revoked/expired refresh token
     - provider API throttling/transient failures
     - unauthorized access to another tenant/user connection
   - Return safe API responses.
   - Update connection status appropriately on failures.

17. **Tests**
   - Add tests for:
     - starting OAuth returns provider URL
     - callback persists encrypted tokens and connected status
     - mailbox connection is tenant- and user-scoped
     - manual scan uses 30-day window only
     - keyword filtering includes all required terms
     - ingestion run is persisted on success and failure
     - no payment action or approval proposal is created
   - Mock provider clients rather than calling real external APIs.

18. **Implementation constraints**
   - Keep code modular and aligned with clean architecture boundaries.
   - Do not let controllers call external providers directly.
   - Do not bypass application layer for tenant checks.
   - Do not introduce microservice-style complexity.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration generation/application follows repo conventions:
   - create/update EF migration for mailbox connection and ingestion run tables
   - ensure schema includes tenant keys, indexes, and encrypted token columns

4. Manually validate API flow with mocked/test configuration:
   - start Gmail connection and confirm auth URL contains only intended scopes
   - start Microsoft 365 connection and confirm auth URL contains only intended scopes
   - complete callback and verify:
     - `MailboxConnection` created/updated
     - tokens stored encrypted
     - status set correctly
   - trigger manual scan and verify:
     - only last 30 days queried
     - only configured folders/labels evaluated
     - keyword filter applied
     - `EmailIngestionRun` persisted with counts and timestamps

5. Negative-path validation:
   - invalid tenant/user access returns forbidden/not found
   - invalid OAuth state is rejected
   - provider failure still writes `EmailIngestionRun` failure details when scan had started
   - no payment or approval entities are created by connect or scan flows

6. Code quality validation:
   - no plaintext token logging
   - no direct external API calls from API layer
   - provider-specific logic isolated behind interfaces
   - acceptance criteria traceable in tests/comments

# Risks and follow-ups
- **Scope ambiguity on folder/label configuration UI**: acceptance requires configured folders/labels, but current task may not include full configuration UX. If missing, implement backend support and minimal defaults or minimal API contract, then flag richer UI as follow-up.
- **Refresh token behavior differs by provider**: exact offline access requirements may force specific scopes/parameters. Keep scopes minimal while still enabling a durable connection.
- **Provider API filtering limitations**: some keyword filtering may need to happen application-side after server-side date/folder filtering.
- **Token encryption key management**: if the repo lacks an established encryption pattern, use ASP.NET Core Data Protection now and flag production-grade key storage/rotation as follow-up.
- **Large mailbox scans**: current task is manual and 30-day bounded, but future work may need paging, throttling, retries, and background execution hardening.
- **No downstream bill workflow yet**: candidate detection should stop at audit/counting and must not create payment or approval artifacts; future stories can build candidate persistence and review UX on top of this.
- **Potential follow-up tasks**:
  - mailbox folder/label configuration UI
  - scheduled scans
  - candidate persistence/review queue
  - token refresh daemon/health checks
  - webhook/subscription support for incremental sync
  - richer audit/explainability views for ingestion results