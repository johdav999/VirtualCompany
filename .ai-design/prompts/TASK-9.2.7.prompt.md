# Goal
Implement backlog task **TASK-9.2.7 — Retrieval must enforce company and scope filters before similarity ranking** for story **ST-302 Chunking, embeddings, and semantic retrieval**.

The coding agent must update the semantic retrieval implementation so that **tenant/company isolation and access scope constraints are applied in the database query before vector similarity ordering/top-k selection**, not after candidate retrieval. This must prevent cross-tenant leakage and avoid returning chunks that were only filtered out after ranking.

The result should align with the architecture and backlog expectations:
- PostgreSQL + pgvector retrieval
- shared-schema multi-tenancy with `company_id` enforcement
- retrieval scoped by company and access policy
- explainable results with source document references
- deterministic, testable retrieval behavior

# Scope
In scope:
- Find the current semantic retrieval path for `knowledge_chunks` / document retrieval.
- Ensure retrieval queries apply:
  - `company_id` filter
  - document/chunk access scope filter(s)
  - any agent/user/company scope constraints already modeled in code
- Apply those filters **before** similarity ranking and limiting.
- Refactor repository/query/service code as needed so the contract makes scoped retrieval mandatory rather than optional.
- Add or update tests proving:
  - cross-company chunks are never considered
  - out-of-scope chunks are never considered
  - ranking occurs only within the allowed candidate set
- Preserve or improve source document reference return shape for explainability.
- Update any relevant comments/docs if needed to clarify the invariant.

Out of scope:
- Redesigning the full retrieval architecture
- Adding a new vector database
- Implementing broad new authorization models unrelated to retrieval
- UI changes
- Large schema redesign unless absolutely required for existing scope metadata to be queryable
- Full ST-304 context retrieval composition beyond what is necessary for this task

# Files to touch
Start by inspecting these likely areas and adjust based on actual code structure:

- `src/VirtualCompany.Application/**`
  - retrieval/query service interfaces and handlers
  - DTOs/requests for semantic search
  - any access-scope evaluation abstractions
- `src/VirtualCompany.Infrastructure/**`
  - pgvector/PostgreSQL retrieval repositories
  - EF Core or SQL query implementations for `knowledge_chunks` / `knowledge_documents`
  - persistence mappings for document scope metadata
- `src/VirtualCompany.Domain/**`
  - value objects or policies related to company/scope filtering, if present
- `src/VirtualCompany.Api/**`
  - only if request contracts or DI wiring must change
- `README.md`
  - only if there is a retrieval architecture note worth updating
- Test projects under `tests/**` or existing test locations
  - unit tests for retrieval filtering logic
  - integration tests for SQL/EF retrieval behavior if present

If there is an existing semantic retrieval implementation, prefer modifying the current path rather than introducing a parallel one.

# Implementation plan
1. **Locate the current retrieval flow**
   - Identify the entry point used for semantic search over knowledge chunks.
   - Trace the call chain from application service/query handler to infrastructure repository/SQL.
   - Confirm how `company_id` and access scope are currently passed and where filtering happens.
   - Specifically look for anti-patterns such as:
     - retrieving top-N by similarity first, then filtering in memory
     - filtering only by `company_id` after query execution
     - optional scope parameters that can be omitted accidentally
     - broad document fetch followed by post-processing

2. **Define the required retrieval invariant**
   - The retrieval contract should require enough context to enforce scoping before ranking.
   - If needed, introduce or tighten a request model such as:
     - `companyId`
     - actor/agent identifier
     - allowed scope descriptors
     - topK
     - embedding vector
   - Make it difficult to call retrieval without tenant context.
   - Prefer explicit typed parameters over loose JSON/dictionary inputs.

3. **Refactor query construction so filters happen before ranking**
   - Update the repository/SQL/EF query so the candidate set is constrained first by:
     - `knowledge_chunks.company_id = @companyId`
     - join/filter to `knowledge_documents` if access scope lives there
     - any applicable scope predicates derived from access metadata
   - Then apply vector similarity ordering and `LIMIT`.
   - In SQL terms, the intended shape is conceptually:
     - `FROM knowledge_chunks kc`
     - `JOIN knowledge_documents kd ...`
     - `WHERE kc.company_id = @companyId AND <scope predicate>`
     - `ORDER BY kc.embedding <=> @embedding`
     - `LIMIT @topK`
   - If using EF Core with pgvector support, ensure generated SQL preserves this order of operations.
   - If EF translation is unclear or unsafe, use a parameterized SQL query in infrastructure.

4. **Handle access scope correctly**
   - Inspect how `knowledge_documents.access_scope_json` and/or chunk metadata are modeled.
   - Reuse existing scope semantics if already implemented elsewhere.
   - If scope evaluation is currently only available in memory, move the queryable subset into SQL-compatible predicates for retrieval prefiltering.
   - If full JSON scope evaluation is too broad, implement the currently supported scope rules explicitly and document any remaining limitations.
   - Do not silently drop scope enforcement because it is inconvenient to query.

5. **Preserve explainability data**
   - Ensure retrieval results still include:
     - chunk content
     - chunk/document identifiers
     - source document title/reference if already supported
     - similarity score/distance if part of the contract
   - If the current query does not join enough document data, extend it safely.

6. **Strengthen service-level safeguards**
   - Add guard clauses so retrieval cannot execute with:
     - empty/default `companyId`
     - missing scope context when scope is required
     - invalid `topK`
   - Prefer fail-closed behavior over permissive fallback.
   - If there are multiple retrieval methods, centralize the scoped query path to avoid one unsafe variant remaining.

7. **Add tests**
   - Add unit and/or integration coverage for the invariant:
     - **Tenant isolation test**: two companies have similar chunks; querying for company A never returns company B chunks even if B is more similar.
     - **Scope isolation test**: within one company, a restricted document chunk is more similar than an allowed one; retrieval returns only allowed chunks.
     - **Ranking-after-filter test**: verify top-K is selected from the filtered set, not from all chunks.
     - **Fail-closed test**: missing required company/scope context is rejected.
   - Prefer integration tests against the actual repository if the risk is in SQL translation.
   - If integration tests are not practical in the current setup, add focused repository tests plus service tests that prove the query path and contract.

8. **Review for consistency with architecture**
   - Ensure the implementation matches:
     - shared-schema multi-tenancy
     - tenant-isolated data access
     - policy/scoping before action/retrieval
   - Keep the change localized and maintainable within the modular monolith structure.

9. **Document the invariant in code**
   - Add concise comments near the retrieval repository/service stating:
     - company and scope filters must be applied before similarity ranking/top-k
     - post-filtering ranked results is not acceptable
   - This is to prevent future regressions.

# Validation steps
Run the relevant validation for the workspace and include results in your final summary.

1. Build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects for retrieval/infrastructure, run them explicitly as well.

4. Manually verify in code review that:
   - retrieval query includes `company_id` predicate before ordering/limit
   - scope predicate is part of the database query, not only in-memory filtering
   - top-K is applied after filtering
   - result DTOs still include source references

5. In your final response, summarize:
   - which files changed
   - what the old unsafe behavior was
   - how the new query enforces pre-ranking filtering
   - what tests were added/updated
   - any limitations or follow-up work

# Risks and follow-ups
- **JSON scope complexity**: if access scope is stored in flexible JSONB, translating all semantics into SQL may be non-trivial. Implement the supported subset safely and fail closed where scope cannot be evaluated.
- **EF Core translation risk**: LINQ may not generate the intended SQL shape for pgvector ordering. Verify generated SQL or use parameterized SQL for the critical query.
- **Performance trade-offs**: adding joins/scope predicates before vector ranking may affect index usage. Correctness and tenant isolation take priority; note any indexing follow-up needed.
- **Multiple retrieval paths**: there may be more than one semantic search entry point. Ensure all production paths use the safe scoped query.
- **Test environment limitations**: if pgvector-backed integration tests are hard to run locally/CI, document the gap and add the strongest possible repository/service tests now.

Potential follow-up items to note if discovered:
- add/adjust indexes for `(company_id, document_id)` and any scope-related query paths
- normalize frequently queried scope fields out of JSONB if current schema makes safe filtering awkward
- add retrieval-specific observability/audit for scoped candidate counts
- extend the same pre-ranking filter rule to memory retrieval if a similar pattern exists