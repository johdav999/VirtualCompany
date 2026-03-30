# Goal

Implement backlog task **TASK-9.2.3 — Retrieval results include source document references for explainability** for story **ST-302 Chunking, embeddings, and semantic retrieval**.

The coding agent should update the semantic retrieval flow so that every returned retrieval result includes clear, structured source document reference data derived from the originating `knowledge_documents` record and chunk metadata. This should support downstream explainability, auditability, and future grounded context persistence without exposing chain-of-thought.

# Scope

In scope:

- Inspect the current document ingestion, chunk storage, and semantic retrieval implementation.
- Extend retrieval result contracts/models so each returned chunk includes source document reference information.
- Ensure references are tenant-scoped and respect existing access/scope filtering.
- Include enough source metadata to be human-usable for explainability, such as:
  - `documentId`
  - `documentTitle`
  - `documentType`
  - `sourceType`
  - `sourceRef` if available
  - `chunkId`
  - `chunkIndex`
- Update query/projection logic so retrieval joins `knowledge_chunks` to `knowledge_documents`.
- Add or update tests covering retrieval results with source references.
- Keep implementation aligned with modular monolith / clean architecture boundaries.

Out of scope unless required by existing code structure:

- UI work in Blazor or MAUI.
- New audit screens.
- Full persistence of retrieval references into audit tables for downstream stories.
- Reworking chunking/embedding generation behavior beyond what is necessary to expose references.
- Large schema redesigns unless the current schema is missing fields already described in the architecture.

# Files to touch

Start by inspecting these likely areas and adjust based on actual repository structure:

- `src/VirtualCompany.Application/**`
  - Retrieval/query contracts
  - DTOs/view models for semantic search results
  - Query handlers / services for knowledge retrieval
- `src/VirtualCompany.Domain/**`
  - Domain models/value objects if retrieval result types live here
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entities/configurations
  - Repositories / SQL / pgvector retrieval implementation
  - Document + chunk query projections
- `src/VirtualCompany.Api/**`
  - API contracts/endpoints if retrieval results are exposed directly
- `src/VirtualCompany.Shared/**`
  - Shared contracts if retrieval DTOs are shared across layers
- Test projects under `tests/**` or any existing `*.Tests` projects
- Potentially:
  - migration files, only if required
  - README or developer docs, only if there is an established pattern for documenting API contract changes

Do not touch unrelated mobile/web UX files unless retrieval contracts are consumed there and compilation requires updates.

# Implementation plan

1. **Discover the current retrieval path**
   - Find where semantic retrieval for knowledge chunks is implemented.
   - Identify:
     - request model
     - result DTO/model
     - repository/query handler
     - API endpoint or orchestration consumer
   - Confirm whether retrieval currently returns only chunk text/similarity score or already includes partial metadata.

2. **Identify the source-of-truth data available**
   - Verify the actual persistence model for:
     - `knowledge_documents`
     - `knowledge_chunks`
   - Confirm whether chunk rows already contain `document_id`, `chunk_index`, and metadata.
   - Confirm whether document rows already contain `title`, `document_type`, `source_type`, and `source_ref`.
   - If all required fields already exist, prefer query/projection changes only.
   - Only add schema changes if absolutely necessary and keep them minimal.

3. **Define a structured source reference contract**
   - Add or extend a retrieval result model to include a nested source reference object or equivalent structured fields.
   - Prefer a shape similar to:
     - retrieval result
       - chunk/content
       - score
       - source reference
         - documentId
         - documentTitle
         - documentType
         - sourceType
         - sourceRef
         - chunkId
         - chunkIndex
   - Keep naming consistent with existing project conventions.
   - Avoid returning raw storage URLs unless already part of a safe contract.

4. **Update retrieval query/projection**
   - Modify the retrieval implementation so semantic search returns chunk rows joined with their parent document.
   - Preserve existing tenant and access-scope filtering before or alongside similarity ranking, per story notes.
   - Ensure the result projection populates the new source reference fields.
   - If raw SQL is used for pgvector similarity search, update the SQL projection carefully.
   - If EF Core LINQ is used, ensure the generated query remains efficient.

5. **Preserve explainability and safety**
   - Ensure references are human-readable and stable enough for audit/explanation use.
   - Do not expose sensitive internal-only fields.
   - Do not weaken tenant isolation or access-scope enforcement.
   - If a document is missing or soft-deleted, handle gracefully rather than returning broken references.

6. **Update consumers**
   - If orchestration or context retrieval services consume retrieval results, update them to use the new contract without breaking behavior.
   - If there are API response models, map the new fields through the API layer.
   - Keep backward compatibility where practical; if not practical, update all internal callers in the same change.

7. **Add tests**
   - Add unit/integration tests that verify:
     - retrieval returns source document references
     - returned references match the originating document and chunk
     - tenant scoping still applies
     - access/scope filtering still applies if such logic exists
   - Prefer integration tests around the retrieval query/service if the repository is query-heavy.
   - If there is an API endpoint, add endpoint-level coverage if the project already uses API tests.

8. **Keep implementation small and coherent**
   - Do not introduce speculative abstractions.
   - Follow existing solution patterns for CQRS-lite, DTO placement, and infrastructure queries.
   - Keep changes focused on ST-302 explainability support.

# Validation steps

1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there is a targeted retrieval test suite, run it first or additionally.

4. Manually verify in code or via tests that a retrieval result now includes source reference data with at least:
   - document identifier
   - document title
   - chunk identifier or chunk index
   - document/source classification fields where available

5. Confirm no tenant isolation regressions:
   - retrieval for company A must not return references from company B

6. Confirm no contract breakages:
   - API/application consumers compile and tests pass after DTO/result changes

# Risks and follow-ups

- **Risk: retrieval query complexity/performance**
  - Joining documents during vector search may affect performance if implemented poorly.
  - Prefer efficient projection and existing indexes; avoid loading full entities unnecessarily.

- **Risk: inconsistent metadata availability**
  - Some older documents/chunks may lack optional metadata like `sourceRef`.
  - Handle nullable fields safely.

- **Risk: contract ripple effects**
  - Retrieval result DTO changes may affect orchestration, API, or shared contracts.
  - Update all internal consumers in one pass.

- **Risk: access-scope leakage**
  - Ensure document joins do not bypass existing company/access filters.

Follow-ups after this task, if not already covered elsewhere:

- Persist retrieval source references into downstream audit/explainability records for **ST-304** and **ST-602**.
- Add human-readable citation formatting for UI surfaces.
- Consider embedding model/version metadata in retrieval responses if useful for diagnostics, but not required for this task.