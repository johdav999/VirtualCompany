# Goal
Implement backlog task **TASK-7.3.7 — Do not couple human roles to agent permissions** for story **ST-103 Human user invitation and role assignment**.

The coding agent should ensure the codebase clearly separates:

- **human membership roles** used for tenant access and UI/application authorization
- **agent permissions/policies** used for AI tool execution and orchestration guardrails

This task is about preventing accidental or implicit reuse of human role assignments as agent capability grants. Human roles like `owner`, `admin`, `manager`, `employee`, `finance approver`, and `support supervisor` must not directly determine what agents can read, recommend, or execute.

Because no explicit acceptance criteria were provided for this task, implement the safest interpretation aligned with the architecture and backlog notes:
- human authorization remains in Identity & Tenant Access / Company Setup
- agent permissions remain in Agent Management / Policy Guardrail Engine
- any existing shared enums, helpers, policy mappings, or naming that imply coupling should be refactored
- invitation and role assignment flows must continue to work after the change

# Scope
In scope:

- Review current domain, application, infrastructure, and web/API code for any coupling between:
  - company membership roles
  - approval roles for humans
  - agent tool permissions / scopes / autonomy
- Refactor models and logic so that:
  - human roles are represented as human authorization concepts only
  - agent permissions are represented as agent policy/config concepts only
- Rename ambiguous types/methods/properties if needed to make separation explicit
- Add or update tests that prove:
  - changing a human membership role does not mutate or imply agent permissions
  - agent permission evaluation does not read human membership role definitions except where explicitly required for human approval routing
  - invitation and membership role assignment still function
- Preserve existing behavior for ST-103 invitation flows unless current behavior depends on the forbidden coupling

Out of scope unless required to complete the refactor safely:

- redesigning the full authorization model
- implementing new agent policy features beyond decoupling
- changing mobile behavior
- broad UI redesign
- introducing a new permissions framework if a smaller refactor is sufficient

Important architectural guardrails:

- Follow modular monolith boundaries
- Keep tenant scoping intact
- Do not let agent execution policy depend on human membership role names
- Approval routing may still reference human roles for approver selection, but that must remain distinct from agent execution permissions

# Files to touch
Start by inspecting these likely areas, then adjust based on actual code discovered.

Core projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`

Likely file categories to inspect and potentially modify:

- Domain enums/value objects/constants for roles and permissions
- Membership entities and invitation models
- Agent entities/configuration models
- Authorization policies and handlers
- Approval routing models that reference required human roles
- Tool execution / policy guardrail services
- DTOs/view models that may expose ambiguous `Role` or `Permissions` fields
- Seed data or configuration defaults
- Tests in any corresponding test projects

Examples of likely targets by intent:
- membership role enum/class
- agent permission enum/class
- shared authorization helper classes
- invitation command/handler and membership update command/handler
- agent profile command/handler or policy evaluator
- approval creation logic using `required_role`
- Blazor pages/components for user role assignment and agent profile editing
- API contracts where role/permission naming is ambiguous

If there is a shared type named generically like `Role`, `Permission`, or `UserRole` being reused across both humans and agents, split it into explicit concepts such as:
- `MembershipRole` or `HumanMembershipRole`
- `AgentPermission`, `AgentToolPermission`, `AgentPolicyScope`, or similar

# Implementation plan
1. **Discover current coupling**
   - Search the solution for:
     - `role`
     - `roles`
     - `permission`
     - `permissions`
     - `approver`
     - `tool_permissions`
     - `required_role`
     - any enums/constants reused across modules
   - Identify whether any of the following anti-patterns exist:
     - one enum used for both human and agent roles
     - agent permission checks based on membership role names
     - UI forms or DTOs that serialize both concepts through the same property/type
     - approval role routing incorrectly reused as agent execution authorization
     - seed/default logic that derives agent permissions from inviter/invitee human role

2. **Define explicit domain separation**
   - Introduce or refine distinct domain concepts:
     - human membership role for company access and admin capabilities
     - agent permission/policy configuration for orchestration/tool execution
   - Ensure naming is explicit everywhere possible.
   - If there is a generic `Role` type in shared/domain code, split it carefully and update references.

3. **Refactor human role handling**
   - Keep ST-103 membership role assignment focused on:
     - invite
     - pending membership
     - accept/revoke/reinvite
     - subsequent authorization checks for human users
   - Ensure membership role persistence remains on membership records and is only consumed by human authorization paths.

4. **Refactor agent permission handling**
   - Ensure agent execution policy reads only agent-owned configuration such as:
     - autonomy level
     - tool permissions
     - data scopes
     - thresholds
     - escalation rules
   - Remove any fallback or implicit mapping from human roles to agent permissions.
   - If defaults are needed, use agent template/config defaults, not human role defaults.

5. **Preserve approval semantics without re-coupling**
   - Approval workflows may legitimately target human roles such as finance approver.
   - Keep this as a human approval routing concern only.
   - Verify that `required_role` or approval-step role references are not used to grant agent execution rights.

6. **Update contracts and mappings**
   - Update DTOs, API contracts, view models, and mapping profiles to use explicit names.
   - Avoid ambiguous payloads like:
     - `role`
     - `permissions`
   - Prefer context-specific names where practical:
     - `membershipRole`
     - `agentToolPermissions`
     - `requiredApproverRole`

7. **Update UI/API behavior if needed**
   - Ensure invitation and role assignment screens still show assignable human roles from ST-103.
   - Ensure agent management screens continue to use agent-specific policy fields and do not display or depend on human membership roles except where explicitly intended.

8. **Add regression tests**
   - Add unit/integration tests covering:
     - membership role assignment persists and authorizes human actions
     - agent permission evaluation ignores membership role definitions
     - changing a user from `employee` to `admin` does not alter any agent policy record
     - approval routing by human role still works independently
   - Prefer tests near application/domain boundaries where the coupling risk is highest.

9. **Run build/tests and fix fallout**
   - Compile all touched projects
   - Update any serialization, mapping, or UI bindings broken by renames
   - Keep changes cohesive and minimal, but prioritize clarity over preserving ambiguous naming

10. **Document intent in code**
   - Add concise comments where useful, especially in policy/authorization code, stating that:
     - human membership roles govern human access
     - agent permissions govern agent actions
     - the two must not be implicitly mapped

# Validation steps
Run these checks after implementation.

1. Build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects, run the most relevant ones first, especially for:
   - domain
   - application
   - API/integration tests

4. Manually verify code-level outcomes:
   - invitation flow still creates pending membership with a human membership role
   - accepting or changing membership role still affects human authorization only
   - agent policy evaluation path does not read membership role enum/constants except for explicit human approval routing
   - approval models using human roles remain isolated from agent permission models

5. Add/confirm tests for these scenarios:
   - **Human role change does not affect agent permissions**
     - create membership + agent
     - change membership role
     - assert agent tool permissions/scopes unchanged
   - **Agent permission denial is based on agent config, not human role**
     - configure agent with restricted permissions
     - execute policy check
     - assert denial even if acting human is admin/owner, unless the flow is explicitly a human override/approval path
   - **Approval routing still supports human roles**
     - create approval requiring a human role
     - assert approver resolution works without granting agent execution rights

# Risks and follow-ups
- **Risk: broad rename churn**
  - If the codebase uses generic names like `Role` heavily, renaming may touch many files.
  - Prefer precise refactoring with IDE-assisted rename and focused tests.

- **Risk: hidden coupling in shared models**
  - Shared DTOs or enums in `VirtualCompany.Shared` may be reused by both web and API.
  - Be careful not to break serialization contracts unintentionally.

- **Risk: approval role confusion**
  - Human approver roles are valid domain concepts.
  - Do not remove them; just keep them distinct from agent execution permissions.

- **Risk: authorization regressions**
  - ST-103 depends on role-based human authorization.
  - Ensure owner/admin invite and role-change capabilities still work after refactor.

Suggested follow-ups if not already present:
- introduce explicit types for:
  - `MembershipRole`
  - `ApproverRole`
  - `AgentPermissionSet` / `AgentPolicy`
- add architecture tests or lint-style tests preventing references from agent policy code to membership role types
- document the separation in README or module docs so future contributors do not reintroduce coupling