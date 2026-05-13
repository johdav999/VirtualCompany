# Goal
Implement backlog task **TASK-33.1.1** for story **US-33.1 Implement Fortnox connection persistence, OAuth security, and token lifecycle management** by adding the initial **domain entities, EF Core configurations, DbSet registrations, and EF Core migration(s)** needed to persist:

- tenant-scoped Fortnox connections
- encrypted token storage metadata
- OAuth state records
- sync history
- external record references

This task is primarily a **persistence foundation task**. Build the schema and entity model so later tasks can implement OAuth start/callback, token encryption, refresh, disconnect, and audit behavior on top of it.

The implementation must align with the existing **modular monolith**, **shared-schema multi-tenancy**, and **PostgreSQL + EF Core** architecture.

# Scope
In scope:

- Add new persistence entities for Fortnox integration state.
- Ensure every tenant-owned table includes tenant/company scoping.
- Add EF Core entity type configurations with indexes, constraints, lengths, and relationships.
- Register the entities in the application DbContext.
- Create EF Core migration(s) that generate the required PostgreSQL tables.
- Include fields that support later acceptance criteria, especially:
  - single-use OAuth state with expiry and consumption tracking
  - encrypted token persistence metadata
  - connection lifecycle/status tracking
  - sync history/error summary storage
  - external reference mapping for synced records
- Preserve safe-by-design storage patterns:
  - token values stored only in encrypted form fields
  - no plaintext token columns
  - safe error summary fields only, not raw provider payload dumps

Out of scope unless required to make the migration compile cleanly:

- Implementing API endpoints
- Implementing OAuth redirect/callback logic
- Implementing encryption services
- Implementing token refresh logic
- Implementing disconnect command/handler
- Implementing audit event creation beyond any existing shared infrastructure
- Background jobs or sync execution logic
- UI changes

If you discover existing integration abstractions or naming conventions in the repo, follow them. Do not invent a parallel pattern if a project-standard approach already exists.

# Files to touch
Touch only the files needed for this task, likely under these projects:

- `src/VirtualCompany.Domain`
  - add Fortnox-related domain/entity classes if entities live in Domain
  - add enums/value objects if the codebase uses them for statuses/types

- `src/VirtualCompany.Infrastructure`
  - persistence entity configurations
  - DbContext updates
  - migration files
  - model snapshot

Potential file areas to inspect first before editing:

- existing DbContext class
- existing EF Core configuration classes
- existing entity base classes/interfaces for:
  - `Id`
  - `CompanyId` / `TenantId`
  - timestamps
  - soft delete
  - concurrency tokens
- existing migration naming conventions
- any existing integration module folders
- any existing audit event entity to understand FK patterns

Expected new files may include names similar to:

- `FortnoxConnection.cs`
- `FortnoxOAuthState.cs`
- `FortnoxSyncHistory.cs`
- `FortnoxExternalReference.cs`
- `FortnoxConnectionConfiguration.cs`
- `FortnoxOAuthStateConfiguration.cs`
- `FortnoxSyncHistoryConfiguration.cs`
- `FortnoxExternalReferenceConfiguration.cs`
- EF migration file(s)

If the repo already has a provider/integration namespace structure, place these under the matching Fortnox/integrations folder hierarchy instead of creating a new arbitrary location.

# Implementation plan
1. **Inspect the current persistence architecture**
   - Find the primary EF Core DbContext in Infrastructure.
   - Identify how tenant-owned entities are modeled today:
     - `CompanyId` vs `TenantId`
     - base entity inheritance
     - timestamp conventions
     - snake_case naming or explicit table naming
   - Check whether entities belong in Domain or Infrastructure.
   - Check whether enums are stored as strings or ints.
   - Check whether there is already an `Integration` or `Accounting` module namespace to extend.

2. **Design the Fortnox persistence model**
   Create a minimal but future-proof schema covering the acceptance criteria.

   Recommended entities and intent:

   - **FortnoxConnection**
     - one active connection per company/tenant, or support historical rows with uniqueness on active connection depending on existing patterns
     - stores provider/account identity and encrypted token payload fields
     - tracks token expiry and connection health/status

   - **FortnoxOAuthState**
     - stores generated OAuth state values
     - bound to company and user
     - has expiry timestamp
     - supports single-use semantics via consumed/redeemed timestamp and optional callback metadata
     - supports rejection of reused/expired/mismatched state later

   - **FortnoxSyncHistory**
     - stores sync attempts/runs for the tenant connection
     - includes sync type/direction/status/timestamps/counts
     - stores safe error summary only

   - **FortnoxExternalReference**
     - maps internal records to Fortnox external IDs
     - tenant-scoped
     - supports entity type + internal entity id + external id/reference
     - indexed for idempotent sync and lookup

3. **Recommended fields**
   Use repo conventions first. If no convention exists, prefer the following practical fields.

   **FortnoxConnection**
   - `Id`
   - `CompanyId`
   - `CreatedAt`
   - `UpdatedAt`
   - `ConnectedByUserId` or equivalent nullable FK if user entity relationships are already modeled
   - `Status`  
     Suggested values: `Active`, `NeedsAttention`, `Disconnected`, `TokenExpired`, `RefreshFailed`
   - `FortnoxTenantId` or provider-specific account/company identifier if available
   - `FortnoxCompanyName` nullable
   - `AccessTokenCiphertext`
   - `RefreshTokenCiphertext`
   - `TokenEncryptionKeyId` or `EncryptionKeyVersion`
   - `TokenEncryptionAlgorithm` nullable if your encryption design tracks it
   - `AccessTokenExpiresAt`
   - `RefreshTokenExpiresAt` nullable
   - `LastRefreshedAt` nullable
   - `LastValidatedAt` nullable
   - `LastSyncAt` nullable
   - `LastErrorSummary` nullable, bounded length
   - `DisconnectedAt` nullable

   Notes:
   - Do **not** add plaintext token columns.
   - If the codebase prefers a reusable encrypted-secret value object/owned type, use that instead of raw columns.
   - If only one Fortnox connection should exist per company, enforce uniqueness on `CompanyId` for active/current row strategy, or simply one row per company if history is not needed yet.

   **FortnoxOAuthState**
   - `Id`
   - `CompanyId`
   - `UserId`
   - `StateHash` or `StateValue`
   - `CreatedAt`
   - `ExpiresAt`
   - `ConsumedAt` nullable
   - `CallbackReceivedAt` nullable
   - `ConnectionId` nullable if useful later
   - `RedirectUri` nullable if needed for validation
   - `CodeVerifierCiphertext` nullable if PKCE is planned
   - `FailureReason` nullable safe summary only

   Strong recommendation:
   - Prefer storing a **hash** of the state rather than plaintext if feasible within current architecture.
   - If hashing is too large a deviation for this task, store the state in a bounded field and leave a TODO, but do not block the task if repo patterns are simple.

   **FortnoxSyncHistory**
   - `Id`
   - `CompanyId`
   - `FortnoxConnectionId`
   - `SyncType`
   - `Status`
   - `StartedAt`
   - `CompletedAt` nullable
   - `TriggeredBy` or `TriggeredByUserId` nullable
   - `RecordsProcessed` default 0
   - `RecordsSucceeded` default 0
   - `RecordsFailed` default 0
   - `CorrelationId` nullable
   - `ErrorSummary` nullable
   - `MetadataJson` nullable if the project already uses JSONB for flexible sync metadata

   **FortnoxExternalReference**
   - `Id`
   - `CompanyId`
   - `FortnoxConnectionId` nullable if mapping should survive reconnects; otherwise required
   - `EntityType`
   - `InternalEntityId`
   - `ExternalEntityType`
   - `ExternalId`
   - `ExternalDisplayReference` nullable
   - `LastSyncedAt` nullable
   - `CreatedAt`
   - `UpdatedAt`

4. **Add entity classes**
   - Follow existing entity style exactly.
   - Add constructors/factory methods only if that is already the project pattern.
   - Keep behavior minimal; this task is about persistence shape, not business workflows.
   - If the domain layer uses enums, add them there for:
     - connection status
     - sync status
     - sync type
   - If the project avoids enums in entities, use constrained strings with max lengths.

5. **Add EF Core configurations**
   For each entity:
   - map to explicit table names
   - configure primary keys
   - configure required fields
   - configure max lengths
   - configure timestamp column types consistent with PostgreSQL usage
   - configure JSONB columns if used
   - configure foreign keys if user/company/connection relationships exist
   - add indexes for tenant-safe and operational lookups

   Recommended indexes:
   - `FortnoxConnection`
     - unique/index on `CompanyId`
     - index on `Status`
     - index on `AccessTokenExpiresAt`
   - `FortnoxOAuthState`
     - unique index on `StateHash` or `StateValue`
     - index on `(CompanyId, UserId)`
     - index on `ExpiresAt`
     - index on `ConsumedAt`
   - `FortnoxSyncHistory`
     - index on `(CompanyId, FortnoxConnectionId, StartedAt desc)`
     - index on `Status`
   - `FortnoxExternalReference`
     - unique index on `(CompanyId, EntityType, InternalEntityId, ExternalEntityType)`
     - index on `(CompanyId, ExternalEntityType, ExternalId)`

6. **Register DbSets and model configuration**
   - Add DbSet properties to the DbContext.
   - Ensure `ApplyConfigurationsFromAssembly` or explicit registrations pick up the new configurations.
   - Verify naming conventions and schema behavior remain consistent.

7. **Create EF Core migration**
   - Generate a migration with a clear name, e.g.:
     - `AddFortnoxConnectionPersistence`
   - Ensure the migration creates all required tables, indexes, constraints, and FK relationships.
   - Review generated migration code manually; do not assume scaffolding is correct.
   - Confirm PostgreSQL-specific types are appropriate, especially for:
     - UUIDs
     - timestamptz
     - jsonb
     - text/varchar lengths

8. **Review for security and acceptance alignment**
   Before finishing, verify the schema supports these future behaviors:
   - OAuth state can be single-use and expire
   - callback can reject reused/expired state
   - tokens are only persisted in encrypted form
   - refresh failures can mark connection as needing attention
   - safe error summaries can be stored without leaking secrets
   - disconnect can clear active state or mark disconnected
   - audit can later reference the connection/disconnect action

9. **Keep implementation narrow**
   - Do not add controllers, handlers, services, or API contracts unless compilation requires a tiny supporting change.
   - Do not expose token fields in DTOs.
   - Do not add logging of token-bearing entities anywhere.

# Validation steps
Run and verify as much of the following as possible:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Migration sanity**
   - Ensure the solution compiles with the new migration and model snapshot.
   - If local EF commands are already used in the repo, generate/apply the migration using the repo’s normal startup project and context.
   - Review the migration for:
     - all expected tables created
     - tenant/company scoping columns present
     - no plaintext token columns
     - indexes and uniqueness constraints present
     - FK delete behavior is sensible and consistent with existing patterns

4. **Code review checklist**
   - All tenant-owned tables include `CompanyId` or the repo-standard tenant key.
   - OAuth state supports expiry and single-use tracking.
   - Connection entity supports encrypted token metadata and refresh lifecycle timestamps.
   - Sync history stores only safe summaries, not raw secrets.
   - External references support idempotent lookup and mapping.
   - No API response models or logs were introduced that could expose tokens.

5. **If there are existing architecture tests or migration tests**
   - Run them too and fix any naming/configuration issues.

# Risks and follow-ups
- **Risk: naming mismatch with existing tenant conventions**
  - The architecture text uses both tenant/company language. Use the repo’s actual convention consistently, likely `CompanyId`.

- **Risk: entity placement**
  - Some repos keep EF entities in Infrastructure, others in Domain. Follow the existing pattern exactly to avoid architectural drift.

- **Risk: token storage design may already have a shared secret/encryption abstraction**
  - Reuse it if present instead of inventing Fortnox-specific ciphertext fields.

- **Risk: OAuth state storage security**
  - If feasible, store a hash of the state rather than plaintext. If not implemented now, note it as a follow-up.

- **Risk: uniqueness strategy for connections**
  - Decide whether there is exactly one row per company or one current row plus historical rows. Prefer the simplest approach that matches acceptance criteria and existing repo patterns.

Follow-up tasks likely needed after this one:
- implement OAuth start endpoint and state creation
- implement OAuth callback validation and token persistence
- implement encryption service for token values
- implement automatic token refresh flow
- implement disconnect command and audit event
- implement sync services using `FortnoxSyncHistory`
- implement DTO redaction and logging safeguards for secret-bearing entities