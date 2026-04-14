# Goal
Implement backlog task **TASK-2.1.1 — Add agent communication profile schema for tone, persona, and style constraints** for story **US-2.1 ST-A301 — Communication style enforcement across response channels**.

The coding agent should add a first-class, runtime-resolved **agent communication profile** that is used by the shared orchestration pipeline across:
- direct chat responses
- task summaries / task output generation
- document or generated output paths

The implementation must ensure:
- prompt payloads include resolved communication profile fields before model invocation
- the same agent’s configured style directives are consistently applied across all supported generation paths
- updated agent configuration is picked up on subsequent requests without service restart
- a default fallback profile is applied when no explicit profile exists, and fallback usage is logged

# Scope
In scope:
- Domain/application/infrastructure changes needed to represent and resolve an agent communication profile
- Persistence/schema updates for communication profile fields
- Prompt-building/orchestration integration so profile fields are injected before model invocation
- Runtime fallback behavior and structured logging
- Automated tests covering acceptance criteria
- Minimal rule-check mechanism for forbidden tone/style violations in generated outputs where current architecture allows

Out of scope unless already trivial in current codebase:
- Full UI/editor experience for managing communication profiles
- Advanced moderation/classification models for style compliance
- Broad refactors unrelated to agent configuration or orchestration
- New external dependencies unless clearly necessary and low-risk

Assumptions to verify in the repo:
- There is already an agent entity/configuration model under Agent Management
- There is a shared orchestration/prompt builder path in Application/Infrastructure
- Chat/task/document generation paths already converge enough to reuse one profile resolution service
- Logging uses Microsoft.Extensions.Logging or equivalent structured logging

# Files to touch
Inspect the solution first, then update the most relevant files in these areas.

Likely areas:
- `src/VirtualCompany.Domain/**`
  - agent aggregate/entity/value objects
  - enums/constants for communication rules
- `src/VirtualCompany.Application/**`
  - agent configuration commands/queries
  - orchestration contracts
  - prompt builder abstractions
  - generation pipeline services
  - validators
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations / persistence mappings
  - repositories
  - prompt builder implementation
  - logging
  - any LLM invocation adapter
- `src/VirtualCompany.Api/**`
  - DTOs if agent config APIs expose these fields
  - request/response contracts if needed
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- potentially additional test projects if present for Application/Infrastructure

Also check for:
- existing migration approach in repo
- any archived migration guidance in `docs/postgresql-migrations-archive/README.md`
- solution-wide conventions in `README.md`

Expected concrete artifacts:
- communication profile model/schema
- fallback/default profile provider
- prompt payload enrichment
- style rule checker
- tests

# Implementation plan
1. **Discover existing architecture and align with conventions**
   - Inspect how agents are currently modeled and persisted.
   - Find the shared orchestration entry points for:
     - chat
     - task generation/summaries
     - document/generated output
   - Identify where prompt payloads are assembled before model invocation.
   - Identify whether agent config is stored as columns, JSONB, or both.
   - Follow existing naming, layering, and test conventions.

2. **Add a communication profile model**
   Introduce a dedicated model for communication identity/profile, ideally as a value object or structured config object, containing at minimum:
   - `Tone`
   - `Persona`
   - `StyleDirectives` or `StyleConstraints`
   - `CommunicationRules`
   - `ForbiddenToneRules` or equivalent
   - optional metadata such as `ProfileSource` or `IsFallback`

   Prefer a shape that is explicit and prompt-ready, for example:
   - tone: concise, empathetic, executive, formal, etc.
   - persona: trusted CFO, pragmatic operator, supportive assistant, etc.
   - style constraints: bullet preference, no slang, no emojis, cite assumptions, etc.
   - forbidden rules: avoid aggressive tone, avoid overpromising, avoid casual slang, etc.

   If the codebase already uses JSON-backed config objects, add this as a nested JSON config under agent configuration rather than scattering many columns.

3. **Persist the profile on agents**
   Update persistence so an agent can store an explicit communication profile.
   Preferred options:
   - add `communication_profile_json` JSONB to `agents`, or
   - extend an existing JSON config field if there is already a suitable structured config object

   Requirements:
   - null/empty profile must be allowed so fallback can apply
   - updates must persist normally and be visible on subsequent reads
   - add validation for malformed/invalid profile content

   If EF Core is used:
   - add entity configuration / owned type / value converter as appropriate
   - add migration if the repo uses migrations in-source

4. **Create a runtime profile resolver**
   Add an application service such as:
   - `IAgentCommunicationProfileResolver`
   - `AgentCommunicationProfileResolver`

   Responsibilities:
   - load the agent’s explicit profile from current persisted configuration
   - if missing, return a default fallback profile
   - annotate whether fallback was used
   - log fallback usage with structured fields:
     - `AgentId`
     - `CompanyId` if available
     - generation path/channel
     - correlation/request id if available

   Important:
   - do not cache in a way that requires restart to see updates
   - if caching exists, ensure it is request-safe and invalidated or bypassed for this config
   - subsequent generations after update must use latest persisted settings

5. **Define a default fallback profile**
   Add a central default profile provider, e.g.:
   - `DefaultAgentCommunicationProfileProvider`

   The fallback should be safe, neutral, and business-appropriate. Example characteristics:
   - tone: professional, clear, helpful
   - persona: reliable business assistant
   - style: concise, structured, factual, avoid unsupported claims
   - forbidden tone: hostile, manipulative, flippant, overly casual, abusive

   Keep this centralized so all channels use the same fallback behavior.

6. **Inject profile into prompt payloads before model invocation**
   Update the shared prompt builder / orchestration pipeline so every supported generation path resolves and includes the communication profile before invoking the model.

   Ensure the prompt payload includes explicit identity profile fields, not just free-form text. If there is a structured prompt object, add fields such as:
   - `AgentIdentityProfile.Tone`
   - `AgentIdentityProfile.Persona`
   - `AgentIdentityProfile.StyleConstraints`
   - `AgentIdentityProfile.CommunicationRules`
   - `AgentIdentityProfile.ForbiddenToneRules`

   Then ensure these are rendered into the final system/runtime prompt in a deterministic section, e.g.:
   - Agent identity
   - Tone and persona
   - Style directives
   - Forbidden tone/rule constraints

   Apply this consistently for:
   - chat response generation
   - task summary/output generation
   - document/generated output generation

   If these paths currently duplicate prompt assembly, extract a shared helper rather than copy/paste.

7. **Add automated rule checks for forbidden tone violations**
   Implement a lightweight post-generation rule checker that validates generated text against configured forbidden tone/style rules.

   Keep this pragmatic:
   - start with deterministic checks based on configured forbidden phrases/patterns/rules if architecture supports it
   - if there is already a response validation pipeline, plug into it
   - return or log violations in a structured way

   Suggested components:
   - `ICommunicationStyleRuleChecker`
   - `CommunicationStyleRuleChecker`
   - result object with:
     - `Passed`
     - `Violations[]`
     - `RuleIds[]`

   Minimum expected behavior:
   - same-agent outputs across chat/task/document paths are validated against the same configured rules
   - forbidden tone violations are detectable in automated tests

   Do not over-engineer semantic tone detection if not already present; deterministic rule enforcement is acceptable for this task.

8. **Ensure config updates are hot-effective**
   Verify that agent profile updates flow through normal persistence and are re-read on subsequent generation requests.
   If there is any singleton snapshot/config cache around agent definitions, fix it so:
   - updates do not require service restart
   - stale profile data is not reused indefinitely

   Add/update tests proving:
   - first generation uses old profile
   - after update, next generation uses new profile

9. **Expose profile fields through relevant contracts**
   If agent create/update/read APIs already exist and are the correct place for this config:
   - add DTO fields for communication profile
   - add validation and mapping
   - preserve backward compatibility where possible

   If UI/API work is not yet present in this task’s slice, at least ensure internal command models support the fields.

10. **Add structured logging**
   Log at appropriate points:
   - fallback profile applied
   - style rule check failed
   - prompt payload built with profile metadata if debug/trace level is appropriate

   Avoid logging full prompt or sensitive content unless existing conventions allow it.
   Prefer structured metadata over raw text.

11. **Add tests**
   Add focused tests for acceptance criteria.

   Minimum test coverage:
   - **Prompt payload inclusion**
     - given an agent with configured tone/persona/rules
     - when chat/task/document generation is requested
     - then prompt payload includes those profile fields before model invocation
   - **Cross-channel consistency**
     - same agent across chat/task/document paths includes same style directives
     - outputs pass rule checks when compliant
   - **Forbidden tone violation detection**
     - generated output containing forbidden phrase/tone marker fails automated rule check
   - **Hot update behavior**
     - update agent communication profile
     - subsequent generation uses new settings without restart
   - **Fallback behavior**
     - no explicit profile
     - fallback profile is used
     - fallback usage is logged

   Prefer integration tests where prompt builder and orchestration wiring can be observed end-to-end; use unit tests for resolver/rule checker.

12. **Keep implementation small and cohesive**
   Favor:
   - one shared profile model
   - one resolver
   - one fallback provider
   - one rule checker
   - one prompt builder integration point

   Avoid:
   - channel-specific profile logic
   - hardcoded style text in controllers/UI
   - restart-dependent config loading

# Validation steps
1. Restore/build/test the solution:
   - `dotnet build`
   - `dotnet test`

2. Verify schema/persistence:
   - confirm agent records can persist communication profile data
   - confirm null profile is allowed and fallback still works

3. Verify prompt payload enrichment:
   - inspect tests or instrumentation to confirm prompt payload contains communication profile fields before model invocation
   - verify this for chat, task, and document/generation paths

4. Verify consistency:
   - same agent profile should produce the same injected style directives across all supported channels

5. Verify update behavior:
   - update an agent’s communication profile
   - invoke generation again
   - confirm new profile is used without restart

6. Verify fallback:
   - generate for an agent with no explicit profile
   - confirm fallback profile is applied
   - confirm structured log entry records fallback usage

7. Verify rule checks:
   - run tests for compliant output
   - run tests for forbidden tone violation detection

8. If migrations are used:
   - generate/apply migration per repo convention
   - ensure tests pass against updated schema

# Risks and follow-ups
- **Risk: prompt assembly is duplicated across channels**
  - Mitigation: extract a shared prompt profile enrichment step and reuse it.

- **Risk: agent config is cached in memory**
  - Mitigation: inspect registry/repository layers and remove or invalidate stale caching for communication profile reads.

- **Risk: style rule checking becomes too subjective**
  - Mitigation: keep v1 deterministic and config-driven; use explicit forbidden phrases/rules rather than fuzzy semantic detection.

- **Risk: API/UI contracts are not yet ready**
  - Mitigation: implement domain/application support first and expose DTOs only where already appropriate.

- **Risk: logging sensitive prompt content**
  - Mitigation: log metadata and fallback/rule-check events, not full prompts or generated text unless existing secure conventions permit it.

Suggested follow-ups after this task:
- add UI support in agent profile management for editing communication profile fields
- add audit visibility for which communication profile was applied per generation
- expand style validation from deterministic rules to richer policy checks if needed
- version communication profiles for future audit/history support