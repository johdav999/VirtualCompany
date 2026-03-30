# Goal
Implement backlog task **TASK-ST-304 — Grounded context retrieval service** in the existing .NET solution by adding a deterministic, testable application service that assembles grounded orchestration context from tenant-scoped sources including:

- company knowledge documents/chunks
- agent/company memory
- recent task history
- relevant structured records available in current modules

The service must enforce tenant and scope boundaries, return **structured prompt-ready sections**, and persist **retrieval source references** for downstream audit/explainability use.

# Scope
Implement the minimum vertical slice needed for ST-304 in the current modular monolith, aligned to the architecture and backlog.

In scope:

- Add a **context retrieval service** in the application layer, independent from controllers/UI.
- Define request/response contracts for retrieval that are deterministic and prompt-ready.
- Retrieve and compose context from:
  - `knowledge_chunks`
  - `memory_items`
  - recent `tasks`
  - optionally lightweight relevant records already available without inventing new subsystems
- Enforce:
  - `company_id` tenant scoping
  - agent data scopes / access scope filtering
  - safe defaults when scope config is missing or ambiguous
- Persist retrieval source references in a business-facing store suitable for later audit/explanation linkage.
- Add Redis-backed caching only if there is already an established cache abstraction; otherwise leave extension points and keep implementation simple.
- Add tests for composition, scoping, determinism, and persistence behavior.

Out of scope unless already trivial in the codebase:

- full prompt builder implementation
- UI screens
- new ingestion/chunking pipeline work beyond what ST-302/ST-303 already established
- broad new audit UX
- speculative generic search framework
- direct LLM invocation

If the repository already contains partial implementations for retrieval, memory, audit, or orchestration, extend them rather than duplicating patterns.

# Files to touch
Inspect the solution first and then touch only the files needed. Expected areas:

- `src/VirtualCompany.Application/...`
  - add retrieval service interface and implementation
  - add DTOs/contracts for request/response
  - add query/specification abstractions if needed
  - add command/service for persisting retrieval references
- `src/VirtualCompany.Domain/...`
  - add domain models/value objects/enums only if needed for retrieval source references or scope evaluation
- `src/VirtualCompany.Infrastructure/...`
  - implement data access for retrieval against PostgreSQL/EF Core
  - add pgvector-backed similarity query integration if ST-302 already introduced embeddings
  - add persistence for retrieval source references
  - add optional cache integration behind existing abstractions
- `src/VirtualCompany.Api/...`
  - register DI only if needed
  - expose endpoint only if the project already exposes internal orchestration APIs; otherwise keep internal
- tests in the existing test projects
  - unit tests for composition and scope filtering
  - integration tests for tenant-safe retrieval and persistence

Likely concrete additions, adapt to actual project structure:

- `Application/AI/Context/...`
- `Application/Knowledge/...`
- `Application/Tasks/...`
- `Infrastructure/Persistence/...`
- `Infrastructure/AI/...`
- `Infrastructure/Caching/...`

Also inspect:
- existing entity mappings for `knowledge_chunks`, `memory_items`, `tasks`, `audit_events`
- existing multi-tenant query enforcement patterns
- existing orchestration service boundaries
- existing outbox/audit patterns

# Implementation plan
1. **Inspect the current architecture in code**
   - Identify how the solution organizes:
     - application services / handlers
     - repositories or DbContext usage
     - tenant context resolution
     - authorization/scope enforcement
     - audit persistence
     - caching abstractions
   - Reuse existing conventions, namespaces, result types, and DI patterns.
   - Do not introduce a parallel architecture.

2. **Define the retrieval contract**
   Create a request/response model for a grounded retrieval operation. The request should support at minimum:
   - `CompanyId`
   - `AgentId`
   - actor/user context if available
   - task context:
     - `TaskId` optional
     - task title/description or query text
   - retrieval intent / query text
   - limits per source type
   - correlation id if the codebase uses one

   The response should be **structured prompt-ready sections**, not a flat blob. Example shape:
   - `CompanyContextSection`
   - `KnowledgeSection`
   - `MemorySection`
   - `RecentTaskSection`
   - `RelevantRecordsSection`
   - `SourceReferences`
   - `Diagnostics` or `AppliedFilters` if useful for tests/internal tracing

   Each section should contain normalized items with:
   - source type
   - source id
   - title/label
   - concise content/snippet/summary
   - relevance score if available
   - timestamps/metadata needed for explainability

   Keep ordering deterministic:
   - stable sort by source type priority, score, recency, then id
   - explicit caps per section

3. **Model retrieval source references persistence**
   Because ST-304 requires source references to be persisted for downstream audit/explanation, add a persistence mechanism that fits the current schema style.

   Preferred approach:
   - add a dedicated table/entity such as `context_retrievals` and/or `context_retrieval_sources`
   - store:
     - retrieval id
     - company id
     - agent id
     - task id/workflow id if present
     - query text / retrieval purpose
     - created at
     - normalized source references:
       - source type
       - source entity id
       - source label/title
       - rank
       - score
       - snippet/summary
       - metadata json

   If adding a new table is too heavy relative to current codebase patterns, persist as structured business records in an existing audit/explainability mechanism only if that mechanism is already clearly intended for source references. Prefer explicit retrieval records over overloading technical logs.

4. **Implement scope evaluation**
   Add a small, testable scope evaluator that determines what the agent may read for retrieval.
   Inputs should include:
   - agent `data_scopes_json`
   - company/tenant id
   - human/company permission context if available
   - document access scope metadata
   - memory ownership rules
   - task visibility rules already present in code

   Rules:
   - always filter by `company_id`
   - default deny on ambiguous scope
   - only include documents/chunks whose access scope is allowed
   - only include memory items allowed for the agent/company context
   - only include recent tasks visible within tenant and allowed scope
   - never bypass scope checks for similarity search

   Keep this logic isolated and unit tested.

5. **Implement source retrieval pipelines**
   Build separate internal retrievers per source type, then compose them in one orchestrating service.

   ## Knowledge retrieval
   - Use semantic search over `knowledge_chunks` if embeddings/vector search already exist.
   - Filter by:
     - `company_id`
     - document access scope
     - ingestion/availability status if applicable
   - Join back to `knowledge_documents` for title/reference metadata.
   - Return top N chunks with document references.
   - If vector search infra is not yet present in code, implement a clean abstraction and a temporary fallback only if necessary; do not fake semantic retrieval if ST-302 already exists.

   ## Memory retrieval
   - Query `memory_items` by:
     - `company_id`
     - agent-specific or company-wide applicability
     - validity window
     - optional semantic relevance if embeddings exist
     - salience and recency
   - Exclude expired items.
   - Prefer summaries over raw content if both exist.

   ## Recent task retrieval
   - Pull recent relevant tasks by:
     - same agent
     - same parent task/workflow if provided
     - recency window
     - status relevance
   - Include concise fields only:
     - title
     - description summary
     - rationale summary
     - output summary
     - confidence score
   - Avoid dumping large payloads into prompt-ready output.

   ## Relevant records retrieval
   - Only include if there are already obvious structured records in the codebase that can be safely queried now.
   - Examples might include workflow/approval/task-linked records already modeled.
   - Keep this section optional and conservative.

6. **Compose normalized prompt-ready output**
   The service should return a structured object suitable for the future prompt builder. Normalize content into concise sections such as:
   - `InstructionsContext` or `RuntimeContext`
   - `KnowledgeSnippets`
   - `MemorySummaries`
   - `RecentHistory`
   - `StructuredRecords`
   - `Citations`

   Requirements:
   - no UI formatting concerns
   - no direct prompt string assembly
   - concise, bounded content
   - deterministic ordering
   - include source references for every included item

7. **Persist retrieval event and source references**
   After composing the final result:
   - persist a retrieval record and included source references
   - link to `task_id`, `workflow_instance_id`, or correlation id when available
   - ensure persistence is tenant-scoped
   - make persistence part of the application workflow, not just logging

   If the codebase uses unit of work / transaction boundaries, follow existing patterns.

8. **Add optional caching behind abstraction**
   Only if there is already a cache abstraction or Redis pattern:
   - cache low-risk repeated retrievals for a short TTL
   - cache key should include:
     - company id
     - agent id
     - normalized query
     - task id if present
     - relevant scope/version markers
   - do not cache across tenants
   - do not cache if request includes highly dynamic or sensitive context unless existing patterns allow it

   If no cache abstraction exists, add a TODO/extension point and skip implementation.

9. **Wire into DI and orchestration boundary**
   - Register the service in DI.
   - If there is an orchestration service already, integrate the new retrieval service there through an interface.
   - Keep controllers/UI unaware of retrieval internals.

10. **Testing**
   Add focused tests covering:

   ## Unit tests
   - retrieval composes sections from multiple source types
   - deterministic ordering for equal/near-equal results
   - default-deny behavior when scope config is missing/ambiguous
   - memory validity window filtering
   - task history normalization excludes oversized/raw payloads
   - source references are generated for all included items

   ## Integration tests
   - tenant A cannot retrieve tenant B data
   - agent scope restrictions exclude disallowed documents/memory/tasks
   - persisted retrieval references match returned results
   - vector/semantic retrieval respects pre-filtering by tenant/scope before ranking, or equivalent safe query behavior in the chosen implementation

11. **Keep implementation production-sensible**
   - Use async APIs and cancellation tokens.
   - Bound result sizes.
   - Avoid N+1 queries.
   - Prefer explicit projections over loading full entities.
   - Keep snippets concise and sanitized.
   - Do not expose chain-of-thought or internal reasoning.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects, run the retrieval-related tests specifically.

4. Manually verify in code/tests that:
   - retrieval requires tenant/company context
   - all queries filter by `company_id`
   - scope filtering is applied before results are returned
   - response shape is structured and prompt-ready
   - source references are persisted
   - ordering is deterministic
   - no prompt assembly logic leaked into controllers/UI

5. If an API/internal endpoint exists for orchestration testing, exercise a sample retrieval flow and confirm:
   - knowledge chunks are returned with document references
   - memory items are filtered by validity and scope
   - recent tasks are concise and relevant
   - persisted retrieval records can be linked to the request/task

6. If caching was added:
   - verify cache keys are tenant-safe
   - verify cache invalidation/TTL is conservative
   - verify no cross-tenant leakage is possible

# Risks and follow-ups
- **Schema gap:** ST-304 requires persisted source references, but the provided architecture does not explicitly define retrieval tables. Add a focused schema for retrieval records if none exists.
- **Dependency on ST-302/ST-303:** If chunk embeddings or memory retrieval infrastructure is incomplete, implement clean abstractions and the smallest safe path without blocking future semantic retrieval.
- **Scope ambiguity:** `data_scopes_json` and document access metadata may not yet have a finalized schema. Default deny and keep evaluator isolated for later refinement.
- **Relevant records ambiguity:** The story mentions “relevant records,” but the exact record types may vary by current codebase maturity. Keep this section optional and limited to already-modeled entities.
- **Caching correctness:** Retrieval caching can create stale or over-broad results if scope/version markers are weak. Prefer no cache over unsafe cache.
- **Audit integration:** If audit/explainability infrastructure is immature, persist retrieval references in dedicated business tables now and connect them to audit views later.
- **Performance:** pgvector queries plus joins can become expensive. Use bounded top-N retrieval, explicit projections, and indexes; leave tuning notes if needed.
- **Follow-up candidates:**
  - integrate with prompt builder in ST-502
  - expose retrieval diagnostics for internal admin tooling
  - add re-ranking/version metadata
  - add retrieval observability metrics
  - add invalidation hooks when documents or memory change