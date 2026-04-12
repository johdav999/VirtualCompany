# Goal

Implement backlog task **TASK-11.2.2** for **ST-502 Shared orchestration pipeline for single-agent tasks** by adding or completing a **Prompt Builder** in the shared orchestration subsystem that composes a prompt package from:

- agent role instructions
- company context
- retrieved memory
- policy/guardrail instructions
- tool schemas

The implementation should fit the existing **.NET modular monolith** architecture, remain **tenant-aware**, and be designed as an **application/service-layer capability** rather than something coupled to HTTP controllers or UI.

The output of this task should make prompt composition deterministic, testable, and reusable by the orchestration pipeline for any single-agent task.

# Scope

In scope:

- Add or extend domain/application contracts for prompt composition inputs and outputs.
- Implement a prompt builder service that accepts normalized orchestration context and returns a structured prompt package.
- Ensure the builder composes the required sections in a stable order:
  1. role instructions
  2. company context
  3. memory
  4. policies
  5. tool schemas
- Keep the output suitable for downstream LLM invocation, but do **not** directly call the LLM in this task unless already required by existing orchestration flow.
- Preserve tenant and agent scoping in all prompt inputs.
- Add unit tests covering composition behavior and edge cases.

Out of scope unless required by existing code structure:

- Full end-to-end orchestration execution
- Tool execution implementation
- Retrieval implementation beyond consuming already-prepared context
- UI changes
- New database schema unless the current design absolutely requires persistence for prompt artifacts
- Multi-agent coordination

# Files to touch

Start by inspecting these projects and place code in the most appropriate layer based on existing patterns:

- `src/VirtualCompany.Application/`
- `src/VirtualCompany.Domain/`
- `src/VirtualCompany.Infrastructure/`
- `tests/VirtualCompany.Api.Tests/`

Likely files/folders to add or update:

- `src/VirtualCompany.Application/.../Orchestration/`
  - add prompt builder interfaces, DTOs, and service implementation
- `src/VirtualCompany.Domain/...`
  - add value objects/enums only if the domain already models orchestration concepts there
- `src/VirtualCompany.Infrastructure/...`
  - register DI wiring if application services are composed there
- `src/VirtualCompany.Api/...`
  - only if service registration or orchestration endpoint wiring is needed
- `tests/VirtualCompany.Api.Tests/...`
  - add tests for prompt builder composition behavior

Also inspect:

- `README.md`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`

If there is already an orchestration namespace or prompt-related abstraction, extend it instead of creating parallel structures.

# Implementation plan

1. **Inspect existing orchestration structure**
   - Search for existing concepts such as:
     - orchestration
     - agent runtime context
     - prompt builder
     - tool schema
     - policy guardrail
     - memory retrieval
   - Reuse naming and layering conventions already present in the solution.

2. **Define prompt builder contract**
   - Add an application-layer interface, for example:
     - `IPromptBuilder`
   - The contract should accept a single normalized request model, for example:
     - company identity/context
     - agent identity/profile
     - task/intent context
     - memory snippets
     - policy instructions
     - tool definitions/schemas
   - The contract should return a structured result, not just a raw string, for example:
     - system prompt text
     - optional developer/runtime sections
     - serialized tool schema payload
     - source section metadata if useful

3. **Create normalized prompt models**
   - Add DTOs/value models for prompt composition, such as:
     - `PromptBuildRequest`
     - `PromptBuildResult`
     - `PromptSection`
     - `ToolSchemaDefinition`
     - `MemorySnippet`
     - `PolicyInstruction`
   - Keep them simple, serializable, and deterministic.
   - Prefer immutable records if consistent with the codebase.

4. **Implement section composition**
   - Build prompt content in a fixed, explicit order.
   - Include clear section headers so downstream debugging is easier.
   - Recommended structure:
     - agent role and operating instructions
     - company/workspace context
     - current task/request context
     - relevant memory
     - policy and guardrail instructions
     - tool usage instructions and tool schemas
   - If the story wording is strict, ensure the required five categories are always represented, even if some sections are empty or omitted with explicit fallback text.

5. **Add safe defaults and omission rules**
   - Handle null/empty inputs gracefully.
   - Do not emit malformed prompt text when optional sections are absent.
   - Use conservative defaults for policy language if policy input is missing, aligned with architecture guidance:
     - default-deny
     - no execution beyond explicit permissions
   - Avoid exposing chain-of-thought or internal hidden reasoning instructions.

6. **Keep tool schemas structured**
   - If the downstream LLM client expects tools separately, return them separately in `PromptBuildResult`.
   - If the current architecture embeds tool schemas in prompt text, still keep a structured representation in code and derive text from it.
   - Ensure tool schema formatting is deterministic.

7. **Integrate with orchestration pipeline**
   - Wire the prompt builder into the existing single-agent orchestration flow where prompt assembly currently happens or should happen.
   - Remove any duplicated prompt assembly logic from controllers/services if found.
   - Keep orchestration service responsible for coordination, while prompt builder is responsible only for composition.

8. **Dependency injection**
   - Register the prompt builder in DI in the appropriate composition root.
   - Follow existing service registration conventions.

9. **Add tests**
   - Cover at minimum:
     - composes all required sections in correct order
     - handles missing memory
     - handles missing tool schemas
     - includes policy instructions
     - includes company context and role instructions
     - produces deterministic output for same input
   - Prefer focused unit tests over broad integration tests for this task.

10. **Document assumptions in code**
   - Add concise XML comments or inline comments where the composition rules are non-obvious.
   - Do not over-comment trivial code.

# Validation steps

1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there is an existing orchestration test suite, run targeted tests for prompt/orchestration components.

4. Manually verify in code that:
   - prompt builder is application/service-layer code, not UI/controller logic
   - section order is deterministic
   - required categories are represented:
     - role instructions
     - company context
     - memory
     - policies
     - tool schemas
   - tenant/agent context is passed through normalized models
   - no direct DB access is introduced into the prompt builder

5. If an orchestration entry point already exists, add or run a focused test that confirms the orchestration flow now consumes the prompt builder output rather than assembling prompt text inline.

# Risks and follow-ups

- **Risk: existing orchestration abstractions may already partially implement this**
  - Mitigation: extend existing contracts rather than duplicating them.

- **Risk: unclear downstream LLM client shape**
  - Mitigation: return both structured sections and a composed prompt string if needed.

- **Risk: prompt content may become too coupled to current provider**
  - Mitigation: keep provider-agnostic prompt models in Application layer; provider-specific mapping should live elsewhere.

- **Risk: policy wording may drift from actual enforcement**
  - Mitigation: keep prompt policies informational only; real enforcement remains in the policy guardrail/tool execution path.

- **Risk: missing acceptance criteria**
  - Mitigation: treat the ST-502 acceptance bullets as the effective completion target for this task.

Follow-up items after this task, if not already implemented elsewhere:

- connect prompt builder output to LLM invocation adapter
- persist prompt/task correlation metadata for auditability
- align prompt builder inputs with the grounded context retrieval service from ST-304
- add snapshot-style tests for prompt formatting stability
- add redaction/sensitivity filtering before prompt composition if not already present