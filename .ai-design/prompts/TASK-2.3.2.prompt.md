# Goal
Implement `TASK-2.3.2` by adding deterministic instruction precedence rules to the shared AI prompt builder so prompt assembly correctly resolves overlapping instructions from:
1. tenant policy,
2. agent identity,
3. task context,

while preserving shared system safety instructions.

This work must support **US-2.3 ST-A303 — Identity-driven prompt shaping with role, seniority, and behavior patterns** and satisfy these outcomes:
- prompt assembly includes a structured identity section with role, seniority, business responsibility, collaboration norms, and personality traits,
- different agent identity configurations produce different prompt payloads,
- shared system safety instructions remain unchanged across agents,
- non-production preview/debug output exposes the resolved identity section,
- unit tests verify precedence behavior when all three instruction sources overlap.

# Scope
In scope:
- Update the orchestration/prompt-building layer in the .NET backend.
- Introduce or refine a structured model for prompt instruction sources and resolved prompt sections.
- Implement explicit precedence/merge rules for overlapping instructions from tenant policy, agent identity, and task context.
- Ensure the final prompt payload contains a structured identity section.
- Add non-production-only debug/preview visibility for the resolved identity section.
- Add unit tests covering composition differences and precedence behavior.

Out of scope:
- UI-heavy agent profile redesign.
- New persistence schema unless absolutely required.
- Changes to tool execution policy enforcement beyond prompt composition.
- Production exposure of internal debug prompt details.
- Broad refactors unrelated to prompt assembly.

Assumptions to preserve:
- This is a **shared orchestration engine** with configurable agent personas.
- Prompt building belongs in the application/orchestration layer, not controllers or UI.
- Safety/system instructions are a protected base layer and must not be overridden by identity/task customization.
- Tenant isolation and environment-aware behavior must be respected.

# Files to touch
Inspect the solution first and then update the most relevant existing files. Prefer extending current orchestration/prompt builder types over creating parallel abstractions.

Likely areas:
- `src/VirtualCompany.Application/**`
  - prompt builder/orchestration services
  - DTOs/models for prompt assembly
  - environment-aware preview/debug services if they live here
- `src/VirtualCompany.Domain/**`
  - value objects or domain models for agent identity if needed
- `src/VirtualCompany.Infrastructure/**`
  - only if prompt preview/debug behavior or environment wiring is implemented there
- `src/VirtualCompany.Api/**`
  - only if there is an existing internal preview/debug endpoint or DI registration to update
- `tests/**`
  - add/extend unit tests for prompt composition and precedence

Before coding, locate concrete files/classes related to:
- prompt builder
- orchestration service
- agent configuration/identity
- tenant policy/config
- task context models
- environment detection (`IHostEnvironment`, `ASPNETCORE_ENVIRONMENT`, etc.)
- existing tests for orchestration or prompt assembly

# Implementation plan
1. **Discover current prompt assembly flow**
   - Find the shared orchestration pipeline and prompt builder implementation.
   - Identify where these inputs currently enter prompt composition:
     - system safety instructions,
     - tenant/company policy,
     - agent role/persona/identity,
     - task/request context.
   - Document the current composition order in code comments or PR notes through clear implementation structure.

2. **Define explicit precedence rules**
   - Implement a deterministic merge strategy for overlapping instruction fields.
   - Use this precedence unless the existing codebase has a stronger established convention:
     - **System safety instructions**: immutable base, always preserved, never overridden.
     - **Tenant policy**: organization-wide behavioral constraints/defaults.
     - **Agent identity**: role-specific shaping that overrides tenant defaults where appropriate for identity fields.
     - **Task context**: task-specific instructions that can refine execution behavior but must not erase protected safety/system rules.
   - If fields differ by category, make precedence field-aware rather than naive string concatenation.

3. **Introduce a structured resolved identity section**
   - Ensure the final prompt payload contains a clearly structured identity section with at least:
     - role,
     - seniority,
     - business responsibility,
     - collaboration norms,
     - personality traits.
   - Prefer a typed model such as `ResolvedAgentIdentitySection` or equivalent over raw string-only assembly.
   - If the final provider payload is text-based, build from the typed model into a stable formatted section.

4. **Implement source-aware instruction resolution**
   - Represent instruction inputs in a way that preserves source origin where useful for testing/debugging.
   - Merge overlapping values deterministically.
   - Recommended behavior:
     - scalar fields: resolve by precedence,
     - list fields: merge with stable ordering and deduplication,
     - protected safety directives: always included first and unchanged,
     - empty/null lower-priority values must not wipe out higher-priority values.
   - Avoid brittle free-form string replacement logic.

5. **Preserve shared system safety instructions**
   - Ensure different agent identities produce different prompt payloads only in the identity/task-resolved sections.
   - Shared safety/system instructions must remain identical across agents for the same platform version/config.
   - Add tests that compare payload segments to verify this.

6. **Add non-production debug/preview exposure**
   - If a prompt preview/debug mechanism already exists, extend it to include the resolved identity section.
   - If none exists, add a minimal internal-only debug representation used by tests and non-production diagnostics.
   - Gate exposure to non-production environments only.
   - Do not expose hidden reasoning or chain-of-thought; only expose resolved prompt inputs/sections.
   - Include enough structure for inspection, e.g.:
     - resolved identity fields,
     - source attribution per field if practical,
     - final rendered identity section.

7. **Add comprehensive unit tests**
   - Cover at minimum:
     - prompt includes structured identity section fields,
     - two agents with different identity configs produce different prompt payloads,
     - shared system safety instructions remain unchanged across those payloads,
     - precedence resolution when tenant policy, agent identity, and task context overlap,
     - null/empty values do not incorrectly override populated values,
     - list merging/deduplication if collaboration norms or traits are list-based,
     - debug/preview output includes resolved identity section in non-production,
     - debug/preview output is suppressed or unavailable in production mode.
   - Prefer focused unit tests over broad integration tests unless the current test suite already validates prompt builder end-to-end.

8. **Keep implementation aligned with architecture**
   - Keep prompt composition in the shared orchestration subsystem.
   - Do not move prompt assembly into controllers, pages, or mobile/web clients.
   - Keep the design testable and deterministic.
   - Respect modular monolith boundaries and existing DI patterns.

9. **Code quality expectations**
   - Use clear naming around precedence and resolution.
   - Add concise comments only where precedence/protection rules are non-obvious.
   - Avoid overengineering; implement the smallest clean abstraction that supports current acceptance criteria and future prompt shaping work.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there is a targeted test project for application/orchestration logic, run it directly as well.

4. Manually verify in code/tests that:
   - the final prompt contains a structured identity section,
   - role, seniority, business responsibility, collaboration norms, and personality traits are present,
   - two different agent identities produce different prompt payloads,
   - the shared safety/system section is identical across those payloads,
   - overlapping instructions resolve according to the implemented precedence rules,
   - non-production debug/preview includes the resolved identity section,
   - production mode does not expose that debug detail.

5. If a debug endpoint/service exists, validate both environment modes with tests or environment-mocked unit coverage rather than manual runtime-only checks.

# Risks and follow-ups
- **Risk: unclear existing prompt model**
  - The current code may assemble prompts as ad hoc strings. If so, introduce a minimal typed intermediate model before rendering to avoid fragile precedence logic.

- **Risk: ambiguous precedence semantics**
  - Tenant policy vs. agent identity vs. task context may not map cleanly for every field. Keep rules explicit and field-aware, and document them in code/tests.

- **Risk: accidental safety regression**
  - The most important guardrail is preserving shared system safety instructions. Add direct assertions for this.

- **Risk: debug leakage**
  - Ensure preview/debug output is strictly non-production gated and does not expose chain-of-thought.

- **Risk: hidden coupling to future stories**
  - Keep the implementation extensible for later prompt shaping, retrieval grounding, and audit/explainability work without broad speculative abstraction.

Follow-ups after completion:
- Consider documenting prompt precedence rules in developer docs if no orchestration docs exist.
- Consider adding source attribution to resolved prompt sections for future audit/explainability support.
- Consider aligning this with later retrieval/context composition work so all prompt sections use the same deterministic merge pattern.