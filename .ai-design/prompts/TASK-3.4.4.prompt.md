# Goal
Implement backlog task **TASK-3.4.4 — Persist per-user insight acknowledgment state** for story **US-3.4 Action-oriented insights and deep links to operational work**.

Deliver a complete vertical slice so that:
- dashboard action insights can be acknowledged by a user,
- acknowledgment state persists across refreshes for that same user,
- acknowledgment is isolated by tenant and user,
- existing or new action queue responses include acknowledgment state,
- automated tests cover scoring stability, deep-link generation, and acknowledgment persistence.

# Scope
In scope:
- Add persistence model for **per-user insight acknowledgment**.
- Expose application/API behavior to **mark an insight as acknowledged**.
- Ensure dashboard/action queue query returns whether each insight is acknowledged for the current user.
- Preserve stable ordering for identical scores in the prioritized action queue.
- Ensure deep links are generated for supported target types: task, workflow, approval.
- Add automated tests for:
  - action scoring,
  - stable ordering for identical scores,
  - deep-link generation,
  - acknowledgment persistence across refreshes / repeated reads.

Out of scope:
- Full redesign of dashboard UI.
- Bulk acknowledge/unacknowledge unless trivial and already aligned with current patterns.
- Notification/read-state modeling beyond this insight acknowledgment feature.
- Mobile-specific implementation unless shared API contracts require no extra work.

Assumptions to validate in the codebase before implementation:
- There is already an executive dashboard or action queue query/endpoint in place for US-3.4.
- Authentication and tenant/user resolution already exist and should be reused.
- CQRS-lite patterns are preferred in the application layer.
- PostgreSQL migrations are managed in the current repo’s established way.

# Files to touch
Likely areas; adjust to actual project structure and naming conventions found in the repo.

**Domain**
- `src/VirtualCompany.Domain/...`
  - Add entity/value object for user insight acknowledgment if domain entities are modeled explicitly.
  - Add any enums/constants for insight target type if not already present.

**Application**
- `src/VirtualCompany.Application/.../Dashboard/...`
  - Action queue query/handler: include acknowledgment state in returned DTO.
- `src/VirtualCompany.Application/.../Insights/...`
  - New command + handler to acknowledge an insight.
- `src/VirtualCompany.Application/.../Abstractions/...`
  - Repository/query interfaces for acknowledgment persistence.
- Shared DTO/contracts for action queue items if they live here.

**Infrastructure**
- `src/VirtualCompany.Infrastructure/...`
  - EF Core configuration / repository implementation.
  - Query implementation joining action insights with per-user acknowledgment state.
  - Migration adding new table and indexes.

**API**
- `src/VirtualCompany.Api/...`
  - Endpoint/controller/minimal API route for acknowledge action.
  - Wire command dispatch and current user/company context.

**Web**
- `src/VirtualCompany.Web/...`
  - Dashboard action queue component/page updates to show acknowledged state.
  - UI action/button to acknowledge an insight.
  - Refresh/rebind behavior after acknowledgment.
  - Preserve deep-link rendering if already present.

**Tests**
- `tests/VirtualCompany.Api.Tests/...`
  - API integration tests for acknowledgment persistence and tenant/user isolation.
- Add or update application/unit tests for:
  - scoring,
  - stable ordering,
  - deep-link generation.

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
for migration/build/test conventions.

# Implementation plan
1. **Inspect existing dashboard/action queue implementation**
   - Find the current action insight model, scoring logic, ordering logic, and deep-link generation.
   - Identify the canonical insight identity. Prefer a deterministic identifier already present in the queue item model.
   - If no durable insight identifier exists, introduce one that is stable enough for persistence. It must uniquely represent the actionable insight instance for a user-visible queue item.

2. **Define persistence model for acknowledgment**
   Create a tenant- and user-scoped persistence record, e.g.:
   - `id`
   - `company_id`
   - `user_id`
   - `insight_id`
   - `acknowledged_at`

   Recommended constraints:
   - unique index on `(company_id, user_id, insight_id)`
   - indexes supporting lookup by `(company_id, user_id)`

   Important:
   - Do **not** store acknowledgment globally on the insight itself.
   - State must be per-user, per-tenant.

3. **Add migration**
   Add a PostgreSQL migration for the new table using the repo’s existing migration approach.
   Ensure:
   - proper PK,
   - FK relationships if users/companies are referenced in the current schema style,
   - unique constraint for idempotent acknowledge behavior.

4. **Implement domain/application contract**
   Add a command such as:
   - `AcknowledgeInsightCommand(companyId, userId, insightId)`

   Handler behavior:
   - validate current user/company context,
   - upsert or insert-if-not-exists acknowledgment record,
   - be idempotent,
   - return success without duplicating rows.

5. **Update action queue query**
   Extend the dashboard/action queue query so each item includes:
   - `IsAcknowledged`
   - existing fields required by acceptance criteria:
     - priority,
     - reason,
     - owner,
     - due time or SLA state,
     - deep link.

   Query should:
   - fetch current user acknowledgments for the current company,
   - map `IsAcknowledged = true` when matching `insight_id` exists.

6. **Preserve stable priority ordering**
   Review current scoring/order implementation.
   Acceptance requires:
   - ordering consistent with configured scoring rules,
   - stable ordering for identical scores.

   Implement explicit deterministic secondary ordering if missing, e.g.:
   - score descending,
   - due time ascending/null handling as appropriate,
   - created/occurred timestamp ascending,
   - insight id ascending.

   Do not rely on database default ordering for ties.

7. **Verify or implement deep-link generation**
   Ensure each action item resolves a deep link to the target entity:
   - task,
   - workflow,
   - approval.

   Centralize link generation if currently duplicated.
   Add deterministic tests for each supported target type.

8. **Add API endpoint**
   Add an authenticated tenant-scoped endpoint, e.g.:
   - `POST /api/dashboard/insights/{insightId}/acknowledge`
   or align with existing route conventions.

   Requirements:
   - derive current user and company from auth/context, not request body,
   - dispatch command,
   - return appropriate success status,
   - remain idempotent.

9. **Update Blazor dashboard UI**
   In the action queue UI:
   - render acknowledgment state,
   - provide an acknowledge action/button,
   - call the API/command,
   - update local state or refresh query result,
   - ensure acknowledged state remains after page refresh.

   Keep UI changes minimal and consistent with existing component patterns.

10. **Add tests**
   Add automated coverage at the right layers:

   **Unit/Application tests**
   - scoring produces expected priority order,
   - identical scores use deterministic stable ordering,
   - deep-link generation returns correct route for task/workflow/approval.

   **Integration/API tests**
   - acknowledging an insight persists for the current user,
   - acknowledged state is returned on subsequent dashboard reads,
   - refresh/re-read behavior shows persisted acknowledgment,
   - another user in same company does not inherit acknowledgment,
   - another company cannot see/use the acknowledgment.

11. **Keep implementation aligned with architecture**
   - Use CQRS-lite: command for acknowledgment, query for dashboard queue.
   - Enforce tenant isolation in repository/query layer.
   - Keep business logic out of controllers/UI.
   - Prefer modular monolith boundaries and typed contracts.

# Validation steps
1. Review repo conventions:
   - inspect migration approach,
   - inspect existing dashboard/action queue implementation,
   - inspect auth/current user/company resolution.

2. Build after changes:
   - `dotnet build`

3. Run automated tests:
   - `dotnet test`

4. Manually validate behavior if web app can be run locally:
   - load dashboard action queue,
   - acknowledge one insight,
   - refresh page,
   - confirm same user still sees it acknowledged,
   - sign in as different user in same tenant and confirm it is not acknowledged there,
   - verify deep links navigate correctly,
   - verify ordering remains deterministic across repeated loads.

5. Validate database artifacts:
   - migration applies cleanly,
   - unique constraint prevents duplicate acknowledgment rows,
   - query plan is reasonable for `(company_id, user_id)` lookups.

# Risks and follow-ups
- **Insight identity instability**: if queue items are computed dynamically without a stable ID, acknowledgment may not persist correctly. If needed, introduce a canonical deterministic `insight_id` derived from target type + target id + insight category.
- **Ordering regressions**: changing sort logic may alter current dashboard behavior. Keep scoring rules intact and only add explicit tie-breakers.
- **Tenant/user leakage**: ensure acknowledgment joins always filter by both `company_id` and `user_id`.
- **UI/API mismatch**: if the web app uses server-side handlers instead of API calls, adapt to the existing pattern rather than forcing a new one.
- **Migration approach uncertainty**: follow the repo’s established migration mechanism exactly; do not invent a parallel process.
- **Future follow-up**: consider unacknowledge, acknowledged timestamp display, and optional filtering/hiding of acknowledged insights if later backlog items require it.