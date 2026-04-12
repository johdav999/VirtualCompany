# Goal
Implement `TASK-12.2.7` for story `ST-602 — Audit trail and explainability views` by ensuring all audit and explainability views enforce role-based access correctly.

The coding agent should:
- identify the existing audit/explainability endpoints, queries, pages, and components
- enforce tenant-scoped, role-aware authorization for both list and detail views
- prevent unauthorized users from viewing restricted audit data, linked approvals, tool executions, and affected entities
- preserve concise explainability behavior without exposing raw chain-of-thought
- add or update tests proving authorized access works and unauthorized access is denied safely

Because no explicit acceptance criteria were provided for this task, derive behavior from:
- `ST-602` notes: “Ensure audit views respect role-based access.”
- architecture guidance favoring ASP.NET Core policy-based authorization
- tenant isolation requirements from `ST-101`
- auditability as a domain feature, not just logging

# Scope
In scope:
- audit history list access control
- audit detail access control
- filtering behavior for audit records by user role and company membership
- authorization for linked data surfaced from audit detail, such as:
  - approvals
  - tool executions
  - affected entities
  - rationale summaries
  - data source references
- web UI behavior for hidden/disabled links or safe empty/forbidden states
- API/application-layer authorization and query scoping
- automated tests for role-based access and tenant isolation

Out of scope unless required by current implementation shape:
- redesigning the audit domain model
- adding entirely new audit features unrelated to access control
- broad RBAC refactors outside the audit/explainability surface
- mobile-specific audit UX unless it already consumes the same endpoints and breaks due to authorization changes
- changing business audit event generation semantics beyond what is needed to support secure viewing

Assume the intended access model should be conservative:
- only users with appropriate company membership and sufficient role/permission can access audit views
- unauthorized access should return `403` or `404` according to existing app conventions for sensitive tenant-owned resources
- cross-tenant visibility must never be possible
- if linked entities are more restricted than the parent audit record, they must be omitted, redacted, or blocked according to existing authorization patterns

# Files to touch
Start by inspecting these likely areas and update the actual files you find:

Core projects:
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Web`
- `tests/VirtualCompany.Api.Tests`

Likely file categories:
- audit/explainability controllers or minimal API endpoint registrations
- application queries/handlers for audit list/detail retrieval
- authorization policies, requirements, handlers, or permission helpers
- tenant membership / current user context services
- repository/query implementations for `audit_events` and linked entities
- Blazor pages/components for audit history and audit detail
- shared DTOs/view models used by audit pages
- integration/API tests covering authorization and tenant scoping

Also inspect:
- `README.md`
- any architecture or conventions docs referenced from the solution
- existing auth/role constants and policy registration code
- existing test fixtures for authenticated users, memberships, and seeded tenant data

# Implementation plan
1. **Discover the current audit surface**
   - Locate all audit/explainability routes, pages, handlers, and DTOs.
   - Identify:
     - list endpoint/page
     - detail endpoint/page
     - linked entity loading for approvals, tool executions, tasks, workflows, agents, or other targets
   - Document the current authorization path:
     - endpoint-level `[Authorize]` or policy usage
     - application-layer checks
     - repository-level tenant filters
     - UI-level hiding/disabling

2. **Discover the existing RBAC model**
   - Find role definitions and membership authorization patterns from stories like `ST-101`, `ST-103`, and `ST-204`.
   - Reuse existing policy-based authorization instead of inventing a parallel mechanism.
   - Determine whether the app uses:
     - role names only
     - permissions JSON on membership
     - policy requirements with company context
   - Identify which roles should be allowed to view audit data based on current conventions. If no explicit audit policy exists, introduce one conservatively and align it with manager/admin/owner-style access already present in the codebase.

3. **Define and implement an audit-view authorization policy**
   - Add a dedicated policy/requirement if one does not exist, such as an audit read/view policy.
   - Ensure the policy is tenant-aware and membership-aware.
   - Prefer central enforcement in API/application layers, not only UI hiding.
   - If the app distinguishes list vs detail permissions, preserve that distinction; otherwise use one consistent read policy for both.

4. **Enforce authorization on audit list retrieval**
   - Require authenticated company membership.
   - Restrict access to users with allowed roles/permissions.
   - Ensure all queries are scoped by `company_id`.
   - If the list currently returns all audit events for the tenant, verify that is acceptable for the allowed roles.
   - If some audit events are more sensitive, apply row-level filtering in the query/handler based on actor/target/action categories if such classification already exists.

5. **Enforce authorization on audit detail retrieval**
   - Validate the requested audit record belongs to the current company.
   - Validate the current user is allowed to view audit details.
   - For linked data:
     - approvals
     - tool executions
     - tasks/workflows
     - affected entities
     - source references
     apply authorization checks before materializing or returning them.
   - If linked resources are unauthorized, do not leak their existence through unrestricted nested payloads.

6. **Prevent data leakage in explainability content**
   - Confirm rationale summaries remain concise and operational.
   - Ensure no raw chain-of-thought or internal reasoning fields are exposed through audit DTOs or UI.
   - Review any serialized `request_json`, `response_json`, `policy_decision_json`, or source metadata shown in detail views and redact or limit fields if current UI/API exposes more than intended.
   - Keep data source references human-readable but role-appropriate.

7. **Update the web UI**
   - Audit history page:
     - hide navigation entry or disable access for unauthorized roles if the app already uses role-aware navigation
     - handle forbidden/not-found responses gracefully
   - Audit detail page:
     - avoid rendering restricted linked sections when not authorized
     - show safe messaging instead of broken links or raw errors
   - Do not rely on UI-only enforcement.

8. **Add or update tests**
   - Add API/application tests covering at minimum:
     - authorized role in correct tenant can view audit list
     - unauthorized role in correct tenant is denied
     - user from another tenant cannot access audit list/detail
     - authorized role can view audit detail for own tenant
     - unauthorized nested linked data is not leaked
   - If there are Blazor/component tests already in use, add focused UI tests for hidden navigation or safe forbidden states; otherwise prioritize API/integration coverage.

9. **Keep implementation aligned with existing conventions**
   - Reuse existing result/error patterns.
   - Reuse existing authorization handlers and current company context abstractions.
   - Reuse existing DTO mapping style and test fixture patterns.
   - Avoid introducing broad framework changes unless necessary.

10. **Document assumptions in code comments or PR notes**
   - If role-to-audit access mapping is not explicit in the backlog, choose the most conservative interpretation supported by current roles and note it clearly in the implementation summary.

# Validation steps
Run discovery first, then validate with the project’s standard commands.

Required:
- `dotnet build`
- `dotnet test`

Targeted validation checklist:
- authenticated user with allowed role can open audit history for their company
- authenticated user without allowed role receives forbidden/safe denial
- user cannot access another company’s audit history or detail
- audit detail does not expose unauthorized linked approvals/tool executions/entities
- explainability output remains concise and does not expose raw chain-of-thought
- existing non-audit authorization behavior is not regressed

If there are endpoint tests or manual verification routes, validate:
- audit list filters still work by agent, task, workflow, and date range
- detail pages still render when linked entities are authorized
- navigation/menu entries behave correctly for restricted roles

# Risks and follow-ups
Risks:
- role semantics may be underspecified, causing ambiguity about which memberships should access audit views
- authorization may currently exist only at the UI layer, requiring deeper API/application fixes
- linked entity loading may bypass authorization and leak data through nested DTOs
- inconsistent `403` vs `404` conventions may cause test failures if not matched to existing patterns
- audit detail may currently expose raw tool execution payloads or policy metadata that need redaction

Follow-ups:
- if audit sensitivity levels are not modeled, consider a future task to classify audit events and apply finer-grained access rules
- if permissions are currently role-name based only, consider introducing explicit audit read permissions in membership permissions JSON later
- add dedicated documentation for who can access audit/explainability views across owner/admin/manager/employee/approver roles
- consider extending the same policy to mobile or exported audit bundles if those surfaces exist
- if gaps are found in tenant enforcement at repository level, create a separate hardening task for broader shared-schema isolation review