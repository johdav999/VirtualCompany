# Goal
Implement backlog task **TASK-9.4.1** for **ST-304 Grounded context retrieval service** by adding a deterministic, tenant-safe retrieval service in the .NET backend that composes prompt-ready context from:

- company documents / semantic knowledge chunks
- agent/company memory
- recent task history
- relevant structured records

The service must:

- respect tenant boundaries and agent data scopes
- return normalized structured sections suitable for downstream prompt building
- produce source references that can be persisted for audit/explainability
- remain application-layer driven, not embedded in controllers or UI
- be testable with clear contracts and predictable ranking/selection behavior

# Scope
In scope:

- Define application-layer contract(s) for grounded context retrieval
- Implement orchestration-facing retrieval service in the Application layer
- Add Infrastructure query/repository support for:
  - semantic document retrieval from `knowledge_chunks`
  - memory retrieval from `memory_items`
  - recent task retrieval from `tasks`
  - relevant record retrieval from currently available structured entities
- Normalize results into structured prompt-ready sections
- Include source reference metadata in the response model
- Enforce:
  - `company_id` scoping
  - agent scope filtering
  - deterministic ordering and limits
- Add unit/integration tests for retrieval composition and scope enforcement

Out of scope unless already trivially supported by existing code:

- Full prompt builder implementation
- UI/controller work beyond minimal wiring if an internal endpoint/test harness already exists
- Redis caching unless there is already a cache abstraction in place
- New ingestion/chunking pipeline work from ST-301/ST-302
- New audit persistence pipeline beyond returning source references in a persistable shape

Assumptions to honor:

- Modular monolith / clean architecture
- PostgreSQL + pgvector
- Shared-schema multi-tenancy with `company_id`
- Retrieval should be deterministic and testable
- No direct prompt assembly in controllers/UI

# Files to touch
Inspect the solution first, then update the closest existing files/modules. Expected areas:

- `src/VirtualCompany.Application/**`
  - add retrieval service interface and models
  - add use case / query handler if the codebase uses MediatR/CQRS
  - add scope filtering and normalization logic
- `src/VirtualCompany.Domain/**`
  - add value objects / enums only if needed for source references or retrieval section typing
- `src/VirtualCompany.Infrastructure/**`
  - implement repositories/query services for:
    - knowledge chunk semantic search
    - memory retrieval
    - recent task retrieval
    - relevant record lookup
  - add EF Core / SQL query logic with tenant filters
- `src/VirtualCompany.Api/**`
  - register DI wiring
  - optionally expose/internalize a minimal endpoint only if the architecture already exposes application queries this way
- `src/VirtualCompany.Shared/**`
  - shared DTOs only if this repo already uses Shared for cross-project contracts
- `tests/**` or corresponding test projects
  - add unit tests for composition logic
  - add integration tests for tenant/scope filtering and deterministic ordering

Likely concrete file additions/updates, depending on existing conventions:

- `Application/AI/ContextRetrieval/IContextRetrievalService.cs`
- `Application/AI/ContextRetrieval/Models/*`
- `Application/AI/ContextRetrieval/ContextRetrievalService.cs`
- `Infrastructure/AI/ContextRetrieval/*`
- `Infrastructure/Persistence/*DbContext*` or repository/query classes
- `Api/DependencyInjection/*` or `Program.cs`
- test files under existing Application/Infrastructure test projects

Use actual project structure and naming conventions already present in the repository rather than forcing these exact paths.

# Implementation plan
1. **Inspect existing architecture and conventions**
   - Review `README.md`, project references, DI setup, persistence patterns, and any existing:
     - agent config models
     - knowledge/memory repositories
     - task query services
     - orchestration/prompt builder contracts
   - Determine whether the codebase uses:
     - MediatR
     - repository pattern
     - query services
     - Result/Error wrapper types
     - specification pattern
   - Reuse existing patterns exactly.

2. **Define retrieval contract**
   Create an application-layer contract for grounded context retrieval. The request should include enough information to enforce scope and compose context, for example:
   - `CompanyId`
   - `AgentId`
   - optional `TaskId`
   - optional `UserId` / actor context if needed for permission-aware retrieval
   - user/query text or task prompt text
   - optional retrieval options:
     - max document chunks
     - max memory items
     - max recent tasks
     - max records
     - recency window
   - correlation id if the codebase already uses one

   The response should be normalized and prompt-ready, for example:
   - `Documents` section
   - `Memory` section
   - `RecentTasks` section
   - `RelevantRecords` section
   - flattened `SourceReferences`
   - optional summary metadata such as counts and truncation flags

3. **Define normalized models**
   Add strongly typed models for:
   - retrieval request
   - retrieval result
   - context section entries
   - source references

   Source references should be persistable and human/audit friendly. Include fields like:
   - source type (`document_chunk`, `memory_item`, `task`, `record`)
   - source entity type
   - source entity id
   - title/label
   - excerpt/summary
   - score / rank
   - document id / chunk id where applicable
   - metadata needed for explainability

4. **Implement deterministic composition service**
   Build the application service that:
   - validates request inputs
   - loads agent configuration/scopes
   - queries each source independently
   - applies deterministic ranking and caps
   - normalizes into structured sections
   - returns source references in stable order

   Determinism requirements:
   - explicit ordering rules
   - stable tie-breakers (e.g. score desc, recency desc, id asc)
   - fixed defaults for limits
   - no hidden randomness

5. **Implement document retrieval**
   In Infrastructure, add semantic retrieval over `knowledge_chunks` with:
   - `company_id` filter
   - access scope filter
   - agent data scope filter
   - top-k similarity ranking
   - source document reference projection

   Ensure results include:
   - document title
   - chunk content/excerpt
   - document/chunk ids
   - similarity score
   - metadata/access scope if needed for filtering

   If embeddings/query vector generation already exists, use it. If not, define an abstraction for embedding the query text and use the existing provider registration pattern.

6. **Implement memory retrieval**
   Add retrieval for `memory_items` with:
   - `company_id` filter
   - agent-specific + company-wide memory support
   - validity window filtering (`valid_from`, `valid_to`)
   - salience/recency/relevance ordering
   - optional semantic relevance if embeddings are already available

   Prefer deterministic blended ranking rather than opaque heuristics. Document the ranking formula in code comments.

7. **Implement recent task retrieval**
   Add retrieval for recent tasks scoped by:
   - `company_id`
   - assigned agent / related task / parent task if relevant
   - recent completed/in-progress tasks
   - optional text relevance if supported, otherwise recency-based selection

   Return concise normalized task context:
   - title
   - description summary
   - status
   - rationale summary
   - output summary if available
   - timestamps

8. **Implement relevant record retrieval**
   Retrieve structured records from currently available entities only. Do not invent broad generic DB access.
   Start with the most relevant existing entities in the repo, likely:
   - agent profile/config summary
   - current task
   - workflow instance summary
   - approvals linked to the task/workflow
   - company metadata if already modeled for orchestration context

   Keep this bounded and typed. The goal is “relevant records,” not unrestricted table querying.

9. **Enforce scope and permission filtering**
   Respect:
   - tenant isolation via `company_id`
   - agent data scopes from agent configuration
   - any existing human/company permission model if available in current application services

   If scope rules are not fully implemented elsewhere, add a narrow internal scope evaluator for retrieval only, with explicit TODOs for future centralization.

   Default behavior should be conservative:
   - if scope is missing/ambiguous, exclude the item
   - never cross tenant boundaries
   - never return unrestricted records by default

10. **Normalize into prompt-ready sections**
    Return structured sections with concise text and metadata, not raw EF entities. Example shape:
    - `Documents`: list of snippets with title + excerpt
    - `Memory`: list of summaries/preferences/patterns
    - `RecentTasks`: list of task summaries
    - `RelevantRecords`: list of typed record summaries

    Keep formatting neutral so the Prompt Builder can assemble final prompt text later.

11. **Prepare source references for downstream audit**
    Ensure every returned item maps to a source reference entry that downstream orchestration/audit code can persist without re-querying. Include enough metadata for explainability:
    - what source was used
    - why it was included (section/rank/score)
    - human-readable label

12. **Register DI and wire into existing orchestration path**
    - Register the new service in DI
    - If an orchestration service already exists, integrate via interface only
    - Do not move prompt assembly into this service
    - Keep HTTP/UI concerns out of the implementation

13. **Add tests**
    Add focused tests for:
    - composition from all four source categories
    - tenant isolation
    - agent scope filtering
    - deterministic ordering
    - exclusion of expired memory
    - source reference generation
    - empty-result behavior
    - ambiguous/missing scope defaults to conservative exclusion

    Prefer:
    - unit tests for composition/ranking/normalization
    - integration tests for EF/SQL filtering behavior

14. **Keep implementation incremental and repository-aligned**
    If some dependencies are missing in the current repo:
    - add minimal abstractions
    - avoid speculative framework build-out
    - leave concise TODOs for caching/audit persistence integration if not yet present

# Validation steps
1. Restore/build solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there is a targeted test project, run it first for faster iteration.

4. Verify retrieval behavior with tests covering:
   - document + memory + task + record composition in one response
   - same query across two companies does not leak data
   - agent with narrower scope gets fewer/no results than broader-scope agent
   - expired memory items are excluded
   - source references are returned for every included item
   - ordering is stable across repeated runs with same seed data

5. If an internal endpoint or orchestration integration exists, manually verify:
   - request for a known task/agent returns structured sections
   - no raw entities or prompt text blobs leak from the service
   - source references are suitable for later audit persistence

6. Check code quality:
   - no tenant-unfiltered queries
   - no controller/UI prompt assembly
   - no direct DB access from orchestration outside Infrastructure abstractions
   - no nondeterministic ordering without tie-breakers

# Risks and follow-ups
- **Schema/repo mismatch risk:** The architecture describes tables/entities that may not yet exist in code. Adapt to actual implemented entities and keep the service extensible.
- **Embedding dependency risk:** Query embedding generation may not yet be wired. If missing, add a minimal abstraction and keep semantic retrieval isolated behind it.
- **Scope model ambiguity:** Agent `data_scopes_json` may not yet have a finalized schema. Implement conservative filtering and document assumptions clearly.
- **Relevant records breadth:** Avoid overengineering a generic record search engine. Start with currently available typed records and extend later.
- **Audit persistence gap:** This task should at minimum return persistable source references; full persistence may belong to downstream orchestration/audit stories.
- **Caching follow-up:** Redis caching is noted in story guidance, but only add it if a cache abstraction already exists. Otherwise leave a targeted follow-up.
- **Ranking tuning follow-up:** Initial deterministic ranking should favor correctness and explainability over sophistication; future tuning can improve relevance once production data exists.