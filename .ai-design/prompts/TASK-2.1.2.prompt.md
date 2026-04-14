# Goal
Implement `TASK-2.1.2` by ensuring the configured agent communication identity profile is injected into all prompt-building paths used for:
- direct chat responses
- task summaries / task output generation
- document or generated artifact prompt construction

The implementation must satisfy `US-2.1 ST-A301 — Communication style enforcement across response channels` by making style directives consistent, runtime-resolved, and refreshable without restart.

# Scope
In scope:
- Identify the shared and channel-specific prompt builders in the .NET orchestration layer.
- Introduce or extend a normalized `communication profile` / `identity profile` model containing at minimum:
  - tone
  - persona
  - communication rules
  - fallback/default profile behavior
- Ensure prompt payloads include these identity fields before model invocation for:
  - chat
  - task output / summary generation
  - document generation
- Ensure updated agent configuration is used on subsequent requests without restart.
- Add logging when fallback profile is used.
- Add automated tests covering prompt payload composition and fallback behavior.
- Add or extend rule-check validation tests for forbidden tone violations across all three output paths.

Out of scope:
- UI changes for editing agent profile fields unless required to compile.
- Large refactors of unrelated orchestration components.
- New persistence schema unless the current model lacks a place for communication profile data.
- Reworking model provider integration beyond what is needed to pass the profile into prompt payloads.

# Files to touch
Inspect and update the actual files that implement these concerns. Likely candidates include:

- `src/VirtualCompany.Application/**/*Prompt*.*`
- `src/VirtualCompany.Application/**/*Orchestration*.*`
- `src/VirtualCompany.Application/**/*Chat*.*`
- `src/VirtualCompany.Application/**/*Task*.*`
- `src/VirtualCompany.Application/**/*Document*.*`
- `src/VirtualCompany.Application/**/*Agent*.*`
- `src/VirtualCompany.Domain/**/*Agent*.*`
- `src/VirtualCompany.Domain/**/*Profile*.*`
- `src/VirtualCompany.Infrastructure/**/*OpenAi*.*`
- `src/VirtualCompany.Infrastructure/**/*Llm*.*`
- `src/VirtualCompany.Infrastructure/**/*Prompt*.*`
- `src/VirtualCompany.Infrastructure/**/*Repository*.*`
- `src/VirtualCompany.Api/**/*DependencyInjection*.*`
- `tests/VirtualCompany.Api.Tests/**/*`
- `tests/**/*Prompt*.*`
- `tests/**/*Orchestration*.*`

Also inspect:
- `README.md`
- any architecture or developer docs describing prompt composition

If the codebase already has named services for prompt building, agent registry, or orchestration, prefer modifying those existing abstractions rather than introducing parallel ones.

# Implementation plan
1. **Discover current prompt-building flow**
   - Find the entry points for:
     - direct agent chat generation
     - task summary/output generation
     - document generation
   - Trace each path to the final model invocation payload.
   - Identify whether there is already:
     - a shared prompt builder
     - an agent config resolver
     - a runtime context builder
     - a default persona/style mechanism

2. **Define a normalized communication profile contract**
   - Add or extend a domain/application model representing the agent identity profile used at generation time.
   - Include at minimum:
     - `Tone`
     - `Persona`
     - `CommunicationRules`
     - optional derived fields like `DisplayName`, `RoleName`, `Seniority` if already part of prompt identity
     - a flag or metadata indicating `IsFallback`
   - Keep the contract prompt-oriented and immutable where practical.
   - If agent config already stores personality JSON, map from that source instead of duplicating persistence.

3. **Implement runtime profile resolution**
   - Create or extend a resolver service that loads the effective communication profile for an agent at request time.
   - Resolution order should be:
     1. explicit agent identity/communication profile
     2. template/default configured profile if applicable
     3. system fallback profile
   - Ensure the resolver does not cache in a way that requires service restart to pick up updates.
   - If caching exists, make it short-lived and invalidation-safe, or bypass it for this profile.
   - Emit structured logs when fallback is used, including tenant/company and agent identifiers where available.

4. **Inject profile into all prompt payload builders**
   - Update chat prompt construction so the agent identity profile fields are included before model invocation.
   - Update task summary/task output prompt construction similarly.
   - Update document generation prompt construction similarly.
   - Prefer one shared helper/formatter for rendering communication directives into prompt sections to avoid drift.
   - Ensure the profile appears in the system/instruction portion of the prompt payload, not appended as an afterthought in user content.

5. **Standardize prompt section formatting**
   - Add a shared prompt section such as `Agent Identity Profile` or equivalent.
   - Include explicit style directives, for example:
     - who the agent is
     - required tone/persona
     - communication rules to follow
     - forbidden tone/rule constraints
   - Keep formatting deterministic for testability.
   - Avoid channel-specific wording differences unless required by product behavior.

6. **Preserve channel-specific behavior while enforcing shared style**
   - Chat, task, and document builders may still differ in output format and task instructions.
   - Ensure all three consume the same resolved communication profile and shared rendering logic.
   - Do not duplicate fallback logic in each builder.

7. **Add or extend forbidden tone rule checks**
   - If automated rule checks already exist, wire the new prompt content into those tests.
   - If they do not exist, add focused tests that assert the generated prompt payload contains the configured directives needed to prevent forbidden tone violations.
   - Where feasible, add tests that verify the same agent config yields equivalent style directives across chat/task/document paths.

8. **Add tests**
   - Unit tests for profile resolution:
     - explicit profile used when present
     - fallback profile used when absent
     - fallback usage logged
     - updated agent config reflected on subsequent resolution
   - Unit tests for prompt builders:
     - chat prompt includes identity profile fields
     - task prompt includes identity profile fields
     - document prompt includes identity profile fields
     - ordering places identity/style instructions before model invocation payload is finalized
   - Integration or application tests:
     - same agent produces prompt payloads with consistent style directives across all channels
     - no restart assumption: update config, invoke again, assert new directives are used

9. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries:
     - domain/application for contracts and orchestration logic
     - infrastructure for provider-specific payload mapping
   - Avoid placing prompt assembly in controllers or UI.
   - Keep tenant-scoped agent resolution intact.

10. **Document any new abstractions**
   - Add concise code comments or README notes if a new shared communication profile resolver or prompt section renderer is introduced.
   - Document fallback behavior and logging expectations.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add/verify targeted automated coverage for:
   - explicit profile injection in chat prompts
   - explicit profile injection in task prompts
   - explicit profile injection in document prompts
   - fallback profile usage when no explicit profile exists
   - fallback usage logging
   - updated config reflected on next generation without restart
   - consistent style directives across channels

4. If there are snapshot/string-based prompt tests, verify:
   - identity profile section is present
   - tone/persona/rules appear before provider invocation
   - formatting is deterministic

5. If there are integration tests around orchestration/model clients, verify:
   - the final provider payload contains the rendered identity profile
   - no channel bypasses the shared resolver

# Risks and follow-ups
- **Risk: prompt logic is duplicated across channels**
  - Mitigation: extract a shared communication profile rendering helper and resolver.

- **Risk: agent config shape is inconsistent**
  - Mitigation: add a mapping layer from existing agent/personality config to a normalized runtime profile rather than forcing broad schema changes.

- **Risk: stale cache prevents updated settings from applying**
  - Mitigation: resolve from source on each generation path or use safe invalidation/version-aware caching.

- **Risk: fallback logging becomes noisy**
  - Mitigation: use structured informational logs with clear event IDs and avoid excessive duplication if retries occur.

- **Risk: tests become brittle due to full prompt string assertions**
  - Mitigation: assert on stable sections/fields rather than entire prompt bodies unless snapshot tests already exist and are maintained.

Follow-ups after this task:
- Add explicit admin-visible audit/telemetry for communication profile resolution source.
- Consider centralizing forbidden tone/rule policy definitions for reuse in prompt building and post-generation validation.
- If not already present, add a dedicated agent identity profile configuration object separate from broader personality JSON for stronger validation and maintainability.