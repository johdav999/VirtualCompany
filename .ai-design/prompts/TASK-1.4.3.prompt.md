# Goal
Implement backlog task **TASK-1.4.3 — Persist message delivery records and policy decision outcomes** for story **US-1.4 ST-A204 — Proactive messaging with guardrailed autonomy**.

Deliver a vertical slice in the existing .NET modular monolith that:

- creates a **persisted proactive message delivery record** when a proactive task, alert, or escalation results in an allowed user-facing message/briefing
- evaluates **autonomy level and policy guardrails before delivery**
- persists **policy decision outcomes** for both allowed and blocked delivery attempts
- returns a **policy decision reason** when delivery is blocked
- includes **integration tests** covering at least **two distinct autonomy levels** with both allowed and blocked outcomes

Keep the implementation aligned with the architecture:
- ASP.NET Core modular monolith
- PostgreSQL-backed persistence
- CQRS-lite application layer
- policy enforcement before side effects
- auditability as business persistence, not just logs

# Scope
In scope:

- Add/extend domain model(s) for proactive message delivery records and policy decision outcomes
- Persist delivered proactive messages with:
  - `channel`
  - `recipient`
  - `subject` or `title`
  - `body`
  - `sourceEntityId`
  - `sentAt`
- Link the message to the originating proactive entity (task, alert, escalation, or equivalent source abstraction already present in code)
- Enforce policy/autonomy checks before delivery
- Persist structured policy decision metadata for both:
  - allowed deliveries
  - blocked deliveries
- Expose or return blocked decision reason from the application/API path used to create/send proactive messages
- Add integration tests proving:
  - allowed action is delivered and persisted
  - disallowed action is blocked and not delivered
  - behavior differs across at least two autonomy levels

Out of scope unless required by existing code structure:

- real external email/SMS/push delivery providers
- UI work in Blazor/MAUI
- broad notification center redesign
- unrelated audit/explainability screens
- introducing a message broker

If the repo already has adjacent concepts like notifications/messages/outbox/audit/tool execution/policy engine, reuse them instead of creating parallel abstractions.

# Files to touch
Inspect the solution first and then modify only the minimum necessary files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - proactive messaging/message delivery entity or value objects
  - policy decision entity/value object
  - enums for channel, delivery status, decision outcome, source entity type
- `src/VirtualCompany.Application/**`
  - command + handler for creating/sending proactive messages
  - DTO/result contract returning delivery outcome and policy reason
  - policy evaluation orchestration
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repository implementations
  - migrations or persistence mapping updates
- `src/VirtualCompany.Api/**`
  - endpoint/controller if this flow is API-driven
  - request/response contracts if needed
- `tests/VirtualCompany.Api.Tests/**`
  - integration tests for allowed/blocked delivery across two autonomy levels

Also inspect for existing relevant files before coding:

- DbContext and entity configurations
- existing `messages`, `notifications`, `audit_events`, `tool_executions`, or policy-related models
- existing migration approach referenced by `docs/postgresql-migrations-archive/README.md`
- any current proactive task/alert/escalation workflow code

Prefer extending existing tables/entities if they already fit the acceptance criteria. If not, add a focused new persistence model.

# Implementation plan
1. **Discover existing architecture and reuse points**
   - Find current implementations for:
     - tasks / alerts / escalations
     - messages / notifications
     - policy guardrails / autonomy levels
     - audit or tool execution decision persistence
   - Determine whether proactive delivery should extend:
     - existing `messages`
     - existing notification records
     - or a new `proactive_message_deliveries` table/entity
   - Determine how tenant/company scoping is enforced and follow that pattern.

2. **Design the persistence model**
   Implement a persistence shape that supports retrieval and auditability. Minimum required fields:

   - id
   - company/tenant id
   - source entity type
   - source entity id
   - channel
   - recipient
   - subject/title
   - body
   - sentAt
   - delivery status
   - policy decision outcome
   - policy decision reason
   - createdAt

   Preferred approach:
   - one persisted delivery record for actual delivered messages
   - one persisted policy decision record for every attempted delivery, including blocked attempts

   If simpler and consistent with the codebase, a single table can store both delivery and decision metadata, as long as:
   - blocked attempts are persisted with reason
   - delivered attempts include all required delivery fields
   - blocked attempts are clearly marked as not delivered

3. **Model policy decision outcome**
   Add a structured policy decision abstraction, e.g.:
   - `Allowed`
   - `Blocked`
   - optional future-friendly values like `RequiresApproval`

   Include:
   - evaluated autonomy level
   - reason / code
   - optional metadata JSON if the codebase already uses structured policy payloads

   Default-deny if policy config is missing or ambiguous, consistent with backlog guidance.

4. **Implement application flow**
   Create or extend an application command/service for proactive message creation, e.g.:
   - input:
     - source entity id/type
     - agent id or actor context
     - channel
     - recipient
     - subject/title
     - body
   - flow:
     1. load source entity and tenant context
     2. resolve agent/autonomy level/policy context
     3. evaluate policy before delivery
     4. persist policy decision outcome
     5. if blocked:
        - do not create/send delivery
        - return blocked result with reason
     6. if allowed:
        - create persisted delivery record with `sentAt`
        - optionally enqueue outbox event if existing architecture expects dispatch
        - return success result

5. **Integrate with existing proactive source creation path**
   Acceptance criteria say: “When a proactive task, alert, or escalation is created, the system can generate a user-facing message or briefing linked to the originating entity.”

   Ensure the implementation supports linkage from originating entity creation flow. This can be done by:
   - invoking the proactive message command from the existing task/alert/escalation creation path, or
   - exposing a dedicated application service used by those flows

   Do not hardcode only one source type if the domain already has multiple proactive source entities. Use a source entity abstraction or enum.

6. **Persistence and EF Core mapping**
   - Add entity configurations
   - Add DbSet(s)
   - Add indexes likely needed for retrieval:
     - by company id
     - by source entity id/type
     - by sentAt / createdAt
     - by decision outcome if useful
   - Add migration using the repo’s established migration workflow

7. **API/result contract**
   If this flow is exposed via API, return a result shape like:
   - success flag
   - delivery status
   - policy decision outcome
   - policy decision reason
   - delivery/message id when delivered

   For blocked actions, ensure the caller receives a safe user-facing reason.

8. **Integration tests**
   Add integration tests covering at least:
   - **Autonomy level 1** (or lower conservative level): blocked proactive delivery
   - **Autonomy level 2 or 3**: allowed proactive delivery

   Verify:
   - blocked attempt returns policy reason
   - blocked attempt does not create a delivered message record
   - blocked attempt does persist policy decision outcome
   - allowed attempt creates persisted delivery with required fields
   - allowed attempt persists policy decision outcome
   - source entity linkage is stored correctly

   Use existing test infrastructure and database setup patterns.

9. **Keep implementation small and consistent**
   - Reuse existing enums, base entities, result wrappers, and repository patterns
   - Avoid introducing a new subsystem if a message/notification module already exists
   - Keep naming aligned with current solution conventions

# Validation steps
1. Inspect and understand current code paths:
   - locate policy/autonomy enforcement
   - locate proactive task/alert/escalation creation
   - locate message/notification persistence

2. Build after changes:
   - `dotnet build`

3. Run tests:
   - `dotnet test`

4. Specifically verify integration scenarios:
   - blocked proactive message at lower autonomy level returns policy reason and is not delivered
   - allowed proactive message at higher autonomy level is delivered and persisted
   - both scenarios persist policy decision outcomes
   - delivered record includes:
     - channel
     - recipient
     - subject/title
     - body
     - sourceEntityId
     - sentAt

5. If migrations are part of normal workflow, ensure:
   - migration is generated correctly
   - schema matches entity configuration
   - tests run against updated schema

6. In the final implementation notes/PR summary, include:
   - which entities/tables were added or extended
   - where policy decision persistence occurs
   - how blocked reasons are surfaced
   - which two autonomy levels were tested

# Risks and follow-ups
- **Repo mismatch risk:** The story/task IDs in this prompt differ from the backlog naming. Implement the acceptance criteria and architecture intent, not the label mismatch.
- **Existing model overlap:** The repo may already have `messages`, `notifications`, `audit_events`, or `tool_executions` that partially satisfy this. Reuse rather than duplicate.
- **Policy engine maturity:** If no reusable policy engine exists yet, implement the smallest focused evaluator for proactive delivery while keeping interfaces extensible.
- **Source entity ambiguity:** “task, alert, or escalation” may not all exist yet. Support the existing source types in code and make the model extensible for the missing ones.
- **Outbox integration:** If current architecture uses outbox for side effects, persist the business record first and only then enqueue dispatch. Do not make external delivery a prerequisite for satisfying persistence acceptance criteria.
- **Retrieval API follow-up:** Acceptance criteria require persistence for retrieval, but not necessarily a new query endpoint. If no retrieval path exists, note a follow-up task for query/read API.
- **Approval path follow-up:** If policy outcomes may later include `RequiresApproval`, design enums/contracts to allow it even if this task only needs allowed vs blocked.
- **Audit convergence follow-up:** Consider later unifying proactive message policy decisions with broader audit/explainability views under EP-6.