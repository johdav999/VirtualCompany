# Goal
Implement backlog task **TASK-11.1.3** for **ST-501 Direct chat with named agents** so that **agent chat responses are generated using the selected agent’s configured persona, role brief, and scoped context**.

This work should ensure the direct-chat pipeline resolves the addressed agent, loads that agent’s runtime profile, retrieves only allowed context for that agent and tenant, and passes those inputs through the shared orchestration path so the final response reflects the selected named agent rather than a generic assistant.

# Scope
In scope:
- Direct agent chat request handling for the web/API path.
- Resolving the selected agent from the conversation or request.
- Loading agent configuration needed for runtime behavior:
  - display identity if needed
  - persona/personality config
  - role brief
  - scoped permissions/data scopes relevant to retrieval
- Building runtime orchestration input for direct chat using:
  - tenant/company context
  - selected agent profile
  - recent conversation history
  - scoped retrieval/context
- Updating prompt/orchestration composition so the selected agent’s persona and role brief are included in the generated response path.
- Ensuring retrieval/context assembly is tenant-scoped and agent-scope-aware.
- Adding/adjusting tests that prove:
  - different agents produce different prompt/runtime inputs
  - role brief/persona are included
  - scoped context retrieval is invoked with the selected agent’s scope
  - cross-tenant or wrong-agent access is rejected safely

Out of scope unless required by existing architecture:
- New UI redesigns beyond wiring existing direct chat flow.
- Multi-agent coordination.
- New tool execution features.
- Approval workflow changes.
- Mobile-specific implementation unless the same backend endpoint is shared and needs no extra work.
- Large schema redesigns unless the current model is missing a minimal field mapping already described in architecture.

# Files to touch
Inspect the solution first and then update the smallest correct set of files. Likely areas:

- `src/VirtualCompany.Api`
  - Chat/conversation endpoints or controllers
  - Request/response DTO mapping if direct chat payloads need agent resolution fields
- `src/VirtualCompany.Application`
  - Communication/chat application services
  - Orchestration service interfaces and handlers
  - Query/command handlers for sending a direct message to an agent
  - Context retrieval abstractions
- `src/VirtualCompany.Domain`
  - Agent aggregate/value objects if persona/role brief accessors or invariants are missing
  - Conversation/message domain models if direct-agent linkage is incomplete
- `src/VirtualCompany.Infrastructure`
  - Repository implementations for agents/conversations/messages
  - Orchestration/prompt builder implementation
  - Context retrieval implementation
- `src/VirtualCompany.Shared`
  - Shared contracts only if already used for chat/orchestration DTOs
- `src/VirtualCompany.Web`
  - Only minimal changes if the web app must pass selected agent/conversation identifiers correctly
- `tests/VirtualCompany.Api.Tests`
  - Endpoint/integration tests for direct chat behavior
- Potentially additional test projects if application-layer tests exist elsewhere in the repo

Also review:
- `README.md`
- any architecture or module docs referenced by the existing implementation
- existing migration/archive docs only for reference; do not add migrations unless truly necessary

# Implementation plan
1. **Inspect the existing direct chat flow**
   - Find the current implementation for:
     - opening a direct conversation with an agent
     - posting a message
     - generating an agent reply
   - Identify whether the response is currently:
     - hardcoded/generic
     - using a shared orchestration service without agent-specific inputs
     - missing scoped retrieval
   - Trace the full path from API -> application -> orchestration -> persistence.

2. **Confirm the source of truth for selected agent identity**
   - Prefer deriving the addressed agent from the direct-agent conversation record if that relationship already exists.
   - If the current API passes `agentId` on send, validate it against the conversation and tenant.
   - Ensure there is no ambiguity about which named agent is responding.
   - If the conversation model lacks a direct link to the selected agent, add the minimal safe mechanism already aligned with existing patterns.

3. **Load the selected agent’s runtime profile**
   - Retrieve the agent by:
     - `company_id`
     - `agent_id`
     - active/allowed status as appropriate for chat
   - Gather runtime fields needed by orchestration:
     - `display_name`
     - `role_name`
     - `department`
     - `personality_json` / persona config
     - `role_brief`
     - `objectives_json` if already part of prompt composition
     - `data_scopes_json`
     - `tool_permissions_json` only if already part of orchestration input
   - Fail safely if the agent is not found, belongs to another tenant, or is not eligible for chat.

4. **Introduce or refine a direct-chat orchestration input model**
   - Create or update an application/orchestration request object that explicitly carries:
     - `CompanyId`
     - `ConversationId`
     - `AgentId`
     - `UserMessage`
     - `RecentMessages`
     - `AgentPersona` / personality config
     - `AgentRoleBrief`
     - `AgentDisplayName` / role metadata
     - `AgentDataScopes`
     - correlation/request metadata if already used
   - Keep this model application-facing and avoid leaking HTTP concerns into orchestration.

5. **Update prompt building to include persona and role brief**
   - In the shared orchestration/prompt builder, ensure direct chat prompt composition includes the selected agent’s:
     - persona/personality instructions
     - role brief
     - any role/objective framing already supported by architecture
   - Keep the prompt deterministic and structured.
   - Do not expose chain-of-thought.
   - Prefer a clear composition order such as:
     1. system/platform safety/policy instructions
     2. tenant/company context
     3. selected agent identity/persona/role brief
     4. scoped retrieved context
     5. recent conversation history
     6. current user message

6. **Apply scoped context retrieval**
   - Ensure the context retrieval service is called with:
     - tenant/company ID
     - selected agent ID
     - agent data scopes
     - current conversation/task context if available
   - Retrieval should return prompt-ready context sections limited to what that agent is allowed to access.
   - If a retrieval abstraction already exists, extend its request object rather than bypassing it.
   - If retrieval is not yet fully implemented, at minimum pass through recent conversation history and any currently available scoped company/agent context in a way that can be extended later.

7. **Persist messages correctly**
   - Confirm the user message is stored before or as part of reply generation according to existing patterns.
   - Persist the generated agent reply as:
     - `sender_type = agent`
     - `sender_id = selected agent id`
     - correct `conversation_id`
     - tenant/company-scoped record
   - Preserve timestamps and any structured payload/rationale summary conventions already in use.

8. **Enforce tenant and conversation safety**
   - Validate:
     - conversation belongs to company
     - selected agent belongs to company
     - conversation is a `direct_agent` channel if that distinction exists
     - user has access to the company/conversation
   - Reject mismatched conversation/agent combinations.
   - Return safe not-found/forbidden behavior consistent with existing API conventions.

9. **Add tests**
   - Add application and/or API tests covering:
     - direct chat response generation uses selected agent persona
     - direct chat response generation uses selected agent role brief
     - retrieval is invoked with selected agent scope
     - two different agents in the same tenant produce different runtime prompt inputs
     - cross-tenant agent lookup is rejected
     - conversation-agent mismatch is rejected
   - Prefer asserting on orchestration request/prompt builder inputs via fakes/mocks rather than brittle string snapshots of full LLM prompts, unless prompt snapshot testing is already established.

10. **Keep implementation aligned with modular monolith boundaries**
   - Do not put orchestration logic in controllers/pages.
   - Keep repositories/infrastructure responsible for data access only.
   - Keep prompt composition and context retrieval in the orchestration subsystem/application layer abstractions.

11. **Document assumptions in code comments or task notes**
   - If acceptance criteria were absent, encode the intended behavior through tests and concise comments.
   - If there are gaps in the current conversation schema, note them in follow-up comments rather than overbuilding.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted tests for chat/orchestration, run them first during iteration, then full suite.

4. Manually verify the implementation path in code:
   - Open a direct-agent conversation flow.
   - Confirm the selected agent is resolved from the correct source.
   - Confirm orchestration input contains persona, role brief, and scoped context request data.
   - Confirm the persisted reply message is authored by the selected agent.

5. Add or verify automated assertions for:
   - tenant scoping
   - conversation-agent consistency
   - agent-specific prompt/runtime composition
   - scoped retrieval invocation

6. If the web app is affected, verify the relevant page/component still compiles and sends the expected identifiers.

# Risks and follow-ups
- **Risk: conversation model may not explicitly bind to an agent**
  - If so, implement the smallest safe binding mechanism and avoid broad schema changes unless necessary.

- **Risk: current orchestration layer may be too generic**
  - Refactor minimally to pass agent runtime profile explicitly rather than duplicating orchestration logic.

- **Risk: prompt tests can become brittle**
  - Prefer testing structured orchestration inputs and prompt sections rather than exact full prompt text.

- **Risk: scoped retrieval may not yet be fully implemented**
  - Ensure the interface supports agent scope now, even if the first implementation uses limited sources.

- **Risk: paused/restricted/archived agent behavior may be ambiguous for chat**
  - Follow existing domain rules if present; otherwise default to safe behavior and note any product ambiguity.

Follow-ups after this task if needed:
- Add richer audit/explainability records showing which agent profile and context sources were used.
- Add task-linking behavior when chat becomes actionable.
- Extend the same backend behavior to mobile if not already shared.
- Add prompt composition telemetry/correlation IDs if missing for easier debugging.