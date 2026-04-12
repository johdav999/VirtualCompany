# Goal

Implement **TASK-ST-501 — Direct chat with named agents** in the existing .NET solution so that a tenant-scoped user can:

- open or resume a **direct conversation** with a specific named agent,
- send and retrieve chat messages,
- receive agent replies generated using that agent’s configured persona, role brief, and scoped context,
- and optionally create or link a task when a chat interaction becomes actionable.

This should fit the documented architecture:
- modular monolith,
- tenant-scoped data access,
- shared orchestration engine,
- direct-agent conversation type first,
- concise rationale summaries only, never raw chain-of-thought.

No explicit acceptance criteria were provided in the task record, so implement to satisfy the backlog story for **ST-501**.

# Scope

Implement the minimum end-to-end slice for **web-first direct agent chat** on the existing stack.

Include:

1. **Domain and persistence**
   - Support `conversations` and `messages` for direct agent chat.
   - Ensure tenant scoping via `company_id`.
   - Support sender metadata:
     - `sender_type`
     - `sender_id`
     - timestamps
   - Support conversation channel type:
     - `direct_agent`

2. **Application layer**
   - Query to list/fetch direct conversations for the current company/user.
   - Command to create or get a direct conversation for a selected agent.
   - Command to post a human message.
   - Command/service to generate and persist an agent reply using the selected agent configuration.
   - Optional command to create/link a task from a conversation message or conversation.

3. **API**
   - Endpoints for:
     - open/get direct conversation with agent,
     - fetch paginated messages,
     - send message,
     - create/link task from chat.
   - All endpoints must enforce tenant and membership authorization.

4. **Web UI**
   - Add a simple direct-chat experience in Blazor Web.
   - Allow opening chat from roster/dashboard entry point if one exists; otherwise add a minimal agent chat page reachable by route.
   - Show paginated/recent message history.
   - Allow sending a message and rendering the agent response.

5. **Orchestration integration**
   - Reuse or introduce a shared application service for single-agent chat orchestration.
   - Agent reply must use:
     - selected agent persona,
     - role brief,
     - scoped context,
     - recent conversation history.
   - Return concise user-facing output and, where relevant, a short rationale summary in structured payload or task linkage metadata.
   - Do not expose raw reasoning.

6. **Task linkage**
   - Support a pragmatic first version:
     - either explicit “Create task from chat” action in UI/API,
     - or automatic task creation only when there is already an established task creation pattern in the codebase.
   - Prefer explicit user action if uncertain.

Out of scope unless already trivial in the codebase:
- mobile UI,
- streaming responses,
- multi-agent collaboration,
- advanced inbox/unified notifications,
- rich attachments,
- voice,
- full dashboard redesign.

# Files to touch

Inspect the solution first and adjust to actual conventions, but expect to touch files in these areas.

## Domain
- `src/VirtualCompany.Domain/**`
  - conversation aggregate/entity
  - message entity
  - agent entity usage
  - task linkage fields/value objects if needed

## Application
- `src/VirtualCompany.Application/**`
  - chat/direct conversation commands and queries
  - DTOs/view models
  - orchestration/chat service interface + implementation
  - validation
  - tenant-aware authorization checks
  - task creation/link command if added

## Infrastructure
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration/mappings
  - repositories/query services
  - migration support or SQL scripts per repo convention
  - orchestration provider integration
  - persistence for conversations/messages

## API
- `src/VirtualCompany.Api/**`
  - chat controller or minimal API endpoints
  - request/response contracts
  - auth wiring
  - pagination support

## Web
- `src/VirtualCompany.Web/**`
  - direct agent chat page/component
  - agent roster/dashboard link into chat
  - API client/service
  - send message form and message list UI
  - optional create-task action

## Tests
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests
  - tenant isolation tests
  - authorization tests
  - happy path chat flow tests

If the repository already has migrations in a specific project or uses archived/manual migration docs, follow the existing pattern rather than inventing a new one.

# Implementation plan

1. **Inspect existing architecture and conventions**
   - Read:
     - `README.md`
     - project startup files
     - existing auth/tenant resolution
     - existing agent/task modules
     - any orchestration abstractions
   - Identify:
     - current CQRS pattern,
     - EF Core DbContext location,
     - API style,
     - Blazor rendering pattern,
     - test setup strategy.

2. **Model direct chat persistence**
   - Add or complete entities/tables for:
     - `conversations`
     - `messages`
   - Ensure fields align with architecture/backlog:
     - `conversations`: `id`, `company_id`, `channel_type`, `subject`, `created_by_user_id`, timestamps
     - `messages`: `id`, `company_id`, `conversation_id`, `sender_type`, `sender_id`, `message_type`, `body`, `structured_payload`, `created_at`
   - For direct agent chat, store enough metadata to resolve the target agent reliably.
     - If no field exists, add a safe mechanism such as:
       - `subject` convention plus linked metadata in JSON,
       - or preferably a dedicated `agent_id` on conversation if consistent with current design.
   - Keep schema tenant-scoped and index for:
     - company + conversation,
     - company + updated_at,
     - conversation + created_at.

3. **Add domain/application contracts**
   - Create use cases such as:
     - `GetOrCreateDirectAgentConversation`
     - `GetConversationMessages`
     - `SendDirectAgentMessage`
     - `CreateTaskFromConversation` or `CreateTaskFromMessage`
   - Validate:
     - user belongs to company,
     - agent belongs to same company,
     - agent status allows chat (`active`, maybe `restricted` depending on policy; reject `archived`, likely reject `paused`),
     - conversation belongs to same company and targets the same agent.

4. **Implement tenant-safe conversation creation/resolution**
   - Opening chat with an agent should:
     - find existing direct conversation for company + user/agent pairing if product behavior prefers one thread,
     - or create a new direct conversation if none exists.
   - Prefer a single reusable direct thread per user-agent pair unless existing UX patterns suggest otherwise.
   - Update `updated_at` when new messages are added.

5. **Implement message retrieval with pagination**
   - Add query endpoint/service to fetch messages by conversation with page size/cursor or page number.
   - Default to recent-first or chronological order based on existing UI conventions; render clearly in UI.
   - Ensure strict tenant/company filtering in query layer.

6. **Implement send-message flow**
   - On user send:
     - persist human message first,
     - invoke shared chat orchestration service,
     - persist agent reply as a message,
     - return both persisted records or refreshed conversation payload.
   - Include correlation IDs if the codebase already supports them.

7. **Implement single-agent chat orchestration**
   - Reuse existing orchestration components if present.
   - Otherwise create a minimal shared service that:
     - resolves the addressed agent,
     - loads agent config (`display_name`, `role_name`, `personality_json`, `role_brief`, scopes/status),
     - loads recent conversation history,
     - optionally loads lightweight scoped context/memory if retrieval services already exist,
     - builds a prompt for direct chat,
     - calls the configured LLM provider abstraction,
     - returns a concise response object.
   - The response object should support:
     - `messageText`
     - optional `rationaleSummary`
     - optional `suggestedTask` metadata
   - Never persist or return chain-of-thought.

8. **Respect policy and scope boundaries**
   - Even for chat-only behavior, ensure:
     - tenant isolation,
     - agent status checks,
     - scoped context retrieval only from same company,
     - no direct tool execution unless already supported and policy-enforced.
   - If tool execution is not yet implemented, keep ST-501 as conversational grounding only.

9. **Implement task creation/linking**
   - Add a simple explicit action:
     - create a task from the current conversation or selected message.
   - Populate task fields from chat context:
     - title from message summary,
     - description from recent exchange,
     - assigned agent = current agent,
     - input payload includes conversation/message references.
   - If tasks already support linkage metadata, store:
     - `conversation_id`
     - source message IDs
   - If full linkage schema does not exist, use `input_payload`/`structured_payload` pragmatically.

10. **Add API endpoints**
   - Add endpoints similar to:
     - `POST /api/companies/{companyId}/agents/{agentId}/conversations/direct`
     - `GET /api/companies/{companyId}/conversations/{conversationId}/messages`
     - `POST /api/companies/{companyId}/conversations/{conversationId}/messages`
     - `POST /api/companies/{companyId}/conversations/{conversationId}/tasks`
   - Match existing route conventions if different.
   - Return safe DTOs only.

11. **Add Blazor Web UI**
   - Build a minimal but usable direct chat page:
     - agent header,
     - message list,
     - composer,
     - optional “Create task” action.
   - Add entry point from:
     - agent roster detail/list if available,
     - otherwise a direct route like `/agents/{agentId}/chat`.
   - Handle loading, empty state, send pending state, and error state.

12. **Add tests**
   - Cover:
     - create/open direct conversation,
     - send message and receive persisted agent reply,
     - fetch paginated messages,
     - forbidden/not found across tenant boundaries,
     - cannot chat with archived/paused agent per chosen rule,
     - create task from chat.
   - Prefer API/integration tests over only unit tests for tenant isolation.

13. **Document any assumptions**
   - If no explicit acceptance criteria exist, add concise implementation notes in code comments or PR notes:
     - one thread per user-agent pair,
     - explicit task creation action,
     - no streaming,
     - no raw reasoning exposure.

# Validation steps

1. **Restore/build**
   - Run:
     - `dotnet build`

2. **Run tests**
   - Run:
     - `dotnet test`

3. **Manual API validation**
   - Verify authenticated tenant-scoped user can:
     - open direct conversation with an in-tenant agent,
     - send a message,
     - retrieve message history,
     - create a task from chat.
   - Verify cross-tenant access returns forbidden/not found.

4. **Manual web validation**
   - In Blazor Web:
     - navigate to an agent,
     - open chat,
     - send a message,
     - confirm agent reply appears,
     - refresh and confirm history persists,
     - create/link a task and confirm task record exists.

5. **Persistence validation**
   - Confirm records are stored with correct metadata:
     - `company_id`
     - `conversation_id`
     - `sender_type`
     - `sender_id`
     - timestamps
     - `channel_type = direct_agent`

6. **Behavior validation**
   - Confirm agent reply reflects selected agent identity/role rather than a generic assistant.
   - Confirm no raw reasoning is exposed.
   - Confirm pagination works for longer conversations.

7. **Regression validation**
   - Ensure no existing auth, agent, or task flows are broken.
   - If migrations are added, ensure app starts cleanly against a fresh/local database per repo conventions.

# Risks and follow-ups

- **Schema uncertainty:** The architecture lists conversation/message schemas, but the repo may already partially implement them. Reuse existing models where possible instead of duplicating.
- **Migration convention risk:** The workspace references archived PostgreSQL migration docs; confirm whether migrations are code-first, SQL-scripted, or manually managed before changing schema.
- **Orchestration maturity:** If shared orchestration abstractions are incomplete, implement a minimal single-agent chat service now but keep it reusable for ST-502.
- **Task linkage ambiguity:** Since “chat can create or link to tasks” is broad, prefer explicit task creation from chat in v1 unless existing task-link infrastructure already exists.
- **Agent status rules:** Decide and document whether `restricted` agents can still chat. Safe default: allow read-only conversational responses if no tools/actions are involved, but block `paused` and `archived`.
- **Performance:** Long conversations may require pagination and bounded prompt history selection. Keep prompt context limited to recent messages plus summaries if available.
- **Auditability follow-up:** ST-602 will likely require richer audit events for chat/task creation; structure current code so audit hooks can be added cleanly later.
- **Mobile follow-up:** Keep API contracts reusable for ST-604 mobile companion.
- **Future enhancements:** streaming responses, conversation summaries, task suggestion UX, richer rationale/source references, and approval-aware action proposals can follow in later stories.