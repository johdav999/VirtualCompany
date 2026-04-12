# Goal
Implement **TASK-11.2.5 / ST-502 note**: ensure the orchestration system uses **one shared engine** with **distinct agent configuration inputs**, and does **not** introduce bespoke orchestration stacks, pipelines, or role-specific execution paths per agent role.

The coding agent should make the shared-engine design explicit in code structure and contracts so that:
- all single-agent orchestration flows go through the same application/service pipeline,
- agent-specific behavior is driven by persisted configuration and runtime context,
- role/persona differences are represented as data, not separate implementations,
- the design remains aligned with the modular monolith, clean boundaries, and policy-enforced orchestration architecture.

# Scope
In scope:
- Review the current orchestration-related code paths for single-agent task execution/chat/task handling.
- Refactor or introduce shared abstractions so orchestration is centralized behind a single engine/service.
- Ensure agent-specific inputs come from agent configuration fields such as role brief, personality, objectives, KPIs, tool permissions, scopes, thresholds, escalation rules, and autonomy level.
- Remove or prevent role-specific branching that creates effectively separate stacks for finance/sales/support/etc.
- Add tests that prove multiple agent roles use the same orchestration pipeline with different configs.
- Update any relevant documentation/comments to clarify the intended architecture.

Out of scope:
- Building full multi-agent coordination.
- Adding new external integrations or new tool categories unless required to preserve the shared-engine contract.
- UI redesigns.
- Large schema redesigns unless a minimal persistence adjustment is required.
- Implementing missing acceptance criteria beyond what is necessary to satisfy this task and ST-502 alignment.

# Files to touch
Prioritize inspection and likely edits in these areas, adjusting to the actual repository structure:

- `src/VirtualCompany.Application/**`
  - orchestration application services
  - commands/queries/handlers related to agent chat or single-agent task execution
  - DTOs/contracts for orchestration requests and responses
- `src/VirtualCompany.Domain/**`
  - agent aggregate/config models
  - orchestration domain contracts/value objects
  - policy/tool execution abstractions if shared-engine concerns belong here
- `src/VirtualCompany.Infrastructure/**`
  - LLM provider adapters
  - tool executor implementations
  - persistence/repository implementations used by orchestration
- `src/VirtualCompany.Api/**`
  - endpoints/controllers that currently invoke orchestration
  - DI registration/composition root
- `src/VirtualCompany.Shared/**`
  - shared contracts only if already used for cross-project orchestration DTOs
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests covering orchestration entry points
- potentially add/update docs:
  - `README.md`
  - architecture notes near orchestration code
  - inline XML/code comments where useful

If present, specifically look for files/classes with names like:
- `OrchestrationService`
- `AgentOrchestrator`
- `PromptBuilder`
- `ContextRetriever`
- `ToolExecutor`
- `PolicyGuardrailEngine`
- `ChatService`
- `TaskExecutionService`
- role-specific services such as `FinanceAgentService`, `SalesAgentService`, etc.

# Implementation plan
1. **Inspect current orchestration paths**
   - Identify all entry points for single-agent execution:
     - direct chat with an agent,
     - task execution assigned to an agent,
     - any background-triggered single-agent run.
   - Map whether they already converge on one service or are split by role/use case.
   - Identify any role-specific classes, switches, factories, or conditional branches that alter orchestration flow by agent role.

2. **Define or tighten the shared orchestration contract**
   - Introduce or standardize a single application-facing interface for single-agent orchestration, for example:
     - `ISingleAgentOrchestrationService`
     - or a clearly named existing equivalent.
   - The request contract should carry normalized inputs such as:
     - `CompanyId`
     - `AgentId`
     - correlation ID
     - conversation/task context
     - user input / task payload
     - execution mode if needed
   - The response contract should include:
     - user-facing output,
     - structured artifacts,
     - tool execution metadata references or summaries,
     - rationale summary if already part of the design.

3. **Centralize orchestration flow**
   - Ensure the shared service performs the common sequence:
     1. resolve target agent,
     2. load agent configuration,
     3. retrieve scoped runtime context,
     4. build prompt from shared builder,
     5. invoke model/provider through shared adapter,
     6. execute tools through shared tool executor and policy checks,
     7. produce structured result,
     8. persist/audit as appropriate.
   - Keep this flow independent of role names.
   - If different entry points need small variations, model them as request parameters/options, not separate role pipelines.

4. **Move role behavior into configuration**
   - Ensure role/persona differences are sourced from agent data/config, not code branches.
   - Prefer using existing agent fields from architecture/backlog:
     - role brief,
     - personality JSON,
     - objectives/KPIs,
     - tool permissions,
     - data scopes,
     - approval thresholds,
     - escalation rules,
     - autonomy level.
   - If current code hardcodes role instructions, migrate them into:
     - agent template defaults,
     - agent records,
     - or prompt-building inputs derived from agent config.
   - Avoid introducing classes like `FinancePromptBuilder` or `SupportExecutionPipeline` unless they are generic strategy abstractions not tied to role identity.

5. **Eliminate bespoke per-role stacks**
   - Remove or refactor any code that selects different orchestration implementations based on role/department.
   - Acceptable variation:
     - different config values,
     - different allowed tools,
     - different policy outcomes,
     - different retrieval scopes.
   - Not acceptable:
     - separate orchestration services per role,
     - separate prompt pipelines per role,
     - separate tool execution stacks per role without a generic capability-based reason.

6. **Preserve clean architecture boundaries**
   - Keep HTTP/UI concerns out of orchestration internals.
   - Keep infrastructure provider details behind interfaces.
   - Ensure domain/application layers do not depend directly on web/API concerns.
   - Register the shared orchestration service once in DI and route all relevant callers through it.

7. **Add tests proving shared-engine behavior**
   - Add tests that demonstrate at least two distinct agent roles/configurations use the same orchestration service path.
   - Example test intent:
     - finance and support agents produce different prompt inputs because of config,
     - but both invoke the same orchestrator class and same tool/policy pipeline.
   - Add tests for:
     - no role-specific implementation resolution,
     - prompt builder receives agent config values,
     - tool execution/policy evaluation is capability/config-driven,
     - correlation ID is preserved if already supported in ST-502 code.
   - Prefer focused unit tests in application/domain layers plus one integration/API test if orchestration is exposed there.

8. **Document the architectural rule**
   - Add concise documentation/comments stating:
     - the platform uses one shared orchestration engine,
     - named agents differ by configuration and permissions,
     - bespoke stacks per role are intentionally disallowed.
   - Keep docs short and implementation-oriented.

9. **Keep changes incremental and safe**
   - Do not over-engineer.
   - Reuse existing abstractions where possible.
   - If a larger refactor is needed, prefer a compatibility layer so existing callers continue to work while routing into the shared engine.

# Validation steps
Run and report relevant results:

1. Build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If orchestration tests are targeted separately, run those too if practical.

4. Manually verify in code review that:
   - there is a single orchestration entry service for single-agent flows,
   - no role-specific orchestration implementations remain,
   - agent role differences are represented via configuration inputs,
   - API/controllers/background handlers call the shared service rather than bespoke role services.

5. In the final implementation summary, explicitly list:
   - the shared orchestration interface/class used,
   - any removed/refactored role-specific paths,
   - the tests added/updated to prove shared-engine behavior.

# Risks and follow-ups
- **Risk: hidden role-specific logic** may exist in prompt builders, factories, or DI registration even if top-level services look shared. Search thoroughly for role/department switches and named registrations.
- **Risk: over-refactor** could destabilize adjacent ST-502 work. Keep the change focused on consolidating orchestration paths.
- **Risk: config gaps** may surface where role behavior was previously hardcoded. If so, add minimal config mapping/defaulting rather than reintroducing bespoke code.
- **Risk: test brittleness** if tests assert exact prompt text. Prefer asserting structured prompt sections/inputs and shared service usage.

Follow-ups to note if not completed here:
- seed/template cleanup so all role defaults live in template/config data,
- stronger architectural tests preventing new role-specific orchestrators from being added,
- broader consolidation across future multi-agent flows so manager-worker orchestration also builds on the same shared primitives.