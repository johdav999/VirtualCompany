# Goal
Implement backlog task **TASK-9.2.5 — Background worker should handle embedding generation asynchronously** for story **ST-302 Chunking, embeddings, and semantic retrieval**.

The coding agent should add an asynchronous embedding pipeline so that document ingestion does **not** generate embeddings inline in the request path. Instead, uploaded/processed documents should enqueue work, and a background worker should pick up pending embedding jobs, chunk document content, generate embeddings, and persist `knowledge_chunks` safely and tenant-scoped.

This work should align with the architecture:
- modular monolith
- ASP.NET Core backend
- PostgreSQL + pgvector
- background workers for long-running jobs
- tenant isolation on all tenant-owned data
- reliable, retryable processing

# Scope
In scope:
- Add or complete a background-job flow for embedding generation for knowledge documents.
- Ensure embedding generation is asynchronous and decoupled from upload/request handling.
- Persist enough job/document state to support retries and failure visibility.
- Chunk processed document content and write embeddings into `knowledge_chunks`.
- Replace or safely refresh prior chunks for a document during re-ingestion.
- Keep all reads/writes company-scoped.
- Add logging and basic observability around worker execution.
- Add tests for the application/infrastructure behavior where practical.

Out of scope unless already partially implemented:
- Full semantic retrieval API/query implementation.
- UI changes beyond minimal status exposure if required by existing contracts.
- New external queue/broker infrastructure; prefer in-process hosted worker, DB-backed coordination, and existing architecture patterns.
- Large redesign of ingestion pipeline beyond what is needed to enqueue and process embedding work asynchronously.

Assumptions to verify in the codebase before implementation:
- There is already a document ingestion flow from ST-301 or partial scaffolding for `knowledge_documents`.
- There may already be an abstraction for background jobs, hosted services, outbox dispatching, or scheduled workers.
- There may already be an AI/embedding provider abstraction; reuse it if present.
- There may already be a place where extracted document text is stored or made available after upload/processing.

# Files to touch
Inspect first, then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`

Likely file categories to touch:
- Domain entities/value objects/enums for:
  - `knowledge_documents`
  - ingestion/processing status
  - embedding job status if modeled explicitly
- Application commands/handlers/services for:
  - document processed event/command
  - enqueue embedding generation
  - chunking orchestration
- Infrastructure persistence for:
  - EF Core entity configs/mappings
  - repositories
  - migrations
- Infrastructure AI integration for:
  - embedding provider abstraction/implementation
- Infrastructure background processing for:
  - hosted service / background worker / polling worker
- API/composition root for:
  - DI registration
  - hosted service registration
- Tests in corresponding test projects if present

Concrete files to inspect early:
- `README.md`
- `src/VirtualCompany.Api/Program.cs`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`

Also search for:
- `knowledge_documents`
- `knowledge_chunks`
- `memory_items`
- `BackgroundService`
- `IHostedService`
- `Outbox`
- `Embedding`
- `OpenAI`
- `pgvector`
- `Document`
- `IngestionStatus`

# Implementation plan
1. **Inspect current ingestion and persistence model**
   - Find the current `knowledge_documents` entity/model and determine:
     - where document metadata is stored
     - how ingestion status is represented
     - whether extracted text/content is already persisted
     - whether reprocessing/versioning is already modeled
   - Find whether `knowledge_chunks` already exists in domain + EF mappings.
   - Find whether there is already an embedding service abstraction and any OpenAI integration.
   - Find whether there is an existing background worker pattern used elsewhere in the solution.

2. **Design the async embedding workflow using existing patterns**
   - Prefer a DB-backed, retryable workflow consistent with the architecture.
   - Recommended minimal flow:
     1. document upload/extraction marks document as ready for embedding
     2. application layer records pending embedding work
     3. background worker polls for pending documents/jobs
     4. worker acquires one item safely
     5. worker chunks content
     6. worker calls embedding provider
     7. worker replaces/inserts `knowledge_chunks`
     8. worker marks document/job completed or failed
   - If no dedicated job table exists, either:
     - use document status fields plus lock/attempt metadata, or
     - add a dedicated embedding job table if that is cleaner and consistent with the codebase.
   - Prefer the smallest maintainable design that supports retries and avoids duplicate processing.

3. **Model processing state explicitly**
   - Ensure there is a clear status progression, for example:
     - `Uploaded`
     - `Processing`
     - `Processed`
     - `EmbeddingPending`
     - `EmbeddingInProgress`
     - `Ready`
     - `Failed`
   - If the project already has a status model, extend it rather than inventing a parallel one.
   - Persist failure reason, attempt count, and timestamps if feasible.
   - Keep status transitions application-driven, not ad hoc in controllers.

4. **Add or refine chunking service**
   - Implement a chunking service in application/infrastructure if not already present.
   - Chunking should:
     - produce deterministic chunk ordering (`chunk_index`)
     - preserve document/company linkage
     - include metadata useful for explainability and future reindexing
   - If architecture notes mention embedding model/version metadata, include it in chunk metadata or document/job metadata.
   - Keep chunk size/overlap configurable via options if possible.

5. **Add embedding generation abstraction**
   - Reuse existing provider abstraction if present; otherwise add one in application and implement in infrastructure.
   - The abstraction should accept chunk texts and return vectors.
   - Keep provider-specific details out of application/domain layers.
   - Include model/version metadata in persisted records where practical.

6. **Implement safe replacement of prior chunks**
   - For re-ingestion, ensure prior chunks for the same document are replaced safely.
   - Recommended transactional behavior:
     - generate new chunk payloads
     - delete existing chunks for the document
     - insert new chunks
     - commit atomically if feasible
   - If full atomic replacement is not practical, ensure the worker cannot leave duplicate active chunk sets for the same document.
   - Preserve tenant scoping in all delete/insert operations.

7. **Implement background worker**
   - Add a hosted background service in Infrastructure or Api composition root.
   - Worker behavior should include:
     - polling interval
     - batch size limit
     - cancellation token support
     - structured logging
     - retry handling
     - safe claim/lock of pending work to avoid duplicate processing across instances
   - If Redis/distributed locking already exists, use it where appropriate.
   - Otherwise use DB-safe claiming semantics consistent with PostgreSQL and EF usage in the project.
   - Ensure worker execution is tenant-aware and logs company/document identifiers safely.

8. **Wire enqueueing into ingestion completion**
   - Wherever document extraction/processing completes, update the flow so it enqueues or marks embedding work pending.
   - Do not generate embeddings inline in HTTP request handlers.
   - If there is currently synchronous embedding generation, remove that path and replace it with async scheduling.

9. **Add observability and failure handling**
   - Log:
     - job claimed
     - chunk count generated
     - embedding provider call start/end
     - completion
     - retryable failure
     - terminal failure
   - Persist failure state so operators/users can see actionable status.
   - Distinguish transient provider failures from permanent content/business failures where possible.

10. **Add tests**
   - Add unit tests for:
     - chunking behavior
     - status transitions
     - replacement of prior chunks
   - Add integration tests where feasible for:
     - enqueue pending work after document processing
     - worker processes pending document/job and writes `knowledge_chunks`
     - retry/failure behavior
   - If full integration tests are too heavy, at minimum cover application services and worker orchestration with mocks/fakes.

11. **Keep implementation aligned with clean architecture**
   - Domain: statuses/rules, no provider code
   - Application: orchestration/use cases/interfaces
   - Infrastructure: EF, OpenAI/provider, hosted worker
   - API: DI and startup only

12. **Document any config required**
   - If new options are introduced, wire them through existing configuration patterns:
     - polling interval
     - batch size
     - chunk size/overlap
     - embedding model name
     - retry limits

# Validation steps
1. **Codebase inspection**
   - Confirm actual existing patterns before coding:
     - background worker pattern
     - EF Core DbContext and migrations
     - document ingestion flow
     - embedding provider integration

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Functional verification**
   - Create or use a processed knowledge document fixture.
   - Verify that after ingestion/extraction completes:
     - document is marked pending for embedding
     - request path returns without waiting for embedding generation
   - Start the app/worker and verify:
     - worker picks up pending work
     - chunks are created in `knowledge_chunks`
     - embeddings are persisted
     - document/job status transitions to completed/ready

5. **Re-ingestion verification**
   - Reprocess the same document.
   - Verify old chunks are replaced safely and not duplicated.
   - Verify chunk indexes are regenerated consistently.

6. **Failure verification**
   - Simulate embedding provider failure.
   - Verify:
     - status moves to failed or retry-pending as designed
     - attempts/failure reason are recorded
     - worker can retry according to policy
     - no partial duplicate chunk state remains

7. **Tenant isolation verification**
   - Verify all queries and writes include `company_id` scoping.
   - Confirm one tenant’s worker processing cannot affect another tenant’s document/chunks.

8. **Logging verification**
   - Confirm structured logs include correlation/document/company context where existing logging conventions support it.

# Risks and follow-ups
- **Unknown current ingestion design**
  - If extracted text is not currently persisted or accessible to workers, you may need a small supporting change to store normalized extracted content or a reference the worker can read.

- **Duplicate processing in multi-instance deployments**
  - If there is no existing distributed coordination, implement safe DB claiming semantics now and note Redis/distributed locking as a follow-up if needed.

- **Schema evolution**
  - Adding job/status metadata may require a migration. Keep it minimal and consistent with current naming conventions.

- **Embedding model dimensions**
  - Ensure vector dimensions match the configured model and existing `pgvector` schema. If dimensions are hardcoded, verify compatibility before shipping.

- **Large documents / provider limits**
  - Chunking must respect token/size limits. If not fully addressed now, leave configuration hooks and document operational limits.

- **Retry policy**
  - If no shared retry framework exists, implement a simple bounded retry and document future alignment with broader worker retry infrastructure from ST-404.

- **Explainability metadata**
  - If not fully implemented now, at least persist enough metadata on chunks/documents to support future retrieval source references and reindexing.

- **Follow-up candidates**
  - dedicated embedding job table if document-status-only approach becomes limiting
  - admin/operator visibility for failed embedding jobs
  - reindex-all workflow by embedding model version
  - semantic retrieval query service and ranking tests
  - outbox/event-based trigger from document processing completion if not already present