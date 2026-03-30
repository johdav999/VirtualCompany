# Goal
Implement backlog task **TASK-12.3.6 — Prioritize approval and exception alerts in sorting** for **ST-603 Alerts, notifications, and approval inbox** in the existing .NET solution.

The coding agent should update the alerts/notifications/inbox flow so that **approval-related items** and **exception/high-urgency operational alerts** are surfaced first in sorted results, while preserving tenant isolation, existing unread/read/actioned behavior, and current inbox functionality.

Because no explicit acceptance criteria were provided for this task, infer completion from the story notes and architecture/backlog context:
- approval requests should appear ahead of lower-priority notifications
- exception alerts such as escalations and workflow failures should also rank ahead of routine items
- sorting should be deterministic and test-covered
- behavior should apply to the approval/alert inbox query path used by web and any shared API consumed by mobile

# Scope
In scope:
- Find the current notification/inbox domain model, query handlers, DTOs, and UI/API endpoints involved in listing alerts and approvals.
- Introduce or refine a **sorting priority model** for inbox items.
- Ensure **approvals** and **exception alerts** sort above routine notifications.
- Preserve existing filters/status semantics such as unread/read/actioned.
- Add or update tests for sorting behavior.
- Make the implementation tenant-safe and consistent with CQRS-lite patterns already used in the codebase.

Out of scope unless required by existing design:
- Large UX redesign of the inbox page
- New notification delivery channels
- Reworking notification persistence model from scratch
- Broad schema redesign unless the current model lacks any way to classify notification type/severity
- Mobile-specific UI work unless it directly consumes the same shared query contract and breaks without adjustment

Priority guidance for inferred sorting:
1. Pending approvals
2. Exception alerts (workflow failures, escalations, blocked/failed executions, similar operational exceptions)
3. Other unread alerts/notifications
4. Read/actioned items

Within the same priority bucket, prefer most recent first unless the existing product behavior clearly uses another secondary sort.

# Files to touch
Inspect and modify only the files needed after discovery. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - notification/inbox/approval entities, enums, value objects, or domain services
- `src/VirtualCompany.Application/**`
  - inbox/notification query handlers
  - approval inbox queries
  - DTO/view model mapping
  - sorting/specification logic
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations
  - repository/query implementations
  - SQL/linq ordering logic
- `src/VirtualCompany.Api/**`
  - endpoints/controllers exposing inbox or notification lists
- `src/VirtualCompany.Web/**`
  - inbox/alerts/approval list components if client-side sorting or display assumptions exist
- `src/VirtualCompany.Mobile/**`
  - only if shared contracts require updates
- tests under the relevant test projects
  - query handler tests
  - repository/integration tests
  - UI/component tests if present and lightweight

Before editing, identify the exact implementation path by searching for:
- `Notification`
- `Approval`
- `Inbox`
- `Alert`
- `Unread`
- `Actioned`
- `WorkflowFailure`
- `Escalation`
- list/query handlers and pages for ST-603 functionality

# Implementation plan
1. **Discover the current inbox implementation**
   - Locate the domain model for notifications/alerts/approvals.
   - Identify whether approvals are:
     - separate entities merged at query time, or
     - represented as notification records with a type/category.
   - Find the main query path used to populate the inbox list in web/API/mobile.
   - Confirm current sorting behavior and where it is applied:
     - database query
     - application layer projection
     - UI layer

2. **Define a stable priority strategy**
   - Implement a clear, centralized sort priority rule rather than ad hoc conditional ordering.
   - Prefer a reusable method/value mapping such as:
     - `PendingApproval => 0`
     - `ExceptionAlert => 1`
     - `UnreadGeneral => 2`
     - `ReadOrActioned => 3`
   - If the model already has fields like `Type`, `Category`, `Severity`, `Status`, or `RequiresAction`, use them.
   - If classification is missing, add the smallest safe extension needed:
     - enum/value object/property for notification kind and/or severity
     - mapping logic from approval/exception source records into inbox item priority

3. **Apply sorting at the authoritative query layer**
   - Prefer server-side ordering in the application/infrastructure query so all consumers get consistent results.
   - Avoid relying on UI-only sorting unless the architecture already mandates client-side composition.
   - Ensure ordering is deterministic, for example:
     - primary: computed priority
     - secondary: unread/actionable state if not already encoded
     - tertiary: `CreatedAt desc`
     - quaternary: stable ID if needed

4. **Handle approvals explicitly**
   - Pending approvals should sort to the top.
   - Approved/rejected/cancelled/expired approvals should not outrank active exceptions unless current UX requires otherwise.
   - If the inbox merges approval requests and notifications, ensure pending approval state is recognized even when represented through a projection DTO.

5. **Handle exception alerts explicitly**
   - Treat operational exceptions as high-priority alerts:
     - workflow failures
     - escalations
     - blocked/failed long-running executions
     - similar exception-style alerts already present in the model
   - If severity exists, map high/critical exception severities above routine informational items.
   - Do not accidentally elevate briefing availability or routine summaries above approvals/exceptions.

6. **Preserve tenant and status constraints**
   - Keep all queries scoped by `company_id` / tenant context.
   - Preserve existing filtering for unread/read/actioned states.
   - Ensure actioned items do not remain pinned above active pending items unless explicitly intended.

7. **Update contracts only if necessary**
   - If the UI needs to display or debug ordering, consider exposing a non-breaking field like `SortPriority` only if useful and safe.
   - Prefer not to expand public API contracts unless required.
   - If contracts change, update all consumers consistently.

8. **Add tests**
   - Add focused tests that prove ordering behavior.
   - Minimum scenarios:
     - pending approval sorts above unread general notification
     - exception alert sorts above routine alert
     - pending approval sorts above exception alert if that is the chosen rule
     - unread routine sorts above read routine
     - same-priority items sort newest first
     - tenant scoping remains intact
   - If query tests exist, extend them rather than creating a parallel pattern.

9. **Check UI assumptions**
   - Verify the web inbox page does not re-sort incorrectly on the client.
   - If it does, align it with server ordering or remove conflicting client-side sort logic.
   - Ensure labels/badges still match the underlying item type.

10. **Keep the change minimal and production-safe**
   - Avoid broad refactors.
   - Prefer additive, localized changes with clear naming and comments where the priority mapping may not be obvious.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects for application/infrastructure/web, run those specifically as well.

4. Manually validate by exercising the inbox query/page with representative data:
   - create or seed a pending approval
   - create or seed an exception alert
   - create or seed a routine unread notification
   - create or seed a read/actioned notification
   - confirm the returned/displayed order is:
     - pending approval first
     - exception alert next
     - routine unread next
     - read/actioned last
   - confirm newest-first ordering within each bucket

5. Verify no tenant leakage:
   - ensure records from another company do not appear or influence ordering

6. If API endpoints exist, inspect the response ordering directly to confirm sorting is not only a UI artifact.

# Risks and follow-ups
- **Model ambiguity:** approvals and alerts may be stored separately, making merged sorting more complex than expected. Keep the merge logic centralized.
- **Missing classification fields:** if notifications lack type/severity metadata, a small schema/domain extension may be required. Keep migrations minimal and backward-compatible.
- **Conflicting client-side sort:** the web/mobile UI may override server ordering. Check for duplicate sorting logic.
- **Performance risk:** computed sorting over merged datasets may become inefficient if done in memory. Prefer database-side ordering where feasible.
- **Behavioral ambiguity:** “exception alerts” may not be consistently defined in the current code. Document the exact mapped categories in code/tests.
- **Future follow-up:** consider formalizing inbox prioritization as a reusable domain concept with explicit notification category/severity/actionability rules, and align mobile/web badge/filter UX around the same model.