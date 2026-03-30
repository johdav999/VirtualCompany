# Goal
Implement backlog task **TASK-9.2.6 — Keep embedding model/version metadata for future reindexing** for story **ST-302 Chunking, embeddings, and semantic retrieval**.

The coding agent should update the .NET solution so that embedding-producing persistence records retain enough metadata to identify:
- which embedding provider/model generated the vector
- which model version or deployment/version label was used
- when useful, the embedding dimensionality and any generation metadata needed to support future reindex/re-embed workflows

This should be implemented in a way that fits the existing architecture:
- PostgreSQL as transactional + pgvector store
- asynchronous background embedding generation
- tenant-scoped knowledge and memory records
- future-safe support for reindexing documents and memory when embedding models change

# Scope
In scope:
- Inspect current domain/entity models and EF Core persistence for `knowledge_chunks` and any other embedding-bearing entities, especially `memory_items`
- Add explicit embedding metadata fields to persisted records where embeddings are stored
- Update migrations/schema configuration accordingly
- Update embedding generation pipeline(s) so metadata is populated whenever embeddings are created
- Ensure re-ingestion/replacement flows preserve or refresh metadata consistently
- Add or update tests covering persistence and metadata population
- Keep implementation tenant-safe and backward-compatible where practical

Out of scope unless clearly required by existing code structure:
- Building a full reindex orchestration workflow
- Admin UI for viewing or editing embedding metadata
- Changing embedding provider behavior beyond recording metadata
- Large retrieval algorithm changes unrelated to metadata persistence

If the codebase already stores some metadata in JSON, prefer explicit columns for core reindexing identifiers if that aligns with current conventions; otherwise use the most maintainable approach consistent with the existing schema and architecture.

# Files to touch
Inspect and modify only the files needed after discovery, likely in these areas:

- `src/VirtualCompany.Domain/**`
  - entities/value objects for `KnowledgeChunk`, `MemoryItem`, or equivalent
- `src/VirtualCompany.Application/**`
  - embedding service contracts
  - ingestion/chunking commands/handlers
  - memory creation flows if embeddings are generated there
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - DbContext
  - migrations
  - embedding provider implementation(s)
  - repositories/background worker persistence logic
- `src/VirtualCompany.Api/**`
  - only if DI/contracts need wiring changes
- tests under the existing test projects in the solution

Also inspect:
- `README.md`
- solution/project files only if migration/test project wiring requires it

Do not touch mobile/web UI unless the existing implementation unexpectedly depends on DTO shape changes.

# Implementation plan
1. **Discover current embedding storage design**
   - Find the entities/tables that currently store vectors:
     - `knowledge_chunks`
     - `memory_items`
     - any other semantic index tables if present
   - Identify:
     - current entity properties
     - EF mappings
     - migrations already applied
     - where embeddings are generated and saved
     - whether provider/model info is already partially available in config or service responses

2. **Define the metadata contract**
   - Add explicit persisted fields for embedding provenance. Prefer names aligned with existing naming conventions, for example:
     - `EmbeddingProvider`
     - `EmbeddingModel`
     - `EmbeddingModelVersion` or `EmbeddingVersion`
     - optionally `EmbeddingDimensions`
     - optionally `EmbeddedAt`
   - Apply this to all entities that persist embeddings, at minimum `KnowledgeChunk`, and also `MemoryItem` if it stores embeddings in the current codebase.
   - If the project already uses `metadata_json` for extensibility, keep core reindexing identifiers as first-class fields and use JSON only for secondary provider-specific details.

3. **Update domain and persistence models**
   - Modify entity classes to include the new metadata properties.
   - Update EF Core configurations:
     - column names
     - lengths/nullability
     - defaults if appropriate
   - Make a pragmatic backward-compatible choice:
     - nullable for existing rows if needed
     - required for newly created embeddings through application logic
   - Ensure pgvector mapping remains unchanged except where dimensions are already encoded elsewhere.

4. **Create and verify database migration**
   - Add an EF Core migration that updates the relevant tables.
   - For PostgreSQL, add columns to:
     - `knowledge_chunks`
     - `memory_items` if applicable
   - Avoid destructive changes.
   - If safe and easy, backfill obvious values from current configuration for existing rows; otherwise leave nullable and document follow-up.

5. **Populate metadata during embedding generation**
   - Update the embedding service abstraction or result object so callers can access model/version metadata, not just the vector.
   - If the provider currently returns only float arrays/vectors, introduce a result type such as:
     - vector
     - provider
     - model
     - version
     - dimensions
   - Update all embedding creation call sites so persisted entities are populated consistently.
   - Ensure document re-ingestion replaces or versions chunks with fresh metadata matching the newly generated embeddings.

6. **Preserve consistency in ingestion workflows**
   - Review document chunking/re-ingestion flow for ST-302 behavior.
   - Ensure replacement/versioning of prior chunks does not lose provenance.
   - If there is a memory embedding flow, apply the same consistency there.
   - Keep tenant scoping and access policy behavior unchanged.

7. **Add tests**
   - Add/adjust unit and integration tests to verify:
     - embedding metadata fields are persisted when chunks are created
     - metadata is persisted for memory items if applicable
     - re-ingestion creates replacement records with metadata populated
     - schema mappings are valid
   - Prefer focused tests over broad end-to-end additions.

8. **Document assumptions in code comments where needed**
   - If model version is not directly available from the provider SDK, use the configured deployment/version label and name it clearly.
   - Keep comments concise and implementation-oriented.

9. **Keep changes minimal and production-safe**
   - Do not refactor unrelated retrieval logic.
   - Do not introduce UI/API surface changes unless required by existing DTO/entity boundaries.
   - Preserve existing build/test behavior.

# Validation steps
Run discovery first, then validate with the smallest reliable set of commands.

1. Build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If the repository uses EF Core migrations in a startup project, ensure the migration compiles cleanly.
   - If needed, run the project-specific migration command consistent with the repo’s existing pattern.

4. Verify in code review that:
   - embedding-bearing entities now include model/version provenance
   - persistence mappings and migration cover all relevant tables
   - embedding generation paths populate metadata every time
   - no tenant scoping or retrieval filters were weakened
   - re-ingestion logic still safely replaces or versions prior chunks

5. If integration tests or local DB validation are available, confirm inserted `knowledge_chunks` rows contain:
   - vector
   - provider/model/version metadata
   - expected dimensions if implemented

# Risks and follow-ups
- **Provider SDK may not expose a true version string**
  - Use configured deployment/model version label as the persisted version field and document that meaning clearly.

- **Existing rows will not have metadata**
  - Accept nullable historical records unless there is an easy deterministic backfill path.
  - Follow-up: add a reindex/backfill job for legacy embeddings.

- **Multiple embedding-bearing entities may exist**
  - Do not stop at `knowledge_chunks` if `memory_items` or other tables also persist embeddings.

- **Vector dimension may be fixed in schema**
  - If dimensions are already implied by pgvector column type, storing dimensions is optional; still store model/version because that is the core requirement.

- **Migration compatibility**
  - Keep schema changes additive and non-breaking.

Suggested follow-up backlog after this task:
- add a reindex candidate query/report for rows with outdated embedding model/version
- add operational tooling to trigger tenant-scoped or document-scoped re-embedding
- expose embedding provenance in internal diagnostics/audit views if useful