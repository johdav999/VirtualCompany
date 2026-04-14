# Goal

Implement backlog task **TASK-2.3.3 — Add non-production prompt preview endpoint with redaction for sensitive fields** for story **US-2.3 ST-A303 — Identity-driven prompt shaping with role, seniority, and behavior patterns**.

Deliver a safe internal-only prompt preview/debug capability that lets developers inspect the **resolved identity section** used by the prompt builder in **non-production environments only**, while ensuring sensitive fields are redacted before exposure.

This work must satisfy these outcomes:

- Prompt assembly includes a **structured identity section** containing:
  - agent role
  - seniority
  - business responsibility
  - collaboration norms
  - personality traits
- Prompt builder produces **different prompt payloads** for agents with different identity configurations while preserving **shared system safety instructions**
- A **non-production-only preview/debug endpoint** exposes the resolved identity section for internal inspection
- Unit tests verify **composition precedence** when **tenant policy**, **agent identity**, and **task context** overlap

# Scope

In scope:

- Extend the prompt-building model to explicitly represent a structured identity section
- Ensure prompt composition merges:
  - shared system safety instructions
  - tenant policy instructions
  - agent identity instructions
  - task context instructions
- Add a non-production API endpoint to preview the resolved prompt or prompt sections
- Redact sensitive fields in preview/debug output
- Restrict preview endpoint to non-production environments only
- Add/extend unit tests for:
  - identity section composition
  - prompt differences across agent identities
  - shared safety instruction preservation
  - precedence rules across tenant policy, agent identity, and task context
  - redaction behavior
  - environment gating

Out of scope:

- Production-facing prompt inspection UX
- Full audit UI for prompt previews
- Persisting raw prompt previews to business audit tables unless already supported by existing patterns
- Any change that exposes chain-of-thought or internal reasoning
- Broad refactors outside the prompt builder/orchestration/API boundary needed for this task

# Files to touch

Inspect the solution first and adjust to actual structure, but expect to touch files in these areas:

- `src/VirtualCompany.Application/...`
  - prompt builder/orchestration services
  - DTOs/models for prompt composition
  - tenant policy / agent identity / task context merge logic
- `src/VirtualCompany.Domain/...`
  - value objects or domain models for agent identity if they belong in domain
- `src/VirtualCompany.Api/...`
  - new non-production preview endpoint/controller or minimal API mapping
  - environment guard
  - request/response contracts if API-owned
- `src/VirtualCompany.Infrastructure/...`
  - only if redaction helpers, configuration access, or service wiring belongs here
- `src/VirtualCompany.Shared/...`
  - shared contracts only if already the established pattern
- `tests/VirtualCompany.Api.Tests/...`
  - endpoint tests, environment gating tests, redaction tests
- Add corresponding application/domain test projects if they already exist and are the better home for prompt builder unit tests

Also review:

- `README.md`
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`

# Implementation plan

1. **Discover existing prompt/orchestration implementation**
   - Locate the current prompt builder, orchestration service, agent configuration models, and any existing debug/diagnostic endpoints
   - Identify where these concepts currently live:
     - shared system instructions
     - tenant policy instructions
     - agent identity/persona
     - task context
   - Do not invent parallel abstractions if a prompt model already exists; extend the current one

2. **Introduce a structured identity section**
   - Add or extend a prompt composition model so the identity portion is explicit and structured rather than ad hoc free text
   - The identity section must include:
     - `Role`
     - `Seniority`
     - `BusinessResponsibility`
     - `CollaborationNorms`
     - `PersonalityTraits`
   - If the current agent model stores these in different shapes, normalize them during prompt composition rather than forcing a large persistence refactor unless necessary
   - Preserve compatibility with existing prompt generation flow

3. **Refine prompt composition layering**
   - Make prompt assembly deterministic and testable
   - Explicitly compose prompt content in a clear order, for example:
     1. shared system safety instructions
     2. tenant policy instructions
     3. structured agent identity section
     4. task-specific context/instructions
   - Implement or clarify precedence rules for overlapping instructions
   - Favor a design where:
     - shared safety instructions are always preserved
     - tenant policy can constrain agent behavior
     - task context can narrow execution for the current task
     - agent identity shapes tone/role behavior but does not override safety/policy constraints
   - Document the actual precedence in code comments and tests

4. **Add preview/debug response model**
   - Create a response contract for prompt preview that exposes only what is needed for internal inspection
   - Include:
     - resolved identity section
     - resolved prompt sections or final prompt payload
     - indication of redacted fields if useful
   - Do not expose chain-of-thought or hidden reasoning artifacts
   - Prefer sectioned output over opaque raw strings if the current architecture supports it

5. **Implement sensitive field redaction**
   - Add a redaction layer for preview/debug output
   - Redact sensitive values from preview output, especially if present in:
     - tenant policy secrets/config
     - API keys/tokens
     - credentials
     - connection strings
     - authorization headers
     - secret tool parameters
     - PII or confidential fields if they can flow into prompt preview
   - If there is an existing redaction utility or logging sanitizer, reuse it
   - Keep redaction centralized so tests can target it directly
   - Redaction should apply before serialization/response emission

6. **Add non-production-only endpoint**
   - Add an internal preview endpoint in `VirtualCompany.Api`
   - Gate it so it is unavailable in production
   - Prefer a hard environment check using `IHostEnvironment` / ASP.NET Core environment semantics
   - Return:
     - `404` or `403` in production depending on existing API conventions; prefer the convention already used in the codebase
   - Ensure tenant scoping and authorization patterns are respected if the endpoint resolves real tenant/agent/task data
   - Keep the endpoint clearly marked as debug/internal

7. **Wire endpoint to prompt builder**
   - Endpoint should resolve the same prompt composition path used by runtime orchestration, not a duplicate implementation
   - It should be able to preview prompt composition for a given agent/task/tenant context
   - If needed, add an application service method like `PreviewPromptAsync(...)` that returns a safe preview model

8. **Add unit tests for prompt composition**
   - Cover structured identity section inclusion
   - Verify different agent identity configurations produce different prompt payloads
   - Verify shared safety instructions remain present across variants
   - Verify precedence behavior for overlapping instructions from:
     - tenant policy
     - agent identity
     - task context
   - Make tests explicit and readable; use representative overlapping instruction examples

9. **Add tests for preview endpoint and redaction**
   - Non-production environment: endpoint returns preview successfully
   - Production environment: endpoint is blocked
   - Sensitive fields are redacted in response payload
   - Resolved identity section is visible in non-production response
   - If API tests are integration-style, use the existing test host/environment override patterns

10. **Keep implementation aligned with architecture**
   - Respect clean boundaries:
     - API handles transport and environment gating
     - Application handles prompt preview orchestration
     - Domain/application models hold prompt composition semantics
   - Avoid putting prompt assembly logic in controllers
   - Avoid direct DB access from API layer

11. **Document assumptions in code**
   - Add concise comments where precedence rules or environment gating may be non-obvious
   - If there is a developer docs area for internal endpoints, add a short note only if the repo already maintains such docs

# Validation steps

1. Inspect and restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run targeted and full tests:
   - `dotnet test`

4. Manually verify behavior in a non-production environment:
   - Start API in `Development` or `Staging`
   - Call the new prompt preview endpoint with representative tenant/agent/task inputs
   - Confirm response includes:
     - structured identity section
     - preserved shared safety instructions
     - redacted sensitive fields

5. Manually verify production gating:
   - Run API with `ASPNETCORE_ENVIRONMENT=Production`
   - Confirm the endpoint is unavailable per chosen convention

6. Review code for boundary correctness:
   - no prompt assembly logic duplicated in controller
   - no raw secrets returned
   - no production exposure path

7. Ensure acceptance criteria traceability:
   - identity section fields present
   - prompt differs by agent identity
   - preview available only in non-production
   - precedence tests added for tenant policy vs agent identity vs task context

# Risks and follow-ups

- **Risk: unclear existing prompt model**
  - The codebase may already have prompt composition as plain strings. If so, introduce the smallest structured abstraction necessary without overengineering.

- **Risk: redaction scope ambiguity**
  - Sensitive fields may come from multiple sources. Prefer a conservative redaction strategy and centralize it so future fields can be added easily.

- **Risk: environment gating inconsistency**
  - If the API already uses feature flags or internal auth for debug endpoints, align with that pattern instead of introducing a conflicting mechanism.

- **Risk: precedence semantics may be disputed**
  - If current behavior is undocumented, encode the most defensible rule set:
    - safety > tenant policy > task context constraints > agent identity style/persona
  - If the codebase already implies another order, preserve it and document it in tests.

- **Risk: leaking internal prompt details**
  - Keep preview output limited to operationally useful sections and never expose hidden reasoning artifacts.

Follow-ups to note if not completed in this task:

- Add a reusable internal diagnostics policy for future non-production endpoints
- Add structured audit metadata for prompt preview access if internal diagnostics need traceability
- Add developer documentation for prompt composition precedence and redaction rules
- Consider feature-flagging preview behavior in addition to environment gating if staging environments are shared