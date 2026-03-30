# Goal
Implement `TASK-8.2.7` for `ST-202 Agent operating profile management` by ensuring agent operating profile changes emit durable business audit events so configuration history is auditable later.

This task should add audit-event creation for agent profile/configuration mutations without introducing a full audit-history UI yet. Focus on capturing meaningful, tenant-scoped, business-level events whenever an agent’s operating profile is created or updated, especially for fields called out in ST-202:
- objectives
- KPIs
- role brief
- tool permissions
- data scopes
- approval thresholds
- escalation rules
- trigger logic
- working hours
- status

The implementation should align with the architecture principle that auditability is a domain feature, not just technical logging.

# Scope
In scope:
- Identify the existing command/application flow(s) that create and update agent operating profiles.
- Emit business audit events for relevant agent profile mutations.
- Persist audit events in the transactional store using the existing or intended `audit_events` model.
- Include tenant/company context, actor context, target entity, action, outcome, and concise rationale/summary metadata suitable for future audit views.
- Capture enough structured metadata to support future “config history” inspection, ideally including changed field names and before/after snapshots or diffs in metadata/JSON.
- Ensure events are only written for successful mutations, and preferably within the same transaction boundary as the profile change.
- Add/extend tests covering audit event creation.

Out of scope:
- Building audit trail UI/screens.
- Implementing a generic event sourcing system.
- Capturing raw chain-of-thought or overly verbose internal reasoning.
- Auditing unrelated entities outside agent operating profile management unless required by existing shared abstractions.
- Large refactors of the whole audit subsystem beyond what is needed to support this task cleanly.

# Files to touch
Inspect and update the actual files that implement agent profile management and audit persistence. Likely areas include:

- `src/VirtualCompany.Application/**`
  - Agent create/update commands, handlers, validators, DTOs
  - Any application services for agent management
  - Audit event service/abstractions if present
- `src/VirtualCompany.Domain/**`
  - Agent aggregate/entity methods if domain events or mutation helpers belong here
  - Audit event domain model/value objects if present
- `src/VirtualCompany.Infrastructure/**`
  - EF Core persistence for `audit_events`
  - Repository implementations
  - Transaction/unit-of-work wiring
- `src/VirtualCompany.Api/**`
  - Only if request/actor context plumbing is needed for audit attribution
- `src/VirtualCompany.Shared/**`
  - Shared contracts/enums/constants if audit action names or actor types are centralized
- Tests under the corresponding test projects
  - Application tests
  - Infrastructure persistence tests
  - API/integration tests if the solution already uses them

Also inspect:
- `README.md` if there is a documented pattern for audit events or module conventions.

# Implementation plan
1. **Discover the existing agent profile mutation path**
   - Find the command(s), endpoint(s), handler(s), and persistence flow used for:
     - creating agents from templates
     - updating agent operating profile fields
     - changing agent status
   - Confirm whether ST-202 is already implemented and where profile edits are persisted.
   - Identify how current user/company context is resolved in the app layer.

2. **Discover the current audit model and conventions**
   - Search for:
     - `audit_events`
     - `AuditEvent`
     - `IAudit*`
     - `actor_type`
     - `rationale_summary`
   - Determine whether an audit entity/table/migration already exists.
   - If the audit model is incomplete, minimally extend it to support this task rather than inventing a parallel mechanism.
   - Reuse existing naming conventions for:
     - actor type
     - target type
     - outcome
     - action names

3. **Define audit actions for agent config changes**
   - Introduce clear, stable action names, for example:
     - `agent.created`
     - `agent.profile.updated`
     - `agent.status.updated`
     - or more granular actions if the codebase already prefers that style
   - Keep action naming consistent and future-friendly.

4. **Capture meaningful change details**
   - For profile updates, compute which fields actually changed.
   - Store structured metadata for future auditability, such as:
     - changed field names
     - before values
     - after values
     - optional summary counts
   - Prefer storing this in metadata JSON on the audit event if the schema supports it.
   - If the schema has a `rationale_summary` or similar text field, populate it with a concise operational summary like:
     - “Updated agent operating profile: objectives, tool permissions, approval thresholds.”
   - Do not store secrets or sensitive tokens if any config fields could contain them.

5. **Emit audit events from the successful mutation flow**
   - After a successful create/update/status change, create an audit event with:
     - `company_id`
     - `actor_type` = likely `human` for admin-driven profile edits
     - `actor_id`
     - `action`
     - `target_type` = `agent`
     - `target_id`
     - `outcome` = success-equivalent value used by the system
     - summary/metadata
   - Ensure the event is persisted atomically with the agent change where possible.
   - If the architecture already uses domain events + outbox, follow that pattern. Otherwise, keep it simple and transactional.

6. **Handle no-op and validation behavior correctly**
   - If an update request results in no actual changes:
     - either do not emit an audit event, or emit a clearly labeled no-op event only if that is already a system convention.
   - If validation fails and no mutation is persisted:
     - do not create a success audit event.
   - If the system already records failed business attempts in audit events, follow the existing pattern; otherwise avoid expanding scope.

7. **Add or update persistence mapping**
   - If needed, add EF Core configuration/migration updates for any missing audit metadata columns required by this task.
   - Keep schema changes minimal and backward-compatible.
   - If `audit_events` already has a JSON metadata/details column, reuse it.

8. **Add tests**
   - Add focused tests for:
     - successful profile update creates one audit event
     - audit event contains correct company/actor/target/action/outcome
     - changed fields are captured correctly
     - no audit event on validation failure
     - status change is audited
   - Prefer application/integration tests over brittle unit-only tests if the repo supports them.

9. **Keep implementation small and extensible**
   - Avoid overengineering a universal auditing framework unless one already exists.
   - But do extract a small reusable helper/service if multiple agent mutation handlers need the same audit-writing logic.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted tests for agent management or audit persistence, run/filter them as appropriate.

4. Manually verify in code and/or integration tests that:
   - updating an agent operating profile persists the agent changes
   - a corresponding audit event is written
   - the audit event is tenant-scoped to the correct `company_id`
   - the audit event references the correct actor and target agent
   - the audit summary/metadata identifies the changed fields
   - invalid updates do not create success audit events

5. If migrations are added:
   - ensure the solution builds cleanly after migration changes
   - verify EF mappings and snapshot consistency

# Risks and follow-ups
- **Risk: audit schema may be incomplete or not yet implemented**
  - Follow-up: add the smallest viable schema support needed now, without blocking future ST-602 audit views.

- **Risk: actor context may not be available in the application layer**
  - Follow-up: introduce or reuse a current-user/current-company abstraction so audit attribution is reliable.

- **Risk: JSON config diffs may be noisy**
  - Follow-up: normalize/sort JSON where practical and store only relevant before/after values or changed field names to keep audit records readable.

- **Risk: duplicate audit writes**
  - Follow-up: ensure audit creation happens in one authoritative mutation path, not in both controller and handler layers.

- **Risk: sensitive config leakage**
  - Follow-up: redact or omit any secret-bearing fields from audit metadata.

- **Future follow-up**
  - Reuse this pattern for other business-critical configuration changes.
  - Surface these events in ST-602 audit trail and explainability views.
  - Consider standardizing audit action constants and metadata shape across modules once more audited mutations are added.