# Goal
Implement backlog task **TASK-29.2.1** for story **US-29.2 Unified financial check framework and insight persistence** by introducing shared financial check contracts and a normalized result model that all finance checks can use consistently.

The implementation must:
- Define a shared `IFinancialCheck` contract.
- Define a normalized `FinancialCheckResult` model including:
  - severity
  - recommendation
  - confidence
  - affected entities
- Support persistence into `FinanceAgentInsight` records with the fields required by acceptance criteria.
- Enable resolution of previously active insights when a condition is no longer true.
- Establish a single API response shape suitable for dashboard and entity pages without check-specific branching.

Keep the design aligned with the existing modular monolith, CQRS-lite application layer, tenant-scoped persistence, and .NET solution structure.

# Scope
In scope:
- Add domain/application contracts for shared financial checks.
- Add normalized result DTO/model(s) and supporting enums/value objects.
- Add or update `FinanceAgentInsight` domain/persistence model to support:
  - severity
  - message
  - recommendation
  - entity reference
  - status
  - createdAt
  - updatedAt
  - confidence
  - affected entities if not already represented
- Add a persistence/upsert-resolution strategy so checks can:
  - create a new active insight when a condition is detected
  - update an existing active insight when the same condition persists
  - mark an existing active insight as resolved when the condition is no longer true
- Add a normalized API response contract for finance insights.
- Update existing cash risk, transaction anomaly, receivables, and payables checks to implement the shared contract.

Out of scope unless required to satisfy compilation:
- Full dashboard UI rendering changes.
- New financial check business logic beyond adapting existing checks to the shared contract.
- Broad refactors outside finance insight/check flow.
- Mobile-specific work.

# Files to touch
Inspect the repo first and adjust paths to actual project structure, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/`
  - finance/check contracts and enums
  - finance insight entity/value objects
- `src/VirtualCompany.Application/`
  - finance check abstractions
  - commands/services for persisting normalized check results
  - query/response models for unified insight API shape
- `src/VirtualCompany.Infrastructure/`
  - EF Core entity configuration
  - repositories
  - migrations if schema changes are needed
- `src/VirtualCompany.Api/`
  - finance insight endpoints or controllers
  - response mapping
- `tests/`
  - unit tests for contract behavior and resolution logic
  - integration tests for persistence and API shape

Likely new files:
- `IFinancialCheck.cs`
- `FinancialCheckResult.cs`
- `FinancialCheckSeverity.cs`
- `FinancialInsightStatus.cs`
- `FinancialCheckAffectedEntity.cs`
- `FinanceAgentInsight` updates or configuration files
- persistence service such as `FinanceInsightPersistenceService.cs`
- unified API response model such as `FinanceInsightDto.cs`

Also inspect for any existing equivalents before creating new types. Reuse and extend existing finance insight models if present rather than duplicating concepts.

# Implementation plan
1. **Discover existing finance models and check implementations**
   - Search for:
     - `FinanceAgentInsight`
     - cash risk checks
     - transaction anomaly checks
     - receivables checks
     - payables checks
     - any existing insight status/severity enums
   - Identify current boundaries:
     - where checks run
     - how results are returned
     - how insights are persisted
     - how dashboard/entity pages currently consume insight data
   - Prefer extending existing patterns over introducing parallel abstractions.

2. **Define the shared financial check contract**
   - Add a shared interface, likely in Domain or Application depending on current architecture conventions.
   - The contract should be generic enough for all finance checks and tenant-safe.
   - Recommended shape:
     - stable check identifier/code
     - display name/title if useful
     - execution method returning normalized results
   - Example intent, not exact code:
     - `Task<IReadOnlyList<FinancialCheckResult>> ExecuteAsync(FinancialCheckContext context, CancellationToken cancellationToken)`
   - If a context object does not exist, add one to carry company/tenant scope and any required execution inputs.

3. **Define normalized result contracts**
   - Create `FinancialCheckResult` with fields sufficient for persistence and API projection.
   - Include at minimum:
     - `CheckCode`
     - `Severity`
     - `Message`
     - `Recommendation`
     - `Confidence`
     - `AffectedEntities`
     - optional primary entity reference
     - optional metadata for future extensibility
     - a deterministic deduplication/resolution key if needed
   - Add supporting types:
     - `FinancialCheckSeverity` enum
     - `FinancialCheckAffectedEntity`
     - entity reference type if one does not already exist
     - optional `FinancialCheckResultStatus` only if needed before persistence
   - Confidence should be normalized consistently, e.g. decimal in range `0..1` or `0..100`; choose one and document it in code comments/tests.

4. **Define insight identity and deduplication strategy**
   - To support “resolve instead of create new record,” introduce a stable way to identify the same logical condition across runs.
   - Preferred approach:
     - derive a deterministic insight key from:
       - company/tenant
       - check code
       - primary entity reference or affected entity set
       - condition discriminator if needed
   - Persist this key on `FinanceAgentInsight` if the entity does not already have an equivalent natural key.
   - Ensure the same ongoing condition updates the same active record rather than creating duplicates.

5. **Update `FinanceAgentInsight` to support normalized persistence**
   - Ensure the entity includes or can map:
     - severity
     - message
     - recommendation
     - entity reference
     - status
     - createdAt
     - updatedAt
     - confidence
     - check code / insight key
   - Add status enum values such as:
     - `Active`
     - `Resolved`
   - If affected entities are plural, decide whether to:
     - store a primary entity reference plus JSON metadata for all affected entities, or
     - normalize into a child table if the repo already uses relational child collections
   - Prefer the least invasive option that fits current architecture and acceptance criteria.

6. **Implement persistence/upsert/resolve workflow**
   - Add an application service responsible for reconciling current check results against persisted active insights.
   - Expected behavior per check run:
     - For each current result:
       - find existing active insight by deterministic key
       - if found, update message/recommendation/severity/confidence/entity refs and `updatedAt`
       - if not found, create a new active insight with `createdAt` and `updatedAt`
     - For previously active insights for the same check scope not present in current results:
       - mark them `Resolved`
       - update `updatedAt`
       - do not create a new record
   - Keep this logic idempotent and tenant-scoped.
   - Avoid deleting historical insights.

7. **Adapt all finance checks to the shared contract**
   - Update cash risk, transaction anomaly, receivables, and payables checks so they all implement `IFinancialCheck`.
   - Remove check-specific result shapes where possible.
   - Map each check’s current output into `FinancialCheckResult`.
   - Preserve existing business rules; only normalize the contract and persistence behavior.

8. **Add unified API response shape**
   - Create a single DTO for finance insights used by dashboard and entity pages.
   - Include fields needed by acceptance criteria and rendering:
     - id
     - checkCode
     - severity
     - message
     - recommendation
     - status
     - confidence
     - primary entity reference
     - affected entities
     - createdAt
     - updatedAt
   - Update API mapping so consumers receive one normalized shape regardless of originating check type.
   - Do not expose check-specific payloads that force branching in clients.

9. **Add/update persistence configuration and migration**
   - If schema changes are required, update EF Core configuration and add a migration.
   - Ensure indexes support:
     - tenant/company filtering
     - active insight lookup by deterministic key
     - dashboard/entity page queries
   - If using JSON columns for affected entities, configure serialization consistently.

10. **Add tests**
   - Unit tests:
     - each check implements `IFinancialCheck`
     - normalized result mapping includes severity/recommendation/confidence/affected entities
     - persistence service creates active insight on first detection
     - persistence service updates existing active insight on repeated detection
     - persistence service marks missing previously active insight as resolved
   - Integration tests:
     - API returns a single normalized response shape
     - tenant scoping is preserved
     - resolved insights are not duplicated as new active records when condition clears

11. **Keep code quality and architecture consistency**
   - Follow existing naming, namespace, and layering conventions.
   - Keep domain logic out of controllers.
   - Keep persistence concerns in Infrastructure/Application services.
   - Use UTC timestamps.
   - Ensure cancellation tokens flow through async methods.

# Validation steps
1. Inspect and build after implementation:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify contract adoption:
   - Confirm cash risk, transaction anomaly, receivables, and payables checks all implement `IFinancialCheck`.

4. Verify persistence behavior manually or via tests:
   - Run a check that produces a condition:
     - confirm one active `FinanceAgentInsight` is created
   - Run again with same condition:
     - confirm same record is updated, not duplicated
   - Run with condition cleared:
     - confirm existing record is marked `Resolved`
     - confirm no new record is created

5. Verify API normalization:
   - Call the finance insights endpoint(s)
   - Confirm dashboard and entity-oriented responses use the same DTO shape and do not require check-specific branching.

6. If a migration was added:
   - Apply migration locally
   - Confirm schema supports new fields/indexes and existing data remains compatible.

# Risks and follow-ups
- **Existing model mismatch risk:** The repo may already contain partial finance insight abstractions. Reconcile carefully to avoid duplicate contracts.
- **Deduplication ambiguity:** If the same check can emit multiple conditions for the same entity, the deterministic insight key must include a condition discriminator to avoid collisions.
- **Affected entities storage choice:** JSON is faster to introduce, but a relational child table may be better if querying affected entities individually is a near-term requirement.
- **API compatibility risk:** If current clients depend on check-specific payloads, preserve backward compatibility only if already required elsewhere; otherwise normalize cleanly.
- **Migration risk:** Adding non-null columns to existing insight tables may require defaults/backfill.
- **Follow-up recommended:** After this task, consider a dedicated query service for finance insights by:
  - company dashboard
  - entity detail
  - active vs resolved history
  - severity/status filters
- **Follow-up recommended:** Add audit events around insight creation, update, and resolution if not already covered by the audit model.