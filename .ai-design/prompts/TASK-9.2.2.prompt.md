# Goal
Implement backlog task **TASK-9.2.2** for **ST-302 — Chunking, embeddings, and semantic retrieval** so that semantic search returns the **top relevant knowledge chunks** while enforcing **company isolation** and **access policy scope**, and includes **source document references** for explainability.

# Scope
Focus only on the retrieval portion required by this task:

- Add or complete a semantic search capability over `knowledge_chunks` using **pgvector**.
- Ensure retrieval is filtered by:
  - `company_id`
  - document/chunk access policy derived from `knowledge_documents.access_scope_json`
  - any existing caller/agent/user scope model already present in the codebase
- Return top relevant chunks ordered by semantic similarity **after scope enforcement**
- Include source document metadata/reference in results for explainability
- Keep implementation aligned with the modular monolith / clean architecture structure already in the solution

Out of scope unless required to make this task work end-to-end:

- New UI
- Full ingestion pipeline redesign
- New embedding generation workflow
- Re-ingestion/versioning behavior beyond what is necessary to avoid breaking retrieval
- Broader ST-304 context composition work
- External vector database adoption

# Files to touch
Inspect the solution first and then update the appropriate files in these areas as needed:

- `src/VirtualCompany.Domain/**`
  - knowledge/retrieval domain models or value objects
  - access scope abstractions if they already exist
- `src/VirtualCompany.Application/**`
  - query/service contracts for semantic retrieval
  - DTOs for retrieval request/response
  - authorization/scope evaluation interfaces
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration for `knowledge_documents` / `knowledge_chunks`
  - pgvector query implementation
  - repository/query service implementation
  - JSON access-scope parsing/evaluation support
- `src/VirtualCompany.Api/**`
  - API endpoint or internal application wiring if retrieval is exposed via HTTP
- `README.md`
  - only if a short note is needed for setup or behavior clarification

Likely file categories to create or modify:

- `Knowledge` or `Retrieval` application service/query handler
- infrastructure repository such as `KnowledgeSearchRepository` / `SemanticSearchService`
- DTOs like:
  - `SemanticChunkSearchRequest`
  - `SemanticChunkSearchResult`
  - `SemanticChunkResultItem`
- policy/scope evaluator such as:
  - `IKnowledgeAccessPolicyEvaluator`
  - `KnowledgeAccessContext`

Do not rename or reorganize major project structure unless clearly necessary.

# Implementation plan
1. **Inspect existing knowledge and retrieval code**
   - Find current entities, migrations, EF configurations, and any existing chunk/embedding support.
   - Identify how tenant scoping is enforced elsewhere in the app.
   - Identify whether access policy is already modeled for documents, agents, or users.
   - Reuse existing patterns for CQRS-lite queries, authorization, and repository abstractions.

2. **Define retrieval contract in Application layer**
   - Add a request model for semantic chunk search that includes:
     - `CompanyId`
     - query text or query embedding input path used by current architecture
     - caller access context
     - optional `TopK`
     - optional document type / source filters only if already supported by existing patterns
   - Add a response model that includes:
     - chunk id
     - document id
     - document title
     - chunk index
     - chunk content
     - similarity score
     - source reference metadata for explainability
   - Keep the contract deterministic and testable.

3. **Model access context and policy evaluation**
   - Introduce or reuse a policy evaluator that can answer whether a document is visible for a given caller context.
   - The evaluator should support current known access dimensions from `access_scope_json`, for example:
     - company-wide visibility
     - role-based visibility
     - agent/data-scope visibility
     - restricted/private flags
   - If the schema is flexible JSON, implement a conservative parser:
     - missing/ambiguous policy should default safely
     - company mismatch must always deny
   - Prefer filtering at query time where feasible; if some policy logic cannot be translated to SQL safely, do a two-stage approach:
     1. prefilter by company and obvious scope constraints in SQL
     2. apply final policy evaluation in application/infrastructure before final top-K selection
   - Important: acceptance notes require **scope filters before similarity ranking**, so do not rank globally and then filter afterward.

4. **Implement pgvector-backed semantic search**
   - Use the existing PostgreSQL/pgvector setup in Infrastructure.
   - Generate or accept a query embedding using the project’s current embedding provider abstraction.
   - Query `knowledge_chunks` joined to `knowledge_documents`.
   - Filter by:
     - `knowledge_chunks.company_id == request.CompanyId`
     - matching `knowledge_documents.company_id`
     - allowed access scope
     - only valid/processed documents if such status exists
   - Order by vector similarity and take top K.
   - Return source document references in the result.

5. **Include explainability metadata**
   - Each result item should include enough source information for downstream audit/explanation:
     - document title
     - document type if available
     - source type / source ref if available
     - chunk index
   - Keep payload concise and suitable for later use by ST-304.

6. **Wire service into DI and API/application entry points**
   - Register the retrieval service and any policy evaluator in dependency injection.
   - If an API endpoint already exists for knowledge search, extend it.
   - If no endpoint exists and one is needed for validation, add a minimal internal/admin endpoint following existing API conventions.
   - Do not add UI.

7. **Add tests**
   - Unit tests for access policy evaluation:
     - same company allowed
     - different company denied
     - restricted scope denied when caller lacks access
     - allowed scope returns visible documents
   - Unit/integration tests for retrieval ordering and filtering:
     - only chunks from the requested company are returned
     - inaccessible documents are excluded before ranking
     - top K returns expected count/order among allowed chunks
     - result includes source document references
   - Prefer integration tests if the repo already has database-backed test patterns; otherwise use focused unit tests plus repository tests where practical.

8. **Keep implementation production-safe**
   - Handle null/missing embeddings gracefully.
   - Avoid returning chunks for documents in failed/unprocessed states if such states exist.
   - Add cancellation token support.
   - Use structured logging with company context if existing logging patterns are present.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there is an API endpoint for retrieval, validate manually with seeded/test data:
   - create documents/chunks for at least two companies
   - assign different `access_scope_json` values
   - run a semantic search as caller A in company 1
   - verify:
     - only company 1 chunks are returned
     - restricted documents outside caller scope are not returned
     - results are ordered by similarity among the allowed set
     - each result includes document/source reference fields

4. Verify failure/safety behavior:
   - search with invalid or missing caller scope context
   - confirm default-deny or safe-empty behavior for ambiguous access policy
   - confirm no cross-tenant leakage

5. If migrations/config were changed, ensure startup still succeeds and EF mappings are valid.

# Risks and follow-ups
- **Access policy ambiguity:** `access_scope_json` may not yet have a finalized schema. Implement a conservative evaluator and document assumptions in code comments.
- **Query translation limits:** complex JSON policy logic may not fully translate to SQL. If needed, use a safe prefilter + final in-memory policy check, but still ensure ranking is applied only to the allowed candidate set.
- **Embedding provider coupling:** if query embedding generation is not yet abstracted cleanly, add a minimal interface rather than hardcoding provider calls.
- **pgvector environment gaps:** local/test environments may not have pgvector fully configured. Reuse existing infrastructure patterns and avoid introducing environment-specific hacks.
- **Future alignment with ST-304:** keep result DTOs reusable for the grounded context retrieval service.
- **Potential follow-up work:** add explicit acceptance tests for document re-ingestion/versioning and persist retrieval source references for audit trails if not already covered elsewhere.