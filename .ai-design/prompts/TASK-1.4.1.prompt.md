# Goal
Implement backlog task **TASK-1.4.1 — proactive message generation and delivery workflow for inbox and notification channels** for story **US-1.4 ST-A204 — Proactive messaging with guardrailed autonomy**.

Deliver a coding change that enables the system to:

- generate a user-facing proactive message or briefing when a proactive task, alert, or escalation is created
- enforce autonomy level and policy guardrails before delivery
- block disallowed deliveries with a structured policy decision reason
- persist delivered proactive messages for later retrieval
- cover the workflow with integration tests for at least **two distinct autonomy levels**

Use the existing **.NET modular monolith** structure and keep the implementation aligned with:
- modular application/domain/infrastructure boundaries
- tenant-scoped behavior
- outbox/background-dispatcher-friendly design
- policy enforcement before side effects

# Scope
In scope:

- Domain and application support for a **proactive message delivery workflow**
- Support for at least these originating entity types:
  - proactive task
  - alert
  - escalation
- Support for at least these delivery channels:
  - inbox
  - notification
- A structured proactive message record that includes:
  - channel
  - recipient
  - subject or title
  - body
  - sourceEntityId
  - sentAt
- Policy/autonomy enforcement before delivery
- Structured blocked result with policy decision reason
- Persistence and retrieval support for delivered proactive messages
- Integration tests proving:
  - allowed action is delivered
  - disallowed action is blocked
  - behavior differs across at least two autonomy levels

Out of scope unless already trivially supported by existing code:

- email/SMS/push delivery
- UI polish in Blazor or MAUI
- full notification preference management
- broad refactors unrelated to proactive messaging
- introducing a message broker
- implementing a generic workflow engine if not already present

Assumptions to preserve:

- prefer **database-backed persistence** and **background-safe design**
- keep policy decisions **structured and auditable**
- default-deny when policy config is missing or ambiguous
- do not bypass existing tenant scoping or authorization patterns

# Files to touch
Inspect the solution first, then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add or modify:

## Domain
- proactive message entity/value objects
- policy decision/result types
- enums/constants for:
  - channel
  - source entity type
  - delivery status
- domain service or specification for proactive delivery eligibility

Possible examples:
- `src/VirtualCompany.Domain/.../ProactiveMessage.cs`
- `src/VirtualCompany.Domain/.../PolicyDecision.cs`

## Application
- command/handler for generating and delivering proactive messages
- DTOs/contracts for delivery request/result
- orchestration service interface
- retrieval query for persisted proactive messages
- integration with existing policy guardrail service if present

Possible examples:
- `src/VirtualCompany.Application/.../Commands/GenerateProactiveMessageCommand.cs`
- `src/VirtualCompany.Application/.../Services/IProactiveMessageService.cs`

## Infrastructure
- EF Core configuration and persistence mapping
- repository implementation
- migration or schema update for proactive messages
- background dispatcher hook if the architecture already uses outbox/worker patterns
- policy engine adapter wiring if needed

Possible examples:
- `src/VirtualCompany.Infrastructure/.../Configurations/ProactiveMessageConfiguration.cs`
- `src/VirtualCompany.Infrastructure/.../Migrations/...`

## API
- endpoint(s) to trigger or inspect proactive message delivery if needed by tests
- DI registration
- request/response contracts if exposed through HTTP

Possible examples:
- `src/VirtualCompany.Api/.../Program.cs`
- `src/VirtualCompany.Api/.../Endpoints/...`

## Tests
- integration tests covering:
  - allowed delivery at one autonomy level
  - blocked delivery at another autonomy level
  - persistence assertions on delivered messages
  - blocked result includes policy reason

Possible examples:
- `tests/VirtualCompany.Api.Tests/.../ProactiveMessagingTests.cs`

Also inspect:
- existing task/alert/escalation models
- existing notification/inbox models
- existing policy/autonomy enforcement code
- existing outbox/audit patterns
- existing test fixtures and database setup

# Implementation plan
1. **Discover existing architecture before coding**
   - Find current implementations for:
     - tasks
     - alerts/escalations
     - notifications/inbox/messages
     - autonomy levels
     - policy guardrails
     - audit events
     - outbox/background dispatch
   - Reuse existing abstractions instead of creating parallel ones.
   - Identify whether there is already:
     - a `messages` table/entity
     - a `notifications` table/entity
     - a policy engine service
     - command/query patterns in application layer
     - integration test fixture patterns

2. **Model the proactive delivery concept**
   - Introduce or extend a domain model representing a delivered proactive communication.
   - Ensure the persisted model captures the acceptance criteria fields:
     - `Channel`
     - `Recipient`
     - `Subject` or `Title`
     - `Body`
     - `SourceEntityId`
     - `SentAt`
   - Include tenant/company scoping.
   - If the codebase already separates inbox messages and notifications, either:
     - create a shared proactive delivery record plus channel-specific linkage, or
     - extend the existing communication model with source metadata
   - Prefer the smallest change that fits the current architecture.

3. **Define the application workflow**
   - Implement an application service or command handler that:
     1. receives a proactive source request or source entity reference
     2. loads the originating entity
     3. generates the user-facing message/briefing content
     4. evaluates autonomy level and policy guardrails
     5. if allowed:
        - persists the delivered proactive message
        - marks/send-dispatches it through the appropriate channel abstraction
        - returns success result with persisted data
     6. if blocked:
        - does not deliver
        - returns a structured blocked result with policy decision reason
        - records audit/policy metadata if the codebase supports it
   - Keep generation deterministic/testable. If no LLM integration is already present for this path, use template/structured generation rather than introducing external dependencies.

4. **Enforce guardrails before delivery**
   - Reuse the existing policy/autonomy engine if present.
   - The policy check must happen **before** any delivery persistence that implies a sent message.
   - The decision should consider at minimum:
     - tenant scope
     - action type for proactive delivery
     - agent autonomy level
     - any configured thresholds/approval requirements relevant to proactive outreach
   - For blocked actions:
     - return a machine-readable reason/code if possible
     - include a safe human-readable explanation
   - Default to deny if required policy configuration is absent or ambiguous.

5. **Link messages to originating entities**
   - Ensure the proactive message is linked to the originating task/alert/escalation via `sourceEntityId`.
   - If the codebase supports source entity type, persist that too even if not explicitly required.
   - Preserve traceability for future audit and explainability.

6. **Persist delivered messages for retrieval**
   - Add EF Core entity/configuration and migration if needed.
   - Ensure retrieval queries can fetch delivered proactive messages by tenant and optionally by source entity.
   - If there is already a communication read model, extend it rather than duplicating storage.

7. **Integrate with inbox and notification channels**
   - Implement channel routing for:
     - inbox
     - notification
   - Keep channel handling behind an abstraction so the workflow decides “allowed + target channel” and infrastructure handles persistence/dispatch details.
   - If the current architecture already uses an outbox:
     - persist the proactive message and enqueue dispatch atomically where appropriate
   - If in-app persistence itself is the delivery mechanism, still keep the code structured so a dispatcher can be added later.

8. **Return explicit results**
   - The workflow result should clearly distinguish:
     - delivered
     - blocked
   - For delivered results, include persisted message metadata.
   - For blocked results, include policy decision reason.
   - Avoid throwing exceptions for expected policy denials.

9. **Add integration tests**
   - Add end-to-end or application+infrastructure integration tests using the existing test harness.
   - Cover at least:
     - **Autonomy level A**: allowed proactive delivery succeeds and persists a message
     - **Autonomy level B**: disallowed proactive delivery is blocked and no delivered message is persisted
   - Assert delivered record contains:
     - channel
     - recipient
     - subject/title
     - body
     - sourceEntityId
     - sentAt
   - Assert blocked result contains policy reason.
   - Prefer two clearly distinct levels, e.g.:
     - level 1 blocked
     - level 3 allowed
     - or whatever matches existing policy semantics in the codebase

10. **Keep auditability aligned**
   - If audit events already exist, emit business audit records for:
     - proactive message delivered
     - proactive message blocked by policy
   - Do not add noisy technical logging in place of business audit records.
   - Keep rationale concise and operational.

11. **Wire DI and API surface minimally**
   - Register new services/handlers in DI.
   - Expose only the minimal API/controller endpoints needed for integration tests or existing app flows.
   - Do not create unnecessary public endpoints if the workflow is internal and can be tested through existing entry points.

12. **Document implementation assumptions in code comments only where necessary**
   - Keep comments sparse and useful.
   - Prefer clear naming over explanatory comments.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Specifically validate the new proactive messaging workflow through integration tests:
   - allowed proactive action at one autonomy level results in delivery
   - blocked proactive action at another autonomy level returns policy reason
   - delivered message is persisted with:
     - channel
     - recipient
     - subject/title
     - body
     - sourceEntityId
     - sentAt

4. Manually verify, via tests or direct query/debugging, that:
   - blocked actions do **not** create delivered message records
   - tenant/company scoping is preserved
   - source entity linkage is correct
   - policy denial is returned as a structured result, not an unhandled exception

5. If migrations are added:
   - ensure the app starts and tests run against the updated schema
   - verify EF mappings and constraints are correct

6. In the final implementation summary, include:
   - what entities/contracts were added or changed
   - how policy enforcement is applied before delivery
   - which autonomy levels are covered by tests
   - any assumptions made due to current codebase limitations

# Risks and follow-ups
- **Risk: duplicate communication models**
  - The codebase may already have both messages and notifications. Avoid creating a third overlapping model unless necessary.
- **Risk: unclear alert/escalation domain objects**
  - If alerts/escalations are not yet first-class entities, use a source reference abstraction that can support current and future entity types cleanly.
- **Risk: policy semantics may already exist**
  - Do not invent new autonomy rules if the repository already defines them; align to existing policy engine behavior.
- **Risk: delivery vs persistence ambiguity**
  - For in-app channels, persistence may effectively be delivery. Keep naming explicit so “blocked” never looks “sent”.
- **Risk: test fragility**
  - Reuse existing integration fixtures and avoid brittle time-based assertions; inject clock/time provider if available.

Follow-ups after this task, if not already present:
- add unread/read/actioned state for proactive notifications
- add retrieval/filter endpoints for inbox/notification views
- add outbox-backed dispatcher if current implementation is synchronous
- add approval-routing behavior for proactive actions that require approval instead of outright deny
- add UI surfaces in web/mobile for proactive briefings and alerts