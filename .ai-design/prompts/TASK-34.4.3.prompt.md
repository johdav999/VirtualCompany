# Goal
Implement backlog task **TASK-34.4.3** for story **US-34.4** by adding the won-deal finance handoff flow to the real Fortnox-backed finance APIs, with strict approval gating, durable linkage storage, retry-safe execution, audit/logging, emitted domain events, and UI/API status exposure.

The implementation must ensure that:
- marking a deal as won emits `sales.deal.won`
- finance document creation is **never auto-executed before approval** in MVP
- approval of the finance recommendation triggers real Fortnox-backed draft quote or invoice creation
- created finance artifacts are linked back to the originating deal
- failures are visible, logged, and retriable without duplicate finance documents or duplicate business activities
- status is exposed in both API and UI

Also preserve and align with the broader acceptance criteria already in scope for US-34.4:
- lead qualification persistence + `sales.lead.qualified`
- follow-up recommendation generation with approval requirements from automation policy
- approval/execution state for draft/send recommendations
- automation policy modes: manual only, draft only, auto-send low-risk follow-ups
- finance creation always approval-gated in MVP
- retry-safe failure handling for email and finance handoff

# Scope
Implement only what is necessary to satisfy this task and its direct dependencies in the existing modular monolith architecture.

Include:
- domain model additions for finance handoff recommendation/execution state on won deals
- approval-gated finance handoff workflow integration
- Fortnox-backed finance adapter invocation for draft quote/invoice creation
- persistence for external finance linkage and idempotency metadata
- event emission via existing outbox/event mechanism
- API query/command support for finance handoff status and retry
- Blazor UI status exposure for deal detail / sales workflow views
- audit/business logging for approval requested, approved, executed, failed, retried
- retry-safe behavior and duplicate prevention

If missing but required for acceptance, include minimal supporting work for:
- recommendation approval/execution state model reuse
- surfaced failure state in UI/API
- emitted `sales.lead.qualified` and `sales.deal.won` events if not already implemented
- automation policy enforcement that finance creation always requires approval

Do not:
- introduce microservices
- bypass approval flow for finance creation
- auto-create final posted Fortnox documents; create draft quote/invoice only
- redesign unrelated sales, inbox, or orchestration subsystems
- add speculative abstractions beyond what the current solution structure needs

# Files to touch
Inspect the solution first and then update the actual relevant files. Expected areas:

- `src/VirtualCompany.Domain/**`
  - sales/deals entities, value objects, enums
  - approval/recommendation entities if present
  - finance linkage entities if needed
  - domain events for `sales.deal.won` and possibly `sales.lead.qualified`

- `src/VirtualCompany.Application/**`
  - commands/handlers for marking deal won
  - approval handlers
  - finance handoff orchestration service
  - retry command/handler
  - DTOs/view models for finance handoff status
  - policy enforcement logic

- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations/migrations support
  - repositories
  - outbox/event dispatch integration
  - Fortnox finance adapter/client
  - idempotency handling and persistence
  - logging/audit persistence

- `src/VirtualCompany.Api/**`
  - endpoints/controllers for:
    - mark deal won
    - approve finance handoff
    - retry failed finance handoff
    - query deal finance handoff status

- `src/VirtualCompany.Web/**`
  - deal detail/status components
  - approval UI hooks if web owns them
  - failure/retry/status presentation

- `tests/VirtualCompany.Api.Tests/**`
  - API integration tests
- potentially add/update:
  - `tests/VirtualCompany.Application.Tests/**` if present
  - `tests/VirtualCompany.Infrastructure.Tests/**` if present

- migration-related files in the project’s actual migration location
  - do not use `docs/postgresql-migrations-archive/` for active runtime code

Before coding, locate:
- existing sales/deal aggregate
- approval subsystem
- outbox/event publishing pattern
- Fortnox integration code or finance integration abstractions
- existing recommendation/activity/audit models
- existing UI deal detail page/components

# Implementation plan
1. **Discover existing architecture and map extension points**
   - Inspect current modules for:
     - sales lead/deal entities and won-state transition
     - recommendation + approval workflow
     - communication/email execution state
     - finance integration abstractions and any Fortnox adapter
     - audit event persistence
     - outbox/domain event publishing
   - Reuse existing patterns rather than inventing parallel ones.

2. **Model finance handoff state in the domain**
   - Add explicit state for finance handoff on a deal or linked sales workflow entity.
   - Support at minimum:
     - not_requested / pending_approval / approved / in_progress / completed / failed
   - Persist:
     - company/tenant scope
     - deal id
     - requested finance document type (`quote` or `invoice`)
     - approval id / recommendation id linkage
     - external system name (`Fortnox`)
     - external document id/reference/number if created
     - idempotency key / execution key
     - last error code/message
     - timestamps for requested/approved/executed/failed/retried
   - Ensure linkage is durable and queryable for UI/API.

3. **Emit and handle won-deal event**
   - When a deal is marked won:
     - persist won state
     - emit `sales.deal.won`
     - create a finance handoff recommendation/request that requires approval
   - Do **not** create the Fortnox draft document at this stage.
   - If architecture already uses workflow instances or recommendations, attach to that model instead of duplicating concepts.

4. **Enforce approval gating**
   - Update automation/policy logic so finance document creation always requires approval in MVP, regardless of other automation settings.
   - Approval of the finance handoff should transition state to approved and enqueue execution.
   - Rejection should leave a clear rejected/cancelled state and no external finance side effect.

5. **Implement Fortnox-backed draft creation**
   - Use or extend the real finance integration adapter to create a **draft quote or invoice** in Fortnox.
   - Map internal deal/customer/outcome data into the Fortnox request contract.
   - Persist returned external identifiers and references.
   - Ensure the adapter is invoked only from application/infrastructure service boundaries, never directly from UI/API.

6. **Add idempotent execution and retry safety**
   - Generate a stable idempotency key per deal + requested finance document type + tenant.
   - Before creating a Fortnox draft, check whether a linked finance document already exists for that handoff.
   - On retry:
     - retry only failed/incomplete handoffs
     - do not create duplicate finance documents
     - do not duplicate business activities/audit entries beyond explicit retry records
   - Distinguish transient integration failures from permanent validation/business failures where possible.

7. **Audit and business activity logging**
   - Record business audit events for:
     - deal won
     - finance handoff requested
     - finance handoff approval requested
     - finance handoff approved/rejected
     - finance draft creation started
     - finance draft creation succeeded
     - finance draft creation failed
     - finance handoff retried
   - Keep rationale concise and operational.
   - Include source references and linked entities where existing audit model supports it.

8. **Expose API status**
   - Add/extend endpoints and DTOs so clients can retrieve:
     - current finance handoff status
     - approval status
     - external linkage/reference if created
     - last failure details safe for UI
     - retry availability
   - Add command endpoints for:
     - mark deal won
     - approve finance handoff
     - retry failed handoff
   - Ensure tenant scoping and authorization are enforced.

9. **Expose UI status**
   - Update Blazor UI to show on the relevant deal page or sales outcome view:
     - won status
     - finance handoff pending approval / approved / creating / created / failed
     - created draft quote/invoice reference if available
     - failure message
     - retry action when allowed
   - Make approval requirement explicit in the UI.
   - Do not imply auto-creation before approval.

10. **Cover adjacent acceptance criteria if missing**
   - If lead qualification persistence/event/API/UI is incomplete, implement the minimum needed:
     - fit, temperature, priority, suggested next action
     - `sales.lead.qualified`
   - If follow-up recommendation approval/execution state is incomplete, align state handling patterns with finance handoff.
   - If automation policy modes are partially implemented, ensure finance creation remains approval-only even when low-risk follow-ups can auto-send.

11. **Database migration**
   - Add EF/Core migration(s) for any new tables/columns/indexes.
   - Include indexes for:
     - `company_id + deal_id`
     - approval linkage
     - external reference uniqueness where appropriate
     - idempotency key uniqueness
   - Keep migration names descriptive.

12. **Tests**
   - Add focused tests for:
     - marking deal won emits event and creates approval-gated finance handoff
     - approval triggers Fortnox draft creation
     - no finance document is created before approval
     - successful creation stores external linkage
     - failed creation surfaces failure and is retriable
     - retry does not duplicate finance documents
     - API returns correct status for UI
     - tenant isolation/authorization on finance handoff endpoints

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add or update automated tests covering:
   - deal won -> `sales.deal.won` emitted
   - deal won -> finance handoff enters pending approval
   - approval required regardless of automation mode
   - approval -> Fortnox draft quote creation
   - approval -> Fortnox draft invoice creation
   - external linkage persisted on success
   - failure persisted and exposed
   - retry succeeds after transient failure
   - retry does not create duplicate finance documents
   - API/UI DTO includes status, approval state, external reference, failure info
   - unauthorized cross-tenant access is rejected

4. Manually verify in web UI:
   - mark a deal as won
   - confirm UI shows finance handoff pending approval
   - approve the handoff
   - confirm UI updates to created/linked draft status
   - simulate or force a Fortnox failure
   - confirm failure is visible and retry action appears
   - retry and confirm no duplicate document is created

5. Verify persistence:
   - inspect created migration
   - confirm new records include tenant/company scoping
   - confirm idempotency/linkage fields are populated
   - confirm audit events are written

6. Verify logs/outbox behavior:
   - confirm business audit events exist
   - confirm domain/integration event dispatch is consistent with existing outbox pattern
   - confirm no side effect occurs before approval

# Risks and follow-ups
- **Existing model mismatch:** The repo may already have recommendation/approval/execution models. Reuse them instead of creating a parallel finance-specific workflow.
- **Fortnox adapter gaps:** If the real Fortnox integration is incomplete, implement the minimum production-shaped adapter and clearly isolate any unavoidable TODOs.
- **Idempotency ambiguity:** If Fortnox lacks native idempotency support for the target endpoint, enforce idempotency internally with unique execution/linkage constraints before external calls.
- **UI placement uncertainty:** If there is no dedicated deal detail page yet, expose status in the nearest existing sales detail/status surface and note the follow-up.
- **Acceptance spillover:** This task references broader US-34.4 criteria. If some adjacent pieces are missing, implement only the minimum required to keep finance handoff coherent and acceptance-testable.
- **Migration safety:** Avoid editing archived migration docs; place runtime migrations in the active infrastructure project.
- **Authorization:** Ensure finance approval/retry actions are restricted to appropriate roles, likely finance approver/admin/owner based on existing authorization patterns.
- **Follow-up recommendation:** If not already present, a later task should add richer finance document preview/edit before approval, but do not block this task on that UX.