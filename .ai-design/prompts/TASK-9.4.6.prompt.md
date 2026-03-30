# Goal
Implement backlog task **TASK-9.4.6 — Avoid direct prompt assembly in UI or controllers** for story **ST-304 Grounded context retrieval service**.

The coding agent should refactor the codebase so that:
- UI layers and HTTP controllers/endpoints do **not** concatenate or assemble prompt text directly.
- Prompt-ready context composition is centralized in the **application/orchestration layer**, aligned with the architecture’s **Context Retriever** and **Prompt Builder** responsibilities.
- Any existing chat/task entry points call a dedicated service that returns **structured context sections** or a prompt input model, rather than building strings inline.
- The result supports deterministic, testable grounded retrieval and keeps HTTP/UI concerns separate from orchestration concerns.

Because this task has no explicit acceptance criteria, treat the story notes and architecture as the source of truth:
- “Avoid direct prompt assembly in UI or controllers.”
- Retrieval should be deterministic and testable.
- Returned context should be normalized into structured prompt-ready sections.
- Retrieval should respect tenant/company and agent scope boundaries.

# Scope
In scope:
- Inspect the solution for any direct prompt construction in:
  - ASP.NET Core controllers/endpoints in `VirtualCompany.Api`
  - Blazor components/pages/services in `VirtualCompany.Web`
  - any thin UI-facing application adapters that currently assemble prompt strings
- Introduce or strengthen an application-layer abstraction for prompt/context preparation, likely in `VirtualCompany.Application`
- Move prompt assembly responsibilities out of UI/controllers into a dedicated service such as:
  - `IContextRetrievalService`
  - `IPromptContextService`
  - `IPromptBuilder`
  - or equivalent existing abstraction already present in the codebase
- Ensure the service returns structured data, not just raw concatenated text, where feasible
- Update callers to use the centralized service
- Add or update tests covering the separation of concerns

Out of scope unless required by existing code:
- Reworking the entire orchestration engine
- Changing LLM provider integrations beyond what is needed for this refactor
- Broad UI redesign
- New retrieval algorithms unrelated to removing prompt assembly from UI/controllers

# Files to touch
Start by inspecting these areas, then adjust based on actual code discovered.

Likely files/folders:
- `src/VirtualCompany.Api/**`
  - controllers, minimal API endpoint mappings, request handlers
- `src/VirtualCompany.Web/**`
  - Blazor pages/components
  - UI services that may currently prepare prompts before API calls
- `src/VirtualCompany.Application/**`
  - orchestration services
  - chat/task handlers
  - commands/queries
  - interfaces for retrieval/prompt building
- `src/VirtualCompany.Infrastructure/**`
  - DI registrations
  - concrete retrieval/prompt builder implementations if they live here
- `src/VirtualCompany.Domain/**`
  - value objects/models if a structured prompt context model belongs here or in Application
- tests projects, if present under `tests/**` or equivalent

Potential artifacts to add:
- A structured model such as:
  - `PromptContext`
  - `PromptSection`
  - `GroundedContextResult`
  - `PromptAssemblyRequest`
- A service interface and implementation such as:
  - `IPromptContextAssembler`
  - `IGroundedContextService`
  - `PromptBuilderService`
- Unit tests verifying:
  - controllers/UI no longer assemble prompts
  - application service composes prompt-ready sections
  - deterministic ordering/formatting of sections

# Implementation plan
1. **Discover current prompt assembly paths**
   - Search the repo for likely indicators:
     - `"system prompt"`
     - `"prompt"`
     - string interpolation involving user message + context
     - concatenation of role brief, memory, task, company context, documents, etc.
   - Identify all places where prompt text is assembled in:
     - API controllers/endpoints
     - Blazor code-behind/components/services
   - Document each call path before changing it.

2. **Identify the correct architectural home**
   - Prefer placing the abstraction in `VirtualCompany.Application`, since this is orchestration/application logic rather than UI logic.
   - If an orchestration service already exists, extend it instead of creating a parallel abstraction.
   - Keep infrastructure-specific concerns behind interfaces.

3. **Define a structured prompt/context contract**
   - Introduce a model that represents prompt-ready context in normalized sections, for example:
     - agent identity/role
     - company context
     - current task/request
     - memory snippets
     - retrieved knowledge/document snippets
     - recent history
     - policy/tool context
   - Avoid making the UI/controller aware of formatting rules.
   - Ensure the model is deterministic:
     - stable section ordering
     - explicit labels/types
     - bounded collections where applicable

4. **Move prompt assembly into a dedicated service**
   - Create or refactor a service responsible for converting structured context into final prompt input for the LLM pipeline.
   - If the codebase already separates retrieval from prompt building, preserve that split:
     - retrieval service gathers normalized context
     - prompt builder formats it
   - If not, minimally introduce a service that centralizes the assembly while keeping future separation easy.

5. **Refactor controllers/endpoints**
   - Replace any inline prompt string construction with calls to the application-layer service.
   - Controllers should only:
     - validate request
     - resolve tenant/user/agent/task identifiers
     - call application service
     - return response
   - Do not leave fallback prompt concatenation in controllers.

6. **Refactor Blazor/UI code**
   - Remove any prompt-building logic from components or UI services.
   - UI should send user intent/input and identifiers, not assembled prompt text.
   - If the UI currently calls the LLM layer directly, route through the proper backend/application service if that is already the project pattern.
   - Keep UI focused on presentation and request dispatch.

7. **Wire dependency injection**
   - Register any new interfaces/implementations in the appropriate composition root.
   - Ensure API and Web projects consume the abstraction through existing application services.

8. **Preserve tenant and scope boundaries**
   - Verify the centralized service accepts enough context to enforce:
     - company/tenant scope
     - agent scope
     - user permissions where relevant
   - Do not move security-sensitive filtering into the UI.

9. **Add tests**
   - Unit test the new service for:
     - deterministic section ordering
     - correct inclusion/exclusion of sections
     - no null/empty section noise if that matters to current conventions
   - Add application tests for the endpoint/handler path if test infrastructure exists.
   - If there were existing tests asserting prompt strings in controllers/UI, update them to assert service usage or output contract instead.

10. **Keep the refactor minimal and coherent**
   - Do not over-engineer a full prompt framework if the codebase is still early.
   - The key outcome is architectural separation and centralized prompt/context assembly.

# Validation steps
1. **Static inspection**
   - Confirm no controllers/endpoints in `src/VirtualCompany.Api` directly concatenate prompt text.
   - Confirm no Blazor components/services in `src/VirtualCompany.Web` directly assemble prompts.
   - Search again for suspicious prompt-building patterns after refactor.

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Behavior verification**
   - Exercise the relevant chat/task/context retrieval flow and confirm it still works.
   - Verify the request path now goes through the centralized application/orchestration service.

5. **Code quality checks**
   - Ensure new abstractions are named consistently with existing project conventions.
   - Ensure no infrastructure or provider-specific logic leaked into UI/controllers.
   - Ensure structured context models are serializable/testable if they cross boundaries.

# Risks and follow-ups
- **Risk: hidden prompt assembly in multiple layers**
  - There may be prompt concatenation in helper classes or UI services, not just controllers. Search broadly before concluding the refactor is complete.

- **Risk: over-centralizing too much too soon**
  - Avoid turning this task into a full orchestration redesign. Keep changes focused on moving assembly out of UI/controllers.

- **Risk: breaking existing prompt behavior**
  - If current prompt formatting affects outputs, preserve semantics while relocating the logic. Add regression tests around section content/order.

- **Risk: unclear boundary between retrieval and prompt building**
  - If the codebase currently mixes them, introduce a pragmatic seam now and leave a clean follow-up path.

Suggested follow-ups after this task:
- Add explicit story-level acceptance tests for grounded context retrieval
- Separate `Context Retriever` and `Prompt Builder` if currently combined
- Persist retrieval source references for audit/explainability if not already implemented
- Add caching for low-risk repeated retrievals in Redis per ST-304 notes
- Add analyzers or architecture tests to prevent prompt assembly in UI/controller projects going forward