# Goal
Implement **TASK-4.4.2 — Apply preference-based filtering during briefing assembly** for story **US-4.4 Support personalized briefing preferences and delivery controls**.

Deliver an end-to-end .NET implementation so that:
- authenticated users can save briefing preferences via API,
- briefing generation applies user preferences during assembly,
- tenant defaults are used when user preferences do not exist,
- the system records which defaults were applied,
- preference updates affect the next generated briefing immediately,
- invalid preference values are rejected with descriptive 4xx responses.

Keep the implementation aligned with the existing modular monolith architecture, tenant scoping, CQRS-lite patterns, and background briefing generation flow.

# Scope
In scope:
- Add or complete domain/application/infrastructure support for **briefing preferences**.
- Support preference fields:
  - delivery frequency
  - included focus areas
  - priority threshold
- Add authenticated API endpoints to create/update and fetch current user briefing preferences.
- Apply preference-based filtering in the briefing assembly/generation pipeline.
- Fall back to tenant-level defaults when user preferences are absent.
- Persist metadata indicating whether user preferences or tenant defaults were used for a generated briefing.
- Ensure no restart/manual cache invalidation is required for preference changes to take effect.
- Add validation for unsupported focus areas and invalid delivery frequencies with descriptive error responses.
- Add/update tests covering API validation, fallback behavior, and briefing filtering behavior.

Out of scope unless required by existing code structure:
- New UI work in Blazor or MAUI.
- Email delivery implementation.
- Large refactors unrelated to briefing preference persistence or assembly.
- Introducing new infrastructure beyond what the solution already uses.

# Files to touch
Inspect the solution first and then touch the minimum necessary files across these likely areas:

- `src/VirtualCompany.Domain/**`
  - briefing preference entities/value objects/enums
  - briefing generation metadata models
  - validation constants for supported focus areas/frequencies/priority thresholds

- `src/VirtualCompany.Application/**`
  - commands/queries for get/save briefing preferences
  - DTOs/contracts for API payloads and responses
  - briefing assembly service/use case updates to resolve effective preferences
  - fallback/default resolution logic
  - error/result models for descriptive validation codes

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories for user preferences and tenant defaults
  - migrations or persistence mappings
  - any cache bypass/refresh logic if currently present in briefing generation

- `src/VirtualCompany.Api/**`
  - authenticated controller/endpoints for preferences
  - request/response models if API-specific
  - exception/error mapping for 4xx validation responses

- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests for auth, validation, and persistence behavior

- `tests/**` in other test projects if present
  - application/service tests for briefing filtering and fallback/default recording

Also inspect:
- `README.md`
- existing briefing-related modules/services/entities
- any current notification/message models used to store generated briefings
- any migration guidance under `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Discover existing briefing and preference implementation**
   - Search for:
     - briefing generation/assembly services
     - scheduled daily/weekly summary jobs
     - notification/message persistence for briefings
     - tenant/company settings/defaults
     - existing preference models/endpoints
   - Reuse existing patterns for:
     - tenant resolution
     - authenticated user context
     - command/query handlers
     - validation and API error responses

2. **Model briefing preferences explicitly**
   - Add or complete a tenant-scoped/user-scoped persistence model for briefing preferences.
   - Ensure the model supports:
     - `UserId`
     - `CompanyId`/tenant id
     - `DeliveryFrequency`
     - `IncludedFocusAreas`
     - `PriorityThreshold`
     - timestamps
   - If tenant defaults are not already modeled, add a tenant-level configuration source in the most idiomatic existing place, likely company settings/config JSON or a dedicated settings entity.
   - Define canonical supported values in one place:
     - allowed delivery frequencies
     - allowed focus areas
     - allowed priority thresholds or threshold range

3. **Add authenticated preference API endpoints**
   - Implement endpoints to:
     - `GET` current effective or saved user briefing preferences
     - `PUT` or `POST` save/update user briefing preferences
   - Scope all operations by authenticated user and current tenant.
   - Return descriptive validation failures with stable error codes for:
     - unsupported focus areas
     - invalid delivery frequencies
     - invalid threshold values
   - Prefer existing API conventions for problem details or error envelopes.

4. **Implement validation**
   - Validate request payloads in the application layer, not only controller layer.
   - Reject unsupported focus areas and invalid delivery frequencies with 4xx responses.
   - Include machine-readable error codes, e.g. similar to:
     - `briefing_preferences.invalid_delivery_frequency`
     - `briefing_preferences.unsupported_focus_area`
     - `briefing_preferences.invalid_priority_threshold`
   - If multiple invalid focus areas are supplied, return enough detail for the client to correct the request.

5. **Resolve effective preferences during briefing assembly**
   - Update the briefing generation pipeline so each run resolves preferences in this order:
     1. user-specific saved preferences
     2. tenant-level defaults
   - Do not require restart or manual cache invalidation:
     - read fresh values for each generation, or
     - if caching exists, ensure writes invalidate/refresh the relevant cache key automatically
   - Keep this logic in an application/domain service, not in controllers or background job glue.

6. **Apply filtering during briefing assembly**
   - Update briefing assembly so generated briefings:
     - exclude sections/items outside included focus areas
     - exclude sections/items below the configured priority threshold
   - Apply filtering before final persistence of the briefing message/notification payload.
   - Preserve deterministic behavior and tenant/user scoping.
   - If the current briefing builder aggregates alerts, approvals, KPI highlights, anomalies, and agent updates, ensure each section is either:
     - mapped to a focus area and filtered accordingly, and/or
     - filtered at item level where priority metadata exists

7. **Record preference source and defaults used**
   - Persist metadata with each generated briefing indicating:
     - whether user preferences were used or tenant defaults were used
     - which default values were applied when no user preference existed
   - Store this in the existing briefing/message/notification payload or a dedicated metadata field, whichever best matches current architecture.
   - Make the metadata auditable and queryable enough for future explainability.

8. **Ensure next briefing reflects latest preferences**
   - Review current briefing scheduling/generation path for any in-memory caching.
   - Remove stale preference reads.
   - If Redis or in-process caching is used, implement targeted invalidation on preference save.
   - Add tests proving that after updating preferences, the next generation uses the new values without restart.

9. **Persistence and migration**
   - Add EF configuration and migration(s) if new tables/columns are required.
   - Keep naming and tenant foreign keys consistent with the rest of the schema.
   - If using JSONB for flexible settings/defaults, ensure serialization is stable and tested.

10. **Testing**
   - Add/extend tests for:
     - authenticated save/get preference endpoints
     - tenant/user isolation
     - validation failures and error codes
     - fallback to tenant defaults when no user preference exists
     - recording which defaults were used
     - filtering out excluded focus areas
     - filtering out items/sections below threshold
     - preference update affecting next generated briefing immediately

11. **Keep implementation minimal and idiomatic**
   - Avoid broad refactors.
   - Reuse existing abstractions for messages, notifications, scheduled jobs, and settings.
   - If acceptance criteria conflict with current design, implement the smallest coherent extension and document any assumptions in code comments/tests.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify API behavior manually or via integration tests:
   - authenticated user can save preferences
   - authenticated user can fetch preferences
   - invalid delivery frequency returns 4xx with descriptive error code
   - unsupported focus area returns 4xx with descriptive error code

4. Verify briefing generation behavior:
   - create/save preferences for a user
   - trigger or invoke briefing generation for that user
   - confirm excluded focus areas are absent
   - confirm items/sections below threshold are absent

5. Verify fallback behavior:
   - remove user preference
   - configure tenant defaults
   - generate briefing
   - confirm defaults are applied and metadata records which defaults were used

6. Verify immediate effect of updates:
   - generate briefing with initial preferences
   - update preferences
   - generate next briefing
   - confirm new preferences are used without restart or manual cache clear

7. Verify tenant isolation:
   - ensure one tenant’s defaults/preferences do not affect another tenant
   - ensure one user cannot read/write another user’s preferences unless existing authorization rules explicitly allow it

# Risks and follow-ups
- **Unknown existing briefing model:** briefing data may currently be stored as messages, notifications, or another aggregate; adapt without duplicating concepts.
- **Priority threshold ambiguity:** if current briefing items use mixed priority representations, normalize mapping carefully and document assumptions.
- **Focus area taxonomy drift:** if focus areas are already defined elsewhere, reuse that source of truth instead of introducing a competing enum/list.
- **Tenant defaults location:** if company settings already hold notification/briefing defaults, extend that structure rather than creating parallel configuration.
- **Caching pitfalls:** if briefing generation uses cached aggregates or cached preference snapshots, ensure only preference resolution is refreshed without harming performance.
- **Migration impact:** adding new tables/columns may require seed/default handling for existing tenants.
- **Follow-up suggestion:** after implementation, consider exposing effective preference source in dashboard/mobile briefing views for transparency.