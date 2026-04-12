# Goal
Implement `TASK-11.2.6` for `ST-502` by ensuring the shared orchestration pipeline is cleanly separated from HTTP/UI concerns.

The coding agent should refactor or introduce orchestration abstractions so that:
- orchestration logic lives in application/domain-oriented services, not controllers, endpoints, Blazor components, or mobile/UI layers
- HTTP/API and UI layers only translate requests/responses and invoke orchestration through typed contracts
- orchestration can be executed and tested independently of ASP.NET Core transport concerns
- correlation/context needed by orchestration is passed explicitly via request models or execution context objects, not by directly depending on `HttpContext`, controller state, or UI component state

This task supports the story note: **“Keep orchestration service separate from HTTP/UI concerns.”**

# Scope
In scope:
- Inspect current orchestration-related code paths for single-agent task execution/chat/task delegation entry points
- Identify and remove direct dependencies from orchestration services onto:
  - ASP.NET Core controller types
  - `HttpContext` / `IHttpContextAccessor` where used only for orchestration behavior
  - web/mobile view models or component models
  - transport-specific request/response DTOs if they leak into core orchestration
- Introduce or refine application-layer contracts for orchestration requests/results
- Ensure tenant, actor, agent, task, and correlation data are passed through explicit models
- Keep orchestration result shape suitable for downstream task/audit persistence
- Add/update tests proving orchestration can run without HTTP hosting concerns

Out of scope:
- Building the full orchestration engine from scratch if not already present
- Implementing multi-agent coordination
- Major API redesign unrelated to separation of concerns
- UI feature changes beyond adapting to new contracts
- New persistence schema unless absolutely required for the refactor

# Files to touch
Prioritize inspection and likely edits in these areas, adjusting to actual repository structure:

- `src/VirtualCompany.Application/**/*Orchestration*.cs`
- `src/VirtualCompany.Application/**/*Agent*.cs`
- `src/VirtualCompany.Application/**/*Task*.cs`
- `src/VirtualCompany.Application/**/*Chat*.cs`
- `src/VirtualCompany.Api/**/*.cs`
- `src/VirtualCompany.Web/**/*.cs`
- `src/VirtualCompany.Infrastructure/**/*Orchestration*.cs`
- `src/VirtualCompany.Shared/**/*.cs` if shared contracts currently belong there and need relocation or cleanup
- `tests/VirtualCompany.Api.Tests/**/*.cs`

Also inspect project registration/composition roots:
- `src/VirtualCompany.Api/Program.cs`
- any DI extension files in API/Application/Infrastructure

If present, prefer touching:
- application command/query handlers
- application service interfaces
- infrastructure implementations of orchestration dependencies
- API endpoint/controller mapping code

Avoid placing orchestration business logic in:
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`
- `src/VirtualCompany.Mobile`

# Implementation plan
1. **Discover the current orchestration entry points**
   - Search for orchestration-related classes, methods, and registrations:
     - `Orchestration`
     - `Agent`
     - `Prompt`
     - `ToolExecutor`
     - `Chat`
     - `Task`
   - Trace how a request flows from API/UI into orchestration.
   - Document where HTTP/UI concerns currently leak into orchestration, such as:
     - direct use of `HttpContext`
     - reading claims inside orchestration service
     - returning `IActionResult`, API DTOs, or UI models from orchestration
     - constructing prompts in controllers/components

2. **Define clean application-layer contracts**
   - Create or refine a transport-agnostic request model for single-agent orchestration, for example:
     - `OrchestrationRequest`
     - `OrchestrationExecutionContext`
     - `OrchestrationResult`
   - Include explicit fields as needed by current flow, such as:
     - `CompanyId`
     - `ActorType`
     - `ActorId`
     - `AgentId`
     - `TaskId` or task reference
     - user input / message / task payload
     - correlation ID
     - timestamp if needed
   - Keep these contracts in `VirtualCompany.Application` unless there is a strong existing convention otherwise.
   - Ensure result contracts contain only business/application data, not HTTP status or UI formatting concerns.

3. **Move orchestration behavior behind an application service interface**
   - Introduce or refine an interface such as `IOrchestrationService` in the application layer.
   - The interface should expose a method like:
     - `Task<OrchestrationResult> ExecuteAsync(OrchestrationRequest request, CancellationToken cancellationToken)`
   - If there are distinct use cases, keep them explicit but still transport-agnostic:
     - direct chat turn
     - single-agent task execution
   - Ensure the interface does not depend on ASP.NET Core packages or UI assemblies.

4. **Refactor API layer to become a thin adapter**
   - Update controllers/endpoints to:
     - resolve tenant/user/correlation data from HTTP/auth context
     - map incoming API DTOs to `OrchestrationRequest`
     - call the application service
     - map `OrchestrationResult` to API response DTOs
   - Keep validation at the boundary where appropriate, but do not embed orchestration logic in controllers.
   - If correlation IDs are currently implicit, extract them in API and pass them explicitly.

5. **Refactor UI layer to avoid orchestration logic**
   - Inspect Blazor and any shared UI-facing services for prompt building or orchestration decision logic.
   - Move any business/orchestration logic into application services.
   - UI should only:
     - collect input
     - call backend/API or application-facing client abstractions
     - render results

6. **Remove HTTP-specific dependencies from orchestration implementation**
   - In orchestration service implementations, replace any use of:
     - `HttpContext`
     - claims principal access
     - request headers
     - route/query parsing
   - Instead consume explicit values from the request/context object.
   - If tenant/user resolution is needed, do it before orchestration is invoked.

7. **Preserve architecture boundaries**
   - Keep responsibilities aligned with the architecture:
     - Application: orchestration coordination contracts/use cases
     - Infrastructure: LLM provider adapters, tool execution adapters, persistence implementations
     - API/Web/Mobile: transport and presentation only
   - Do not let infrastructure or API DTOs leak upward into application contracts.

8. **Add or update tests**
   - Add unit tests for orchestration service using pure application contracts.
   - Add API tests verifying endpoint mapping still works after refactor.
   - Prefer tests that prove orchestration can execute without an HTTP runtime, e.g.:
     - instantiate service with mocks/fakes
     - pass explicit `OrchestrationRequest`
     - assert result and downstream calls
   - If existing tests are API-only, add at least one lower-level test around the application service boundary.

9. **Keep changes minimal but complete**
   - Refactor only what is necessary to satisfy separation of concerns.
   - Avoid broad renames or speculative abstractions unless required by current code smell.
   - Preserve existing behavior and response semantics where possible.

10. **Document with code comments only where necessary**
   - Add concise comments only if a boundary decision is non-obvious.
   - Prefer self-explanatory naming over heavy comments.

# Validation steps
1. Build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Perform targeted verification in code review:
   - Confirm orchestration service interfaces and implementations do not reference:
     - `Microsoft.AspNetCore.*`
     - `HttpContext`
     - controller base classes
     - Blazor component types
   - Confirm API/Web layers only map to/from application contracts
   - Confirm correlation/tenant/actor data is passed explicitly

4. If API tests exist, verify:
   - endpoint/controller still returns expected response shape
   - orchestration invocation is delegated through application service

5. If application tests are added, verify:
   - orchestration can be invoked with a plain request object and mocks/fakes
   - no HTTP hosting setup is required

# Risks and follow-ups
- **Risk: hidden coupling to HTTP context**
  - Existing services may rely on claims, headers, or request services indirectly.
  - Follow-up: introduce a small execution context object and migrate remaining callers incrementally if needed.

- **Risk: DTO duplication**
  - Separating API DTOs from application contracts may introduce similar-looking models.
  - This is acceptable if it preserves clean boundaries.

- **Risk: misplaced shared contracts**
  - `VirtualCompany.Shared` may currently contain transport and business models mixed together.
  - Follow-up: consider a later cleanup task to clarify contract ownership across projects.

- **Risk: incomplete separation in prompt/tool code**
  - Prompt building or tool selection may still be partially embedded in API/UI helpers.
  - Follow-up: extract those into application/infrastructure services if discovered.

- **Risk: test coverage gaps**
  - If orchestration currently lacks application-layer tests, regressions may slip through.
  - Follow-up: add focused tests for request mapping, correlation propagation, and result shaping.

- **Follow-up recommendation**
  - After this task, consider a small architecture hardening task to standardize:
    - orchestration request/result contracts
    - actor/tenant execution context
    - correlation ID propagation across task, tool execution, and audit records