# Goal
Implement backlog task **TASK-30.1.1 — Add MailboxConnection and EmailIngestionRun persistence models with tenant-scoped encryption support** for story **US-30.1 Implement tenant-scoped mailbox connection and manual bill scan initiation**.

The coding agent should add the persistence-layer foundation only, centered on:
- a tenant-scoped `MailboxConnection` model for Gmail and Microsoft 365 OAuth mailbox connections
- a tenant-scoped `EmailIngestionRun` audit model for manual inbox scan runs
- encrypted storage support for provider tokens/secrets using tenant-scoped encryption context
- EF Core configuration and database migration(s)
- minimal domain/application/infrastructure plumbing needed so later tasks can build OAuth connect flows and manual scan execution on top

Do **not** implement full OAuth UI, provider API calls, inbox scanning logic, payment creation, approval proposal creation, or background worker orchestration unless strictly required to support persistence contracts.

# Scope
Implement only what is necessary to satisfy the persistence and encryption foundation implied by the acceptance criteria.

Include:
- New domain entities/value objects/enums for:
  - `MailboxConnection`
  - `EmailIngestionRun`
- Tenant ownership via `CompanyId`/tenant ID conventions already used in the solution
- User ownership on `MailboxConnection`
- Provider support for:
  - Gmail
  - Microsoft 365
- Connection status support
- Encrypted persistence for OAuth token fields and any other sensitive credential material
- Folder/label configuration persistence needed for later “scan only configured folders/labels” behavior
- Audit fields on `EmailIngestionRun`:
  - start time
  - end time
  - provider
  - scanned message count
  - detected candidate count
  - failure details
- EF Core mappings
- DbContext registration updates
- PostgreSQL migration
- Repository or persistence access abstractions only if the current architecture requires them for consistency
- Unit/integration tests around:
  - model mapping
  - tenant scoping
  - encryption conversion/round-trip
  - migration sanity if there is an existing migration test pattern

Explicitly exclude:
- actual OAuth authorization code flow
- token refresh logic against providers
- mailbox API clients
- manual scan command handler behavior beyond persistence contracts
- bill detection logic
- creation of payments, approvals, or approval proposals
- scheduled jobs or inbox processors

# Files to touch
Inspect the solution structure first and adapt to existing conventions. Likely files/folders include:

- `src/VirtualCompany.Domain/...`
  - add domain entities/enums for mailbox connections and ingestion runs
- `src/VirtualCompany.Application/...`
  - add contracts/DTOs only if needed for persistence-facing use cases
- `src/VirtualCompany.Infrastructure/...`
  - EF Core entity configurations
  - encryption service/value converter support
  - DbContext updates
  - migration files
- `src/VirtualCompany.Api/...`
  - only if DI registration or startup wiring is needed for encryption services / DbContext
- `tests/...`
  - persistence mapping tests
  - encryption round-trip tests
  - tenant isolation tests where applicable

Also inspect:
- existing entity base classes/interfaces for tenant-owned entities
- existing audit base types
- existing encryption abstractions or secure settings patterns
- existing EF Core migration organization
- existing enum/value object conventions
- existing repository/query patterns

# Implementation plan
1. **Discover existing architecture conventions before coding**
   - Find:
     - the main EF Core `DbContext`
     - how tenant-owned entities are modeled
     - whether `CompanyId` or `TenantId` is the canonical field name
     - how `User` references are modeled
     - whether there is already an encryption abstraction for sensitive fields
     - how migrations are created and named
   - Reuse existing patterns instead of inventing new ones.

2. **Design the persistence model**
   - Add `MailboxConnection` as a tenant- and user-owned entity.
   - Recommended fields:
     - `Id`
     - `CompanyId`
     - `UserId`
     - `Provider` enum/string (`Gmail`, `Microsoft365`)
     - `Status` enum/string (for example `Pending`, `Active`, `TokenExpired`, `Revoked`, `Failed`, `Disconnected`)
     - `EmailAddress` or mailbox identifier if appropriate
     - `DisplayName` optional
     - encrypted access token
     - encrypted refresh token
     - token expiry timestamp
     - granted scopes
     - configured folders/labels payload
     - last successful sync/scan timestamp optional
     - last error summary optional
     - created/updated timestamps
   - Add `EmailIngestionRun` as an audit entity.
   - Recommended fields:
     - `Id`
     - `CompanyId`
     - `MailboxConnectionId`
     - `TriggeredByUserId` if aligned with current conventions
     - `Provider`
     - `StartedAt`
     - `CompletedAt`/`EndedAt`
     - `ScannedMessageCount`
     - `DetectedCandidateCount`
     - `FailureDetails`
     - optional scan window fields if useful, such as `ScanFromUtc` and `ScanToUtc`, to support the “last 30 days” audit trail
     - created timestamp if separate from started time
   - Ensure the model does not imply any payment or approval side effects.

3. **Model provider-specific minimal scope persistence**
   - Persist granted scopes in a provider-agnostic way, likely as text/JSON.
   - Do not hardcode provider API behavior here, but make the model capable of storing the minimal read scopes required for message and attachment retrieval.
   - If the codebase uses enums plus JSON/text collections, follow that pattern.

4. **Add configured folders/labels persistence**
   - Support storing configured folders/labels per mailbox connection.
   - Use JSONB if the project already uses JSONB for flexible config fields.
   - Keep it generic enough to represent:
     - Gmail labels
     - Microsoft folders
   - Suggested shape:
     - list of provider-native identifiers and display names
     - optional include/exclude semantics if needed by current conventions
   - This is needed because acceptance criteria require scans to evaluate only configured folders/labels.

5. **Implement tenant-scoped encryption support**
   - Reuse an existing encryption abstraction if present.
   - If none exists, add a small infrastructure abstraction for encrypting/decrypting sensitive string fields with tenant-scoped context, such as:
     - `IFieldEncryptionService`
     - methods accepting plaintext plus tenant/company context
   - Ensure encryption is applied to sensitive token fields before persistence and decrypted on materialization, using the project’s preferred pattern:
     - EF Core value converter/interceptor
     - repository-layer encryption
     - owned type with explicit conversion
   - Prefer deterministic architecture choices already present in the codebase.
   - Do not store OAuth tokens in plaintext.
   - If true tenant-derived keys are not yet available, implement a context-aware encryption envelope that at minimum binds encryption operations to tenant/company ID and is easy to upgrade later.

6. **Add EF Core configuration**
   - Register both entities in the DbContext.
   - Configure:
     - table names
     - primary keys
     - foreign keys
     - required fields
     - max lengths where appropriate
     - indexes, especially:
       - `CompanyId`
       - `UserId`
       - `MailboxConnectionId`
       - uniqueness constraints as appropriate, e.g. one active connection per provider/mailbox per user/tenant if that fits current requirements
     - JSONB columns for flexible config if used
     - encrypted columns for token fields
   - Ensure delete behavior is explicit and safe.

7. **Create migration**
   - Add a PostgreSQL migration creating the new tables and indexes.
   - Use the project’s migration naming conventions.
   - Ensure column types align with PostgreSQL best practices:
     - `uuid`
     - `timestamptz`
     - `jsonb`
     - `text`
   - If encrypted payloads are stored as text, document that in code comments where helpful.

8. **Add minimal persistence-facing APIs/contracts only if needed**
   - If the architecture requires repositories or application interfaces for new aggregates, add minimal ones.
   - Keep them narrowly focused on persistence operations.
   - Do not add speculative service layers unless the solution already mandates them.

9. **Add tests**
   - Add tests for:
     - entity persistence round-trip
     - encrypted token fields are not stored as plaintext
     - tenant/company scoping fields are required
     - configured folders/labels persistence round-trip
     - `EmailIngestionRun` audit field persistence
   - If there is an integration test setup with PostgreSQL or EF Core test containers, use it.
   - Otherwise follow the existing test strategy in the repo.

10. **Document assumptions in code comments or PR-style notes**
   - Note that:
     - actual OAuth flows are out of scope
     - actual scan execution is out of scope
     - no payment or approval artifacts are created by these models alone
     - scan-window enforcement and keyword filtering will be implemented in later tasks, but the audit model should support them

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration compiles and is included in the infrastructure project.

4. Validate schema expectations manually from the generated migration:
   - `MailboxConnection` table exists
   - `EmailIngestionRun` table exists
   - tenant/company foreign key columns exist
   - user foreign key exists on `MailboxConnection`
   - provider/status fields exist
   - encrypted token columns exist
   - configured folders/labels column exists
   - audit count/failure fields exist
   - indexes for tenant/company lookups exist

5. Validate encryption behavior in tests or local inspection:
   - persisted token column values are not equal to plaintext inputs
   - decrypted values round-trip correctly through the domain/persistence layer
   - encryption path uses tenant/company context

6. Validate no unintended business side effects:
   - no payment entities are created
   - no approval/proposal entities are created
   - no scan execution logic is triggered merely by creating a `MailboxConnection` or `EmailIngestionRun`

# Risks and follow-ups
- **Risk: no existing encryption abstraction**
  - If absent, implement the smallest viable infrastructure abstraction without over-designing key management.
  - Follow-up task should harden key rotation and secret management strategy.

- **Risk: unclear tenant naming convention**
  - The architecture text uses tenant/company language interchangeably.
  - Follow the actual codebase convention exactly, likely `CompanyId`.

- **Risk: provider-specific folder/label modeling**
  - Gmail labels and Microsoft folders differ.
  - Use a generic JSONB representation now; refine later if provider adapters need stronger typing.

- **Risk: uniqueness rules may be product-sensitive**
  - Avoid over-constraining if requirements do not explicitly define whether multiple connections per provider/user/mailbox are allowed.
  - Prefer indexes over aggressive unique constraints unless existing patterns indicate otherwise.

- **Risk: acceptance criteria mention manual scan semantics**
  - This task should only prepare persistence for later implementation.
  - If useful, include optional audit fields for scan window start/end to support the future “last 30 days only” rule.

- **Follow-up tasks likely needed**
  - OAuth connect/disconnect flows for Gmail and Microsoft 365
  - token refresh handling
  - manual “Scan inbox for bills” command and worker
  - provider adapters for message/attachment retrieval
  - keyword and folder/label filtering logic
  - audit/event surfacing in UI
  - policy checks ensuring no payment or approval artifacts are created from connection/scan initiation alone