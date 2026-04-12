# Goal
Implement backlog task **TASK-11.1.2** for **ST-501 Direct chat with named agents** by adding persistence support so that **messages are stored with sender type, sender ID, conversation type, and timestamps** in the .NET solution.

This task should establish the backend/domain/data-model foundation for chat message persistence, aligned with the architecture’s **Communication Module** and the documented relational model:

- `conversations`
  - includes `channel_type`
  - timestamps (`created_at`, `updated_at`)
- `messages`
  - includes `sender_type`
  - `sender_id`
  - timestamps (`created_at`)
  - tenant scoping via `company_id`

Focus on durable storage and application-facing persistence behavior, not full chat UX or full orchestration.

# Scope
In scope:

- Add or complete domain/data model support for:
  - conversations
  - messages
- Ensure persisted message records include:
  - `company_id`
  - `conversation_id`
  - `sender_type`
  - `sender_id`
  - `created_at`
- Ensure conversation records include:
  - `channel_type` (this is the “conversation type” from the task wording)
  - timestamps
- Add/update EF Core configuration and database migration(s)
- Add application/infrastructure support to create and read persisted messages
- Keep everything tenant-scoped
- Add tests covering persistence mapping and basic create/read behavior

Out of scope unless required by existing code structure:

- Full Blazor or MAUI chat UI
- Streaming responses
- LLM orchestration behavior
- Task-linking behavior from chat
- Notifications/inbox features
- Rich pagination/filtering beyond what is minimally needed for persistence verification

If the codebase already contains partial chat/conversation models, extend them rather than duplicating them.

# Files to touch
Inspect the solution first and then touch the minimum necessary files, likely in these areas:

- `src/VirtualCompany.Domain/**`
  - conversation entity/value objects/enums
  - message entity/value objects/enums
- `src/VirtualCompany.Application/**`
  - commands/queries/services/contracts for storing and retrieving messages
- `src/VirtualCompany.Infrastructure/**`
  - EF Core DbContext
  - entity type configurations
  - repositories
  - migrations
- `src/VirtualCompany.Api/**`
  - endpoints/controllers if needed to exercise persistence through the app layer
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- possibly `tests/**` in other test projects if domain/infrastructure tests exist

Also review:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`

Do not invent new architectural layers if the repository already has established patterns.

# Implementation plan
1. **Inspect existing communication/chat implementation**
   - Search for existing types related to:
     - `Conversation`
     - `Message`
     - `ChannelType`
     - `SenderType`
     - communication/chat endpoints
   - Reuse existing naming and module boundaries.
   - Determine whether this task is:
     - creating these models from scratch, or
     - filling missing persistence fields/mappings.

2. **Model the persistence contract**
   - Ensure the conversation entity/table supports:
     - `Id`
     - `CompanyId`
     - `ChannelType` or equivalent conversation type
     - `Subject` if already present in architecture/model
     - `CreatedByUserId` if already part of current design
     - `CreatedAt`
     - `UpdatedAt`
   - Ensure the message entity/table supports:
     - `Id`
     - `CompanyId`
     - `ConversationId`
     - `SenderType` (`human`, `agent`, `system` or project equivalent enum)
     - `SenderId` nullable where appropriate
     - `MessageType` if already present
     - `Body`
     - `StructuredPayload` nullable if already present
     - `CreatedAt`

3. **Use strong typing where the codebase supports it**
   - Prefer enums or constrained value objects for:
     - sender type
     - conversation/channel type
     - message type
   - Persist them consistently using existing EF conventions.
   - Avoid magic strings scattered across the codebase.

4. **Add/update domain entities**
   - Add constructors/factory methods/guard clauses so invalid records cannot be created easily.
   - Enforce:
     - non-empty body for text messages unless current design allows structured-only messages
     - valid sender type
     - required company and conversation identifiers
   - Keep timestamps generated server-side.

5. **Update EF Core mappings**
   - Configure table names and columns to align with architecture docs where practical:
     - `conversations`
     - `messages`
   - Configure required/optional columns correctly.
   - Add indexes likely needed now:
     - `messages(company_id, conversation_id, created_at)`
     - `conversations(company_id, channel_type)`
   - Add FK from `messages.conversation_id -> conversations.id`
   - Ensure tenant-owned entities include `company_id`

6. **Create database migration**
   - Add or update migration for the new/changed schema.
   - If tables already exist, make the migration additive and safe.
   - Be careful with enum/string conversions and nullability changes.
   - Do not modify archived migration docs except if repository guidance explicitly requires it.

7. **Add application-layer operations**
   - Implement the minimal use cases needed to satisfy persistence:
     - create/open direct-agent conversation if needed
     - append message to conversation
     - retrieve conversation messages ordered by timestamp
   - Keep CQRS-lite if the project uses it.
   - Ensure tenant scoping is enforced in handlers/services/repositories.

8. **Add API surface only if needed**
   - If the solution already exposes communication endpoints, wire them to the new persistence behavior.
   - If no endpoint exists and tests need one, add minimal endpoints such as:
     - create conversation
     - post message
     - get messages for conversation
   - Follow existing auth, company context, and error handling patterns.

9. **Update timestamps correctly**
   - `CreatedAt` should be set when records are created.
   - `UpdatedAt` on conversations should update when:
     - conversation is created
     - a new message is appended
   - Use UTC consistently.

10. **Add tests**
   - Cover at least:
     - message persistence includes sender type, sender ID, conversation ID, company ID, created timestamp
     - conversation persistence includes channel/conversation type and timestamps
     - messages are returned in chronological order
     - tenant scoping prevents cross-company access
     - nullable `sender_id` behavior for system messages if supported
   - Prefer integration tests against the actual persistence stack used by the project.

11. **Keep implementation aligned with ST-501**
   - This task is only the storage foundation for direct chat.
   - Do not overbuild orchestration, task creation, or UI features unless required to make persistence reachable.

# Validation steps
Run these after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration compiles and applies in the project’s normal workflow.

4. Manually validate, via tests or endpoint calls, that:
   - a conversation can be created with a direct-agent channel/conversation type
   - a message can be stored with:
     - sender type
     - sender ID
     - timestamp
   - stored messages are linked to the correct conversation and company
   - conversation timestamps are populated
   - conversation `updated_at` changes when a new message is added
   - cross-tenant reads/writes are rejected or not found per existing conventions

5. Confirm schema/mapping alignment with the architecture model:
   - `conversations.channel_type`
   - `messages.sender_type`
   - `messages.sender_id`
   - timestamps present and populated

# Risks and follow-ups
- **Existing partial implementation risk:** The repository may already contain chat entities with different naming. Prefer adapting existing models over introducing parallel ones.
- **Migration risk:** If prior migrations already created conversation/message tables, nullability or enum storage changes may require careful data migration.
- **Tenant isolation risk:** It is easy to scope by `conversation_id` only and accidentally miss `company_id`; enforce both.
- **Timestamp consistency risk:** Ensure UTC usage and consistent update of conversation `updated_at` when messages are appended.
- **Enum/string compatibility risk:** If APIs already serialize string values, preserve backward-compatible representations.
- **Follow-up likely needed:** Subsequent tasks will probably add:
  - direct-agent conversation creation UX
  - agent response generation
  - pagination
  - task linking from chat
  - rationale summaries and structured payload handling

When done, provide:
- a concise summary of files changed
- migration name
- tests added/updated
- any assumptions made about existing chat models or tenant context handling