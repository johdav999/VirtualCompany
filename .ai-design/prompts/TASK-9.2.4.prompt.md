# Goal
Implement backlog task **TASK-9.2.4 — Re-ingesting a document replaces or versions prior chunks safely** for story **ST-302 Chunking, embeddings, and semantic retrieval** in the existing .NET modular monolith.

The coding agent should add safe re-ingestion behavior for knowledge documents so that when a document is uploaded/processed again, prior `knowledge_chunks` are not left ambiguously active for retrieval. The implementation must preserve tenant isolation, support explainable retrieval, and fit the PostgreSQL + pgvector architecture.

The desired outcome is:

- a document can be re-ingested without producing duplicate active chunk sets,
- retrieval only returns the current valid chunk set for a document,
- prior chunk sets are either replaced atomically or versioned/inactivated safely,
- failures during re-ingestion do not leave the document with partially active chunks,
- the design leaves room for future reindexing by embedding model/version.

# Scope
Implement only what is necessary for this task within the current architecture and codebase conventions.

Include:

- Domain/application/infrastructure changes needed to support safe document re-ingestion.
- Persistence changes for chunk versioning or replacement semantics.
- Updates to ingestion workflow/background processing so chunk writes are safe and idempotent.
- Retrieval query updates so only the active/current chunk set is returned.
- Tests covering first ingestion, re-ingestion, and failure safety.

Prefer a pragmatic design such as one of these patterns, based on what best fits the current codebase:

1. **Versioned chunk sets**
   - Add a document ingestion/version concept or version fields on `knowledge_chunks`.
   - Mark one version as current/active.
   - Retrieval filters to current/active version only.

2. **Atomic replace**
   - Stage new chunks, then atomically swap active set and deactivate/remove old set in one transaction.
   - Retrieval only sees active chunks.

If the codebase already has ingestion job/state concepts, extend them rather than introducing a parallel abstraction.

Do not expand scope into:
- UI work unless required to surface existing ingestion status semantics,
- broad retrieval-service redesign beyond filtering current chunks,
- memory item changes,
- unrelated workflow/approval features.

# Files to touch
Inspect the solution first and then update the most relevant files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - knowledge document/chunk entities, enums, value objects, domain rules
- `src/VirtualCompany.Application/**`
  - document ingestion commands/handlers
  - chunking/embedding orchestration
  - retrieval query/services
  - DTOs/contracts for ingestion status/version metadata
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories
  - migrations
  - pgvector query logic
  - background worker persistence logic
- `src/VirtualCompany.Api/**`
  - only if API contracts or endpoints need minor updates for re-ingestion behavior
- Tests across:
  - `tests/**` or corresponding test projects in application/infrastructure layers

Also inspect:
- existing migration history,
- any document ingestion worker/job classes,
- any semantic retrieval service/query handlers,
- any repository methods that currently read/write `knowledge_chunks`.

# Implementation plan
1. **Inspect current ingestion and retrieval flow**
   - Find how `knowledge_documents` and `knowledge_chunks` are modeled.
   - Identify how ingestion status is tracked today.
   - Identify where chunk generation and embedding persistence happen.
   - Identify retrieval query path and current filters.
   - Determine whether there is already a concept of processing attempt, ingestion run, or version metadata.

2. **Choose the safest minimal persistence design**
   Prefer the smallest coherent change that guarantees correctness.

   Recommended approach if no versioning exists yet:
   - Add chunk-set versioning metadata, for example:
     - `knowledge_documents.current_ingestion_version` or similar
     - and/or `knowledge_chunks.ingestion_version`
     - optionally `knowledge_chunks.is_active`
     - optionally embedding metadata such as `embedding_model`, `embedding_dimensions`, `chunking_strategy`, `created_at`
   - Retrieval should filter by the document’s current version or active flag.

   Alternative acceptable approach:
   - Add a separate `document_ingestions` or `knowledge_document_versions` table if the codebase already trends toward explicit processing records.
   - Then link chunks to that record and mark one ingestion/version as current.

   Design requirements:
   - no mixed old/new active chunks for the same document,
   - no retrieval of stale chunks once re-ingestion succeeds,
   - failed re-ingestion must not remove the previously active chunk set,
   - tenant scoping remains enforced.

3. **Update domain and EF mappings**
   - Add the necessary fields/entities.
   - Add indexes to support retrieval and version filtering efficiently, likely including combinations around:
     - `company_id`
     - `document_id`
     - current/active/version fields
   - Preserve pgvector compatibility.
   - Add migration(s) with careful defaults for existing data.

   Migration guidance:
   - Existing chunks should remain retrievable after migration.
   - If introducing versioning, backfill existing rows to version `1` or equivalent current state.
   - If introducing active flags, mark existing rows active.

4. **Make ingestion writes safe**
   Update the ingestion pipeline so re-ingestion behaves transactionally.

   Expected behavior:
   - Start processing a document re-ingestion attempt.
   - Generate chunks and embeddings off to the side in memory or staged records.
   - Persist the new chunk set in a way that is not yet visible to retrieval, or visible only under a new version not marked current.
   - In a single transaction:
     - insert the new chunk set,
     - update the document’s current version / active ingestion pointer,
     - deactivate or supersede prior chunk set if needed,
     - update document ingestion status to processed.
   - On failure:
     - keep the previous current chunk set intact,
     - mark document status appropriately (`failed` or equivalent),
     - avoid partial activation.

   Also ensure idempotency:
   - if the same ingestion job retries, it should not create duplicate active versions,
   - use correlation/attempt identifiers if such infrastructure already exists,
   - if not, at minimum ensure the final activation step is safe under retries.

5. **Update retrieval logic**
   - Ensure semantic search only considers current/active chunks.
   - Enforce company and access-scope filters before or alongside similarity ranking per story notes.
   - Preserve source document references in results.
   - If retrieval currently queries `knowledge_chunks` directly, add the necessary join/filter to `knowledge_documents` or ingestion/version table.

   Validate that retrieval semantics are:
   - tenant-scoped,
   - document-current-version scoped,
   - explainability-friendly.

6. **Handle concurrency**
   Add a simple concurrency strategy for multiple re-ingestion attempts on the same document.

   Acceptable options:
   - transaction with row lock on `knowledge_documents`,
   - optimistic concurrency token if already used,
   - distributed/job-level lock if the worker framework already supports it.

   Minimum requirement:
   - two concurrent re-ingestions must not leave two active versions.

7. **Add or update tests**
   Add focused tests at the appropriate layers.

   Minimum scenarios:
   - **Initial ingestion**
     - document with no chunks gets chunked and becomes retrievable.
   - **Successful re-ingestion**
     - old chunks exist,
     - new ingestion completes,
     - retrieval returns only new/current chunks,
     - old chunks are inactive or non-current.
   - **Failed re-ingestion**
     - old chunks exist,
     - new ingestion fails before activation,
     - retrieval still returns old/current chunks only.
   - **Retry/idempotency**
     - repeated processing of the same re-ingestion path does not create duplicate active chunk sets.
   - **Tenant isolation**
     - retrieval never crosses `company_id`.
   - **Concurrency**
     - if practical in current test setup, verify only one current version remains after competing updates.

8. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries.
   - Keep orchestration/background concerns out of controllers.
   - Use application services/handlers for state changes.
   - Keep retrieval deterministic and testable.

9. **Document assumptions in code comments or PR notes**
   If no explicit acceptance criteria exist beyond the story, encode the operational rules clearly:
   - “current chunk set” definition,
   - failure behavior,
   - retry behavior,
   - migration/backfill assumptions.

# Validation steps
Run the normal repo validation first, then any targeted tests you add.

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects for application/infrastructure, run those specifically as well.

4. Validate migration correctness:
   - ensure migration applies cleanly,
   - verify existing documents/chunks remain retrievable after migration/backfill,
   - verify new schema supports pgvector queries.

5. Manually validate behavior in tests or a local harness:
   - ingest a document once and confirm chunks exist,
   - re-ingest same document with changed content,
   - confirm retrieval returns only latest/current chunks,
   - simulate ingestion failure and confirm previous chunks remain active,
   - confirm no cross-tenant retrieval leakage.

6. If the solution has logging around ingestion workers, verify logs clearly distinguish:
   - document id,
   - company id,
   - ingestion/version id,
   - activation/swap success or failure.

# Risks and follow-ups
- **Schema uncertainty:** The actual codebase may already have ingestion attempt/version concepts. Reuse them instead of adding a competing model.
- **Migration complexity:** Backfilling existing chunks to a default active/current version must be done carefully to avoid breaking retrieval.
- **Concurrency edge cases:** Without locking or concurrency control, simultaneous re-ingestion can produce multiple active sets.
- **Retry duplication:** Background worker retries can create duplicate chunk rows unless activation/versioning is idempotent.
- **Performance:** Retrieval joins/filters for current version must be indexed to avoid degrading semantic search.
- **Embedding metadata:** Story notes mention embedding model/version metadata for future reindexing; include minimal metadata now if it fits naturally, but do not overbuild.
- **Deletion strategy:** Decide whether prior chunks are soft-retained as historical versions or physically deleted after swap. Prefer retention if low-cost and consistent with current design; otherwise ensure replacement is still safe and auditable.
- **Future follow-up:** A later task may formalize explicit `document_ingestions` records, reindex workflows, and audit visibility for document version history if not introduced here.