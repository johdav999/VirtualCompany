# Goal
Implement backlog task **TASK-35.3.2** for story **US-35.3 Add automation policies, approval controls, and website lead capture entrypoints** by delivering the end-to-end web and API functionality for:

- tenant-level automation policy configuration in the agent control panel
- review queue UI for approval-required outbound messages
- policy enforcement during sequence execution with persisted decision reasons
- production website lead capture API endpoint with tenant-safe lead creation/enrollment
- duplicate submission handling within a configurable deduplication window
- auditability for policy decisions and approval actions

The implementation must fit the existing **.NET modular monolith** architecture, preserve **tenant isolation**, use **CQRS-lite** patterns where already present, and persist business-significant actions in domain tables rather than relying only on technical logs.

# Scope
Include only the work necessary to satisfy the acceptance criteria for this task.

## Functional scope
1. **Agent control panel settings**
   - Add tenant-admin-configurable settings for:
     - outbound enabled flag
     - max emails per day
     - approval required for:
       - first contact
       - pricing discussion
       - follow-ups
       - re-engagement
   - Expose these settings in the Blazor web app control panel.
   - Persist settings in a tenant-owned configuration model.

2. **Policy enforcement in outbound/sequence execution**
   - Update sequence or outbound send execution flow so policy is checked before send.
   - Block sends that violate tenant policy.
   - Persist a structured policy decision reason.
   - Make the reason visible in:
     - review queue
     - audit trail
     - any linked outbound/review entity detail if applicable

3. **Review queue**
   - Build a web UI for authorized users to see messages requiring approval.
   - Support actions:
     - approve
     - reject
     - edit before send
   - Persist final decision with:
     - actor
     - timestamp
     - final content if edited
     - decision/comment if supported by current patterns

4. **Website form API**
   - Add a production-ready API endpoint for website lead submissions.
   - Validate payload.
   - Resolve correct tenant safely.
   - Create or update lead.
   - Enroll lead into configured follow-up sequence within 1 minute.
   - Handle duplicate submissions for same email within configured deduplication window by updating existing active lead instead of creating a second active lead.

## Non-functional scope
- Tenant-scoped authorization and data access.
- Idempotent-ish duplicate handling for website submissions.
- Audit events for policy decisions and approval actions.
- Background processing or outbox/job scheduling if needed for sequence enrollment SLA.
- Tests covering core policy, approval, and deduplication behavior.

## Out of scope
- Mobile UI changes unless absolutely required by shared contracts.
- Full workflow builder or generalized policy engine redesign beyond what this task needs.
- New external integrations beyond the website form endpoint.
- Broad redesign of existing approval/task systems if a targeted extension is sufficient.

# Files to touch
Inspect the solution first and adjust to actual structure, but expect to touch files in these areas.

## Domain
- `src/VirtualCompany.Domain/**`
  - tenant/company settings entities or value objects
  - approval/review queue entities
  - outbound/sequence policy decision models
  - lead entity and deduplication-related domain logic
  - audit event models if extension is needed

## Application
- `src/VirtualCompany.Application/**`
  - commands/handlers for:
    - updating automation policy settings
    - listing review queue items
    - approving/rejecting/editing approval-required messages
    - processing website lead submissions
    - enrolling leads into follow-up sequence
  - queries/view models for:
    - agent control panel settings
    - review queue list/detail
    - audit trail detail with policy reason
  - validators
  - authorization policies/requirements if implemented here
  - service interfaces for policy evaluation and deduplication

## Infrastructure
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories
  - migrations support
  - background job/outbox dispatcher updates
  - concrete implementations for:
    - policy evaluation persistence
    - website form processing
    - sequence enrollment scheduling
    - deduplication lookup
  - audit persistence wiring

## API
- `src/VirtualCompany.Api/**`
  - controllers or minimal API endpoints for:
    - tenant automation settings
    - review queue actions
    - website form submission
  - request/response contracts
  - auth/authorization wiring
  - tenant resolution for public website endpoint
  - rate limiting / anti-abuse hooks if already present

## Web
- `src/VirtualCompany.Web/**`
  - Blazor pages/components for:
    - agent control panel automation settings
    - review queue list
    - review queue detail/action modal or page
    - policy decision reason display
  - client service calls
  - form validation and role-based action visibility

## Tests
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests
  - authorization tests
  - duplicate submission tests
  - approval action tests
  - policy block behavior tests

## Documentation / migrations
- migration files in the project’s actual migrations location
- possibly `README.md` or feature docs if the repo documents API endpoints or setup

# Implementation plan
1. **Discover current implementation and map existing modules**
   - Find existing models and flows for:
     - company/tenant settings
     - agents and control panel UI
     - approvals
     - outbound messaging / sequences
     - leads / CRM-like entities
     - audit events
   - Reuse existing patterns instead of inventing parallel abstractions.
   - Identify whether website lead capture belongs under Integration, Communication, Workflow, or a sales/lead module already present.

2. **Design the minimum domain additions**
   - Add or extend a tenant-scoped automation policy aggregate/config object with fields for:
     - `OutboundEnabled`
     - `MaxEmailsPerDay`
     - `RequireApprovalFirstContact`
     - `RequireApprovalPricingDiscussion`
     - `RequireApprovalFollowUps`
     - `RequireApprovalReengagement`
     - `WebsiteLeadDeduplicationWindow`
     - configured follow-up sequence reference for website leads if not already modeled
   - Add a structured policy decision model for outbound execution, including:
     - decision result (`allowed`, `blocked`, `requires_approval`)
     - reason code
     - reason text
     - policy snapshot/context JSON if existing conventions support it
   - Extend approval/review entities to store:
     - original message content
     - edited final content
     - decision actor
     - decision timestamp
     - decision status
   - If lead entities already exist, add only what is needed for deduplication and active lead detection.

3. **Persist schema changes**
   - Add EF/entity configuration updates and create a migration.
   - Ensure all new tenant-owned tables/columns include `company_id` or equivalent tenant key.
   - Add indexes for:
     - review queue pending lookups
     - lead lookup by tenant + normalized email + active status
     - deduplication window queries
     - sequence enrollment job lookup if needed

4. **Implement application commands and queries**
   - Add command/query handlers for:
     - get/update automation settings
     - list review queue items
     - get review queue item detail
     - approve message
     - reject message
     - edit-and-approve message
     - submit website lead
   - Add validation rules:
     - max emails per day must be non-negative and within sane bounds
     - website lead payload required fields
     - normalized email validation
     - approval actions only on pending items
   - Enforce role checks so only tenant admins can update settings and only authorized approvers can act on review items.

5. **Implement policy enforcement in sequence execution**
   - Locate the outbound send/sequence execution path.
   - Before send, evaluate tenant policy:
     - if outbound disabled => block
     - if daily max exceeded => block
     - if message category requires approval => create/maintain review item and prevent direct send
   - Persist the policy decision reason on the execution/review/audit records.
   - Ensure blocked sends do not proceed to downstream send providers.
   - If there is already a tool execution or workflow execution record, attach policy decision metadata there rather than duplicating unnecessarily.

6. **Build review queue backend**
   - Add query endpoints for pending review items with enough metadata for UI:
     - lead/contact
     - tenant
     - message type/category
     - original content
     - policy reason
     - created timestamp
     - requesting actor/agent if applicable
   - Add action endpoints:
     - approve
     - reject
     - edit then approve
   - Persist final decision with actor and timestamp.
   - On approval, resume or enqueue send through the existing outbound pipeline.
   - On rejection, mark item final and ensure no send occurs.

7. **Build Blazor UI**
   - Extend the agent control panel with an automation settings section/form.
   - Add a review queue page with:
     - pending items list
     - filters if easy within current patterns
     - detail view
     - approve/reject/edit actions
   - Show policy decision reason clearly in the queue and any linked audit/history view.
   - Respect role-based visibility and disable unauthorized actions.

8. **Implement website form API**
   - Add a public-facing endpoint such as `/api/website/leads` or align with existing route conventions.
   - Support tenant resolution via secure mechanism already used or introduce one, such as:
     - tenant-specific API key
     - tenant slug/form token
     - signed form identifier
   - Do not trust arbitrary tenant IDs from the caller without verification.
   - Validate payload and normalize email.
   - Within a transaction:
     - check for existing active lead with same normalized email inside deduplication window
     - update existing lead if found
     - otherwise create a new lead
   - Trigger follow-up sequence enrollment immediately or via outbox/background job with SLA under 1 minute.
   - Return safe API responses without leaking tenant internals.

9. **Implement deduplication behavior**
   - Define “same email within deduplication window” precisely in code.
   - Use normalized email comparison.
   - Update existing active lead fields according to a deterministic merge rule:
     - latest submission metadata
     - append or overwrite source details based on current domain conventions
   - Prevent creation of a second active lead in the dedupe case.
   - Add tests for repeated submissions just inside and outside the configured window.

10. **Audit trail integration**
   - Record audit events for:
     - automation settings changes
     - policy-blocked sends
     - approval requested
     - approval approved/rejected/edited
     - website lead created/updated
     - sequence enrollment triggered
   - Ensure audit detail includes concise rationale/policy reason and actor where applicable.

11. **Testing**
   - Add unit/integration/API tests for:
     - admin can update automation settings
     - non-admin cannot update settings
     - outbound disabled blocks send and records reason
     - daily limit exceeded blocks send and records reason
     - approval-required message appears in review queue
     - approve/reject/edit actions persist actor/timestamp/final content
     - website lead submission creates lead under correct tenant
     - duplicate submission updates existing lead within window
     - outside dedupe window creates a new lead if that is the intended domain behavior
     - follow-up sequence enrollment is scheduled/executed within expected flow

12. **Keep implementation aligned with existing architecture**
   - Use modular monolith boundaries.
   - Keep business logic in Application/Domain, not Blazor pages or controllers.
   - Use outbox/background workers for reliable side effects where existing patterns support it.
   - Preserve tenant isolation in every query and mutation.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run the full automated test suite:
   - `dotnet test`

3. If targeted tests are easier during development, run relevant test projects first, then full suite:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

4. Manually verify web UI flows in the Blazor app:
   - tenant admin can open agent control panel
   - update outbound enabled, max emails/day, and approval toggles
   - save and reload shows persisted values
   - unauthorized user cannot edit settings

5. Manually verify policy enforcement:
   - configure outbound disabled
   - trigger/send a sequence message
   - confirm send is blocked
   - confirm policy reason appears in review/audit surfaces as applicable

6. Manually verify review queue:
   - trigger a message category requiring approval
   - confirm item appears in queue
   - approve it and verify actor/timestamp persisted
   - reject another and verify no send occurs
   - edit-and-approve another and verify final edited content is what gets sent/persisted

7. Manually verify website lead endpoint:
   - submit valid payload for tenant A
   - confirm lead created under tenant A only
   - confirm follow-up sequence enrollment occurs within 1 minute
   - resubmit same email within dedupe window and confirm existing active lead is updated, not duplicated

8. Verify migration health:
   - apply migrations in local/dev environment
   - confirm schema/indexes created as expected
   - ensure no tenant isolation regressions in queries

# Risks and follow-ups
- **Unknown existing domain shape**: leads, sequences, approvals, or audit models may already exist under different names. Reuse them rather than creating duplicates.
- **Public endpoint tenant resolution risk**: do not accept raw tenant IDs without authentication/verification. Use a secure tenant/form token pattern.
- **Race conditions on duplicate submissions**: concurrent submissions for the same email may create duplicates unless protected by transaction strategy, locking, or a suitable unique/indexed constraint pattern.
- **Daily outbound limit semantics**: clarify whether the limit applies per tenant, per agent, per sequence, or per channel. Acceptance criteria imply tenant-level email cap; implement that unless existing domain says otherwise.
- **Approval resume flow**: approving a queued message must not double-send if background workers retry. Use idempotency/correlation IDs where possible