# Goal
Implement backlog task **TASK-11.1.1** for **ST-501 Direct chat with named agents** so that **users can open a direct conversation with an agent from the roster or dashboard** in the web app.

This task should establish the end-to-end foundation for starting or resuming a **direct agent conversation** from existing agent entry points, using the current architecture and stack:

- **Blazor Web App** frontend
- **ASP.NET Core** backend
- **Application/Domain/Infrastructure** layering
- **PostgreSQL** persistence
- **tenant-scoped authorization and data access**

Focus on the “open conversation” flow, not full chat orchestration depth. The result should let a user click an agent from roster/dashboard, create or reuse a `direct_agent` conversation for that user/company/agent context, and navigate into the conversation view.

# Scope
Implement only what is necessary for this task, while keeping the design aligned with ST-501 and future chat work.

## In scope
- Add or complete backend support to **create or fetch a direct conversation** for a selected agent.
- Ensure conversation records are **tenant-scoped** and associated to the selected agent context.
- Add a web UI action from:
  - **agent roster**
  - **dashboard agent entry point** if present in current codebase
- Navigate the user into a conversation/chat page after opening.
- Reuse an existing direct conversation when appropriate instead of creating duplicates, if the current model supports that cleanly.
- Persist enough metadata to support future messaging:
  - `conversations.channel_type = direct_agent`
  - `created_by_user_id`
  - `company_id`
  - `subject` or equivalent display label if used by current UI
- Add tests for application/API behavior.

## Out of scope
Unless already trivial and clearly required by existing code paths, do **not** implement:
- Full agent response generation
- LLM orchestration pipeline changes
- task creation/linking from chat
- mobile app support
- notification/inbox features
- advanced pagination/history UX
- broad dashboard redesign

## Behavioral expectations
Because no explicit acceptance criteria were provided for this task, use the story and architecture to infer these minimum expectations:

- A signed-in user with access to a company can open a direct conversation with an active/allowed agent.
- The operation is **company-scoped** and must not allow cross-tenant access.
- Opening from roster/dashboard should land on a stable conversation route.
- Re-opening the same agent chat should preferably reuse the existing direct conversation for that user/company/agent pair, avoiding unnecessary duplicates.
- Archived or otherwise unavailable agents should not be openable for new direct chat.

# Files to touch
Inspect the solution first and adjust to actual project structure, but expect to touch files in these areas.

## Likely backend files
- `src/VirtualCompany.Domain/...`
  - conversation aggregate/entity/value objects
  - agent entity/status rules if needed
- `src/VirtualCompany.Application/...`
  - command/query for open direct conversation
  - DTO/view model for conversation launch result
  - validation/authorization handling
- `src/VirtualCompany.Infrastructure/...`
  - EF Core configuration/repository/query implementation
  - migration support if schema changes are needed
- `src/VirtualCompany.Api/...`
  - endpoint/controller for open direct conversation
  - route wiring

## Likely web files
- `src/VirtualCompany.Web/...`
  - roster page/component: add “Chat” / “Open conversation” action
  - dashboard page/component: add agent chat entry point if dashboard agent cards/list already exist
  - conversation page/component and navigation updates
  - client/service for calling the API

## Likely test files
- `tests/VirtualCompany.Api.Tests/...`
  - API integration/endpoint tests
- any existing application test project if present
  - command handler/unit tests

## Migrations
If the current schema does not support identifying the target agent for a direct conversation, add the minimum viable schema support. Prefer one of:
- a nullable `agent_id` on `conversations`, or
- a conversation participant/link model if one already exists

Do **not** invent a parallel chat schema if the project already has a conversation model.

# Implementation plan
1. **Inspect existing chat/conversation implementation**
   - Find current `conversations` and `messages` domain/application/API/UI support.
   - Determine whether there is already:
     - a conversation details page
     - a create conversation endpoint
     - agent roster actions
     - dashboard agent widgets
   - Reuse existing patterns for CQRS, endpoint style, tenant resolution, and authorization.

2. **Model direct-agent conversation targeting**
   - Ensure a direct conversation can be tied to a specific agent.
   - If the current `conversations` table/entity lacks agent linkage, add the smallest clean extension needed.
   - Recommended shape for this task:
     - `channel_type = direct_agent`
     - `agent_id` nullable FK for direct-agent conversations
   - Keep the model future-friendly but minimal.

3. **Define open-or-create application flow**
   - Add an application command/query such as:
     - `OpenDirectConversationCommand(companyId, userId, agentId)`
   - Behavior:
     - load agent by `company_id` + `agent_id`
     - verify agent exists and is chat-eligible
     - reject archived/inactive states as appropriate per existing domain rules
     - search for existing `direct_agent` conversation for the same company/user/agent
     - if found, return it
     - otherwise create a new conversation and return it
   - Return a DTO with at least:
     - `ConversationId`
     - `AgentId`
     - `ChannelType`
     - route/display metadata if useful

4. **Enforce tenant and authorization boundaries**
   - Use existing company context resolution from ST-101 patterns.
   - Ensure all reads/writes are filtered by `company_id`.
   - Return forbidden/not found according to existing API conventions.
   - Do not allow opening a conversation with an agent from another tenant.

5. **Add API endpoint**
   - Add a focused endpoint, e.g.:
     - `POST /api/companies/{companyId}/conversations/direct-agent/{agentId}`
     - or match existing route conventions
   - Endpoint should:
     - resolve authenticated user
     - call application layer
     - return conversation launch payload
   - Keep HTTP concerns out of application logic.

6. **Wire web app roster action**
   - On the agent roster page, add a visible action like **Chat** or **Open chat** for eligible agents.
   - Clicking it should call the API and navigate to the conversation page.
   - Disable or hide the action for non-chat-eligible agents if status rules already exist.

7. **Wire dashboard action**
   - If the dashboard already renders agent cards/list/quick actions, add the same open-chat action there.
   - If no suitable dashboard agent entry point exists yet, implement the smallest non-invasive entry point or document why only roster was wired now and leave a clear TODO.

8. **Conversation page routing**
   - Ensure there is a route like `/conversations/{conversationId}` or existing equivalent.
   - The page should load the conversation and show enough header context to confirm the selected agent.
   - If message sending already exists, preserve it.
   - If not, a basic conversation shell is acceptable as long as the open flow lands correctly.

9. **Persistence and migration**
   - If schema changes are required:
     - add EF/entity config
     - add migration
     - ensure indexes support lookup by company/user/agent/channel type
   - A useful uniqueness/index strategy is:
     - non-unique index on `(company_id, created_by_user_id, channel_type, agent_id)`
   - Only add a unique constraint if it is safe with current data model and null semantics.

10. **Testing**
   - Add tests covering:
     - open direct conversation creates a new conversation when none exists
     - opening again returns the existing conversation
     - cross-tenant agent access is rejected
     - archived/unavailable agent cannot be opened
     - roster/dashboard action renders when expected, if UI tests exist in current stack

11. **Documentation/comments**
   - Add concise comments only where intent is non-obvious.
   - Update any relevant README or feature notes only if the repo already maintains such docs for implemented stories.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are added, verify they apply cleanly in the project’s normal migration flow.

4. Manually validate web behavior:
   - sign in as a user with company access
   - open the agent roster
   - click **Chat/Open chat** on an eligible agent
   - confirm navigation to the conversation page
   - repeat the action and confirm the same conversation is reused if that behavior was implemented
   - verify unavailable/archived agents do not allow opening chat
   - verify another company’s agent cannot be opened through URL/API tampering

5. Validate persisted data in the database:
   - conversation row has correct `company_id`
   - `channel_type = direct_agent`
   - `created_by_user_id` is set
   - agent linkage is present if added
   - timestamps are populated

# Risks and follow-ups
- **Schema gap risk:** The architecture’s sample `conversations` schema does not explicitly include `agent_id`, which may be necessary for clean direct-agent reuse. Add it carefully and minimally.
- **Dashboard ambiguity:** The backlog says roster or dashboard, but the current dashboard implementation may not yet expose agent quick actions. Prefer adding the smallest consistent entry point if feasible.
- **Conversation uniqueness semantics:** Reusing one direct conversation per user/company/agent is likely correct for v1, but confirm against any existing product conventions before enforcing uniqueness too strictly.
- **Agent status rules:** The story does not define exactly which statuses are chat-eligible. Use sensible defaults aligned with agent management rules, and document assumptions in code/tests.
- **Future follow-up:** Subsequent tasks should cover:
  - sending/storing messages
  - agent response orchestration
  - task creation/linking from chat
  - paginated history
  - audit/explainability hooks for chat actions