# Goal
Implement `TASK-4.4.1` for **US-4.4 Support personalized briefing preferences and delivery controls** by adding persistence, domain/application logic, and authenticated APIs for **user briefing preferences** and **tenant-level defaults**, and by wiring briefing generation to resolve and apply effective preferences at runtime.

The implementation must ensure:

- Authenticated users can create, update, and fetch their briefing preferences.
- Tenant admins or equivalent authorized actors can manage tenant default briefing preferences.
- Briefing generation uses **user preferences when present**, otherwise **tenant defaults**.
- Generated briefings filter out:
  - excluded/unsupported focus areas
  - sections below the configured priority threshold
- When tenant defaults are used because no user preference exists, the system records **which defaults were applied** for traceability/auditability.
- Preference changes take effect on the **next generated briefing** without restart or manual cache invalidation.
- API validation returns **4xx** with descriptive error codes for unsupported focus areas and invalid delivery frequencies.

Follow existing solution conventions and architecture boundaries:
- `Domain` for entities/value objects/enums/rules
- `Application` for commands/queries/DTOs/handlers/interfaces
- `Infrastructure` for EF Core/PostgreSQL persistence and repository implementations
- `Api` for authenticated endpoints and request/response contracts
- `Tests` for API/application coverage

# Scope
In scope:

1. **Persistence**
   - Add tenant-scoped persistence for:
     - user briefing preferences
     - tenant default briefing preferences
     - optional audit/metadata fields indicating whether a generated briefing used user preferences or tenant defaults
   - Add EF Core configuration and migration(s).

2. **Domain model**
   - Introduce strongly modeled briefing preference concepts:
     - delivery frequency
     - included focus areas
     - priority threshold
   - Add validation rules and supported value definitions in one central place.

3. **Application layer**
   - Commands/queries for:
     - get user briefing preferences
     - upsert user briefing preferences
     - get tenant default briefing preferences
     - upsert tenant default briefing preferences
   - Service to resolve **effective briefing preferences** for a user within a tenant.
   - Ensure briefing generation path consumes resolved preferences dynamically from persistence.

4. **API**
   - Authenticated endpoints for user preference CRUD/upsert.
   - Authorized tenant-default endpoints.
   - Validation and descriptive error responses.

5. **Briefing generation integration**
   - Update the briefing generation flow so the next generated briefing reflects latest saved preferences.
   - Record whether user-specific or tenant-default preferences were used, and which defaults were applied when fallback occurred.

6. **Tests**
   - API tests for auth, validation, and success cases.
   - Application/domain tests for fallback resolution and filtering behavior.

Out of scope unless required by existing code patterns:
- New UI pages in Blazor or MAUI
- Email/SMS delivery implementation
- Broad notification redesign
- Large refactors unrelated to briefing preference persistence/API support

# Files to touch
Inspect the existing structure first, then update the appropriate files. Expected areas include:

- `src/VirtualCompany.Domain/**`
  - Add domain entities/value objects/enums for briefing preferences and supported values.
- `src/VirtualCompany.Application/**`
  - Add commands, queries, DTOs, validators, handlers, and preference resolution service/contracts.
- `src/VirtualCompany.Infrastructure/**`
  - Add EF entities/configurations/repositories and migration support.
  - Update DbContext and model registration.
- `src/VirtualCompany.Api/**`
  - Add authenticated endpoints/controllers/minimal API mappings.
  - Add request/response models and error mapping if needed.
- `tests/VirtualCompany.Api.Tests/**`
  - Add endpoint tests for happy path, auth failures, authorization failures, and validation failures.

Also inspect for existing briefing-related code and update it where appropriate, likely in one or more of:
- briefing generation services
- scheduled job handlers
- communication module services
- message/notification persistence models
- audit/event recording paths

Before coding, identify the actual existing files/classes for:
- DbContext
- current briefing generation pipeline
- auth/tenant resolution
- API endpoint style
- validation/error response conventions
- migrations approach in this repo

# Implementation plan
1. **Discover existing implementation patterns**
   - Search for:
     - briefing/summary generation code
     - tenant-scoped entities and repositories
     - command/query handler conventions
     - validation/error code conventions
     - authenticated API endpoint patterns
   - Reuse existing abstractions rather than inventing new ones.

2. **Design the data model**
   Add persistence for:
   - `UserBriefingPreference`
     - id
     - company/tenant id
     - user id
     - delivery frequency
     - included focus areas
     - priority threshold
     - created/updated timestamps
   - `TenantBriefingDefault`
     - id
     - company/tenant id
     - delivery frequency
     - included focus areas
     - priority threshold
     - created/updated timestamps
   - If there is already a generated briefing/message entity, extend it with metadata fields such as:
     - effective preference source (`user` or `tenant_default`)
     - applied default snapshot / applied default fields when fallback occurs
   Prefer normalized or JSON-backed storage based on existing repo conventions, but keep validation server-side and deterministic.

3. **Model supported values centrally**
   Introduce central definitions for:
   - supported delivery frequencies
   - supported focus areas
   - supported priority thresholds or threshold enum/range
   Avoid scattering string literals across API, application, and generation code.

4. **Implement domain validation**
   Ensure invalid values are rejected before persistence:
   - unsupported focus areas
   - invalid delivery frequencies
   - invalid/unknown threshold values
   Validation should produce machine-readable error codes that API can map to 4xx responses.

5. **Add application commands/queries**
   Implement:
   - `GetUserBriefingPreferences`
   - `UpsertUserBriefingPreferences`
   - `GetTenantBriefingDefaults`
   - `UpsertTenantBriefingDefaults`
   - `ResolveEffectiveBriefingPreferences`
   Return DTOs that clearly indicate:
   - effective values
   - source of values
   - whether fallback to tenant defaults occurred

6. **Implement authorization**
   - User preference endpoints: authenticated user can manage only their own preferences in current tenant context.
   - Tenant default endpoints: require appropriate tenant admin/owner authorization based on existing policy model.
   Reuse existing membership/role authorization patterns.

7. **Integrate with briefing generation**
   Update the briefing generation pipeline so that on each generation it:
   - resolves effective preferences from persistence at runtime
   - filters briefing sections/focus areas using included focus areas
   - excludes sections below configured priority threshold
   - records whether user preferences or tenant defaults were used
   - records which defaults were used when no user preference exists
   Do not rely on stale in-memory config or restart-time loading.

8. **Handle cache behavior safely**
   If any caching exists around briefing generation or preference lookup:
   - either bypass it for preference resolution
   - or ensure writes invalidate/refresh the relevant cache automatically
   The acceptance criterion requires no manual cache invalidation and no restart.

9. **Add API endpoints**
   Implement endpoints consistent with existing API style, for example:
   - `GET /api/briefing-preferences/me`
   - `PUT /api/briefing-preferences/me`
   - `GET /api/tenants/{tenantId? or current}/briefing-defaults` or current-tenant equivalent
   - `PUT /api/.../briefing-defaults`
   Use the repo’s actual routing conventions rather than these exact paths if different.

10. **Return descriptive validation errors**
   For invalid requests, return 4xx with structured error codes, e.g. patterns like:
   - `briefing_preferences.invalid_delivery_frequency`
   - `briefing_preferences.unsupported_focus_area`
   - `briefing_preferences.invalid_priority_threshold`
   Match existing error response conventions if the repo already defines a standard envelope.

11. **Add tests**
   Cover at minimum:
   - authenticated user can save and retrieve preferences
   - invalid focus area returns 4xx with descriptive error code
   - invalid delivery frequency returns 4xx with descriptive error code
   - tenant default fallback is used when user preference absent
   - generated briefing excludes disallowed focus areas
   - generated briefing excludes sections below threshold
   - preference update affects next generated briefing immediately
   - unauthorized user cannot manage another tenant’s/default settings

12. **Keep implementation minimal and cohesive**
   - Do not introduce unnecessary abstractions.
   - Prefer extending existing briefing/message/audit models if available.
   - Keep all new code tenant-aware.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If this repo uses integration/API tests with a test host, ensure new endpoint tests pass.

4. Manually verify behavior through tests or local API execution:
   - create tenant defaults
   - fetch tenant defaults
   - create user preferences
   - fetch user preferences
   - generate a briefing for a user with explicit preferences and confirm filtering
   - delete/omit user preferences and generate a briefing to confirm tenant fallback
   - confirm persisted/generated record indicates default source and applied defaults
   - update preferences and immediately generate again to confirm new values are used without restart

5. Validate error handling:
   - unsupported focus area => 4xx + descriptive error code
   - invalid delivery frequency => 4xx + descriptive error code
   - unauthorized default update => 403/401 per existing conventions
   - cross-tenant access blocked

6. If migrations are part of normal workflow, generate/apply the migration using the repo’s established process and ensure the model snapshot stays consistent.

# Risks and follow-ups
- **Unknown existing briefing model**: The repo may already have briefing entities/messages/notifications. Prefer extending existing models rather than duplicating persistence.
- **Authorization ambiguity**: If no explicit admin policy exists yet for tenant defaults, use the closest existing owner/admin policy and note it clearly in code/comments/tests.
- **Validation consistency**: If the repo already has a shared error contract, align with it exactly; do not invent a parallel format.
- **Caching side effects**: Existing dashboard/briefing caching may hide preference changes. Ensure preference resolution is fresh on generation or cache invalidation is automatic on write.
- **Schema choice for focus areas**: If using JSON/array storage, keep validation strict and deterministic. If using a join table is more consistent with the repo, prefer consistency over novelty.
- **Auditability gap**: If there is no current place to record “defaults used,” add the smallest traceable metadata extension possible now and note richer audit/event follow-up later.
- **Follow-up candidates**
  - add delete/reset endpoint for user preferences
  - expose effective resolved preferences endpoint
  - add UI management in web/mobile
  - add audit events for preference changes
  - add delivery channel preferences if not already modeled