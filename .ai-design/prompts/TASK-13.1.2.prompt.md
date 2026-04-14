# Goal
Implement backlog task **TASK-13.1.2 — Build CRUD API endpoints for schedule trigger management with validation** for story **ST-701 Scheduled agent triggers** in the existing .NET modular monolith.

Deliver a persisted, tenant-scoped ASP.NET Core API that allows users to:

- create schedule triggers for an agent
- update schedule triggers
- enable schedule triggers
- disable schedule triggers
- delete schedule triggers

The implementation must validate cron-like expressions and timezones, calculate next-run timestamps, and support reliable downstream scheduling semantics so that enabled triggers can later enqueue exactly one execution per due schedule window.

# Scope
Implement only what is necessary to satisfy this task and its acceptance criteria within the current architecture and codebase conventions.

Include:

- domain model and persistence for schedule triggers
- database migration(s)
- application commands/queries and validation
- API endpoints for CRUD + enable/disable
- next-run calculation logic
- timezone validation
- cron expression validation
- tenant/company scoping and agent ownership checks
- tests covering validation, persistence, and API behavior

Design for future scheduler worker integration, but do not overbuild unrelated orchestration features unless required by existing code patterns.

Assumptions to honor:

- multi-tenant shared-schema model using `company_id`
- PostgreSQL persistence
- ASP.NET Core API in `src/VirtualCompany.Api`
- application/domain/infrastructure separation
- CQRS-lite patterns if already present
- safe validation errors with no persistence on invalid cron/timezone input

Out of scope unless already required by existing architecture:

- full background worker execution engine
- UI/Blazor pages
- mobile changes
- broad workflow engine changes unrelated to schedule trigger management
- external messaging/broker integration

# Files to touch
Inspect the solution first and then update the appropriate files in these areas.

Likely areas:

- `src/VirtualCompany.Domain/**`
  - add schedule trigger entity/value objects/enums
- `src/VirtualCompany.Application/**`
  - commands, queries, DTOs, validators, handlers
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configuration, repositories, persistence mappings, migration support
- `src/VirtualCompany.Api/**`
  - controller or minimal API endpoints, request/response contracts, DI wiring
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests
- possibly `tests` for application/domain validation if such projects already exist
- migration location based on repo convention
  - check whether migrations live under infrastructure or archived docs guidance in `docs/postgresql-migrations-archive/README.md`

Also inspect:

- `README.md`
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`

# Implementation plan
1. **Inspect existing architecture and conventions**
   - Review solution structure, existing modules, tenant resolution, authorization, EF Core setup, API style, validation style, and migration conventions.
   - Search for existing patterns for:
     - agent management endpoints
     - workflow/task entities
     - company-scoped repositories
     - command/query handlers
     - validation exceptions / problem details
     - audit/event patterns
   - Reuse existing conventions rather than inventing new ones.

2. **Model the schedule trigger domain**
   - Add a persisted schedule trigger entity under the appropriate module boundary, likely near agent/workflow/task trigger concerns.
   - Minimum fields should support:
     - `id`
     - `company_id`
     - `agent_id`
     - `name` or identifier if consistent with existing patterns
     - `cron_expression`
     - `timezone`
     - `enabled`
     - `created_at`
     - `updated_at`
     - `enabled_at` nullable
     - `disabled_at` nullable
     - `next_run_at` nullable
     - optional scheduler bookkeeping fields if needed for exact-once due-window support, such as:
       - `last_evaluated_at`
       - `last_enqueued_window_at`
       - concurrency token/version
   - Keep the model minimal but future-safe for the acceptance criterion:
     - “When enabled, the system enqueues exactly one execution request for each due schedule window.”
     - “Disabling prevents any new executions after the disable timestamp.”
   - If an enum/status model is more idiomatic than a boolean, use the existing convention.

3. **Choose and integrate cron/timezone libraries**
   - Prefer established .NET libraries already present in the repo if available.
   - If no existing library is present, use a well-supported option for cron parsing and next occurrence calculation.
   - Validate timezone using `TimeZoneInfo` or an existing timezone abstraction.
   - Be careful about Windows vs IANA timezone identifiers:
     - inspect current platform conventions in the repo
     - if the app targets Linux containers/PostgreSQL cloud hosting, prefer IANA if consistent
     - if supporting both, normalize explicitly
   - Reject unsupported timezones with validation errors.

4. **Add persistence**
   - Create EF Core configuration and migration for the schedule trigger table.
   - Add indexes appropriate for:
     - company + agent lookup
     - enabled triggers by next run
     - scheduler polling if future worker uses it
   - Enforce foreign key to agents and tenant ownership through application checks.
   - If the codebase uses soft delete, follow that pattern; otherwise use hard delete.

5. **Implement application layer commands and queries**
   - Add commands/handlers for:
     - create schedule trigger
     - update schedule trigger
     - enable schedule trigger
     - disable schedule trigger
     - delete schedule trigger
   - Add query/handler(s) for:
     - get trigger by id
     - list triggers for an agent
   - On create/update:
     - validate agent exists and belongs to current company
     - validate cron expression
     - validate timezone
     - compute `next_run_at`
     - persist only if valid
   - On enable:
     - set enabled state and `enabled_at`
     - clear/update `disabled_at`
     - recompute `next_run_at` from current timestamp
   - On disable:
     - set disabled state and `disabled_at`
     - ensure future scheduler logic can honor “no new executions after disable timestamp”
   - On update:
     - if enabled, recompute `next_run_at`
     - if disabled, next run may remain nullable or computed according to existing design choice; keep behavior explicit and consistent
   - Return DTOs with enough information for clients:
     - id
     - agent id
     - cron expression
     - timezone
     - enabled/status
     - next run at
     - timestamps

6. **Implement API endpoints**
   - Add tenant-scoped endpoints under an agent-oriented or trigger-oriented route consistent with existing API design.
   - Recommended shape if no stronger convention exists:
     - `POST /api/agents/{agentId}/schedule-triggers`
     - `GET /api/agents/{agentId}/schedule-triggers`
     - `GET /api/agents/{agentId}/schedule-triggers/{triggerId}`
     - `PUT /api/agents/{agentId}/schedule-triggers/{triggerId}`
     - `POST /api/agents/{agentId}/schedule-triggers/{triggerId}/enable`
     - `POST /api/agents/{agentId}/schedule-triggers/{triggerId}/disable`
     - `DELETE /api/agents/{agentId}/schedule-triggers/{triggerId}`
   - Use existing auth/tenant resolution middleware and authorization policies.
   - Return proper status codes:
     - `201 Created` for create
     - `200 OK` for reads/updates/enable/disable
     - `204 No Content` for delete if consistent
     - `400 Bad Request` or validation problem details for invalid cron/timezone
     - `404 Not Found` for cross-tenant or missing agent/trigger as appropriate per existing security conventions

7. **Prepare scheduler-safe semantics**
   - Even if the worker is not fully implemented here, structure the data model so exact-once due-window enqueueing is possible.
   - Add comments or small internal abstractions documenting intended semantics:
     - due windows are derived from cron occurrences in the configured timezone
     - each due occurrence should be enqueued once
     - disable timestamp is a hard cutoff for future enqueueing
   - If there is already a scheduler polling component, integrate minimally so enabled triggers can be discovered by `next_run_at`.
   - Do not implement speculative infrastructure beyond what this task needs.

8. **Validation and error handling**
   - Ensure invalid cron expressions are rejected before persistence.
   - Ensure unsupported timezones are rejected before persistence.
   - Ensure update operations also reject invalid values and do not partially persist.
   - Use existing validation framework/pipeline if present, such as FluentValidation.
   - Return field-level validation errors if the project already supports them.

9. **Testing**
   - Add tests for:
     - create valid trigger persists and returns next run
     - create invalid cron returns validation error and does not persist
     - create invalid timezone returns validation error and does not persist
     - update valid trigger recalculates next run
     - enable sets enabled state and next run
     - disable sets disabled timestamp and prevents future active state
     - delete removes trigger
     - tenant scoping prevents access to another company’s agent/trigger
   - Prefer integration/API tests if the repo already supports them.
   - Add focused unit tests for cron/timezone validation and next-run calculation if there is a suitable test project.

10. **Keep implementation clean**
   - Follow existing naming, namespaces, and folder structure.
   - Avoid leaking infrastructure concerns into controllers.
   - Keep business logic in application/domain layers.
   - Add concise code comments only where behavior is non-obvious, especially around schedule semantics and timezone handling.

# Validation steps
Run and report the results of the relevant validation commands after implementation.

Minimum:

- `dotnet build`
- `dotnet test`

Also validate manually through tests or API integration coverage that:

- valid trigger creation succeeds and persists
- invalid cron expression is rejected and not persisted
- invalid timezone is rejected and not persisted
- update recalculates next run correctly
- enable/disable endpoints change persisted state correctly
- delete removes or deactivates the trigger per repo convention
- cross-tenant access is blocked
- next-run timestamps are timezone-aware and deterministic in tests

If migrations are part of normal workflow, ensure the migration is generated/applied according to repo conventions and compiles cleanly.

# Risks and follow-ups
- **Timezone identifier mismatch risk**: .NET timezone handling differs across Windows/Linux and between IANA/Windows IDs. Inspect current deployment assumptions and normalize carefully.
- **Cron semantics risk**: different libraries interpret cron formats differently. Match the accepted format explicitly and document it in request validation/messages.
- **Exact-once scheduling is only partially addressed here**: this task should prepare persisted state for exact-once due-window enqueueing, but a worker/poller may still need a follow-up task if not already present.
- **Disable cutoff semantics**: ensure `disabled_at` is persisted in UTC and future scheduler logic compares against due windows correctly.
- **Tenant isolation risk**: all reads/writes must verify both `company_id` and `agent_id` ownership, not just trigger id.
- **Migration placement risk**: the repo may have a specific migration workflow; follow existing conventions rather than creating a parallel pattern.
- **Potential follow-up tasks**:
  - scheduler worker polling and enqueue logic for due windows
  - idempotent execution request creation keyed by trigger + due window
  - audit events for trigger lifecycle changes
  - API documentation/OpenAPI examples for cron/timezone formats