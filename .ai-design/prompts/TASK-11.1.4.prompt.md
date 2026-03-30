# Goal

Implement backlog task **TASK-11.1.4** for **ST-501 — Direct chat with named agents**: enable direct agent chat to **create a new task or link an existing task when the conversation becomes actionable**.

The implementation should fit the existing modular monolith architecture and preserve:
- tenant isolation
- clean application boundaries
- CQRS-lite patterns
- auditability
- future orchestration extensibility

This task should add the minimum viable end-to-end capability so that:
- a direct agent conversation can surface actionable state
- the system can create a task from chat context
- the system can link a message/conversation to an existing task
- the UI can show and navigate that relationship
- all operations remain company-scoped and authorization-safe

Because no explicit acceptance criteria were provided for this task beyond the story-level requirement, define and implement pragmatic acceptance behavior consistent with ST-501.

# Scope

In scope:
- Extend the communication/chat domain so messages or conversations can be associated with tasks
- Add application commands/handlers for:
  - creating a task from a conversation/message
  - linking a conversation/message to an existing task
- Add persistence support and EF mappings/migration as needed
- Add API endpoints for the above actions
- Update web direct-chat UI to expose these actions in a simple, explicit way
- Add basic audit/event recording if an audit mechanism already exists nearby
- Add tests for tenant scoping, happy paths, and invalid cases

Recommended functional behavior:
- A user in a direct agent conversation can choose **Create task**
- The created task should:
  - belong to the same company as the conversation
  - default assignment to the chatted agent when appropriate
  - capture source conversation/message references in `input_payload` or a dedicated link table
- A user can choose **Link to existing task**
- Linking should only allow tasks from the same company
- The conversation/task relationship should be queryable and visible in chat/task detail surfaces where practical
- If the current codebase already has task detail pages/routes, link navigation should reuse them

Out of scope unless already trivial in the codebase:
- automatic LLM intent detection that silently creates tasks
- multi-agent decomposition
- workflow instantiation from chat
- mobile UI changes
- advanced approval logic triggered from chat
- broad redesign of chat orchestration

Implementation preference:
- favor explicit user action over implicit AI-triggered task creation
- keep the model/orchestrator free to suggest actionability later, but this task should deliver deterministic product behavior now

# Files to touch

Inspect the solution first and adjust to actual structure, but expect to touch files in these areas.

Likely domain files:
- `src/VirtualCompany.Domain/.../Conversations/*`
- `src/VirtualCompany.Domain/.../Messages/*`
- `src/VirtualCompany.Domain/.../Tasks/*`

Likely application files:
- `src/VirtualCompany.Application/.../Communication/*`
- `src/VirtualCompany.Application/.../Tasks/*`
- command/query handlers, DTOs, validators, mapping

Likely infrastructure files:
- `src/VirtualCompany.Infrastructure/.../Persistence/*`
- EF Core entity configurations
- migrations
- repositories

Likely API files:
- `src/VirtualCompany.Api/.../Controllers/*` or endpoint modules
- request/response contracts

Likely web files:
- `src/VirtualCompany.Web/.../Pages/*`
- `src/VirtualCompany.Web/.../Components/*`
- direct agent chat page/component
- task picker/modal/component if one exists

Potential shared contracts:
- `src/VirtualCompany.Shared/...`

Also review:
- `README.md`
- existing task APIs and chat APIs
- tenant resolution and authorization patterns
- any audit/event abstractions already present

# Implementation plan

1. **Inspect current chat and task implementation**
   - Find how direct agent conversations are modeled and loaded
   - Confirm whether `conversations` and `messages` are already implemented
   - Find current task creation flow for ST-401-related groundwork
   - Identify existing tenant context abstraction and authorization checks
   - Identify whether there is already a generic entity-linking pattern or audit event service

2. **Choose the relationship model**
   Prefer the simplest model that supports both create and link cleanly.

   Recommended option:
   - add a dedicated join/link entity such as `ConversationTaskLink` or `MessageTaskLink`
   - if message-level granularity is useful, support:
     - `company_id`
     - `conversation_id`
     - `message_id` nullable
     - `task_id`
     - `link_type` (`created_from_message`, `linked_from_message`, `created_from_conversation`, `linked_from_conversation`)
     - `created_by_user_id`
     - timestamps

   If the codebase is still early and a join table is too heavy, acceptable fallback:
   - store source references in `tasks.input_payload`
   - but still expose a query path from conversation to related tasks
   - only use this fallback if it materially reduces complexity

   Strong preference: use a dedicated link table for future auditability and navigation.

3. **Add/extend domain model**
   - Introduce the link entity/value object and invariants:
     - all linked entities must share the same `company_id`
     - `message_id`, if provided, must belong to the specified `conversation_id`
   - Add navigation properties if the project uses them
   - Keep domain logic minimal and explicit

4. **Add persistence and migration**
   - Add EF configuration for the link table
   - Add indexes for:
     - `(company_id, conversation_id)`
     - `(company_id, task_id)`
     - unique constraint as appropriate to prevent duplicate links
   - Add migration
   - Ensure delete behavior is safe and does not cascade unexpectedly across business records

5. **Add application commands**
   Implement at least these commands:

   - `CreateTaskFromConversationCommand`
     - inputs:
       - `conversationId`
       - optional `messageId`
       - `title`
       - `description`
       - `priority`
       - optional `dueAt`
       - optional `assignedAgentId`
     - behavior:
       - load conversation in tenant scope
       - validate direct-agent conversation or supported channel type
       - infer default assigned agent from conversation if not supplied
       - create task using existing task creation pathway where possible
       - create conversation-task link
       - optionally append a system message to the conversation noting task creation

   - `LinkConversationToExistingTaskCommand`
     - inputs:
       - `conversationId`
       - optional `messageId`
       - `taskId`
     - behavior:
       - load conversation and task in same tenant
       - validate both exist and belong to same company
       - create link if not already present
       - optionally append a system message noting the link

   If the codebase already has a reusable task creation service, call it instead of duplicating task creation logic.

6. **Add queries**
   Add query support to retrieve related tasks for a conversation:
   - `GetConversationRelatedTasksQuery`
   - return lightweight task summaries:
     - task id
     - title
     - status
     - priority
     - assigned agent
     - link type
     - created/linked timestamp

   If task detail already loads related metadata, optionally add reverse lookup:
   - related conversations/messages for a task

7. **Expose API endpoints**
   Add endpoints consistent with existing API style, for example:
   - `POST /api/conversations/{conversationId}/tasks`
   - `POST /api/conversations/{conversationId}/task-links`
   - `GET /api/conversations/{conversationId}/tasks`

   Requirements:
   - tenant-scoped authorization
   - validate message belongs to conversation when `messageId` is supplied
   - return safe 404/403 behavior consistent with tenant isolation
   - use existing problem-details/error conventions

8. **Update web UI**
   In the direct agent chat surface:
   - add a visible action such as:
     - `Create task`
     - `Link task`
   - support action from:
     - conversation header, or
     - per-message action menu if message-level linking is implemented
   - for create:
     - open simple form/modal with title, description, priority, due date
     - prefill from selected message or recent conversation text if practical
   - for link:
     - allow selecting an existing task from same company
     - if no picker exists, implement a simple search/list dialog using existing task query endpoints
   - show related tasks in the conversation UI
   - after create/link, show confirmation and navigation to task detail if available

   Keep UI simple and explicit. Do not add speculative AI automation unless already present.

9. **Optional but recommended: system/audit messages**
   If the communication model supports system messages, append one like:
   - “Task ‘Prepare Q2 hiring plan’ created from this conversation.”
   - “Linked this conversation to task ‘Vendor invoice review’.”

   If audit events are already implemented:
   - emit business audit events for create/link actions with actor, target, and outcome

10. **Testing**
   Add tests at the appropriate layers:

   Application tests:
   - create task from conversation succeeds in same tenant
   - link existing task succeeds in same tenant
   - cannot link task from another tenant
   - cannot use message from different conversation
   - duplicate link is prevented or idempotent per chosen design
   - default assignment to chatted agent works if applicable

   Infrastructure/API tests:
   - migration/configuration works
   - endpoints enforce tenant scoping
   - related tasks query returns expected results

   UI tests if present in repo:
   - create/link actions render and call backend correctly

11. **Implementation notes aligned to architecture**
   - Keep orchestration separate from UI/API concerns
   - Do not let chat directly mutate task state without application commands
   - Preserve tenant isolation on every query and command
   - Prefer structured linkage over parsing chat history later
   - Keep rationale concise; do not expose chain-of-thought

12. **Pragmatic acceptance definition for this task**
   Treat the task as complete when:
   - users can explicitly create a task from direct agent chat
   - users can explicitly link direct agent chat to an existing task
   - linked/created tasks are visible from the conversation
   - all operations are tenant-safe
   - tests cover the main happy path and tenant-boundary failures

# Validation steps

1. Restore/build the solution
   - `dotnet build`

2. Run tests before changes to establish baseline
   - `dotnet test`

3. After implementation, verify migration generation and application
   - generate/apply EF migration if this repo uses EF CLI
   - confirm schema includes the new link table or chosen persistence changes

4. Manual API validation
   - create or use an existing company, agent, conversation, and task
   - call:
     - create task from conversation endpoint
     - link existing task endpoint
     - get related tasks endpoint
   - verify:
     - created task has correct `company_id`
     - assigned agent defaults correctly when expected
     - cross-tenant task linking is rejected
     - invalid `messageId`/`conversationId` combinations are rejected

5. Manual web validation
   - open a direct agent conversation
   - create a task from chat
   - confirm task appears in related tasks section
   - link an existing task
   - confirm duplicate linking behavior is correct
   - navigate to task detail if route exists

6. Regression validation
   - verify normal chat send/load still works
   - verify existing task creation flows still work
   - verify no tenant leakage in conversation/task queries

7. Final verification
   - `dotnet test`
   - `dotnet build`

# Risks and follow-ups

- **Unclear existing task maturity**
  - ST-401 may be partially implemented or not yet present.
  - If task creation infrastructure is missing, keep this task narrowly scoped and reuse whatever minimal task creation path exists.

- **Relationship modeling choice**
  - Using only `input_payload` is faster but weaker for querying and auditability.
  - Prefer a dedicated link table unless the repo is extremely early-stage.

- **UI complexity for task selection**
  - If there is no existing task search/picker, implement a minimal same-tenant list/search dialog.
  - Avoid overbuilding a generic picker if not needed elsewhere yet.

- **Conversation ownership semantics**
  - Direct agent conversations may not explicitly store the target agent in a normalized way.
  - If so, infer carefully or add a small extension to conversation metadata.

- **Audit/event consistency**
  - If audit infrastructure is incomplete, at minimum add system messages and structured logs.
  - Follow up with full audit event integration later.

- **Future enhancement opportunities**
  - agent response can suggest “This sounds actionable — create a task?”
  - automatic extraction of title/description from selected message(s)
  - reverse linking from task detail to originating conversation/message
  - workflow/approval creation from chat
  - mobile support for related tasks in direct chat

- **Do not do in this task**
  - hidden automatic task creation by the LLM
  - free-form multi-agent delegation
  - direct DB access from UI or orchestration shortcuts around application commands