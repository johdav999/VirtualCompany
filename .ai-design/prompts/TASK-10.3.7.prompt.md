# Goal
Implement backlog task **TASK-10.3.7 — Rejection should support comments for agent/user feedback** for story **ST-403 Approval requests and decision chains**.

The coding agent should extend the approval rejection flow so that:
- approvers can provide a rejection comment,
- the comment is persisted and tenant-scoped,
- linked approval/entity state updates still behave correctly,
- the rejection comment is available for downstream agent/user feedback and audit/history views,
- existing approval behavior remains backward compatible where possible.

Because the story note explicitly says *“Rejection should support comments for agent/user feedback”*, treat this as the primary acceptance target even though no separate acceptance criteria were provided for the task.

# Scope
In scope:
- Domain/application/API changes required to capture a rejection comment on approval decisions.
- Persistence changes if the current implementation does not already store rejection comments at the approval decision level.
- Validation rules for rejection comments.
- Audit/event/history propagation needed so the comment can be surfaced to users/agents.
- Tests covering reject-with-comment behavior.

Out of scope unless already trivial in the existing codebase:
- Large UX redesigns.
- Full notification templating overhaul.
- Mobile-specific UI work unless the same shared API contract is already consumed there.
- Adding comments to approval actions other than rejection unless the current design naturally supports both approve/reject.

Business intent to preserve:
- Approval requests may be single-step or ordered multi-step.
- Rejection must not allow execution of the blocked action.
- Rejection comments should be useful feedback, not internal chain-of-thought.
- Auditability and tenant isolation are mandatory.

# Files to touch
Inspect the solution and update the actual files that implement approvals. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - approval aggregate/entity/value objects
  - approval step entity
  - domain enums/statuses
  - domain methods for reject decisions

- `src/VirtualCompany.Application/**`
  - commands/handlers for approval rejection
  - DTOs/contracts/view models
  - validators
  - query models if approval detail/history surfaces comments
  - audit/event creation logic

- `src/VirtualCompany.Api/**`
  - request models for reject endpoints
  - controllers/endpoints
  - API contract mapping

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration/mappings
  - repository implementations
  - migrations if schema changes are needed
  - outbox/audit persistence if applicable

- `src/VirtualCompany.Web/**`
  - approval action form/view/component to enter rejection comment
  - approval detail/history rendering if comments should be visible in web UX

- `src/VirtualCompany.Shared/**`
  - shared contracts used by web/mobile if approval DTOs live here

- `tests/VirtualCompany.Api.Tests/**`
- other relevant test projects under `tests/**`
  - API tests
  - application handler tests
  - domain tests
  - persistence tests if migration/mapping behavior needs coverage

Also review:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
for any repo-specific migration conventions.

# Implementation plan
1. **Discover the current approval rejection flow**
   - Find the approval aggregate, approval step model, reject command/endpoint, and any approval detail query.
   - Determine whether comments already exist anywhere:
     - architecture suggests `approval_steps.comment text null`
     - verify whether the live code/schema already has this field and whether it is actually used.
   - Identify whether rejection is recorded at:
     - approval-level only,
     - step-level only,
     - or both.

2. **Choose the minimal correct persistence model**
   - Prefer storing the rejection comment on the actual decision record for the rejecting step.
   - If the current implementation only tracks approval-level rejection and not step comments, add the smallest schema/model change that preserves ordered-chain semantics.
   - If `approval_steps.comment` already exists in code/schema, wire it through instead of inventing a parallel field.
   - If approval-level summary exists, consider whether the rejection comment should also populate a summary field for easier display, but do not duplicate unnecessarily.

3. **Update domain behavior**
   - Extend the reject operation to accept a comment parameter.
   - Enforce domain rules such as:
     - only pending/current approver can reject,
     - rejection transitions approval to rejected,
     - linked entity/action remains blocked/not executed,
     - comment is stored with the decision metadata.
   - Normalize comment input:
     - trim whitespace,
     - treat empty/whitespace-only as null or invalid based on chosen rule.
   - Recommended rule for this task: rejection comments should be supported and encouraged; if product conventions allow, make them required for rejection. If making them required would break existing clients/tests too broadly, allow null but persist when supplied. Prefer requirement only if clearly aligned with current UX/API patterns.

4. **Update application command/contracts**
   - Add `Comment` / `RejectionComment` to the reject approval command/request DTO.
   - Ensure handlers pass the comment into domain methods.
   - If there are separate flows for:
     - single-step approvals,
     - multi-step chain approvals,
     - role-based vs user-based approvals,
     update all relevant paths consistently.

5. **Update API surface**
   - Modify the reject endpoint request contract to accept the comment.
   - Preserve backward compatibility if possible:
     - optional field in request body is preferred unless the API is still internal-only and easy to update.
   - Return the updated approval state and any relevant comment field in response DTOs if current API patterns include decision details.

6. **Update persistence and mappings**
   - If needed, add/update EF configuration and create a migration.
   - Ensure comment length is bounded reasonably, e.g. via validation and column type constraints if conventions exist.
   - Keep tenant scoping unchanged.
   - Do not store raw reasoning or hidden model internals; only user-entered rejection feedback.

7. **Expose comment in read models**
   - Update approval detail/history queries so rejection comments are visible where approvers, requesters, or agents need feedback.
   - At minimum, ensure the comment is available in:
     - approval detail response,
     - approval step history if such a view exists,
     - audit/event payloads if the system uses them for explainability.

8. **Audit and feedback propagation**
   - If approval decisions create audit events, include the rejection comment in a safe, concise way.
   - If linked tasks/workflows receive rationale or status messages, include the rejection comment as feedback text where appropriate.
   - Do not expose sensitive internal metadata; only the approver-provided comment.

9. **Web UI update**
   - If the web app already has approval action controls, add a rejection comment input.
   - Keep UX minimal:
     - textarea or text input,
     - validation message if required,
     - display saved comment in approval history/detail.
   - Avoid broad styling churn.

10. **Testing**
   - Add/adjust tests for:
     - rejecting an approval with a comment persists the comment,
     - approval status becomes rejected,
     - linked entity does not proceed,
     - comment is returned in detail/history queries,
     - unauthorized or wrong-tenant users cannot reject,
     - invalid state transitions still fail,
     - optional/required validation behavior for empty comments.
   - Prefer focused tests near the changed behavior rather than broad snapshot churn.

11. **Keep implementation aligned with architecture**
   - Respect modular boundaries:
     - domain rules in Domain,
     - orchestration in Application,
     - transport in Api/Web,
     - persistence in Infrastructure.
   - Maintain CQRS-lite patterns already present in the repo.
   - Preserve auditability and tenant isolation.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of the change:
   - generate/apply migration per repo conventions,
   - verify the approval rejection comment column/mapping exists and persists correctly.

4. Manual verification through API or web flow:
   - create or use a pending approval,
   - reject it with a meaningful comment,
   - confirm:
     - approval status is `rejected`,
     - decision timestamp/user is recorded,
     - rejection comment is persisted,
     - linked task/workflow/action remains blocked/not executed,
     - approval detail/history shows the comment,
     - audit/history includes the feedback where expected.

5. Regression checks:
   - approve flow still works,
   - pending approval listing still works,
   - multi-step chain behavior is not broken,
   - tenant isolation remains intact.

# Risks and follow-ups
- **Schema mismatch risk:** Architecture shows `approval_steps.comment`, but the actual codebase may differ. Use the live implementation as source of truth and avoid duplicating fields.
- **Backward compatibility risk:** If existing clients call reject without a body/comment, making comments required may break them. Prefer optional unless the repo clearly supports coordinated client updates.
- **Multi-step chain nuance:** Ensure the comment is attached to the rejecting step and that chain termination behavior remains correct.
- **Audit duplication risk:** Avoid storing the same comment redundantly in too many places unless needed for query performance/read models.
- **UX consistency:** If web/mobile/shared contracts diverge, update shared DTOs carefully.
- **Follow-up candidates:**
  - require rejection comments by product policy,
  - notify requesting user/agent with the rejection comment,
  - surface comments in approval inbox summaries,
  - support approval comments on approve/override actions too,
  - add comment length/content moderation rules if needed.