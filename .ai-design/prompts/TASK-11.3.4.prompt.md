# Goal

Implement **TASK-11.3.4** for **ST-503 Policy-enforced tool execution** by ensuring **internal tools invoke domain/application capabilities through typed contracts instead of direct database access**.

This work should strengthen the architecture boundary around tool execution so that:

- tool handlers do **not** query or mutate persistence directly
- tool handlers call into **application/domain module contracts**
- contracts are **typed**, explicit, and tenant-aware
- policy-enforced execution remains the only path for agent-driven internal actions
- the design supports future auditability, approvals, and module extraction

Because this task sits under **policy-enforced tool execution**, prefer a design that makes internal tools look like controlled adapters over application services, not mini-repositories or ad hoc SQL callers.

# Scope

In scope:

- Identify current or emerging internal tool execution paths in the orchestration/tooling area
- Introduce or refine **typed request/response contracts** for internal tool calls
- Route internal tool implementations through **application-layer interfaces/services** rather than DB/infrastructure access
- Preserve tenant scoping and action typing (`read`, `recommend`, `execute`) in the contract flow
- Keep contracts structured enough for policy checks, audit metadata, and safe tool responses
- Add or update tests proving tools use contracts and return structured results

Out of scope unless required to complete this task cleanly:

- Large-scale redesign of the entire orchestration subsystem
- New external connector implementations
- Full approval workflow expansion beyond what is needed for contract shape compatibility
- Broad schema changes unless a minimal persistence adjustment is necessary
- UI/mobile work

If the codebase does not yet have a mature tool execution implementation, create the **minimum vertical slice** needed to establish the pattern and leave clear extension points.

# Files to touch

Prioritize these projects and likely areas:

- `src/VirtualCompany.Application/`
  - add typed internal tool contracts and application-facing interfaces
  - add command/query handlers or service abstractions if missing
- `src/VirtualCompany.Domain/`
  - add domain value objects/enums only if needed for shared policy-safe types
- `src/VirtualCompany.Infrastructure/`
  - remove/avoid any direct persistence usage from tool handlers
  - wire DI for contract implementations
- `src/VirtualCompany.Api/`
  - only if composition root or endpoint wiring must be updated
- `src/VirtualCompany.Shared/`
  - place shared DTOs only if they truly need cross-project reuse; otherwise prefer `Application`
- `tests/VirtualCompany.Api.Tests/`
  - integration/behavior tests for tool execution path
- potentially add/update tests in a corresponding application test project if one exists

Also inspect:

- orchestration/tool executor classes
- internal tool registry/provider classes
- policy guardrail execution path
- any existing repositories being called from tool handlers
- DI registration files
- existing command/query patterns

Do **not** touch mobile/web UI unless required by compilation.

# Implementation plan

1. **Inspect the current tool execution architecture**
   - Find the classes responsible for:
     - tool registration
     - tool invocation
     - policy checks
     - internal tool implementations
   - Identify any places where a tool directly uses:
     - EF `DbContext`
     - repositories
     - SQL/query objects from infrastructure
   - Document the current flow in code comments or PR notes mentally before changing it.

2. **Define the target contract pattern**
   - Internal tools should call a typed application contract shaped roughly like:
     - `ToolContext` / `InternalToolExecutionContext`
     - typed request DTO
     - typed response DTO
   - The contract should carry at least:
     - `CompanyId`
     - initiating `AgentId` if available
     - correlation/execution id if available
     - action type (`read`, `recommend`, `execute`)
     - tool-specific payload
   - Response should be structured and safe for orchestration use:
     - result data
     - user-safe summary/message
     - optional metadata for audit/policy linkage

3. **Introduce application-layer module contracts**
   - For each internal capability used by tools, expose a typed interface in `Application`, not `Infrastructure`.
   - Examples of acceptable patterns:
     - `IInternalTaskTools`
     - `IInternalApprovalTools`
     - `IInternalKnowledgeTools`
     - `I[Module]ToolContract`
   - These interfaces should represent business capabilities, not persistence concerns.
   - Avoid generic “service locator” contracts; keep them module-oriented and explicit.

4. **Refactor tool handlers to depend on contracts**
   - Update internal tool implementations so they:
     - accept typed request objects
     - call application contracts
     - return structured responses
   - Remove direct DB access from tool handlers.
   - If a tool currently assembles SQL-shaped data, move that logic behind an application query/service.

5. **Keep policy enforcement before contract invocation**
   - Ensure the execution flow remains:
     1. tool request created
     2. policy evaluated
     3. allowed request mapped to typed contract
     4. application contract invoked
     5. structured result persisted/returned
   - Do not let contracts bypass policy checks.
   - If needed, add guard clauses or naming that makes this boundary obvious.

6. **Preserve tenant isolation explicitly**
   - Typed contracts must require tenant/company context.
   - Avoid optional tenant parameters for tenant-owned operations.
   - Ensure downstream application handlers use the provided company scope and do not infer it from ambient state alone.

7. **Align with CQRS-lite where practical**
   - Reads should go through query-style contracts/services.
   - Mutations should go through command-style contracts/services.
   - If the codebase already uses MediatR or a similar pattern, integrate with it rather than inventing a parallel abstraction.
   - If not, use straightforward application interfaces with typed methods.

8. **Support structured tool execution persistence**
   - Ensure the tool execution layer can still persist:
     - tool name
     - action type
     - request payload
     - response payload
     - policy decision metadata
     - status
   - If request/response serialization changes due to typed contracts, update mapping/serialization cleanly.

9. **Add tests for the architectural behavior**
   - Add tests that verify:
     - internal tool execution succeeds through typed contracts
     - denied policy paths do not invoke the contract
     - tool responses are structured and safe
     - tenant-scoped requests are passed through correctly
   - If feasible, use mocks/fakes to prove the tool handler depends on application contracts rather than infrastructure persistence.

10. **Keep the implementation minimal but extensible**
   - If only one or two internal tools exist, refactor those as exemplars and establish the pattern for future tools.
   - Add concise comments where the boundary may otherwise be unclear.

# Validation steps

Run and verify at minimum:

1. Build:
   - `dotnet build`

2. Tests:
   - `dotnet test`

3. Targeted validation in code/tests:
   - confirm internal tool classes no longer inject `DbContext`, repositories, or infrastructure query types directly
   - confirm application contracts are typed and tenant-aware
   - confirm policy denial prevents downstream contract invocation
   - confirm allowed execution returns structured response payloads
   - confirm serialization/persistence of tool execution request/response still works

4. If there are existing API/integration tests around orchestration:
   - run them and update only where contract-driven behavior changes expected payload shape

# Risks and follow-ups

- **Risk: over-abstracting too early**
  - Avoid building a huge generic tool framework. Prefer a small number of explicit typed contracts.

- **Risk: leaking infrastructure concerns into contracts**
  - Contracts should express business operations, not tables, repositories, or EF entities.

- **Risk: bypassing policy through direct application service usage**
  - Keep tool execution orchestration as the enforced entry point for agent actions.

- **Risk: tenant scope inconsistencies**
  - Make `CompanyId` mandatory in contract requests for tenant-owned operations.

- **Risk: response DTOs exposing too much internal detail**
  - Return structured, user-safe results and metadata only; no raw reasoning or persistence internals.

Follow-ups to note in code/TODOs if not completed here:

- standardize a shared `ToolExecutionContext` type across all tools
- add architectural tests preventing tool-layer references to infrastructure persistence
- expand the typed-contract pattern across all internal modules used by orchestration
- align tool contract result metadata with audit/explainability and approval creation flows