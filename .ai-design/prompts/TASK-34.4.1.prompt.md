# Goal
Implement backlog task **TASK-34.4.1** for story **US-34.4** in the existing .NET modular monolith so that sales qualification, follow-up recommendations, approval-aware execution, automation policy enforcement, finance handoff on won deals, workflow triggers, and audit persistence work end-to-end.

The coding agent should deliver a vertical slice across **Domain, Application, Infrastructure, API, background workflows, and Web UI** that satisfies the acceptance criteria using the project’s existing architecture patterns:
- ASP.NET Core modular monolith
- PostgreSQL transactional persistence
- outbox/event-driven internal workflows
- approval-first sensitive actions
- auditability as business persistence, not just logs
- tenant-scoped behavior throughout

# Scope
Implement only what is required for this task, favoring pragmatic MVP behavior aligned to the acceptance criteria.

Include:

1. **Lead qualification service**
   - Persist qualification fields:
     - fit
     - temperature
     - priority
     - suggested next action
   - Expose qualification in API and UI
   - Emit internal/domain event `sales.lead.qualified`
   - Persist audit trail for qualification actions

2. **Follow-up recommendation workflow**
   - Detect these conditions:
     - no-response
     - unanswered proposal
     - hot lead idle
     - stuck deal
   - Create recommendation records with:
     - type/category
     - target lead/deal/thread
     - rationale/summary
     - approval requirement
     - execution state
   - Determine approval requirement from automation policy

3. **Approval + execution flow for recommendations**
   - Support recommendation actions for at least:
     - create draft reply
     - send email
   - Approval of a draft/send recommendation must:
     - create a thread-aware draft reply or
     - send an email through the real provider integration path already used by the app
   - Log activity
   - Persist approval state and execution state
   - Ensure idempotent retries

4. **Automation policy service**
   - Support policy modes:
     - manual only
     - draft only
     - auto-send low-risk follow-ups
   - Finance document creation must always require approval in MVP
   - Policy decisions must be structured and auditable

5. **Won deal finance handoff**
   - Marking a deal as won triggers finance handoff workflow
   - Use the real finance integration adapter path
   - Create a draft quote or invoice linked to the deal
   - Emit `sales.deal.won`
   - Do **not** auto-create finance documents before approval
   - Persist handoff state, approval state, and audit events

6. **Failure handling**
   - Email send failures and finance handoff failures must:
     - be logged
     - be surfaced in UI
     - be retriable
     - avoid duplicate activities or finance documents

7. **Tests**
   - Add/extend unit and integration tests for policy decisions, workflow triggers, approval transitions, idempotent retries, and API behavior

Do not:
- redesign unrelated modules
- introduce microservices
- add speculative generic workflow builders
- replace existing integration abstractions if adapters already exist
- expose chain-of-thought; persist concise rationale summaries only

# Files to touch
Inspect the solution first and then update the most relevant files in these areas. Prefer existing module conventions and naming.

## Likely projects
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`
- `tests/VirtualCompany.Api.Tests`

## Likely file categories to add or modify

### Domain
Add or extend sales/approval/audit/workflow domain models and enums, for example:
- lead qualification entity/value object fields
- recommendation aggregate/entity
- automation policy entity/value object
- finance handoff entity/state
- execution status / approval status enums
- domain events for:
  - `sales.lead.qualified`
  - `sales.deal.won`

If the repo already has sales entities, extend them in place rather than duplicating concepts.

### Application
Add or extend:
- commands/handlers for:
  - qualify lead
  - create recommendation
  - approve recommendation
  - execute recommendation
  - mark deal won
  - approve finance handoff / finance document creation
  - retry failed email send / finance handoff
- queries for API/UI read models
- services:
  - qualification service
  - follow-up recommendation service
  - automation policy evaluator
  - recommendation execution service
  - finance handoff service
  - audit persistence helper if needed
- workflow trigger handlers / scheduled evaluators
- DTOs/view models

### Infrastructure
Add or extend:
- EF Core entity configurations
- repositories
- migrations
- outbox/event dispatch wiring
- provider-backed email send path integration usage
- finance integration adapter usage
- idempotency persistence for retries if not already present
- audit event persistence
- background worker jobs for follow-up detection and retries

### API
Add or extend endpoints/controllers/minimal APIs for:
- lead qualification create/update/get
- recommendation list/detail/approve/retry
- automation policy get/update
- mark deal won / finance handoff status / approve / retry
- ensure tenant scoping and authorization

### Web
Add or extend Blazor pages/components for:
- lead qualification display/edit
- recommendation inbox/list/detail
- approval actions
- failure/retry visibility
- finance handoff status on won deals

### Tests
Add or extend:
- application service tests
- API tests
- workflow/background processing tests where feasible
- idempotency/retry tests

Also update any relevant docs or seed/config files if required by the implementation.

# Implementation plan
1. **Inspect the existing codebase before coding**
   - Find current sales, lead, deal, email, finance, approval, workflow, audit, and integration abstractions.
   - Reuse existing patterns for:
     - commands/queries
     - domain events
     - outbox
     - EF Core mappings
     - API endpoint style
     - Blazor page structure
   - Identify whether lead/deal/thread/activity entities already exist and extend them instead of creating parallel models.

2. **Model the domain changes**
   - Add qualification data to the lead domain model:
     - `Fit`
     - `Temperature`
     - `Priority`
     - `SuggestedNextAction`
     - timestamps / actor metadata if consistent with current patterns
   - Add recommendation model with fields such as:
     - id
     - company/tenant id
     - lead id and/or deal id
     - conversation/thread reference
     - recommendation type
     - trigger condition
     - risk level
     - rationale summary
     - approval requirement
     - approval id nullable
     - execution status
     - execution attempt metadata
     - dedupe/idempotency key
   - Add automation policy model/config for sales follow-ups:
     - manual only
     - draft only
     - auto-send low-risk follow-ups
     - finance-documents-always-require-approval = true in MVP
   - Add finance handoff model/state linked to deal:
     - pending approval
     - approved
     - draft created
     - failed
     - completed
   - Add domain events for lead qualified and deal won.

3. **Persist schema changes**
   - Add EF Core mappings and migration(s) for:
     - qualification fields on leads or related table
     - recommendations table
     - automation policies table or JSON-backed config table, depending on existing conventions
     - finance handoff / finance document request table
     - activity/audit linkage fields if needed
     - failure/retry metadata
   - Ensure all tenant-owned tables include `company_id`.
   - Add indexes for common lookups:
     - by company + lead/deal
     - by status
     - by approval status
     - by retryable failure state
     - by dedupe key

4. **Implement automation policy evaluation**
   - Create a dedicated application/domain service that takes:
     - recommendation type
     - trigger condition
     - risk level
     - target entity type
     - action type (draft/send/finance-create)
   - Return a structured decision:
     - allowed action
     - requires approval
     - execution mode
     - policy reason
     - policy snapshot/metadata for audit
   - Enforce:
     - manual only => recommendation created, no auto execution
     - draft only => draft recommendation allowed, send requires approval/manual action
     - auto-send low-risk => low-risk follow-up send may auto-execute if policy allows
     - finance document creation => always approval required in MVP

5. **Implement lead qualification flow**
   - Add command/handler to qualify a lead.
   - Persist qualification values.
   - Emit `sales.lead.qualified` through the existing event/outbox mechanism.
   - Create business audit event with concise rationale summary.
   - Add query/API/UI support to read qualification data.

6. **Implement follow-up detection workflow**
   - Add a background worker or workflow step that evaluates leads/deals/messages for:
     - no-response
     - unanswered proposal
     - hot lead idle
     - stuck deal
   - Use existing communication/thread/activity data where available.
   - If exact data is missing, implement the minimum deterministic logic using existing timestamps/statuses rather than inventing AI-only heuristics.
   - Create recommendation records idempotently:
     - one active recommendation per condition/target/window unless existing one is resolved/cancelled
   - Evaluate automation policy at creation time and persist the decision.

7. **Implement recommendation approval and execution**
   - Add approval creation when policy requires it.
   - On approval:
     - if recommendation action is draft, create a thread-aware draft reply
     - if recommendation action is send, send through the real email provider integration path
   - Persist:
     - approval state
     - execution state
     - provider/external ids
     - activity log entry
     - audit event
   - Ensure thread awareness by linking to the existing conversation/thread/message identifiers used by the communication module.
   - If execution is retried, use idempotency keys so duplicate drafts/emails/activities are not created.

8. **Implement won deal finance handoff**
   - Extend mark-deal-won flow to emit `sales.deal.won`.
   - Trigger finance handoff workflow from that event or command path using existing workflow conventions.
   - Create a finance handoff request linked to the deal.
   - Require approval before creating quote/invoice draft in MVP.
   - On approval, call the real finance integration adapter path to create a draft quote/invoice.
   - Persist:
     - handoff state
     - external finance document id
     - approval linkage
     - audit/activity records
   - Prevent duplicate finance documents on retries by using a stable idempotency key per deal + handoff action.

9. **Implement failure handling and retries**
   - For email send and finance handoff failures:
     - capture structured failure details in business state
     - log technical details through existing logging
     - create user-visible status/error summary
     - mark as retryable when appropriate
   - Add retry commands/endpoints/actions.
   - Retries must:
     - reuse idempotency keys
     - not duplicate activities
     - not duplicate finance documents
     - transition state cleanly from failed -> retrying -> completed/failed

10. **Expose API endpoints**
   - Add tenant-scoped endpoints for:
     - get/update lead qualification
     - list/get recommendations
     - approve recommendation
     - execute/retry recommendation
     - get/update automation policy
     - mark deal won
     - get finance handoff status
     - approve/retry finance handoff
   - Return safe, concise error payloads.
   - Enforce authorization using existing policy-based patterns.

11. **Add/update Blazor UI**
   - Surface qualification in lead detail/list UI as appropriate.
   - Add recommendation inbox/detail UI showing:
     - trigger condition
     - rationale
     - required approval
     - execution state
     - failure state
     - retry action
   - Add finance handoff status to won deal UI.
   - Show approval-required state clearly and prevent accidental auto-creation of finance docs.

12. **Persist audit events**
   - For all important actions, create business audit events with:
     - actor
     - action
     - target
     - outcome
     - rationale summary
     - policy decision snapshot
     - linked approval/tool execution/data source refs where available
   - Cover at least:
     - lead qualified
     - recommendation created
     - recommendation approved/rejected
     - draft created
     - email sent / failed
     - deal won
     - finance handoff requested
     - finance draft created / failed
     - retry attempted

13. **Add tests**
   - Unit tests:
     - automation policy decisions for all modes
     - finance always requires approval
     - recommendation dedupe behavior
     - retry idempotency behavior
   - Application/integration tests:
     - qualifying a lead persists fields and emits event
     - follow-up detection creates expected recommendations
     - approval transitions execute draft/send correctly
     - mark deal won creates finance handoff request but not finance doc before approval
     - failure + retry does not duplicate activity or finance docs
   - API tests:
     - qualification endpoints
     - recommendation approval/retry endpoints
     - finance handoff endpoints
     - tenant isolation

14. **Keep implementation aligned with MVP**
   - Prefer deterministic business rules over speculative AI logic.
   - Use concise rationale summaries.
   - Reuse existing provider adapters and workflow infrastructure.
   - Avoid introducing broad abstractions unless needed by current code patterns.

# Validation steps
Run these after implementation and fix issues before finishing.

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Migration sanity**
   - Ensure new EF Core migrations compile and apply cleanly in the project’s normal migration flow.
   - Verify schema includes tenant-scoped tables/columns and indexes.

4. **Acceptance criteria verification**
   - Confirm lead qualification:
     - stores fit, temperature, priority, suggested next action
     - is visible in API and UI