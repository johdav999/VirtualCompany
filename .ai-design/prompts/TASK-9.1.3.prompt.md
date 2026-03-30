# Goal
Implement backlog task **TASK-9.1.3** for **ST-301 Company document ingestion and storage** so that document ingestion status is explicitly tracked through its lifecycle from **uploaded** to terminal states **processed** or **failed**.

This task should establish the domain and persistence behavior needed to represent ingestion progress reliably in the .NET modular monolith, aligned with the existing architecture:
- PostgreSQL as source of truth
- object storage for uploaded files
- background-worker-friendly lifecycle
- tenant-aware document metadata in `knowledge_documents`

Because no task-specific acceptance criteria were provided beyond the story, treat the story’s relevant requirement as the implementation target:
- document metadata persists an ingestion status
- status starts at `uploaded`
- status can transition to `processed` or `failed`
- failed states can carry actionable error information for later UI/API surfacing

# Scope
In scope:
- Add or refine a **document ingestion status model** in the domain/application layers
- Ensure `knowledge_documents` persistence supports the lifecycle states:
  - `uploaded`
  - `processed`
  - `failed`
- Ensure newly uploaded documents are created with `uploaded` status by default
- Add application/service logic to update status during ingestion processing
- Persist failure details in a structured or at least queryable way if the current model allows it
- Add tests covering valid lifecycle transitions and persistence behavior

Out of scope:
- Full document parsing/chunking/embedding pipeline from ST-302
- UI work unless required by existing compile-time contracts
- Virus scanning implementation
- Rich retry orchestration or worker scheduling beyond what is necessary to support status updates
- Broad redesign of the knowledge module

If the codebase already contains partial ingestion concepts, extend and normalize them rather than duplicating them.

# Files to touch
Inspect first, then update only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Domain/**`
  - knowledge document entity/aggregate
  - status enum/value object
  - domain validation for status transitions

- `src/VirtualCompany.Application/**`
  - commands/handlers/services for document upload and ingestion processing
  - DTOs/contracts that expose ingestion status
  - any validators

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - migrations
  - repository implementations
  - background processing persistence hooks if present

- `src/VirtualCompany.Api/**`
  - request/response contracts only if status must be surfaced or existing endpoints require updates

- Tests in corresponding test projects if present
  - domain tests
  - application tests
  - integration/persistence tests

Also inspect:
- `README.md`
- existing migration strategy
- any knowledge/document module files under `Knowledge`, `Documents`, or similar folders

# Implementation plan
1. **Discover the current document ingestion model**
   - Find the `knowledge_documents` entity mapping and any existing `ingestion_status` field.
   - Identify whether status is currently a raw string, enum, or absent.
   - Find upload flow and any background ingestion/processing flow already in place.
   - Identify where failure details are or should be stored, likely in `metadata_json` or a dedicated field if one already exists.

2. **Define a canonical ingestion status representation**
   - Prefer a strongly typed domain representation:
     - enum or value object such as `KnowledgeDocumentIngestionStatus`
   - Required states for this task:
     - `Uploaded`
     - `Processed`
     - `Failed`
   - If the database stores strings, map enum/value object consistently to lowercase persisted values:
     - `uploaded`
     - `processed`
     - `failed`

3. **Enforce lifecycle rules in the domain**
   - Ensure new documents are initialized with `Uploaded`.
   - Add explicit methods on the document aggregate/entity for transitions, e.g.:
     - `MarkProcessed(...)`
     - `MarkFailed(...)`
   - Prevent invalid or silent state mutation where practical.
   - Update `updated_at` on transitions.
   - If failure details are supported, capture a safe actionable message without leaking internal stack traces.

4. **Update persistence mapping**
   - Ensure EF Core maps the ingestion status correctly.
   - If the column does not exist or is not constrained appropriately, add a migration.
   - If useful and consistent with current conventions, add a DB check constraint for allowed values.
   - Preserve tenant-aware behavior and existing schema naming conventions.

5. **Wire upload flow to default status**
   - In the document creation/upload command path, set ingestion status to `uploaded` immediately after metadata/object storage persistence succeeds.
   - Do not mark documents as processed synchronously unless that is already the established architecture.
   - Keep the upload flow compatible with later background worker processing.

6. **Wire processing flow to terminal states**
   - In the ingestion processor/background handler/service, update status to:
     - `processed` on successful completion
     - `failed` on known unsupported/processing failure
   - If unsupported file types are already validated before persistence, keep that behavior; otherwise ensure persisted failed documents can still reflect actionable error state if the architecture expects that.
   - Make failure handling idempotent and safe for retries where possible.

7. **Surface failure context**
   - If there is an existing field for metadata or error details, store a concise actionable reason such as:
     - unsupported file type
     - extraction failed
     - file corrupted
   - Do not introduce a large new error model unless necessary for this task.
   - If no suitable field exists and adding one is low-risk, add a nullable failure reason field; otherwise use existing metadata JSON consistently.

8. **Add tests**
   - Domain tests:
     - new document defaults to `Uploaded`
     - valid transitions to `Processed`
     - valid transitions to `Failed`
   - Application tests:
     - upload command persists `uploaded`
     - processing success updates to `processed`
     - processing failure updates to `failed` with reason
   - Persistence/integration tests if the repo already uses them:
     - status round-trips correctly through EF Core
     - migration/schema supports expected values

9. **Keep implementation aligned with modular monolith boundaries**
   - Domain owns lifecycle semantics
   - Application orchestrates use cases
   - Infrastructure handles EF/storage/migrations
   - API remains thin

10. **Document assumptions in code comments only where needed**
   - Especially if this task lays groundwork for ST-302 asynchronous chunking/embedding

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If EF Core migrations are used in this repo:
   - generate/apply the migration for ingestion status changes
   - verify schema includes the expected column/default/constraint behavior

4. Manually verify the main lifecycle in code/tests:
   - upload/create document => status is `uploaded`
   - successful ingestion processing => status becomes `processed`
   - failed ingestion processing => status becomes `failed`
   - failure reason is persisted in the chosen field

5. Confirm no tenant-scoping regressions:
   - document updates remain scoped to the owning company/document identity

6. Confirm serialization/API contracts still compile if status is exposed externally

# Risks and follow-ups
- **Risk: existing code already uses free-form strings**
  - Normalize carefully to avoid breaking existing queries or API contracts.
- **Risk: no current ingestion processor exists**
  - Implement only the status transition hooks needed by current flows; do not overbuild ST-302.
- **Risk: failure details storage is unclear**
  - Prefer existing metadata/error fields over introducing broad schema changes unless necessary.
- **Risk: unsupported file handling may happen before persistence**
  - Preserve current behavior if intentional, but ensure the model still supports `failed` for persisted ingestion attempts.

Follow-ups after this task:
- Add richer intermediate statuses if needed later, such as `queued` or `processing`, but do **not** add them unless required by current code/story scope.
- Extend the pipeline for ST-302 chunking/embedding so `processed` reflects successful downstream indexing.
- Surface ingestion status and failure reason in web/API views for actionable user feedback.
- Add audit events/outbox notifications when ingestion fails or completes if the surrounding module already supports them.