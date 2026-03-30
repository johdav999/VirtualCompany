# Goal
Implement backlog task **TASK-ST-302 — Chunking, embeddings, and semantic retrieval** for the existing .NET solution.

Deliver a production-ready first version of the knowledge retrieval pipeline that:
- asynchronously processes uploaded/processed knowledge documents into chunks
- generates embeddings for those chunks
- stores chunks and vectors in PostgreSQL with pgvector
- supports semantic retrieval scoped by **company** and **access policy**
- returns source document references for explainability
- safely replaces or versions prior chunks when a document is re-ingested

This work should fit the documented architecture:
- modular monolith
- ASP.NET Core backend
- PostgreSQL + pgvector
- background worker driven ingestion/embedding
- tenant-aware retrieval
- clean separation across Domain / Application / Infrastructure / API

There are no explicit acceptance criteria beyond the story details, so use the story notes and architecture as the source of truth.

# Scope
Implement the minimum coherent vertical slice for ST-302, including:

1. **Domain and persistence support**
   - Ensure `knowledge_chunks` persistence exists or is added
   - Include fields needed for:
     - company scoping
     - document linkage
     - chunk ordering
     - chunk content
     - embedding vector
     - metadata
     - embedding model/version metadata
     - lifecycle/versioning or replacement safety

2. **Chunking pipeline**
   - Add a chunking service that converts processed document text into ordered chunks
   - Use deterministic chunking suitable for semantic retrieval
   - Preserve enough metadata for explainability and future reindexing

3. **Embedding generation**
   - Add an abstraction for embedding generation in Application
   - Implement provider integration in Infrastructure
   - Store embedding model/version metadata with chunks
   - Make embedding generation asynchronous via background processing, not request path

4. **Background processing**
   - Add or extend a background worker/job handler that:
     - picks up documents ready for chunking/embedding
     - creates chunks
     - generates embeddings
     - persists them transactionally/safely
     - marks ingestion/indexing status
     - handles retries/failures cleanly

5. **Semantic retrieval**
   - Add an application service/query for semantic search over `knowledge_chunks`
   - Enforce filters **before** similarity ranking as much as practical:
     - `company_id`
     - document access scope / policy constraints available in current model
     - only active/current chunk set
   - Return top-N results with:
     - chunk content
     - score
     - document id
     - document title if available
     - chunk index
     - source metadata/reference payload

6. **Re-ingestion safety**
   - When a document is reprocessed, do not leave ambiguous active chunk sets
   - Prefer one of:
     - versioned chunk sets with one active version
     - replace-in-transaction semantics
   - Keep implementation simple but safe and auditable

7. **Tests**
   - Add unit/integration tests for:
     - chunking behavior
     - retrieval scoping
     - re-ingestion replacement/version behavior
     - failure handling where practical

Out of scope unless required by existing code structure:
- full ST-304 grounded retrieval composition across tasks/memory/records
- UI for semantic search
- advanced OCR/parsing improvements
- dedicated vector database
- large-scale reindex orchestration
- full policy engine integration beyond document/company scope enforcement available today

# Files to touch
Inspect the solution first, then update the most appropriate files. Expect to touch files in these areas if they exist:

- `src/VirtualCompany.Domain/**`
  - knowledge document/chunk entities
  - enums/value objects for ingestion/indexing status
  - repository contracts if domain-owned

- `src/VirtualCompany.Application/**`
  - commands/queries for indexing and semantic search
  - interfaces for chunking and embedding generation
  - DTOs for retrieval results
  - validators
  - orchestration/use-case handlers

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations / DbContext mappings
  - migrations
  - pgvector configuration
  - repository/query implementations
  - embedding provider implementation
  - background worker/job implementation
  - SQL/vector similarity query implementation

- `src/VirtualCompany.Api/**`
  - DI registration
  - optional internal/admin endpoints if the project already exposes document indexing or retrieval APIs
  - configuration binding for embedding provider/model settings

- `src/VirtualCompany.Shared/**`
  - shared contracts only if this solution already uses Shared for cross-layer DTOs/options

- `README.md`
  - only if needed to document required configuration or local setup for pgvector/embedding settings

Also inspect for existing related files before creating new ones:
- document ingestion pipeline from **ST-301**
- background worker infrastructure
- outbox/job abstractions
- tenant context abstractions
- OpenAI or LLM provider abstractions
- existing PostgreSQL migrations and naming conventions

Do not create parallel patterns if the solution already has an established one.

# Implementation plan
1. **Inspect the current implementation**
   - Review the solution structure and existing patterns for:
     - clean architecture boundaries
     - MediatR/CQRS usage if present
     - EF Core mappings and migrations
     - background jobs/hosted services
     - tenant scoping
     - document ingestion entities from ST-301
   - Identify whether `knowledge_documents` and `knowledge_chunks` already exist and what fields are missing.
   - Identify how document content is stored after upload/processing. If raw extracted text is not yet persisted, add the smallest viable mechanism needed for indexing from already-processed documents.

2. **Design the indexing lifecycle**
   - Introduce or refine statuses so the pipeline is explicit. Example lifecycle:
     - uploaded
     - processing
     - processed
     - indexing
     - indexed
     - failed
   - If the project already has ingestion statuses, extend rather than replace.
   - Add fields needed to support safe re-indexing, such as:
     - `chunk_set_version` or `index_version`
     - `is_active`
     - `embedding_model`
     - `embedding_model_version`
     - timestamps
   - Keep the schema pragmatic and aligned with the architecture docs.

3. **Implement chunking abstraction**
   - Add an application-layer interface such as `IKnowledgeChunker`.
   - Implement deterministic chunking with sensible defaults:
     - chunk by paragraphs/sentences with max character or token-ish target
     - overlap between adjacent chunks to preserve context
     - stable ordering via `chunk_index`
   - Include metadata per chunk, for example:
     - source section if derivable
     - character offsets if practical
     - chunking strategy/version
   - Keep the implementation simple, testable, and provider-agnostic.

4. **Implement embedding abstraction and provider**
   - Add an application-layer interface such as `IEmbeddingGenerator`.
   - Implement Infrastructure provider using the project’s existing AI provider approach if present.
   - Make model name and dimensions configurable.
   - Ensure the provider returns vectors in a format compatible with pgvector.
   - Handle provider failures with retry-friendly exceptions/logging.

5. **Persist chunk records with pgvector**
   - Add/update EF Core configuration for `knowledge_chunks`.
   - Ensure pgvector support is configured correctly.
   - Add indexes appropriate for:
     - company/document filtering
     - active version filtering
     - vector similarity search
   - If using versioned chunk sets:
     - insert new version first
     - switch active version atomically
     - deactivate old version
   - If using replace semantics:
     - perform replacement in a transaction and avoid partial visibility

6. **Add background indexing worker**
   - Implement a hosted service/job processor that finds documents ready for indexing.
   - For each document:
     - acquire a lock or otherwise avoid duplicate concurrent indexing
     - mark status as indexing
     - load extracted text
     - chunk content
     - generate embeddings
     - persist new chunk set
     - mark document indexed/processed-successfully
   - On failure:
     - mark failure state with actionable error details
     - preserve retryability
   - Keep tenant context explicit in logs and processing.

7. **Implement semantic retrieval query**
   - Add an application query/service such as `SearchKnowledgeChunks`.
   - Inputs should include at minimum:
     - company id
     - query text
     - top N
     - optional access scope filters / actor context
   - Flow:
     - embed the query
     - filter candidate chunks by company and active/current chunk set
     - apply access scope filtering using available document metadata
     - rank by vector similarity
     - join document metadata for explainability
   - Return a structured result DTO with:
     - chunk id
     - content
     - score
     - document id
     - document title
     - chunk index
     - metadata/source reference
   - Prefer server-side ranking in PostgreSQL/pgvector.

8. **Enforce scope before ranking**
   - Ensure retrieval never ranks across other tenants.
   - Apply company and access-scope filters in SQL before similarity ordering.
   - If access scope is stored in JSONB, use the simplest reliable filter supported by the current schema.
   - If full policy semantics are not yet implemented, document the current enforcement level clearly in code comments and follow-up notes.

9. **Support re-ingestion safely**
   - When a document is reprocessed:
     - create a new chunk set/version or replace old chunks atomically
     - ensure retrieval only sees the current active set
     - avoid duplicate active chunks from prior runs
   - Add tests proving old chunks are not returned after re-index.

10. **Wire up DI and configuration**
   - Register:
     - chunker
     - embedding generator
     - indexing worker
     - retrieval service/query handler
   - Add configuration options for:
     - embedding model
     - dimensions
     - batch size
     - chunk size / overlap
     - polling interval / worker batch size
   - Follow existing options/config conventions.

11. **Add tests**
   - Unit tests:
     - chunker produces stable ordered chunks
     - overlap/size behavior
     - re-index logic at service level
   - Integration tests if feasible in current repo:
     - tenant-scoped retrieval excludes other companies
     - retrieval returns source references
     - only active/current chunk set is searched
   - If pgvector integration tests are hard in current setup, still cover query-building/service logic and note any infra test gap.

12. **Keep implementation aligned with architecture**
   - No direct DB access from controllers/UI
   - No embedding generation in request path for uploads
   - No cross-tenant shortcuts
   - No bespoke retrieval logic in presentation layer

# Validation steps
Run and verify as much of the following as the repository supports:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Migration validation**
   - Ensure EF Core migration compiles and reflects the schema changes for chunk storage/indexing metadata.
   - If the repo uses startup migrations, verify app startup can apply them.

4. **Manual indexing flow validation**
   - Create or use a processed knowledge document for one company.
   - Trigger or wait for background indexing.
   - Verify:
     - chunks are created
     - embeddings are stored
     - document status transitions correctly
     - failures are surfaced if embedding provider is unavailable

5. **Manual retrieval validation**
   - Query semantic search with a relevant phrase.
   - Verify:
     - top results come from the correct company only
     - results include document references/title/chunk index
     - access-scoped documents are filtered appropriately
     - scores/order are reasonable

6. **Re-ingestion validation**
   - Reprocess the same document with changed content.
   - Verify:
     - old chunk set is not returned by retrieval
     - new chunk set is active
     - no duplicate active chunk visibility occurs

7. **Operational validation**
   - Confirm logs include enough context for indexing failures and retries.
   - Confirm worker behavior is idempotent enough to tolerate retries/restarts.

# Risks and follow-ups
- **Document text availability risk:** ST-302 depends on ST-301 having a reliable extracted-text stage. If extracted text is not yet persisted, implement the smallest viable bridge and note it clearly.
- **pgvector setup risk:** local/dev environments may not have pgvector enabled. Add minimal setup guidance if needed.
- **Access policy depth risk:** full policy-aware retrieval may depend on later stories. Enforce company + current document access scope now, and document any remaining gaps.
- **Embedding model drift:** storing model/version metadata is required now to support future reindexing.
- **Batching/performance:** first version should prioritize correctness and tenant safety over aggressive optimization.
- **Retry semantics:** avoid partial chunk replacement on failures; prefer transactional activation of new chunk sets.
- **Future follow-up:** ST-304 should build on this by composing documents + memory + recent history into one grounded retrieval service.
- **Future follow-up:** add explicit audit/business events for indexing and retrieval source usage if not already present.
- **Future follow-up:** consider HNSW/IVFFlat indexes and tuning once real corpus size/performance data exists.