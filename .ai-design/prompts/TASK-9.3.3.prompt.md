# Goal
Implement backlog task **TASK-9.3.3 — Memory retrieval can filter by agent, recency, salience, and semantic relevance** for story **ST-303 Agent and company memory persistence** in the existing .NET modular monolith.

Deliver a production-ready memory retrieval capability in the **Knowledge & Memory Module** that:
- retrieves tenant-scoped memory items from `memory_items`
- supports both **agent-specific** and **company-wide** memory
- filters/ranks by:
  - **agent**
  - **recency**
  - **salience**
  - **semantic relevance**
- respects validity windows and tenant isolation
- is implemented behind application/infrastructure abstractions suitable for later use by the orchestration/context retrieval pipeline

No UI is required unless the existing codebase already exposes this via an internal API pattern and the minimal endpoint/query handler is necessary to exercise the feature.

# Scope
In scope:
- Add or extend domain/application models for memory retrieval criteria and results
- Implement retrieval query/service abstraction in Application
- Implement PostgreSQL/EF Core/SQL-backed retrieval in Infrastructure
- Support semantic similarity against `memory_items.embedding` using pgvector-compatible querying if the project already uses pgvector patterns
- Support deterministic ranking that combines:
  - semantic similarity
  - salience
  - recency
- Include filtering for:
  - `company_id`
  - optional `agent_id`
  - inclusion of company-wide memory (`agent_id is null`)
  - active validity window (`valid_from`, `valid_to`)
  - optional memory types if a suitable enum/value object already exists
- Add tests covering ranking/filter behavior and tenant isolation
- Wire into DI

Out of scope unless already required by existing architecture:
- Full prompt assembly
- UI pages
- Broad context retrieval composition across documents/tasks/records
- Memory creation/deletion flows beyond what is needed for retrieval
- Reworking unrelated ingestion/embedding pipelines

Implementation expectations:
- Prefer existing project conventions, naming, CQRS-lite patterns, MediatR/query handlers, repository/query services, and result models
- Keep retrieval deterministic and testable
- Do not expose raw chain-of-thought; only stored summaries and metadata already modeled in `memory_items`

# Files to touch
Inspect the solution first and then update the actual matching files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - memory entity/value objects/enums if needed
- `src/VirtualCompany.Application/**`
  - memory retrieval contracts
  - query DTOs/models
  - query handler/service
- `src/VirtualCompany.Infrastructure/**`
  - DbContext configuration for `memory_items`
  - repository/query implementation
  - pgvector/SQL query logic
  - DI registration
- `src/VirtualCompany.Api/**`
  - only if an API endpoint/query exposure already fits the project pattern
- `tests/**` or existing test projects
  - unit tests for ranking/filter logic
  - integration tests for persistence/query behavior if test infrastructure exists

Also inspect:
- existing `DbContext` and entity mappings
- any current `Knowledge`, `Memory`, `Retrieval`, or `Orchestration` namespaces
- any existing vector search implementation for `knowledge_chunks` that can be mirrored for `memory_items`

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect the solution structure and identify:
     - how queries are modeled in Application
     - how repositories/query services are defined
     - whether EF Core, Dapper, or raw SQL is used for vector search
     - whether pgvector is already configured for `knowledge_chunks`
     - whether `memory_items` entity/mapping already exists
   - Reuse existing conventions exactly.

2. **Define retrieval contract**
   - Add an application-layer request/criteria model for memory retrieval, for example:
     - `CompanyId`
     - `AgentId` optional
     - `QueryText` optional
     - `QueryEmbedding` optional/preferred depending on current architecture
     - `Top` / `Limit`
     - `MemoryTypes` optional
     - `AsOfUtc`
     - weighting/tuning parameters only if the codebase already supports configurable retrieval scoring
   - Add a result model containing only prompt-safe fields, e.g.:
     - memory item id
     - agent id
     - memory type
     - summary
     - salience
     - created at
     - validity window
     - semantic score / combined score if useful internally
     - source entity references / metadata if already standard

3. **Implement filtering semantics**
   - Retrieval must always enforce tenant scope by `company_id`
   - If `AgentId` is provided:
     - include memory where `agent_id == AgentId`
     - also include company-wide memory where `agent_id is null`
   - If `AgentId` is not provided:
     - retrieve company-wide memory and, only if existing behavior dictates, avoid cross-agent leakage by not returning agent-specific memory unless explicitly requested
   - Enforce validity:
     - `valid_from <= asOf`
     - `valid_to is null or valid_to >= asOf`
   - Exclude expired memory
   - If soft-delete/policy flags already exist in metadata or schema, honor them using existing conventions

4. **Implement ranking**
   - Build a combined ranking strategy that is deterministic and documented in code comments.
   - Expected ranking inputs:
     - **semantic relevance**: vector similarity when query embedding is available
     - **salience**: normalized from stored `salience`
     - **recency**: derived from `created_at` and/or validity window
   - Recommended approach:
     - compute candidate set with SQL/vector similarity first
     - apply normalized weighted score in query or in application code depending on current stack
   - Example scoring shape:
     - `combined = semanticWeight * semanticScore + salienceWeight * salienceScore + recencyWeight * recencyScore`
   - Keep weights centralized and easy to tune, but do not over-engineer configuration unless a config pattern already exists.
   - If no semantic query is provided:
     - fall back to salience + recency ordering
   - Ensure stable tie-breaking, e.g. by `created_at desc`, then `id`

5. **Implement infrastructure query**
   - If pgvector is already used:
     - mirror the same approach for `memory_items.embedding`
     - use provider-supported vector distance operators/functions
   - If embeddings are passed in:
     - query by vector similarity directly
   - If only query text is passed and there is already an embedding generation abstraction:
     - use that abstraction to obtain an embedding before retrieval
   - Avoid introducing a new embedding provider path unless necessary for this task.
   - Keep the query efficient:
     - prefilter by tenant, validity, and agent scope before ranking where possible
     - limit candidate set before expensive post-processing if needed

6. **Add application handler/service**
   - Implement a query handler or service method that:
     - validates inputs
     - calls the retrieval query service
     - returns normalized results
   - Keep orchestration/UI concerns out of this layer.

7. **Wire dependency injection**
   - Register the new retrieval service/repository in Infrastructure/Application DI setup
   - Ensure no circular dependencies are introduced

8. **Testing**
   - Add unit tests for ranking behavior:
     - agent-specific memory outranks unrelated memory because unrelated memory is excluded
     - company-wide memory is included alongside agent-specific memory
     - expired memory is excluded
     - higher salience affects ordering
     - more recent memory affects ordering when semantic scores are similar
     - no semantic input falls back to salience/recency ordering
   - Add integration tests if feasible:
     - tenant isolation
     - validity window filtering
     - vector-backed retrieval path
   - Prefer deterministic test data with fixed timestamps

9. **Document assumptions in code**
   - Add concise comments where scoring or inclusion rules may be non-obvious
   - If exact weighting is a pragmatic default due to missing acceptance criteria, note that clearly for future tuning

# Validation steps
1. Restore/build the solution:
   - `dotnet build`
2. Run tests:
   - `dotnet test`
3. If there is a dedicated test project for Infrastructure/Application, run it specifically as well.
4. Manually verify the implemented retrieval behavior through tests or an existing endpoint/handler:
   - query with `AgentId` set and confirm both:
     - matching agent memory returned
     - company-wide memory returned
   - confirm other agents’ memory is not returned
   - confirm expired memory is excluded
   - confirm ordering changes when:
     - salience differs
     - recency differs
     - semantic similarity differs
   - confirm tenant isolation by using two companies’ seeded data
5. If SQL migrations are required for missing mapping/index support:
   - add/update migration
   - verify schema aligns with `memory_items` including vector column support
6. If pgvector indexes already exist for similar tables, ensure memory retrieval has equivalent indexing or leave a clear follow-up note if index creation is deferred

# Risks and follow-ups
- **Unknown existing patterns**: The repository/query/CQRS style may differ from assumptions. Match the codebase rather than inventing a new pattern.
- **pgvector support may be incomplete**: If `memory_items.embedding` exists in architecture but not yet in code/schema, you may need a migration and provider configuration.
- **Scoring ambiguity**: No explicit acceptance criteria define exact weighting. Use a simple deterministic weighted ranking and document it.
- **Embedding dependency**: If semantic retrieval requires generating embeddings from query text and no abstraction exists yet, prefer accepting a query embedding or reusing an existing embedding service rather than introducing broad new infrastructure.
- **Performance**: Combined ranking across large memory sets may need indexes or candidate preselection. Keep implementation correct first, but note any needed optimization.
- **Policy/privacy evolution**: Future stories may require deletion/expiration/privacy controls beyond retrieval. Do not hardcode assumptions that block those changes.
- **Cross-story alignment**: Keep the retrieval contract compatible with future **ST-304 Grounded context retrieval service** so it can be reused rather than replaced.