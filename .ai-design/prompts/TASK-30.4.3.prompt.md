# Goal
Implement backlog task **TASK-30.4.3 — Add FinanceAgent proposal summary generation constrained to explain-and-propose behavior** for story **US-30.4 Deliver finance inbox review flow and bounded approval proposal experience**.

The coding agent must update the finance inbox / bill review flow so that the **FinanceAgent generates a proposal-style summary from extracted bill data that explains findings and explicitly asks for approval**, while **never initiating payment automatically**.

This task must preserve the product’s conservative autonomy model:
- the finance agent may **explain**
- the finance agent may **propose approval**
- the finance agent may **request clarification**
- the finance agent may **not execute payment/export as part of this flow**
- approval creates only a **proposal or approved-bill record**, not a payment action

# Scope
In scope:
- Finance bill proposal summary generation behavior
- Prompt / orchestration constraints for FinanceAgent bill review summaries
- Domain/application rules ensuring proposal summaries are approval-seeking only
- UI/API behavior needed to surface the proposal summary in the finance inbox bill detail/review flow
- Status transition enforcement relevant to this task:
  - allowed statuses: `Detected`, `Extracted`, `Needs review`, `Proposed for approval`, `Approved`, `Rejected`, `Sent to payment/exported`
- Validation rule enforcement that bills with unresolved validation failures cannot become `Approved`
- Audit/action recording for approve, reject, and request clarification actions with:
  - actor
  - timestamp
  - prior status
  - new status
  - rationale

Out of scope:
- Actual payment execution
- Export/payment connector side effects
- Broad redesign of the finance inbox beyond what is required for this task
- New autonomous finance actions outside explain/propose/review
- Mobile-specific implementation unless existing shared APIs/models require updates

# Files to touch
Inspect the solution first and then touch the minimum necessary files in the relevant layers. Expect changes across these areas:

- **Domain**
  - finance bill aggregate/entity/value objects
  - bill status enum/constants
  - approval/proposal record entities if already present
  - domain rules for invalid approval transitions
- **Application**
  - finance inbox queries / bill detail DTOs
  - command handlers for approve / reject / request clarification
  - finance-agent summary generation service or orchestration handler
  - validators and transition guards
  - audit event creation
- **Infrastructure**
  - EF Core entity configuration / persistence mappings
  - migrations if new fields/tables are required
  - repository/query implementations
- **API**
  - endpoints for bill detail and review actions
  - contracts for proposal summary payloads and action requests/responses
- **Web**
  - FinanceBillInbox list/detail UI
  - bill review panel showing extracted fields, confidence, warnings, evidence refs, and proposal summary
  - action buttons for approve / reject / request clarification
  - disabled state when unresolved validation failures exist
- **Tests**
  - domain tests for status transitions
  - application tests for summary generation constraints
  - API/integration tests for action recording and approval blocking
  - UI/component tests if present in repo conventions

Because the exact file names are unknown, first locate likely finance-related code by searching for:
- `FinanceAgent`
- `Bill`
- `Invoice`
- `Approval`
- `Inbox`
- `AuditEvent`
- `NeedsReview`
- `Approved`
- `Rejected`

# Implementation plan
1. **Discover existing finance bill flow**
   - Find current finance inbox, bill entity/model, statuses, and any existing extraction/review UI.
   - Identify where FinanceAgent summaries are currently generated, if at all.
   - Identify whether there is already a `BillApprovalProposal` concept or equivalent approved-bill record.

2. **Normalize bill statuses to acceptance criteria**
   - Ensure the bill status model supports only the required workflow states for this feature:
     - `Detected`
     - `Extracted`
     - `Needs review`
     - `Proposed for approval`
     - `Approved`
     - `Rejected`
     - `Sent to payment/exported`
   - If current code uses enum/string values, align carefully without breaking persistence.
   - Add mapping/compatibility handling only if necessary.

3. **Add or refine bill review detail model**
   - Ensure bill detail query/view model includes:
     - extracted fields
     - confidence level
     - validation warnings
     - duplicate warnings
     - evidence references for each field
   - If evidence references are per field, expose them in a structured DTO rather than free text.

4. **Implement constrained FinanceAgent proposal summary generation**
   - Add/update a dedicated summary generator for finance bill review.
   - The generated summary must:
     - use extracted bill data
     - explain key bill facts and notable warnings
     - present a recommendation/proposal
     - explicitly ask the user for approval or another review action
     - avoid any wording that implies payment has been or will be executed automatically
   - Hard-code or strongly constrain behavior in application logic/prompt instructions so the output is bounded to explain-and-propose behavior.
   - Prefer structured output if the orchestration layer supports it, e.g.:
     - `headline`
     - `summary`
     - `riskFlags`
     - `approvalAsk`
     - `recommendedAction`
   - Add a final server-side guard so even if model output is imperfect, the persisted/displayed summary remains approval-seeking only.

5. **Prevent automatic payment initiation**
   - Audit the finance approval flow for any side effects triggered by:
     - summary generation
     - proposal creation
     - approval action
   - Ensure approval creates only:
     - a `BillApprovalProposal`, or
     - an approved bill record/state change
   - Ensure no payment/export job, tool execution, integration call, or outbox message is emitted from this v1 approval path unless it is explicitly non-executing metadata/audit.

6. **Implement review actions and audit recording**
   - Support these actions from UI/API:
     - approve
     - reject
     - request clarification
   - For each action, persist an audit/history record containing:
     - actor
     - timestamp
     - prior status
     - new status
     - rationale
   - If an existing audit module exists, use it rather than inventing a parallel mechanism.
   - Ensure tenant scoping is enforced.

7. **Enforce validation-failure approval blocking**
   - Add a domain/application rule that bills with unresolved validation failures cannot transition to `Approved`.
   - Enforce this in:
     - UI: disable/hide approve action with clear reason
     - API/application command handler: reject invalid transition even if called directly
   - Return a safe validation/business error response.

8. **Update finance inbox/detail UI**
   - In the bill detail/review screen, show:
     - extracted fields
     - confidence
     - validation warnings
     - duplicate warnings
     - evidence references
     - FinanceAgent proposal summary
   - Make the proposal clearly framed as:
     - explanation + recommendation
     - explicit approval request
     - no automatic payment
   - Add action controls for approve, reject, request clarification.
   - Show status and action history if already supported nearby.

9. **Add tests**
   - Domain/application tests:
     - valid transitions into `Proposed for approval`
     - approval blocked when unresolved validation failures exist
     - approval does not trigger payment/export side effects
     - summary content includes explicit approval ask
     - summary generation does not contain auto-payment language
   - API/integration tests:
     - approve/reject/request clarification record audit fields correctly
     - approve returns business error when validation failures unresolved
   - UI tests if feasible:
     - approve button disabled when validation failures unresolved
     - proposal summary rendered in bill detail

10. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries.
   - Keep orchestration/prompt logic out of controllers/UI.
   - Persist business audit events separately from technical logs.
   - Use CQRS-lite patterns already present in the codebase.

# Validation steps
Run these checks after implementation:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Manual verification**
   - Open a detected/extracted bill in the finance inbox.
   - Confirm bill detail shows:
     - extracted fields
     - confidence level
     - validation warnings
     - duplicate warnings
     - evidence references per field
   - Confirm FinanceAgent summary:
     - references extracted bill data
     - explains findings
     - proposes approval
     - explicitly asks for approval
     - does not say payment was initiated or will be auto-executed
   - Trigger:
     - approve
     - reject
     - request clarification
   - Confirm each action records actor, timestamp, prior status, new status, rationale.
   - Confirm approving creates only proposal/approved state data and no payment/export execution.
   - Confirm a bill with unresolved validation failures cannot be approved via:
     - UI
     - API

4. **Regression checks**
   - Ensure tenant scoping still applies to finance bill queries/actions.
   - Ensure no unrelated approval flows are broken by status/enum changes.
   - Ensure audit views still render if they consume shared audit event structures.

# Risks and follow-ups
- **Status model drift:** existing bill statuses may not match acceptance criteria exactly; changing enums/strings may require careful migration and compatibility handling.
- **Prompt-only guardrails are insufficient:** do not rely solely on LLM instructions; add deterministic server-side constraints on summary shape/content and side effects.
- **Hidden payment side effects:** existing approval handlers, workflow triggers, or outbox dispatchers may already initiate downstream finance actions; inspect thoroughly.
- **Audit duplication:** if both finance-specific history and shared audit events exist, avoid inconsistent double-writing unless both are intentional.
- **Evidence reference shape:** if extraction evidence is not currently modeled per field, this task may expose a gap that needs a follow-up DTO/domain enhancement.
- **Clarification action semantics:** if no existing status exists for clarification, map it carefully to the current workflow without inventing unsupported statuses unless necessary.
- **Follow-up recommendation:** after this task, consider a dedicated structured `BillApprovalProposal` read model and explicit “proposal generated by agent” audit event if not already present.