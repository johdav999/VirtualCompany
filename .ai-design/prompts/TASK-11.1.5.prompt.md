# Goal
Implement backlog task **TASK-11.1.5 — Keep conversation history tenant-scoped and paginated** for story **ST-501 Direct chat with named agents** in the existing .NET solution.

The coding agent should update the direct-chat conversation history flow so that:

- all conversation and message reads are strictly scoped to the active tenant/company
- cross-tenant access is prevented by query design, not just UI assumptions
- conversation history endpoints/queries return paginated results
- pagination is stable and deterministic for chat history
- the implementation fits the current architecture: ASP.NET Core backend, modular application layer, PostgreSQL persistence, shared-schema multi-tenancy with `company_id` enforcement

No explicit acceptance criteria were provided, so derive behavior from:
- ST-501 notes: **“Keep conversation history tenant-scoped and paginated.”**
- architecture guidance: **tenant-isolated data and agent execution context**
- data model: `conversations.company_id`, `messages.company_id`

# Scope
In scope:

- Find the existing direct conversation history API/query path for conversations/messages.
- Ensure all reads for:
  - listing conversations, and/or
  - loading a conversation thread,
  - loading message history
  are filtered by the resolved tenant/company context.
- Add pagination to message history retrieval, and to conversation listing too if that already exists and is part of the same flow.
- Use application-layer query models/DTOs rather than leaking EF entities.
- Preserve existing behavior for sender metadata and timestamps.
- Prefer deterministic ordering:
  - conversations: most recently updated first
  - messages: chronological display, but fetched via a stable paginated query
- Return pagination metadata if the project already has a standard pattern; otherwise introduce a minimal consistent one.

Out of scope unless required by existing code structure:

- redesigning chat UX
- adding real-time transport
- changing orchestration/prompt behavior
- creating task-linking behavior
- broad refactors unrelated to tenant scoping or pagination
- mobile-specific changes unless it consumes the same API contracts and breaks without updates

# Files to touch
Inspect first, then update only the necessary files. Likely areas:

- `src/VirtualCompany.Api/**`
  - chat/conversation controllers or minimal API endpoint mappings
  - request/response contracts if API-owned
- `src/VirtualCompany.Application/**`
  - conversation/message queries and handlers
  - DTOs/view models
  - pagination abstractions if present
  - tenant-aware application services
- `src/VirtualCompany.Infrastructure/**`
  - EF Core repositories / query services
  - DbContext configurations if needed
  - SQL/LINQ query implementations
- `src/VirtualCompany.Domain/**`
  - only if domain types need small additions; avoid unnecessary domain churn
- `src/VirtualCompany.Shared/**`
  - shared pagination contract if this project centralizes API models there
- tests in whichever test projects already cover application/API/infrastructure behavior

Also inspect:
- tenant resolution/access patterns from ST-101 implementation
- any existing `PagedResult`, `PageRequest`, cursor/offset models, or query conventions
- existing conversation/message entities and indexes

# Implementation plan
1. **Discover the current chat history flow**
   - Locate:
     - conversation entity/repository
     - message entity/repository
     - API endpoints for direct agent chat history
     - tenant context accessor/resolver used elsewhere
   - Identify whether the app currently supports:
     - list conversations
     - get conversation by id
     - get messages by conversation id
   - Reuse existing CQRS-lite patterns and authorization conventions.

2. **Enforce tenant scoping at the query boundary**
   - For every conversation/message read path, require the active `company_id` from tenant context.
   - Filter conversations by `company_id == currentCompanyId`.
   - Filter messages by both:
     - `message.company_id == currentCompanyId`
     - related `conversation.company_id == currentCompanyId` if needed for defense in depth
   - When loading a specific conversation by id, query by:
     - `conversation.Id == requestedId && conversation.CompanyId == currentCompanyId`
   - If not found under tenant scope, return the project’s standard not-found/forbidden-safe behavior, preferably **not found** to avoid tenant data leakage.

3. **Add pagination contracts**
   - Reuse existing pagination primitives if available.
   - If none exist, introduce a minimal request/response model such as:
     - request: `pageNumber`, `pageSize` or `beforeMessageId`/`cursor`
     - response: `items`, `pageNumber`, `pageSize`, `totalCount` or `hasMore`
   - Prefer the project’s established style.
   - Keep defaults conservative, e.g. default page size 50, max 100.

4. **Implement paginated message history retrieval**
   - Use a stable sort for storage query, ideally:
     - fetch by `CreatedAt DESC, Id DESC` for efficient paging
     - then reverse in application mapping if UI expects chronological ascending order
   - This avoids unstable paging when timestamps collide.
   - If the project already uses offset pagination, implement it consistently.
   - If easy and idiomatic, cursor/keyset pagination is better for chat history, but do not introduce unnecessary complexity if the codebase is offset-based.
   - Ensure page boundaries are deterministic.

5. **Paginate conversation listing if applicable**
   - If there is an endpoint/query for listing direct conversations, paginate it too.
   - Sort by `UpdatedAt DESC, Id DESC`.
   - Scope by tenant.
   - Include only fields needed by the UI:
     - conversation id
     - subject/agent display info if already modeled
     - last updated
     - optional last message preview if already supported

6. **Validate API contracts and controller/endpoints**
   - Update endpoint signatures to accept pagination parameters.
   - Validate page size bounds.
   - Ensure tenant context is resolved from the authenticated request, not client input.
   - Do not accept `companyId` from the caller for tenant-owned reads unless that is an established secured pattern.

7. **Add or update persistence indexes if needed**
   - Review whether indexes exist for efficient tenant-scoped chat reads.
   - If migrations are part of the repo and query performance would clearly benefit, add indexes such as:
     - `messages (company_id, conversation_id, created_at desc, id desc)`
     - `conversations (company_id, updated_at desc, id desc)`
   - Only add a migration if the project convention supports it and the change is justified.

8. **Add tests**
   - Application/API tests should cover:
     - user can only read conversation history for their tenant
     - requesting another tenant’s conversation returns not found/forbidden-safe result
     - message history is paginated
     - ordering is stable across pages
     - page size limits are enforced
   - If integration tests exist with EF/Postgres test setup, prefer them for tenant scoping.
   - Otherwise add focused unit tests around query handlers plus API tests where practical.

9. **Keep implementation minimal and aligned**
   - Avoid broad refactors.
   - Preserve existing naming and folder structure.
   - Document any assumptions in code comments only where necessary.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify the relevant API flow:
   - load a conversation for tenant A and confirm messages return
   - attempt to load the same conversation while authenticated as tenant B and confirm not found/forbidden-safe behavior
   - request first page and second page of message history and confirm:
     - no duplicates
     - no missing records
     - deterministic ordering
     - page size respected

4. If conversation listing exists, verify:
   - only tenant-owned conversations are returned
   - results are paginated
   - ordering is by most recently updated

5. If a migration/index was added:
   - ensure migration applies cleanly
   - ensure build/tests still pass

# Risks and follow-ups
- **Risk: hidden tenant leaks in alternate query paths**
  - There may be multiple ways to fetch messages/conversations. Search broadly for all read paths, not just one controller.

- **Risk: unstable pagination with timestamp-only ordering**
  - Use a tie-breaker such as `Id` alongside `CreatedAt`/`UpdatedAt`.

- **Risk: offset pagination on rapidly changing chats**
  - Offset pagination can shift under concurrent inserts. Accept if it matches project conventions, but note cursor/keyset pagination as a future improvement.

- **Risk: UI assumptions about full history**
  - Existing web/mobile clients may assume unpaginated results. Update contracts carefully and preserve sensible defaults.

- **Risk: missing indexes**
  - Tenant-scoped paginated chat queries can become slow without composite indexes.

Follow-ups to note in your final implementation summary if not completed now:
- consider cursor-based pagination for chat history
- add unread counts / last-message projections if conversation list performance becomes an issue
- consider repository/query-layer guardrails or global tenant filters for additional defense in depth