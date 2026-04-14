# Goal
Implement `TASK-1.4.2` for `US-1.4 ST-A204 — Proactive messaging with guardrailed autonomy`.

Add proactive action execution support so that when a proactive task, alert, or escalation is created, the backend can generate a user-facing message/briefing linked to the originating entity, enforce autonomy and policy guardrails before delivery, persist delivered messages for retrieval, and return structured policy decision reasons for blocked actions.

This work must satisfy these outcomes:

- Proactive actions can produce a user-facing message or briefing tied to the source entity.
- Delivery is blocked when autonomy level or policy guardrails disallow it.
- Blocked actions return a structured policy decision reason and are not delivered.
- Delivered proactive messages persist required fields:
  - `channel`
  - `recipient`
  - `subject` or `title`
  - `body`
  - `sourceEntityId`
  - `sentAt`
- Integration tests cover both allowed and blocked behavior across at least two distinct autonomy levels.

# Scope
In scope:

- Domain/application/infrastructure/API changes needed to support proactive message generation and guarded delivery.
- Reuse existing policy/autonomy guardrail patterns where possible.
- Persist delivered proactive messages in the existing communication/persistence model, or add a dedicated persistence model if the current one cannot represent the required fields cleanly.
- Ensure the originating proactive entity is linked to the delivered message.
- Return structured policy decision details for blocked proactive sends.
- Add integration tests for:
  - allowed delivery
  - blocked delivery
  - at least two autonomy levels

Out of scope unless required to complete the task safely:

- New UI work in Blazor or MAUI.
- New external delivery providers.
- Broad refactors of the orchestration engine unrelated to proactive action execution.
- Full notification center redesign.
- Email/SMS/push transport expansion beyond current in-app/backend persistence behavior.

Assumptions to validate in the codebase before implementation:

- There is already some concept of policy guardrail evaluation from ST-203/ST-503 or adjacent work.
- There is already a proactive task/alert/escalation creation flow or at least a place where such actions are initiated.
- There is an existing communication/message persistence model that may be extended.
- Integration tests likely live under `tests/VirtualCompany.Api.Tests`.

# Files to touch
Inspect first, then update only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Domain/**/*`
  - proactive action/message entities or value objects
  - policy decision models
  - autonomy level enums/constants
- `src/VirtualCompany.Application/**/*`
  - command/handler/service for proactive action execution
  - policy guardrail orchestration
  - DTOs/contracts for proactive message generation and delivery result
  - retrieval/query handlers if needed
- `src/VirtualCompany.Infrastructure/**/*`
  - EF Core entity mappings
  - repositories
  - persistence models/migrations support
  - outbox/background dispatch integration if proactive delivery is async
- `src/VirtualCompany.Api/**/*`
  - endpoints/controllers if this flow is API-triggered
  - DI registration
  - request/response contracts if externally exposed
- `tests/VirtualCompany.Api.Tests/**/*`
  - integration tests for allowed/blocked proactive delivery
  - fixtures/builders/seeding for autonomy levels and policy states

Potentially relevant project files if new references or registrations are needed:

- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`

Only add a migration if the persistence model truly requires schema change. If migrations are managed in a specific pattern, follow the repository convention exactly.

# Implementation plan
1. **Inspect existing proactive, communication, and policy code paths**
   - Find where proactive tasks, alerts, or escalations are created.
   - Find existing policy guardrail engine/service and autonomy-level enforcement.
   - Find current message persistence model and retrieval path.
   - Identify whether proactive delivery is synchronous, background-dispatched, or outbox-backed.

2. **Define the application contract for proactive action execution**
   - Introduce or extend a command/service that accepts enough context to execute a proactive action:
     - tenant/company context
     - agent/context actor
     - autonomy level
     - source entity type/id
     - target recipient
     - channel
     - subject/title
     - body/briefing content
   - Define a structured result model with explicit success/blocked outcome:
     - `Delivered`
     - `Blocked`
     - `PolicyDecisionReason`
     - delivered message identifiers/metadata when successful

3. **Enforce guardrails before delivery**
   - Route proactive send attempts through the existing policy engine if available.
   - Ensure evaluation includes at minimum:
     - tenant scope
     - action type appropriate for proactive messaging
     - autonomy level
     - policy/guardrail rules
   - If denied:
     - do not persist as delivered
     - return a structured policy decision reason
     - create audit/tool/policy records if the current architecture expects them
   - Prefer default-deny if policy configuration is missing or ambiguous.

4. **Model and persist delivered proactive messages**
   - Reuse existing `messages`/`conversations` model if it can represent:
     - channel
     - recipient
     - subject/title
     - body
     - sourceEntityId
     - sentAt
   - If not, add the smallest clean extension:
     - either structured payload fields on `messages`
     - or a dedicated proactive message entity/table linked to conversation/message
   - Ensure retrieval can return the required fields without lossy mapping.
   - Persist linkage to the originating proactive entity.

5. **Generate user-facing message/briefing from proactive entity**
   - Implement mapping/composition from proactive task/alert/escalation into a user-facing message payload.
   - Keep generation deterministic and application-layer driven.
   - Do not expose chain-of-thought; only concise user-facing content.

6. **Integrate with auditability**
   - If the codebase already records audit events for policy decisions or message delivery, emit:
     - delivery attempted
     - delivery blocked with reason
     - delivery succeeded with message metadata
   - Keep rationale concise and operational.

7. **Expose retrieval if needed**
   - If acceptance requires retrieval and no query exists yet, add or extend a query/endpoint/repository method to fetch persisted proactive messages by source entity or message id.
   - Keep tenant scoping enforced.

8. **Add integration tests**
   - Cover at least:
     - autonomy level A allows proactive delivery
     - autonomy level B blocks proactive delivery
     - another distinct autonomy level scenario to satisfy “at least two distinct autonomy levels”
   - Verify for allowed case:
     - message persisted
     - required fields present
     - linked `sourceEntityId`
     - `sentAt` populated
   - Verify for blocked case:
     - no delivered message persisted
     - structured policy decision reason returned
   - Prefer end-to-end API/application integration tests over unit-only coverage.

9. **Keep implementation aligned with modular monolith boundaries**
   - Domain: rules/value objects/entities
   - Application: orchestration/use case
   - Infrastructure: persistence/mapping
   - API: transport only

Implementation details to prefer:

- Use typed contracts, not loose dictionaries, for policy decision and proactive delivery result.
- Keep naming explicit: `ProactiveMessage`, `ProactiveDeliveryResult`, `PolicyDecisionReason`, etc., but adapt to existing naming conventions.
- If there is already a `tool_executions` or policy decision persistence pattern, mirror it rather than inventing a parallel one.
- If proactive delivery should be outbox-backed, persist intent + decision atomically, then dispatch. But acceptance can still be met with persisted in-app delivery if that is the current architecture.

# Validation steps
1. Inspect and build before changes:
   - `dotnet build`

2. Implement changes incrementally and run targeted tests during development.

3. Run full test suite:
   - `dotnet test`

4. Verify acceptance criteria explicitly:
   - Create or simulate a proactive task/alert/escalation.
   - Confirm the system generates a linked user-facing message/briefing.
   - Confirm guardrails run before delivery.
   - Confirm blocked actions are not delivered and return a policy decision reason.
   - Confirm delivered records include:
     - `channel`
     - `recipient`
     - `subject` or `title`
     - `body`
     - `sourceEntityId`
     - `sentAt`
   - Confirm retrieval returns persisted delivered messages.
   - Confirm integration tests cover allowed/disallowed behavior for at least two autonomy levels.

5. If schema changed:
   - Apply migration per repo convention.
   - Re-run `dotnet build` and `dotnet test`.

6. In the final implementation notes / PR summary, include:
   - what entry point triggers proactive delivery
   - where guardrail enforcement occurs
   - where delivered messages are persisted
   - how blocked reasons are represented
   - which autonomy levels are covered by tests

# Risks and follow-ups
- **Risk: existing message model may not cleanly support recipient/channel/source linkage**
  - Mitigation: add the smallest explicit persistence extension rather than overloading unrelated fields.

- **Risk: policy engine may currently focus on tool execution, not proactive messaging**
  - Mitigation: extend the policy model carefully so proactive delivery is treated as a first-class guarded action, not a bypass.

- **Risk: proactive creation flow may be fragmented across tasks/alerts/escalations**
  - Mitigation: introduce a shared application service for proactive delivery and call it from each origin path.

- **Risk: ambiguity around “delivered”**
  - Mitigation: treat persisted in-app/backend message creation as delivery unless the codebase already distinguishes queued vs sent. If such distinction exists, preserve it and set `sentAt` only on actual delivery.

- **Risk: missing structured blocked-reason contract**
  - Mitigation: standardize on a typed policy decision result with machine-readable code plus human-readable reason.

Follow-ups to note if not completed in this task:

- Add richer audit/explainability views for proactive delivery decisions.
- Add outbox-backed external channel dispatch if current implementation is in-app only.
- Add UI surfaces for proactive message retrieval/history by source entity.
- Expand policy coverage to approval-required proactive actions if the current task only handles allow/deny.