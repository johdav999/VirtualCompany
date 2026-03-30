# Goal
Implement backlog task **TASK-ST-303 — Agent and company memory persistence** in the existing .NET solution.

Deliver durable, tenant-scoped memory persistence for both **company-wide** and **agent-specific** memory records, aligned to the architecture and backlog story **ST-303**. The implementation should support:
- storing memory summaries and preferences
- classifying memory by type
- retrieving memory with filters for agent, recency, salience, and semantic relevance
- deleting or expiring memory items under policy-safe, tenant-safe controls

Do **not** store raw chain-of-thought. Persist only concise summaries and structured metadata.

# Scope
Implement the minimum production-ready vertical slice for memory persistence in the modular monolith, covering domain, application, infrastructure, and API layers.

Include:
- `memory_items` persistence model and EF Core mapping
- domain model for memory items
- application commands/queries for:
  - create memory item
  - list/search memory items
  - expire memory item
  - delete memory item
- tenant-aware filtering and authorization hooks
- support for:
  - company-wide memory (`agent_id = null`)
  - agent-specific memory (`agent_id != null`)
  - memory types: `preference`, `decision_pattern`, `summary`, `role_memory`, `company_memory`
  - validity windows (`valid_from`, `valid_to`)
  - salience
  - semantic retrieval using pgvector if the project already has vector support patterns; otherwise design the abstraction and implement non-vector fallback filtering now
- API endpoints or internal application handlers consistent with the existing project conventions
- tests for core business rules and tenant isolation behavior

Out of scope unless already scaffolded and trivial to complete:
- UI pages in Blazor
- full policy administration UX
- background summarization pipelines
- automatic memory extraction from conversations/tasks
- advanced privacy workflows beyond delete/expire support
- full audit UI, though emitting audit/domain events where conventions exist is encouraged

# Files to touch
Inspect the solution first and adapt to actual conventions, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - add memory domain entity/value objects/enums
  - add repository abstractions if domain/application owns them

- `src/VirtualCompany.Application/**`
  - commands for create/expire/delete
  - queries for retrieval/search
  - DTOs/contracts for memory records and filters
  - validators
  - service interfaces for embeddings/retrieval if needed
  - authorization/tenant-scoping behaviors if already present

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration for `memory_items`
  - DbContext updates
  - migration for `memory_items` table if not already present
  - repository/query implementation
  - pgvector integration/query logic if available in current stack
  - persistence of metadata JSON and validity windows

- `src/VirtualCompany.Api/**`
  - request/response contracts
  - endpoints/controllers/minimal APIs for memory CRUD/search
  - DI registration

- `src/VirtualCompany.Shared/**`
  - shared enums/contracts only if this repo uses Shared for API-safe contracts

- `README.md`
  - only if a short developer note is needed for migration or vector prerequisites

Also inspect for existing related files before creating new ones:
- tenant context abstractions
- base entity classes
- auditing interfaces
- pagination/query result models
- existing knowledge/document retrieval services from ST-301/ST-302
- existing PostgreSQL/pgvector setup

# Implementation plan
1. **Inspect current architecture and conventions**
   - Review solution structure and existing patterns for:
     - entities and aggregate roots
     - CQRS handlers
     - validation
     - tenant resolution
     - authorization
     - EF Core mappings and migrations
     - pagination/search endpoints
   - Reuse existing naming and layering conventions exactly.

2. **Model the memory domain**
   - Add a `MemoryItem` domain entity representing durable memory.
   - Include fields equivalent to the architecture model:
     - `Id`
     - `CompanyId`
     - `AgentId` nullable
     - `MemoryType`
     - `Summary`
     - `SourceEntityType`
     - `SourceEntityId` nullable
     - `Salience`
     - `ValidFrom`
     - `ValidTo` nullable
     - embedding/vector field handled at infrastructure layer as appropriate
     - `MetadataJson` or typed metadata abstraction depending on current conventions
     - `CreatedAt`
   - Add a `MemoryType` enum or strongly typed value object with allowed values:
     - `Preference`
     - `DecisionPattern`
     - `Summary`
     - `RoleMemory`
     - `CompanyMemory`
   - Enforce core invariants:
     - `CompanyId` required
     - `Summary` required and bounded in length
     - `Salience` within valid range
     - `ValidTo >= ValidFrom` when present
     - company-wide memory has `AgentId = null`
   - Do not add chain-of-thought fields.

3. **Add persistence mapping**
   - Create/update EF Core configuration for `memory_items`.
   - Map to PostgreSQL types appropriately:
     - UUID keys
     - timestamptz
     - numeric/decimal for salience
     - JSONB for metadata
     - vector column if pgvector is already configured
   - Add indexes useful for retrieval:
     - `(company_id, agent_id)`
     - `(company_id, memory_type)`
     - `(company_id, created_at desc)`
     - `(company_id, valid_to)`
     - vector index if supported and already used elsewhere
   - Generate a migration if migrations are part of the repo workflow.

4. **Create application contracts**
   - Add command/query models such as:
     - `CreateMemoryItemCommand`
     - `SearchMemoryItemsQuery`
     - `ExpireMemoryItemCommand`
     - `DeleteMemoryItemCommand`
   - Add DTOs:
     - `MemoryItemDto`
     - `MemorySearchResultDto`
     - `MemorySearchFilters`
   - Include filters for:
     - `AgentId`
     - `MemoryType`
     - `CreatedAfter` / `CreatedBefore` or recency window
     - `MinSalience`
     - `OnlyActive` / validity filtering
     - semantic search text if supported
     - pagination

5. **Implement create memory flow**
   - Create a handler/service that:
     - resolves current tenant/company context
     - validates optional `AgentId` belongs to same company if agent validation infrastructure exists
     - persists memory item
     - optionally generates embedding from summary if embedding service exists
   - If no embedding service exists yet:
     - keep the abstraction in place
     - store null/no embedding
     - ensure non-semantic retrieval still works
   - Return created memory DTO.

6. **Implement retrieval/search flow**
   - Build a tenant-scoped query service for memory retrieval.
   - Retrieval behavior should support:
     - company-wide memory
     - agent-specific memory
     - optional inclusion of both when querying for an agent
     - recency ordering
     - salience filtering/sorting
     - validity window filtering:
       - active if `ValidFrom <= now` and (`ValidTo is null` or `ValidTo > now`)
     - semantic relevance:
       - if vector support exists, embed query text and rank by similarity within tenant/scope filters
       - otherwise fallback to deterministic filtering and ordering, e.g. by active status, salience desc, created_at desc, optional text match on summary
   - Ensure retrieval never crosses tenant boundaries.
   - Prefer deterministic composition so ST-304 can build on this later.

7. **Implement expire and delete flows**
   - `ExpireMemoryItem` should set `ValidTo` to a supplied timestamp or `UtcNow`, tenant-scoped.
   - `DeleteMemoryItem` should hard delete only if that matches current conventions; otherwise soft delete if the codebase already uses soft deletion.
   - If no convention exists, implement hard delete carefully and document it in code comments.
   - Both operations must:
     - verify tenant ownership
     - optionally verify role/authorization hooks if available
     - return not found for cross-tenant access attempts where that is the project norm

8. **Expose API endpoints**
   - Add endpoints consistent with existing API style, for example:
     - `POST /api/memory`
     - `GET /api/memory`
     - `POST /api/memory/search` if complex search body is preferred
     - `POST /api/memory/{id}/expire`
     - `DELETE /api/memory/{id}`
   - Keep request/response contracts concise and safe.
   - Ensure company context is derived from authenticated tenant context, not caller-supplied raw trust.
   - If company ID is passed anywhere, validate it against resolved tenant context.

9. **Authorization and tenant safety**
   - Reuse existing tenant-aware authorization patterns.
   - Ensure all repository queries include `company_id`.
   - Validate that agent-specific memory cannot reference an agent from another company.
   - Deletion/expiration must respect tenant boundaries and future privacy controls by keeping the implementation isolated behind application services.

10. **Testing**
   - Add unit and/or integration tests for:
     - create company-wide memory
     - create agent-specific memory
     - reject invalid memory type or invalid validity window
     - retrieve only active memory
     - retrieve by agent including company-wide records when intended
     - salience and recency filtering
     - semantic retrieval path if infrastructure exists
     - expire memory item
     - delete memory item
     - tenant isolation: cannot read/update/delete another company’s memory
   - Prefer integration tests around persistence/query behavior if the repo already supports database-backed tests.

11. **Keep implementation extensible for ST-304**
   - Structure retrieval behind an interface/service that ST-304 can compose with documents, tasks, and records.
   - Return source/reference metadata in DTOs where useful.
   - Keep embedding model/version extensibility in mind if there is already a pattern from document chunk embeddings.

# Validation steps
1. Restore and build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. Apply/create database migration if applicable and verify schema:
   - confirm `memory_items` exists with expected columns and indexes
   - confirm JSONB and vector mappings are valid for PostgreSQL

4. Verify create flow:
   - create a company-wide memory item
   - create an agent-specific memory item
   - confirm records persist with correct `company_id`, `agent_id`, `memory_type`, `salience`, and validity fields

5. Verify retrieval behavior:
   - query all active company memory
   - query memory for a specific agent
   - confirm filtering by:
     - agent
     - recency
     - salience
     - active validity window
   - if semantic search is implemented, verify relevant results rank above unrelated ones within the same tenant

6. Verify mutation behavior:
   - expire a memory item and confirm it no longer appears in active-only retrieval
   - delete a memory item and confirm it is no longer returned

7. Verify tenant isolation:
   - attempt to read, expire, or delete memory from another tenant context
   - confirm forbidden/not found behavior matches existing API conventions

8. Run full test suite again:
   - `dotnet test`

9. Final build verification:
   - `dotnet build`

# Risks and follow-ups
- **Unknown repository conventions:** The exact CQRS, endpoint, and EF patterns may differ. Inspect first and conform rather than inventing a parallel style.
- **pgvector availability:** If vector support is not yet wired into infrastructure, do not block delivery. Implement the retrieval abstraction and a deterministic non-vector fallback now, leaving semantic ranking pluggable.
- **Agent validation dependency:** Agent-specific memory should ideally validate referenced agent ownership. If agent module APIs are incomplete, add a minimal lookup abstraction rather than coupling directly to DB tables from the API layer.
- **Delete semantics:** The story says users can delete or expire memory items, but current platform-wide deletion conventions may be unclear. Prefer consistency with existing soft/hard delete patterns.
- **Authorization depth:** No explicit acceptance criteria define who may delete/expire memory. Reuse existing admin/manager authorization if present; otherwise keep authorization hooks centralized for later tightening.
- **Auditability gap:** ST-303 does not require full audit UI, but memory mutations are good candidates for audit events. Add them if the audit pipeline already exists.
- **Future privacy controls:** Design handlers so retention rules, redaction, legal hold, or user-requested deletion can be added later without breaking API contracts.
- **ST-304 dependency:** Keep memory retrieval service composable and prompt-agnostic so the grounded context retrieval service can reuse it directly.