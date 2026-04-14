# Goal
Implement backlog task **TASK-2.3.1 — Extend prompt builder to compose structured identity sections from agent and tenant configuration** for story **US-2.3 ST-A303 — Identity-driven prompt shaping with role, seniority, and behavior patterns**.

The implementation must ensure the shared AI orchestration prompt builder can:

- Compose a **structured identity section** using:
  - agent role
  - seniority
  - business responsibility
  - collaboration norms
  - personality traits
- Produce **different prompt payloads** for different agent identity configurations
- Preserve **shared system safety instructions** across all prompt variants
- Expose the **resolved identity section** in prompt preview/debug output for **non-production** environments only
- Include **unit tests** covering precedence when **tenant policy**, **agent identity**, and **task context** overlap

Keep the design aligned with the architecture:
- modular monolith
- shared orchestration engine
- prompt builder in the AI orchestration subsystem
- tenant-aware behavior
- deterministic, testable prompt composition

# Scope
In scope:

- Extend the prompt composition model and builder logic in the orchestration layer
- Add a first-class structured identity section to prompt assembly
- Resolve identity inputs from:
  - tenant/company configuration or policy
  - agent configuration
  - task/runtime context
- Define and implement precedence rules for overlapping instructions
- Preserve existing shared safety/system instructions
- Add non-production-only debug/preview exposure of the resolved identity section
- Add/adjust unit tests for prompt composition and precedence behavior

Out of scope unless required by existing code structure:

- Large UI redesigns
- New persistence schema unless current models cannot represent required identity inputs
- Full audit/explainability feature work beyond prompt debug exposure
- Production exposure of internal prompt details
- Changes to tool execution, retrieval, or approval logic beyond prompt composition inputs

Implementation expectations:

- Prefer extending existing prompt builder abstractions rather than introducing parallel prompt paths
- Keep prompt assembly deterministic and easy to inspect in tests
- Avoid embedding business logic in controllers/UI
- Do not weaken or reorder safety instructions in a way that changes baseline guardrails

# Files to touch
Inspect the solution and update the actual files that match these responsibilities. Likely areas include:

- `src/VirtualCompany.Application/**`
  - orchestration services
  - prompt builder interfaces/implementations
  - prompt models / DTOs
  - agent/task context assembly
- `src/VirtualCompany.Domain/**`
  - agent identity/value objects if needed
  - tenant policy/domain config models if needed
- `src/VirtualCompany.Infrastructure/**`
  - any prompt preview/debug service wiring
  - environment-aware behavior if implemented here
- `src/VirtualCompany.Api/**`
  - only if an existing internal preview/debug endpoint or response contract must expose resolved identity data
- `tests/**`
  - unit tests for prompt builder
  - tests for precedence and environment-gated debug output

Before coding, locate concrete equivalents for concepts such as:

- prompt builder
- orchestration request/context
- agent profile/configuration
- tenant/company settings or policy instructions
- prompt preview/debug output
- environment detection for non-production gating

If no dedicated prompt builder exists yet, implement the smallest cohesive abstraction in the Application layer and wire it into the existing orchestration flow.

# Implementation plan
1. **Discover current prompt composition flow**
   - Find where the system prompt is assembled today
   - Identify:
     - shared safety/system instructions source
     - agent configuration inputs already used
     - tenant/company context inputs
     - task context inputs
     - any preview/debug output path
   - Document the current composition order in code comments or tests if unclear

2. **Define a structured identity model**
   Introduce or extend a prompt composition model to represent a resolved identity section explicitly, for example:
   - role
   - seniority
   - business responsibility
   - collaboration norms
   - personality traits
   - optional additional identity notes if already supported by current models

   Prefer a typed structure over raw string concatenation so tests can validate both:
   - resolved values
   - final rendered prompt text

3. **Implement identity resolution from layered inputs**
   Resolve identity instructions from three sources:
   - **tenant policy/config**
   - **agent identity/config**
   - **task context/runtime instructions**

   Define explicit precedence rules and encode them in one place. Unless the existing codebase already establishes a different convention, use:
   - shared safety/system instructions: always preserved and not overridden by identity shaping
   - tenant policy: baseline organizational constraints/norms
   - agent identity: role-specific defaults and persona
   - task context: task-specific refinements only where allowed

   Important:
   - Task context should not be able to erase mandatory tenant policy or safety instructions
   - Agent identity should differentiate prompt payloads across agents
   - Overlapping fields should resolve deterministically and be covered by tests

4. **Render a structured identity section into the prompt**
   Update prompt rendering so the final prompt includes a clearly structured identity section, for example with labeled subsections or bullet fields. The exact format should match existing prompt style, but it must be explicit and inspectable.

   Ensure the section includes all acceptance-criteria fields:
   - role
   - seniority
   - business responsibility
   - collaboration norms
   - personality traits

   Keep shared safety instructions in their own stable section and preserve their inclusion for all agents.

5. **Preserve shared safety/system instructions**
   Verify the builder still emits the common safety instructions identically across different agent identities.
   Add tests that compare prompt payloads for two agents and confirm:
   - identity section differs
   - shared safety section remains present and unchanged

6. **Add non-production debug/preview exposure**
   Extend existing prompt preview/debug output, or add a minimal internal-only structure, so non-production environments can inspect:
   - resolved identity section
   - optionally the source contributions or final rendered identity text if consistent with existing patterns

   Requirements:
   - available only in non-production environments
   - not exposed in production responses/logs
   - avoid leaking unnecessary internal prompt details beyond what is needed for inspection

   If there is already a preview DTO, add a field such as:
   - `ResolvedIdentity`
   - `IdentitySection`
   - `DebugIdentitySection`

   Gate it using the app environment abstraction already used by the solution.

7. **Add unit tests**
   Add focused tests for:
   - identity section includes all required fields
   - different agent identity configs produce different prompt payloads
   - shared safety instructions are preserved across variants
   - precedence behavior for overlapping instructions from:
     - tenant policy
     - agent identity
     - task context
   - debug/preview identity exposure appears in non-production
   - debug/preview identity exposure is suppressed in production

   Prefer small deterministic tests over broad integration-style assertions.

8. **Keep implementation cohesive and minimal**
   - Avoid scattering precedence logic across multiple services
   - Centralize prompt identity resolution in the prompt builder or a dedicated resolver used by it
   - Add concise comments where precedence or environment gating may be non-obvious

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run all tests:
   - `dotnet test`

3. Run targeted tests for prompt builder/orchestration if available:
   - `dotnet test --filter Prompt`
   - or the closest matching test filter in the repo

4. Manually verify in code/tests that:
   - the prompt contains a structured identity section
   - the section includes:
     - role
     - seniority
     - business responsibility
     - collaboration norms
     - personality traits
   - two different agent configurations produce different identity prompt content
   - shared safety instructions remain unchanged/present
   - precedence is deterministic when tenant, agent, and task all specify overlapping instructions
   - debug/preview output includes resolved identity only in non-production

5. If there is an internal preview/debug endpoint or service, validate:
   - non-production environment returns/exposes resolved identity section
   - production environment suppresses it

6. Ensure no unrelated project warnings/errors were introduced in touched projects.

# Risks and follow-ups
- **Risk: unclear existing precedence rules**
  - Mitigation: infer from current prompt composition patterns and encode the chosen rule set in tests and comments

- **Risk: identity data may be partially modeled today**
  - Mitigation: support graceful defaults/empty fields without breaking existing agents; avoid forcing schema changes unless necessary

- **Risk: debug exposure leaks internal prompt details**
  - Mitigation: strictly gate by non-production environment and expose only the resolved identity section, not full hidden internals unless already supported

- **Risk: prompt formatting regressions**
  - Mitigation: preserve existing safety/system sections and add snapshot/string assertions for critical prompt segments

- **Risk: over-coupling prompt rendering to persistence models**
  - Mitigation: map domain/config models into a prompt-specific resolved model before rendering

Follow-ups to note in code comments or PR notes if relevant:
- future support for richer identity templates per agent template catalog
- admin-facing prompt preview UI if not yet present
- audit/explainability linkage between resolved identity and orchestration records
- stronger policy metadata distinguishing mandatory tenant norms from overridable task phrasing