# Goal
Implement backlog task **TASK-11.1.7 — Support direct-agent channel type first** for story **ST-501 Direct chat with named agents**.

This task should establish the **minimum end-to-end vertical slice** for direct chat conversations with a named agent, using the architecture and backlog as the source of truth. Prioritize the **`direct_agent` conversation channel type** as the first supported communication mode, with tenant-safe persistence and a clean path for later expansion to task threads, workflow threads, inbox, and richer orchestration.

The implementation should enable:
- creating or resolving a direct conversation between a human user and a specific agent
- persisting conversation and message records with correct tenant scoping
- sending human messages into a direct-agent conversation
- returning a placeholder or initial agent response through the shared backend flow
- exposing the capability through backend APIs and, if already scaffolded, a minimal web UI entry point

Do not overbuild full orchestration, multi-agent collaboration, or advanced tool execution in this task. The focus is to make **direct-agent** the first-class and first-supported channel type.

# Scope
In scope:
- Add or complete domain/application/infrastructure/API support for `conversations.channel_type = direct_agent`
- Ensure conversation creation/resolution is tenant-scoped and agent-scoped
- Persist messages with:
  - `company_id`
  - `conversation_id`
  - `sender_type`
  - `sender_id`
  - `message_type`
  - `body`
  - timestamps
- Add application commands/queries for:
  - opening or getting a direct-agent conversation
  - posting a message to a direct-agent conversation
  - retrieving paginated message history
- Enforce that:
  - the conversation belongs to the current company
  - the target agent belongs to the current company
  - only `direct_agent` is supported by this task where channel-specific behavior is needed
- Return an agent response using the selected agent identity/context boundary, even if the response is initially stubbed or routed through a minimal orchestration abstraction
- Add tests covering tenant isolation, direct-agent conversation creation, and message persistence

Out of scope:
- Full LLM orchestration sophistication
- Tool execution and approvals
- Task creation from chat beyond a TODO/hook point
- Mobile-specific implementation unless trivial reuse of API contracts is already present
- Supporting other channel types beyond preserving compatibility with the schema
- Rich chat UX polish

# Files to touch
Touch the smallest coherent set of files needed, likely across these projects:

- `src/VirtualCompany.Domain`
  - conversation/message entities or aggregates
  - enums/value objects for channel type, sender type, message type
- `src/VirtualCompany.Application`
  - commands/queries/DTOs for direct-agent chat
  - validators
  - service interfaces for conversation resolution and agent reply generation
- `src/VirtualCompany.Infrastructure`
  - EF Core entity configuration and repository/query implementations
  - migrations if this repo uses active migrations here
  - persistence wiring for conversations/messages
- `src/VirtualCompany.Api`
  - endpoints/controllers for direct-agent conversation open/get/send/history
  - tenant/user context enforcement
- `src/VirtualCompany.Web`
  - minimal page/component updates only if a direct-agent entry point already exists or is easy to wire
- `tests/VirtualCompany.Api.Tests`
  - API/integration tests for direct-agent flows
- `README.md` or relevant docs only if API surface or setup instructions materially change

Also inspect before editing:
- existing tenant resolution/auth patterns
- existing CQRS/MediatR or equivalent application patterns
- existing EF Core DbContext and entity mappings
- any existing chat, task, agent, or communication module scaffolding
- any enum/string constant conventions already used in the codebase

# Implementation plan
1. **Inspect current communication and agent foundations**
   - Find whether `Conversation`, `Message`, `Agent`, and tenant-scoped entities already exist.
   - Reuse existing patterns for:
     - entity IDs
     - auditing timestamps
     - company scoping
     - API endpoint style
     - command/query handlers
   - Confirm whether channel types are stored as strings or enums. Match the existing convention.

2. **Model direct-agent channel support explicitly**
   - Ensure the conversation model supports `channel_type = direct_agent`.
   - If not already present, add a way to associate a direct conversation to a target agent. Prefer a clear field such as `agent_id` on `conversations` if the current model allows safe extension.
   - If schema changes are needed, keep them minimal and aligned with the architecture. A direct-agent conversation should be uniquely attributable to:
     - `company_id`
     - target `agent_id`
     - channel type `direct_agent`
   - Add a uniqueness rule if appropriate so one company/user/agent pair does not create uncontrolled duplicate direct threads. If user-specific direct threads are intended, scope uniqueness accordingly based on existing product assumptions. If unclear, prefer one direct conversation per company + user + agent.

3. **Add application use cases**
   Implement focused use cases such as:
   - `OpenDirectAgentConversation`
     - validates current company membership
     - validates target agent exists and is active/allowed
     - resolves existing direct conversation or creates one
   - `GetConversationMessages`
     - validates tenant ownership
     - supports pagination ordered by creation time
   - `SendDirectAgentMessage`
     - validates conversation exists, belongs to company, and is `direct_agent`
     - persists human message
     - invokes a minimal agent response service
     - persists agent reply
     - returns updated message payload or response DTO

4. **Introduce a minimal agent reply abstraction**
   - Create an application service interface such as `IAgentChatResponder` or similar.
   - For this task, keep implementation simple:
     - accept company, agent, conversation, and recent messages
     - produce a safe agent reply
   - If shared orchestration scaffolding already exists, route through it.
   - If not, implement a deterministic placeholder responder that clearly uses the selected agent’s display name/role brief/persona boundary without pretending to be full orchestration.
   - Keep the abstraction future-ready for ST-502.

5. **Persist messages correctly**
   - Human message:
     - `sender_type = human`
     - `sender_id = current user id`
     - `message_type = text`
   - Agent reply:
     - `sender_type = agent`
     - `sender_id = target agent id`
     - `message_type = text` or existing equivalent
   - Update conversation `updated_at` when new messages are added.

6. **Add API endpoints**
   Prefer REST endpoints consistent with the existing API style. For example:
   - `POST /api/agents/{agentId}/direct-conversation`
   - `GET /api/conversations/{conversationId}/messages?before=&pageSize=`
   - `POST /api/conversations/{conversationId}/messages`
   
   Requirements:
   - derive company/user context from the authenticated request, not client input
   - reject cross-tenant access
   - reject sending to non-`direct_agent` conversations in this task if channel-specific endpoint is used
   - return stable DTOs suitable for web/mobile reuse

7. **Wire minimal web support if appropriate**
   - If the web app already has an agent roster/detail page, add a simple “Chat” action that opens/resolves the direct conversation.
   - Add a minimal conversation view only if the plumbing already exists or can be added cheaply.
   - Do not spend time on advanced UX; backend completeness is the priority.

8. **Testing**
   Add tests for:
   - opening a direct conversation creates one when none exists
   - opening again returns the existing conversation
   - conversation cannot be opened for an agent in another company
   - posting a message persists both human and agent messages
   - message history is tenant-scoped
   - non-direct channel misuse is rejected where applicable
   - archived/paused/restricted agent behavior follows current business rules; if not yet defined in code, at minimum reject archived and document any temporary behavior

9. **Keep extension points visible**
   Add TODOs or comments only where useful for future stories:
   - task creation/linking from actionable chat
   - richer orchestration via ST-502
   - audit/event fan-out
   - notification hooks
   - additional channel types

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used in the normal workflow, create/apply and verify schema changes compile cleanly.

4. Manually verify via API:
   - create/resolve a direct conversation for an in-tenant agent
   - send a human message
   - confirm an agent reply is returned
   - fetch message history and verify ordering and sender metadata

5. Verify tenant isolation:
   - attempt to access another company’s conversation or agent
   - confirm forbidden/not found behavior matches existing API conventions

6. Verify persistence:
   - `conversations.channel_type` is `direct_agent`
   - messages have correct `sender_type`, `sender_id`, and `company_id`
   - `updated_at` changes on new messages

7. Verify no regressions:
   - existing agent, task, and auth flows still build and tests remain green

# Risks and follow-ups
- **Schema ambiguity risk:** The architecture schema shows `conversations` without an `agent_id`. If the current code also lacks a direct link to the target agent, add it carefully and minimally. This is likely necessary for robust direct-agent support.
- **Uniqueness semantics risk:** It may be unclear whether direct conversations are per company+agent or per company+user+agent. Prefer the option that best matches existing UX/auth assumptions and document the decision in code comments or PR notes.
- **Orchestration gap:** A placeholder responder may be necessary if ST-502 is not yet implemented. Keep the interface clean so it can be swapped with the shared orchestration engine later.
- **Authorization drift:** Ensure company scoping comes from server-side tenant context only. Do not trust client-supplied company IDs.
- **Future follow-up:** Add task-linking/action extraction from chat once ST-401/ST-502 integration is ready.
- **Future follow-up:** Add audit events and rationale summaries once ST-602 and orchestration artifacts are in place.
- **Future follow-up:** Expand channel handling for `task_thread`, `workflow_thread`, and `inbox` after direct-agent is stable.