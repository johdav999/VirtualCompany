# Goal
Implement backlog task **TASK-2.1.3 — Implement response style validation rules and automated tests for forbidden tone patterns** for story **US-2.1 ST-A301 — Communication style enforcement across response channels**.

Deliver a production-ready implementation in the existing **.NET modular monolith** that ensures:

- agent identity profile fields are included in prompt payloads before model invocation
- style directives are consistently applied across **chat**, **task summary**, and **document/generated output** paths
- automated validation detects forbidden tone violations
- updated agent configuration is used on subsequent generations without service restart
- a default fallback identity profile is applied and logged when explicit profile data is missing

Use the current architecture and code conventions already present in the repository. Prefer extending existing orchestration/prompt-building abstractions rather than introducing parallel pipelines.

# Scope
In scope:

- Add or extend an **agent identity/style profile model** used by orchestration
- Ensure prompt construction includes identity/style fields before LLM invocation
- Apply the same style directives across all relevant response-generation paths:
  - direct chat
  - task summaries / task output generation
  - generated document/output paths
- Implement a **forbidden tone validation component** with deterministic rule checks
- Add automated tests covering:
  - prompt payload composition
  - style propagation across channels
  - forbidden tone rule detection
  - runtime config refresh behavior
  - fallback profile usage and logging
- Add structured logging for fallback profile usage

Out of scope unless required by existing code structure:

- UI redesigns
- broad schema redesign unrelated to style enforcement
- replacing the LLM provider integration
- introducing non-deterministic moderation or classifier dependencies for tone validation

Implementation constraints:

- Keep logic in application/orchestration layers, not controllers/UI
- Reuse existing agent configuration sources and dependency injection patterns
- Keep validation deterministic and unit-testable
- Preserve tenant scoping and agent-specific behavior
- Do not require service restart for updated style settings to take effect

# Files to touch
Inspect the solution first and then update the most relevant files. Expected areas include:

- `src/VirtualCompany.Application/**`
  - orchestration services
  - prompt builder abstractions/implementations
  - agent configuration resolution
  - response generation services for chat/task/document flows
- `src/VirtualCompany.Domain/**`
  - agent profile/value objects/entities if style/identity belongs in domain
- `src/VirtualCompany.Infrastructure/**`
  - persistence/config retrieval for agent settings
  - logging integration if needed
  - any provider payload mapping before model invocation
- `src/VirtualCompany.Api/**`
  - only if wiring or DI registration changes are needed
- `tests/**`
  - add unit/integration tests in the most appropriate existing test projects, likely:
    - `tests/VirtualCompany.Api.Tests/**`
    - and/or any existing application/domain test projects if present

Also inspect:
- `README.md`
- any existing orchestration, prompt, agent management, communication, task, or document generation code paths
- any existing logging/test helpers

Do not create new top-level projects unless absolutely necessary.

# Implementation plan
1. **Discover existing orchestration and generation paths**
   - Find the shared orchestration engine, prompt builder, and model invocation boundary.
   - Identify all response-producing paths for:
     - chat
     - task summaries
     - generated documents/outputs
   - Identify where agent configuration is loaded and whether it is cached.

2. **Define/extend the identity profile contract**
   - Introduce or extend a model representing the agent identity/style profile, including fields needed by acceptance criteria, such as:
     - agent name/display name
     - role/persona
     - tone/style directives
     - communication rules
     - forbidden tone patterns/rules
     - fallback/default marker if applicable
   - Keep the model serializable and easy to include in prompt payloads.
   - If a similar model already exists, extend it instead of duplicating.

3. **Implement profile resolution with fallback**
   - Create or extend a resolver/service that returns the effective identity profile for a given agent.
   - Resolution order should be:
     1. explicit agent profile/config
     2. template/default configured profile
     3. system fallback profile
   - When fallback is used:
     - emit structured logs
     - include tenant/company and agent identifiers where available
     - avoid logging sensitive prompt content
   - Ensure this resolver is called per generation request or uses safe invalidation so config updates are reflected without restart.

4. **Ensure runtime config refresh**
   - Review current config caching behavior.
   - Update the implementation so subsequent generations use updated style settings without service restart.
   - Prefer one of:
     - fresh DB/config read per request where acceptable
     - short-lived cache with invalidation on update
     - options monitor / change token pattern if config is file/options based
   - Add tests proving updated settings are used on the next generation.

5. **Inject identity/style into prompt payloads**
   - Update the prompt builder or model request factory so the effective identity profile fields are included before model invocation.
   - Ensure the payload contains explicit style directives and communication rules, not just implicit references.
   - Keep the prompt structure consistent across all generation channels.
   - If there is a shared prompt envelope/request DTO, add the identity profile there.

6. **Apply style directives across all output channels**
   - Refactor channel-specific generation code to use the same shared style/profile injection path.
   - Verify chat, task summary, and document/generated output all pass through the same or equivalent style enforcement layer.
   - Avoid channel-specific drift by centralizing style directive composition.

7. **Implement forbidden tone validation rules**
   - Add a deterministic validator service, e.g. `IResponseStyleValidator`, that inspects generated text against configured forbidden tone patterns/rules.
   - Support simple, testable rule types such as:
     - forbidden phrases
     - regex patterns
     - banned stylistic markers
     - disallowed persona/tone indicators
   - Return structured validation results:
     - pass/fail
     - matched rule(s)
     - message/reason
   - Keep the validator independent from the LLM provider.

8. **Integrate validation into generation flow**
   - Run validation after generation and before final response persistence/return where appropriate.
   - Decide behavior based on existing architecture and minimal disruption:
     - fail the response and surface safe error
     - or mark violation and block downstream completion
   - At minimum, ensure automated checks exist and the generation pipeline can enforce or record violations consistently.
   - If enforcement behavior already exists for other guardrails, align with that pattern.

9. **Add automated tests**
   - Add focused unit tests for:
     - effective profile resolution
     - fallback profile selection
     - fallback logging
     - prompt payload includes identity profile fields before invocation
     - forbidden tone validator catches configured violations
     - validator passes compliant responses
     - updated config is used on subsequent generations without restart
   - Add integration-style tests for:
     - chat path includes style directives
     - task summary path includes style directives
     - document/generated output path includes style directives
     - same agent config is consistently applied across channels
   - Prefer deterministic fake/stub model invocation boundaries so tests can inspect prompt payloads directly.

10. **Keep implementation clean and aligned**
   - Register new services in DI
   - Keep naming consistent with existing modules
   - Add small comments only where logic is non-obvious
   - Avoid overengineering; centralize shared behavior

11. **Document assumptions in code/tests**
   - If the repository lacks an existing document output path, implement against the nearest generated-output abstraction and note the mapping in tests/comments.
   - If forbidden tone rules are stored in JSON/config, validate parsing and defaults.

# Validation steps
Run these after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify targeted scenarios with automated tests:
   - prompt payload for a configured agent contains identity/profile/style fields before model invocation
   - chat generation path includes configured style directives
   - task summary generation path includes configured style directives
   - document/generated output path includes configured style directives
   - forbidden tone validator fails outputs containing banned patterns
   - compliant outputs pass validation
   - updating agent style config is reflected on the next generation without restarting services
   - missing explicit profile triggers fallback profile usage and structured log emission

4. If there are existing integration or API tests for orchestration endpoints, extend them to assert:
   - tenant-scoped agent resolution still works
   - no regression in response generation contracts

5. Ensure code quality:
   - no duplicated style-building logic across channels
   - no hardcoded agent-specific rules outside configuration/default profile definitions
   - no sensitive prompt content written to logs

# Risks and follow-ups
- **Risk: generation paths are fragmented**
  - Mitigation: centralize style/profile injection at the prompt builder or model request factory boundary.

- **Risk: config caching prevents immediate updates**
  - Mitigation: inspect current caching and add invalidation or per-request resolution.

- **Risk: forbidden tone validation becomes brittle**
  - Mitigation: keep rules deterministic and configuration-driven; start with phrase/regex matching and structured results.

- **Risk: fallback behavior hides missing configuration**
  - Mitigation: log fallback usage with structured metadata and cover with tests.

- **Risk: channel-specific output services bypass shared orchestration**
  - Mitigation: refactor minimal shared helper/service used by all response-producing paths.

Follow-ups to note in code comments or task notes if not fully implemented now:
- admin UX for managing forbidden tone rules per agent/template
- audit-event persistence for style validation failures if not already wired
- richer policy/reporting around repeated tone violations
- future expansion from deterministic rules to layered moderation/classification if product requirements evolve