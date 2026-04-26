# Goal
Implement backlog task **TASK-30.4.1** for story **US-30.4 Deliver finance inbox review flow and bounded approval proposal experience** by adding a tenant-scoped **FinanceBillInbox** list and detail experience across the .NET modular monolith.

Deliver:
- A finance bill inbox list with only the allowed statuses:
  - `Detected`
  - `Extracted`
  - `Needs review`
  - `Proposed for approval`
  - `Approved`
  - `Rejected`
  - `Sent to payment/exported`
- A bill detail view showing:
  - extracted fields
  - confidence level
  - validation warnings
  - duplicate warnings
  - evidence references per field
- A finance agent proposal summary that:
  - uses extracted bill data
  - explicitly asks for approval
  - does **not** initiate payment
- Approve / reject / request clarification actions with full audit trail:
  - actor
  - timestamp
  - prior status
  - new status
  - rationale
- Guardrails so unresolved validation failures cannot transition to `Approved` via UI or API
- Approval action creates a `BillApprovalProposal` or approved bill record only, with **no payment execution**

Use existing architecture conventions:
- modular monolith
- CQRS-lite
- tenant-scoped application services
- PostgreSQL persistence
- Blazor Web App for UI
- auditability as a business feature

# Scope
Implement only what is required for this task, favoring the smallest coherent vertical slice.

In scope:
- Domain model additions for finance bill inbox/review state
- Persistence mappings and migration(s)
- Application commands/queries for:
  - inbox list
  - bill detail
  - approve
  - reject
  - request clarification
- Validation rule preventing approval when unresolved validation failures exist
- Audit/event recording for review actions
- Blazor Web UI pages/components for list and detail views
- Proposal summary rendering in detail view
- API endpoints only if the current app pattern exposes application functionality through API controllers used by web/mobile; otherwise follow existing project conventions

Out of scope:
- Actual payment execution
- Export execution
- Full mobile implementation
- OCR/extraction pipeline creation if not already present
- New orchestration engine behavior beyond presenting stored/existing proposal summary data
- Broad redesign of approval module
- Generic inbox framework unless already present and easy to reuse

Assume this task may require introducing finance-bill-specific entities if they do not yet exist. Reuse existing approval/audit abstractions where practical, but do not over-generalize.

# Files to touch
Inspect the solution first and then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Web`
- `src/VirtualCompany.Api` if HTTP endpoints are part of the current pattern
- `tests/VirtualCompany.Api.Tests` and/or other existing test projects

Likely file categories to add or modify:

## Domain
- Finance bill aggregate/entity/value objects, e.g.:
  - `FinanceBillInboxItem` / `FinanceBill`
  - `FinanceBillStatus`
  - extracted field/evidence models
  - validation warning / duplicate warning models
  - review action history model
  - `BillApprovalProposal` if not already modeled
- Domain rules/methods for status transitions
- Guard method preventing approval with unresolved validation failures

## Application
- Queries:
  - `GetFinanceBillInboxQuery`
  - `GetFinanceBillDetailQuery`
- Commands:
  - `ApproveFinanceBillCommand`
  - `RejectFinanceBillCommand`
  - `RequestFinanceBillClarificationCommand`
- DTO/view models for:
  - inbox rows
  - detail page
  - extracted fields with evidence references
  - proposal summary
  - action history
- Validators
- Handlers
- Authorization/tenant scoping hooks
- Audit recording integration

## Infrastructure
- EF Core entity configurations
- DbContext updates
- repository/query implementations
- migration(s) for new tables/columns
- audit persistence wiring
- seed/test data support if applicable

## Web
- Finance inbox list page
- Finance bill detail page
- components for:
  - status badge
  - warnings panel
  - evidence display
  - proposal summary card
  - action form/buttons
  - action history timeline
- navigation/menu entry if appropriate
- disabled approve UI state when validation failures unresolved

## API
- Controller endpoints for list/detail/actions if the app uses API-first access from web/mobile

## Tests
- Domain tests for status transition rules
- Application tests for command/query behavior
- API/UI integration tests where patterns already exist

# Implementation plan
1. **Discover existing finance/approval/audit patterns before coding**
   - Search for existing modules/entities related to:
     - bills
     - invoices
     - approvals
     - audit events
     - inboxes
     - warnings/validation
   - Follow naming and layering conventions already used in the repo.
   - Prefer extending an existing finance bill model over creating a parallel one.

2. **Model the finance bill review domain**
   - Add or extend a finance bill entity with:
     - `CompanyId`
     - current status
     - supplier/vendor info if already available
     - bill number/reference
     - bill date/due date/amount/currency
     - extracted data payload
     - confidence score/level
     - proposal summary text
     - unresolved validation indicator(s)
     - duplicate warning indicator(s)
   - Add a strict status enum/value object limited to the acceptance criteria values.
   - Add child models for:
     - extracted fields
     - field-level evidence references
     - validation warnings
     - duplicate warnings
     - review action history
   - Add domain behavior for:
     - approve
     - reject
     - request clarification
     - transition validation
   - Enforce:
     - cannot approve if unresolved validation failures exist
     - approval creates proposal/approved record only
     - no payment side effects

3. **Persist the review data**
   - Update EF Core mappings and DbContext.
   - Add migration(s) for any new tables/columns, likely including:
     - finance bills
     - bill extracted fields/evidence references if normalized
     - bill review actions/history
     - bill approval proposal if separate
   - If the codebase prefers JSONB for flexible extracted data/warnings/evidence, use that where appropriate rather than over-normalizing.
   - Ensure all tenant-owned records include `company_id`.

4. **Implement inbox list query**
   - Create a query returning tenant-scoped finance bills for the inbox.
   - Include:
     - id
     - supplier/vendor
     - bill number/reference
     - amount/currency
     - detected/received date
     - current status
     - confidence summary
     - warning counts/flags
   - Restrict results to the allowed statuses only.
   - Add sorting optimized for review workflow, e.g. newest first or warning-first if consistent with existing UX patterns.

5. **Implement bill detail query**
   - Return a detail DTO containing:
     - core bill metadata
     - extracted fields
     - confidence level
     - validation warnings
     - duplicate warnings
     - evidence references for each field
     - proposal summary text
     - action history
     - whether approval is currently allowed
     - reason approval is blocked if unresolved validation failures exist
   - Evidence references should be human-readable and tied to each extracted field, e.g. page/region/source snippet if available in current model.

6. **Implement review actions**
   - Add commands for:
     - approve
     - reject
     - request clarification
   - Each command must:
     - resolve tenant scope
     - load current bill
     - validate allowed transition
     - record actor, timestamp, prior status, new status, rationale
     - persist action history
     - create/update proposal/approved record as required
   - Approval behavior:
     - if unresolved validation failures exist, fail with a business validation error
     - create `BillApprovalProposal` or approved bill record only
     - do not enqueue or invoke payment/export execution
   - Clarification behavior:
     - move to an appropriate review state per current domain design, likely `Needs review` unless a more specific clarification state already exists and does not violate the acceptance criteria

7. **Record audit events**
   - Reuse the audit module if present.
   - For each review action, create a business audit event with:
     - actor type/id
     - action
     - target type/id
     - outcome
     - rationale summary
     - timestamp
     - source references if appropriate
   - Keep explanations concise and operational.

8. **Build the Blazor inbox list view**
   - Add a page for the finance inbox.
   - Render a table/list with:
     - bill identifier
     - vendor
     - amount
     - status badge
     - confidence
     - warning indicators
     - link to detail
   - Add empty/loading/error states.
   - Keep SSR-first and use interactivity only where needed.

9. **Build the Blazor detail view**
   - Add a detail page showing:
     - bill summary header
     - extracted fields section
     - confidence display
     - validation warnings panel
     - duplicate warnings panel
     - evidence references per field
     - finance agent proposal summary card explicitly asking for approval
     - action history timeline/table
   - Add action controls:
     - approve
     - reject
     - request clarification
     - rationale/comment input
   - Disable/hide approve action when unresolved validation failures exist and show the reason clearly.

10. **Wire API endpoints if needed**
   - If the web app calls backend APIs rather than application services directly, add tenant-scoped endpoints for:
     - inbox list
     - detail
     - approve
     - reject
     - request clarification
   - Return safe business validation responses for blocked approval attempts.

11. **Add tests**
   - Domain tests:
     - valid status transitions
     - approval blocked by unresolved validation failures
   - Application tests:
     - inbox query returns allowed statuses only
     - detail query includes warnings/evidence/proposal data
     - action commands record history correctly
     - approval creates proposal/approved record only and no payment side effect
   - API tests if applicable:
     - blocked approval via API returns validation error
   - UI/component tests if the repo already uses them; otherwise keep to application/integration coverage.

12. **Keep implementation bounded and consistent**
   - Do not introduce payment execution hooks.
   - Do not expose raw chain-of-thought.
   - Keep proposal summary as a user-facing recommendation that explicitly requests approval.
   - Preserve tenant isolation in all queries and commands.

# Validation steps
1. Inspect existing architecture and confirm build/test patterns:
   - `dotnet build`
   - `dotnet test`

2. Verify migration compiles and applies cleanly if migrations are part of the repo workflow.

3. Manually validate the inbox UI:
   - finance bills appear in list
   - statuses are only from the accepted set
   - warnings/confidence are visible
   - navigation to detail works

4. Manually validate detail UI:
   - extracted fields render
   - confidence level renders
   - validation warnings render
   - duplicate warnings render
   - evidence references render per field
   - proposal summary explicitly asks for approval
   - no payment action is triggered or implied

5. Validate action behavior:
   - approve records actor/timestamp/prior status/new status/rationale
   - reject records actor/timestamp/prior status/new status/rationale
   - request clarification records actor/timestamp/prior status/new status/rationale

6. Validate approval guardrail:
   - with unresolved validation failures, approve is disabled in UI
   - forced API/command attempt to approve fails with business validation error
   - status remains unchanged

7. Validate persistence/audit:
   - action history is visible after refresh
   - audit event exists for each action
   - approval creates only proposal/approved record and no payment execution artifact

8. Run full regression build/tests:
   - `dotnet build`
   - `dotnet test`

# Risks and follow-ups
- **Existing finance model mismatch:** The repo may already have invoice/bill concepts under different names. Reuse them instead of duplicating, even if that means adapting this plan.
- **Status naming consistency:** Acceptance criteria use human-readable statuses. If the codebase uses enum-safe names, map them cleanly in UI/API without changing the business meaning.
- **Evidence storage shape:** Evidence may already exist as OCR/extraction metadata. Prefer surfacing existing references rather than inventing a new format.
- **Clarification state ambiguity:** Acceptance criteria require the action but do not define a dedicated status. If no clarification status is allowed, record the action and transition to `Needs review`.
- **Approval module overlap:** There may already be a generic approval system. Use it where possible, but ensure this task still creates only a proposal/approved record and never payment execution.
- **Audit duplication:** Avoid writing both action history and audit events in inconsistent ways; define one source of truth and project the other if possible.
- **UI/API authorization:** Ensure only appropriate finance/approver roles can act, following existing authorization patterns.
- **Follow-up candidates after this task:**
  - richer filtering/sorting in finance inbox
  - document/image preview for evidence references
  - clarification conversation thread
  - exported/payment handoff workflow in a later version
  - mobile companion support for finance bill review