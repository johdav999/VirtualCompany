# Goal
Implement backlog task **TASK-9.2.1** for **ST-302 — Chunking, embeddings, and semantic retrieval** in the existing .NET solution.

Specifically, add the backend capability so that **processed knowledge documents are chunked and embedded into `knowledge_chunks` using pgvector in PostgreSQL**, with tenant-aware storage and retrieval foundations aligned to the architecture.

This implementation prompt should guide a coding agent to:
- locate the current document ingestion and persistence flow,
- extend it with asynchronous chunk generation + embedding persistence,
- support safe re-ingestion behavior,
- and expose or prepare a semantic retrieval path that returns chunk content plus source document references.

# Scope
In scope:
- Add or complete the domain/application/infrastructure pieces required to:
  - detect when a `knowledge_document` is ready for chunking/embedding,
  - split extracted document text into chunks,
  - generate embeddings for each chunk,
  - persist chunks into `knowledge_chunks`,
  - store embedding/model metadata needed for future reindexing,
  - replace or safely version prior chunks on re-ingestion,
  - enforce `company_id` scoping in persistence and retrieval,
  - support semantic similarity search over `knowledge_chunks` using pgvector,
  - include source document references in retrieval results.
- Add background-worker-compatible processing flow for embeddings.
- Add database migration(s) if schema changes are needed.
- Add tests for chunking, replacement behavior, tenant scoping, and retrieval basics.

Out of scope unless already partially implemented and required to complete this task:
- UI for document upload or retrieval.
- Full orchestration integration beyond a clean application service/retrieval API.
- Memory retrieval (`memory_items`) unless shared infrastructure is necessary.
- Large-scale optimization beyond pragmatic v1 pgvector usage.

Assumptions to validate in the codebase before implementing:
- ST-301 likely already introduced `knowledge_documents` and ingestion states.
- There may already be extracted text storage, object storage integration, or a background job framework.
- There may or may not already be OpenAI/embedding abstractions in Infrastructure.

# Files to touch
Inspect first, then update only what is necessary. Likely areas:

- `README.md`
- `src/VirtualCompany.Domain/**`
  - knowledge document/chunk entities, enums, value objects, repository contracts
- `src/VirtualCompany.Application/**`
  - commands/handlers/services for document processing, chunking, embedding, retrieval
  - DTOs/results for semantic search
  - abstractions for embedding generation and chunking
- `src/VirtualCompany.Infrastructure/**`
  - EF Core DbContext / entity configurations
  - migrations
  - pgvector mapping/configuration
  - repository implementations
  - OpenAI or embedding provider adapter
  - background worker/job implementation
- `src/VirtualCompany.Api/**`
  - DI registration
  - optional internal API endpoints if retrieval is exposed here already
- `src/VirtualCompany.Shared/**`
  - shared contracts only if already used for application DTOs
- Test projects in `tests/**` or existing test locations
  - unit tests for chunking logic
  - integration tests for persistence/retrieval if present in solution structure

Likely concrete files if they exist:
- `src/VirtualCompany.Infrastructure/Persistence/*DbContext*`
- `src/VirtualCompany.Infrastructure/Persistence/Configurations/*`
- `src/VirtualCompany.Infrastructure/Migrations/*`
- `src/VirtualCompany.Application/Knowledge/*`
- `src/VirtualCompany.Domain/Knowledge/*`
- `src/VirtualCompany.Api/Program.cs`

# Implementation plan
1. **Discover the current knowledge ingestion flow**
   - Find existing implementations for:
     - `knowledge_documents`
     - ingestion statuses
     - extracted text storage
     - background jobs/workers
     - OpenAI or embedding service abstractions
     - pgvector/Npgsql setup
   - Identify how a document transitions from uploaded to processed.
   - Determine where extracted text is available. If no extracted text is persisted, identify the current text extraction output path and use that as the chunking input.

2. **Align the data model with ST-302**
   - Confirm whether `knowledge_chunks` already exists.
   - Ensure the table/entity includes at minimum:
     - `id`
     - `company_id`
     - `document_id`
     - `chunk_index`
     - `content`
     - `embedding`
     - `metadata_json`
     - `created_at`
   - If missing, add metadata fields needed for reindexing and explainability, preferably in `metadata_json` to avoid over-modeling. Include:
     - embedding model name
     - embedding model version if available
     - chunking strategy/version
     - token/character counts if practical
   - Ensure `company_id` and `document_id` are indexed.
   - Add pgvector index support if the project already uses vector indexing conventions; otherwise keep it simple but compatible.

3. **Implement chunking abstraction**
   - Add an application-level abstraction such as:
     - `IDocumentChunker`
   - Implement a pragmatic chunking strategy:
     - deterministic chunk ordering,
     - configurable chunk size and overlap,
     - safe handling for empty/short text,
     - normalization of whitespace.
   - Prefer a simple text chunker unless the codebase already has tokenizer support.
   - Return chunk objects with:
     - `chunk_index`
     - `content`
     - metadata like estimated length/counts.

4. **Implement embedding abstraction**
   - Add or reuse an abstraction such as:
     - `IEmbeddingGenerator`
   - Infrastructure implementation should call the configured embedding provider.
   - Keep provider-specific details out of Application.
   - Return:
     - vector values,
     - model identifier/version if available.
   - Handle transient provider failures distinctly from permanent validation failures.

5. **Add document-to-chunks processing workflow**
   - Create an application command/service for processing a single document into chunks, e.g.:
     - `ProcessKnowledgeDocumentEmbeddingsCommand`
   - Flow:
     1. Load document by `company_id` + `document_id`
     2. Validate document is in a processed/extracted-text-ready state
     3. Load extracted text
     4. Chunk text deterministically
     5. Generate embeddings for each chunk
     6. Replace existing chunks for that document safely
     7. Persist new chunks in one unit of work/transaction where practical
     8. Update document ingestion/indexing status if such fields exist
   - If the current model lacks a distinct indexing status, add a minimal status progression only if necessary and consistent with existing patterns.

6. **Implement safe re-ingestion behavior**
   - Acceptance intent says prior chunks must be replaced or versioned safely.
   - Prefer **replace-in-transaction** for v1 unless the codebase already has versioning:
     - delete existing `knowledge_chunks` for the document scoped by `company_id`,
     - insert the newly generated chunks,
     - commit atomically.
   - Ensure retries are idempotent:
     - same document can be reprocessed without duplicate chunks,
     - partial failures do not leave mixed old/new chunk sets.
   - If background jobs can run concurrently, add a guard:
     - distributed lock,
     - row-level status transition,
     - or optimistic concurrency based on existing patterns.

7. **Implement semantic retrieval service**
   - Add an application service/query such as:
     - `SearchKnowledgeChunksQuery`
     - or `IKnowledgeRetrievalService`
   - Inputs:
     - `company_id`
     - query text
     - optional top-k
     - optional access scope filters / document filters
   - Flow:
     1. embed the query text,
     2. filter chunks by `company_id`,
     3. join/filter by document access policy metadata if available in current model,
     4. rank by vector similarity in PostgreSQL,
     5. return top results with:
        - chunk content
        - score/similarity
        - `document_id`
        - document title/reference
        - chunk index
   - Retrieval must apply tenant and scope filters **before or alongside ranking**, not after loading cross-tenant candidates into memory.

8. **Implement pgvector persistence/query support**
   - Ensure EF Core/Npgsql/pgvector mapping is correctly configured.
   - If pgvector extension enablement is handled via migration, add it.
   - Add repository/query implementation that performs similarity search in SQL/EF-compatible form.
   - Prefer server-side ranking in PostgreSQL.
   - Do not fetch all chunks into memory for similarity calculation.

9. **Wire background processing**
   - Integrate with the existing background worker/job mechanism.
   - Trigger embedding generation asynchronously when a document becomes processed.
   - If there is already a document processing pipeline, hook into its completion step.
   - If there is an outbox/event pattern already in use, prefer publishing a document-processed event and handling it in a background worker.
   - Ensure logs include document and company context.

10. **Add tests**
   - Unit tests:
     - chunker splits deterministically,
     - overlap behavior,
     - empty text handling.
   - Application tests:
     - processing a processed document creates chunks,
     - reprocessing replaces prior chunks without duplicates,
     - invalid document state is rejected.
   - Integration tests if infrastructure test setup exists:
     - retrieval is tenant-scoped,
     - retrieval returns source document references,
     - similarity query returns expected top result ordering for seeded vectors.
   - If real embedding provider calls are not test-safe, use a fake embedding generator.

11. **Document configuration**
   - Update README or app settings documentation for:
     - embedding provider configuration,
     - model name,
     - chunking settings,
     - pgvector requirement/migration notes.

12. **Implementation constraints**
   - Follow existing solution architecture and naming conventions.
   - Keep clean boundaries:
     - Domain: entities/contracts
     - Application: orchestration/use cases/abstractions
     - Infrastructure: EF/OpenAI/pgvector/background execution
   - Avoid leaking provider SDK types outside Infrastructure.
   - Preserve tenant isolation in every repository/query path.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations were added, verify they apply cleanly in the existing local/dev setup.

4. Manually validate the processing flow using the existing entry point for document ingestion:
   - upload or seed a document,
   - move it to processed/extracted state,
   - trigger background processing,
   - confirm `knowledge_chunks` rows are created with:
     - correct `company_id`
     - correct `document_id`
     - sequential `chunk_index`
     - non-null vector embeddings

5. Validate re-ingestion:
   - reprocess the same document after changing source text,
   - confirm old chunks are replaced safely,
   - confirm no duplicate chunk sets remain.

6. Validate retrieval:
   - run a semantic query for a known document,
   - confirm top results are returned from the correct tenant only,
   - confirm results include source document references/title,
   - confirm access scope filters are respected if implemented in current model.

7. Validate failure handling:
   - simulate embedding provider failure,
   - confirm document/chunk processing fails safely and logs actionable context,
   - confirm retries do not create duplicate chunks.

# Risks and follow-ups
- **Unknown current extraction model:** ST-301 may not persist extracted text yet. If missing, you may need a minimal extracted-text persistence step or reuse an existing transient pipeline output.
- **pgvector package/config may be absent:** You may need to add Npgsql pgvector support and migration-time extension enablement.
- **Embedding dimension mismatch:** The architecture example shows `vector(1536)`, but the actual provider/model may differ. Do not hardcode 1536 unless the current provider configuration guarantees it; align schema and provider configuration carefully.
- **Access scope enforcement may be underdefined:** If document access policy logic is not yet implemented, at minimum preserve hooks and company scoping, and document the gap clearly.
- **Background concurrency:** Multiple workers could process the same document simultaneously. Add idempotency/concurrency protection consistent with existing worker patterns.
- **Cost/performance:** Per-chunk embedding calls can be expensive. Keep batching opportunities in mind if the provider/client supports it, but do not overcomplicate v1.
- **Follow-up likely needed:** ST-304 should consume this retrieval capability through a unified grounded context retrieval service. Design the retrieval API so it can be reused there.