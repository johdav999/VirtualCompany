# Goal
Implement backlog task **TASK-35.3.1** for story **US-35.3 Add automation policies, approval controls, and website lead capture entrypoints** in the existing .NET modular monolith.

Deliver a tenant-scoped outbound automation policy capability that:
- lets tenant admins configure outbound policy settings
- enforces those settings during campaign/sequence execution
- creates and persists approval/review decisions for gated messages
- exposes review queue APIs for authorized users
- adds a production-ready website form lead capture API
- deduplicates website submissions by tenant + email within a configured window
- enrolls accepted leads into a configured follow-up sequence within 1 minute

Preserve clean architecture boundaries across:
- `VirtualCompany.Api`
- `VirtualCompany.Application`
- `VirtualCompany.Domain`
- `VirtualCompany.Infrastructure`

Use shared-schema multi-tenancy with strict `company_id` enforcement on all tenant-owned data and queries.

# Scope
Implement only what is required for this task, but do it end-to-end enough to satisfy the acceptance criteria.

Include:

1. **Domain model and persistence**
   - Tenant/company-scoped outbound automation policy entity or settings aggregate
   - Policy fields:
     - outbound enabled flag
     - max emails per day
     - approval required for:
       - first contact
       - pricing discussion
       - follow-ups
       - re-engagement
     - website lead deduplication window
     - configured follow-up sequence/workflow reference for website leads
   - Review queue / approval decision persistence for outbound messages
   - Policy decision reason persistence on blocked/gated sends
   - Website lead submission persistence and dedup behavior

2. **Admin configuration APIs**
   - Read/update outbound automation policy from agent/admin control panel backend APIs
   - Tenant admin authorization only
   - Validation with safe defaults and explicit error responses

3. **Enforcement middleware/service**
   - A reusable policy enforcement component invoked before outbound campaign/sequence sends
   - Must:
     - block sends when outbound is disabled
     - block sends when daily max would be exceeded
     - require approval when message category requires it
     - persist structured decision reason
     - surface decision outcome to review queue/audit trail consumers

4. **Review queue APIs**
   - List pending outbound messages requiring approval
   - Get review item detail
   - Approve / reject / edit-before-send
   - Persist final decision with actor and timestamp
   - Enforce authorization for approvers

5. **Website form API**
   - Public or integration-facing validated endpoint for website lead submissions
   - Resolve correct tenant safely
   - Create or update lead based on deduplication window
   - Enroll lead into configured follow-up sequence/workflow within 1 minute
   - Return safe response contract without leaking tenant internals

6. **Auditability**
   - Persist business audit events for:
     - policy configuration changes
     - blocked sends
     - approval requested
     - approval approved/rejected/edited
     - website lead submitted
     - duplicate submission merged
     - sequence enrollment triggered

7. **Tests**
   - Unit tests for policy evaluation logic
   - API/integration tests for policy config, review queue actions, and website lead dedup/enrollment behavior

Do not expand into unrelated UI work unless minimal API contract support requires DTOs already shared by web/mobile.

# Files to touch
Inspect the solution first, then update the most appropriate files. Expected areas:

- `src/VirtualCompany.Domain/**`
  - add policy entities/value objects/enums
  - add outbound message review/decision models
  - add website lead submission/dedup models if not already present

- `src/VirtualCompany.Application/**`
  - commands/queries/handlers for:
    - get/update tenant outbound policy
    - evaluate outbound send policy
    - list/get review queue items
    - approve/reject/edit review items
    - submit website lead
    - trigger sequence/workflow enrollment
  - validation
  - service interfaces for policy enforcement and enrollment dispatch

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations/mappings
  - repositories
  - migrations or migration artifacts consistent with repo conventions
  - background job/outbox integration for sequence enrollment
  - tenant-safe query implementations

- `src/VirtualCompany.Api/**`
  - endpoints/controllers for:
    - outbound policy config
    - review queue
    - website form submission
  - auth policies
  - request/response DTOs if API-local
  - middleware wiring if enforcement is pipeline-based

- `src/VirtualCompany.Shared/**`
  - shared contracts only if already the established pattern

- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests
  - policy enforcement scenarios
  - deduplication/enrollment timing assertions where practical

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
- existing tenancy/auth patterns
- existing approvals/workflows/leads/campaign/sequence models before creating new abstractions

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect current architecture and conventions for:
     - tenant resolution
     - authorization
     - EF Core mappings and migrations
     - command/query handlers
     - approvals/audit events
     - leads/campaigns/sequences/workflows
     - outbox/background jobs
   - Reuse existing modules and naming where possible.
   - If a concept already exists partially, extend it instead of duplicating it.

2. **Design the domain model**
   - Add a tenant-scoped outbound automation policy aggregate/settings model.
   - Suggested fields:
     - `CompanyId`
     - `OutboundEnabled`
     - `MaxEmailsPerDay`
     - `RequireApprovalFirstContact`
     - `RequireApprovalPricingDiscussion`
     - `RequireApprovalFollowUps`
     - `RequireApprovalReEngagement`
     - `WebsiteLeadDeduplicationWindowMinutes`
     - `WebsiteLeadFollowUpWorkflowDefinitionId` or equivalent sequence reference
     - timestamps / updated-by metadata if consistent with codebase
   - Add a structured policy decision model for outbound execution:
     - decision type: allowed / blocked / requires_approval
     - reason code
     - human-readable reason
     - category
     - evaluated limits/context
   - Add review queue entity/model if not already covered by existing approval entities:
     - company id
     - target message/send id
     - status pending/approved/rejected/sent/cancelled
     - original content
     - edited content
     - decision actor
     - decision timestamp
     - rejection reason / comment
   - Prefer extending existing `approvals`, `tasks`, `tool_executions`, `audit_events`, or communication entities if they already fit.

3. **Add persistence and mappings**
   - Create/update EF Core entities and configurations.
   - Add indexes for:
     - `company_id`
     - review queue pending lookups
     - website lead dedup lookups by `company_id + normalized_email + created_at/status`
   - Add migration(s) following repo conventions.
   - Ensure all tenant-owned tables include `company_id`.
   - If leads already exist, add only the minimum fields needed for:
     - source = website_form
     - normalized email
     - active/open state
     - dedup timestamps
     - workflow enrollment tracking

4. **Implement outbound policy configuration application layer**
   - Add query to fetch current tenant policy.
   - Add command to create/update tenant policy.
   - Validate:
     - `MaxEmailsPerDay >= 0`
     - dedup window is within sensible bounds
     - referenced workflow/sequence exists and belongs to tenant
   - Default-deny behavior:
     - if policy missing or ambiguous during execution, block or require approval per existing guardrail conventions
   - Emit audit event on changes with before/after summary.

5. **Expose admin API endpoints**
   - Add endpoints such as:
     - `GET /api/automation/outbound-policy`
     - `PUT /api/automation/outbound-policy`
   - Require tenant admin/owner authorization.
   - Resolve tenant from existing company context mechanism, never from arbitrary body input alone.
   - Return stable DTOs with validation errors.

6. **Implement policy enforcement service**
   - Create a reusable application/domain service, e.g. `IOutboundAutomationPolicyEvaluator`.
   - Inputs should include:
     - company/tenant id
     - message category
     - lead/contact context
     - sequence execution context
     - current day send count
     - whether this is first contact / pricing / follow-up / re-engagement
   - Outputs:
     - allow / block / requires approval
     - structured reason code/message
     - any review item/approval creation requirement
   - Enforce:
     - outbound disabled => block
     - daily max exceeded => block
     - category approval required => create pending review item instead of send
   - Persist decision reason so it is visible in:
     - review queue
     - audit trail
     - linked execution/task/message records
   - Integrate this evaluator into the existing campaign/sequence execution path before send dispatch.
   - Do not let downstream send logic bypass this check.

7. **Integrate with campaign/sequence execution**
   - Find the current outbound execution pipeline for campaigns/sequences/messages.
   - Insert policy evaluation before any actual send side effect.
   - On blocked:
     - mark execution/message as blocked or awaiting review as appropriate
     - persist policy decision reason
     - create audit event
   - On requires approval:
     - create review queue item / approval record
     - prevent send until approved
     - create audit event
   - On approved:
     - continue through normal send path using final approved/edited content
   - Ensure idempotency so retries do not create duplicate review items or duplicate sends.

8. **Implement review queue APIs**
   - Add endpoints such as:
     - `GET /api/review-queue/outbound`
     - `GET /api/review-queue/outbound/{id}`
     - `POST /api/review-queue/outbound/{id}/approve`
     - `POST /api/review-queue/outbound/{id}/reject`
     - `POST /api/review-queue/outbound/{id}/edit-and-approve`
   - Enforce authorized roles only.
   - Persist:
     - final decision
     - actor user id
     - timestamp
     - edited content if applicable
     - optional comment/reason
   - Update linked message/send/execution state accordingly.
   - Emit audit events for each decision.
   - Make sure approved items can be picked up by the send pipeline without race conditions.

9. **Implement website form submission endpoint**
   - Add endpoint such as:
     - `POST /api/website/forms/leads`
   - Validate request:
     - tenant/workspace resolution mechanism
     - email required and normalized
     - name/company/message fields as applicable
     - anti-abuse basics if existing patterns exist
   - Resolve tenant safely:
     - via API key, site token, form configuration id, or existing public-ingestion pattern in repo
     - do not trust raw tenant id from caller unless already secured by signed token pattern
   - On submission:
     - check for duplicate active lead by normalized email within configured dedup window
     - if duplicate found:
       - update existing lead
       - do not create second active lead
       - record audit event for merge/update
     - else:
       - create new lead under correct tenant
       - record source as website form
   - Trigger enrollment into configured follow-up sequence/workflow using outbox/background worker.
   - Ensure enrollment occurs within 1 minute:
     - enqueue immediately in request transaction via outbox
     - background worker processes promptly
   - Return success response that does not reveal whether a duplicate existed unless product conventions allow it.

10. **Implement enrollment dispatch**
   - Reuse existing workflow/sequence start mechanism if present.
   - If none exists, add a minimal internal command/event path:
     - website lead submitted -> outbox event -> background handler -> start configured workflow/sequence
   - Guard against duplicate enrollment for the same submission/update event.
   - Persist linkage between lead and started workflow/sequence instance.

11. **Add audit trail coverage**
   - Create business audit events for:
     - outbound policy updated
     - outbound send blocked by policy
     - outbound send routed to approval
     - review approved
     - review rejected
     - review edited and approved
     - website lead created
     - website lead deduplicated/updated
     - follow-up workflow/sequence enrollment requested/completed/failed
   - Keep rationale concise and operational; no chain-of-thought.

12. **Testing**
   - Unit tests for evaluator:
     - outbound disabled => blocked
     - daily max exceeded => blocked
     - first contact approval required => requires approval
     - pricing discussion approval required => requires approval
     - follow-up approval required => requires approval
     - re-engagement approval required => requires approval
     - valid send under limits => allowed
     - missing policy => conservative deny/gate per implementation choice
   - API/integration tests:
     - admin can get/update policy
     - non-admin forbidden
     - blocked send persists reason
     - approval-required send creates review item
     - approve/reject/edit endpoints persist actor/timestamp
     - website lead submission creates lead for correct tenant
     - duplicate submission within window updates existing lead
     - enrollment event/job is created and processed
   - Run full build/tests and fix any regressions.

13. **Implementation quality constraints**
   - Keep code cohesive and minimal.
   - Prefer extending existing approval/audit/workflow infrastructure.
   - Avoid introducing a new framework or unnecessary abstractions.
   - Keep DTOs/versioning/API naming consistent with existing project style.
   - Ensure all queries are tenant-scoped and authorization-checked.

# Validation steps
1. Restore/build/test baseline:
   - `dotnet build`
   - `dotnet test`

2. After implementation, run:
   - `dotnet build`
   - `dotnet test`

3. Verify migration/setup artifacts are consistent with repo conventions.

4. Manually validate or cover with tests:
   - tenant admin can read/update outbound policy
   - non-admin cannot update policy