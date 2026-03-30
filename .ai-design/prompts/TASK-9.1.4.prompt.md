# Goal
Implement backlog task **TASK-9.1.4 — Unsupported or failed files surface actionable error states** for **ST-301 Company document ingestion and storage** in the existing .NET solution.

This work should ensure that when a company user uploads a document that is unsupported or fails during ingestion, the system persists a clear, tenant-scoped, user-actionable error state and exposes it through the application/API/UI in a way that supports troubleshooting and next steps.

The implementation must fit the documented architecture:
- modular monolith
- ASP.NET Core backend
- PostgreSQL metadata store
- object storage for files
- background ingestion pipeline
- tenant-aware document records in the Knowledge module

Because no explicit acceptance criteria were provided for this task beyond the story-level requirement, derive practical acceptance behavior from ST-301:
- unsupported file types are rejected or marked failed with a specific actionable reason
- ingestion failures are persisted on the document record
- user-facing responses/views expose status and guidance
- tenant isolation is preserved
- implementation is production-safe and test-covered

# Scope
Include:
- domain/application/infrastructure changes needed to represent actionable ingestion failures
- document status/error persistence updates
- upload and/or ingestion pipeline behavior for unsupported files and processing failures
- API/query contract updates so clients can retrieve actionable error details
- minimal web UI updates if a document list/detail/upload result surface already exists
- tests for supported/unsupported/failure scenarios

Do not include:
- broad redesign of the ingestion pipeline
- virus scanning implementation
- new file parsers beyond what already exists
- full notification/inbox integration unless already trivial and local to this flow
- unrelated ST-302 chunking/embedding work except where current ingestion status transitions already depend on it

Use the existing code patterns and naming conventions in the repository. Prefer incremental changes over introducing new abstractions unless clearly justified.

# Files to touch
Start by inspecting these likely areas and adjust based on actual repository structure:

- `src/VirtualCompany.Domain/**`
  - document aggregate/entity/value objects
  - ingestion status enum/state model
  - domain errors or result types

- `src/VirtualCompany.Application/**`
  - upload document command/handler
  - document queries/DTOs/view models
  - ingestion orchestration/background job contracts
  - validation and mapping logic

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration/migrations
  - object storage integration
  - ingestion worker/services/parsers
  - repository implementations

- `src/VirtualCompany.Api/**`
  - document upload endpoints/controllers
  - document retrieval endpoints
  - problem details / error mapping if needed

- `src/VirtualCompany.Web/**`
  - document upload UI
  - document list/detail/status rendering
  - actionable error display components/messages

Also inspect:
- `README.md`
- existing migrations
- any knowledge/document module folders
- shared contracts in `src/VirtualCompany.Shared/**` if DTOs are centralized there

If a migration is required, add it in the appropriate infrastructure project using the repository’s existing EF Core migration conventions.

# Implementation plan
1. **Discover the current document ingestion flow**
   - Find the `knowledge_documents` representation in code and map it to the architecture fields:
     - title
     - document_type
     - source_type
     - storage_url
     - metadata/access scope
     - ingestion_status
   - Identify current statuses and where transitions occur:
     - upload request
     - object storage write
     - background ingestion start
     - parse/chunk/embed completion
     - failure paths
   - Identify whether unsupported file types are currently:
     - blocked at request validation
     - accepted then failed later
     - silently ignored
     - surfaced only as generic errors

2. **Define a durable actionable error model**
   Add a minimal but explicit model for ingestion failure details. Prefer extending the existing document entity rather than creating a separate subsystem unless one already exists.

   The model should support:
   - machine-readable error code
   - human-readable summary
   - actionable guidance
   - failure timestamp
   - optional technical detail safe for operators but not raw stack traces for end users

   Example shape, adapted to existing conventions:
   - `IngestionErrorCode` or string code such as:
     - `unsupported_file_type`
     - `file_empty`
     - `file_corrupted`
     - `storage_unavailable`
     - `parser_failed`
     - `processing_timeout`
   - `IngestionErrorMessage`
   - `IngestionErrorAction`
   - `IngestionFailedAt`

   If the project already uses JSONB metadata for flexible fields, it is acceptable to store structured failure details in a JSON column. Otherwise add explicit columns if that better matches current style.

3. **Clarify and implement status transitions**
   Ensure the document lifecycle clearly distinguishes:
   - uploaded
   - processing
   - processed
   - failed

   Unsupported files should result in one of these consistent behaviors:
   - preferred: reject early with a validation error before storage if file extension/content type is clearly unsupported
   - if the current architecture requires creating a document record first, mark it as `failed` immediately with actionable details

   For processing-time failures:
   - catch known parser/processing exceptions
   - mark the document as `failed`
   - persist actionable details
   - avoid leaving documents stuck in `uploaded` or `processing`

4. **Implement supported file type policy**
   Based on ST-301 notes, support only common text/PDF/doc formats already intended by the system.
   Discover existing supported types and centralize the policy in one place, for example:
   - application validator
   - ingestion service capability registry
   - shared helper/options class

   The policy should validate using existing conventions, likely:
   - extension
   - content type
   - possibly both

   Return or persist actionable guidance such as:
   - “This file type is not supported yet. Upload PDF, DOCX, TXT, or Markdown.”
   - “Export the file to PDF or DOCX and try again.”

5. **Update upload command/API behavior**
   Modify the upload flow so that unsupported/failed states are visible to callers.
   Depending on current API style:
   - for early validation failures, return a structured 400 with field/problem details
   - for accepted uploads that later fail, return the created document with status and expose failure details on subsequent fetch/list calls

   Ensure tenant scoping is enforced on all reads/writes.

6. **Update ingestion worker/service failure handling**
   In the background ingestion path:
   - wrap parsing/processing in targeted exception handling
   - map known failures to actionable codes/messages/actions
   - persist failure state atomically
   - log technical details with correlation/document/company context
   - do not expose raw exception internals in user-facing DTOs

   If retries exist, distinguish:
   - permanent failures like unsupported type/corrupt file
   - transient failures like storage/network/service unavailable

   Permanent failures should not retry indefinitely.

7. **Expose actionable error state in queries/DTOs**
   Update document query models returned by API/application services to include enough information for UI rendering, for example:
   - `IngestionStatus`
   - `IngestionErrorCode`
   - `IngestionErrorMessage`
   - `IngestionErrorAction`
   - `CanRetry` if retry exists today
   - `FailedAt`

   Ensure list views can show a concise status badge and detail views can show the full actionable message.

8. **Add or update UI rendering**
   If the web app already has document upload/list/detail screens:
   - show failed status clearly
   - show actionable guidance inline
   - distinguish unsupported files from generic failures
   - avoid technical jargon
   - if retry/re-upload exists, point users to it

   Example UX copy:
   - Unsupported: “Unsupported file type. Upload PDF, DOCX, TXT, or MD.”
   - Corrupt file: “We couldn’t read this file. Re-save/export it and upload again.”
   - Temporary processing issue: “Processing failed due to a temporary issue. Try uploading again later.”

   Keep UI changes minimal and aligned with existing component patterns.

9. **Persist schema changes**
   If needed, add EF Core migration for new failure detail fields.
   Keep migration backward-compatible and nullable where appropriate.
   If there is already a metadata JSON column suitable for this, prefer using it to minimize schema churn.

10. **Add tests**
   Add focused tests at the right layers:
   - domain/application tests for status/error mapping
   - API tests for unsupported upload behavior
   - ingestion service/worker tests for parser failure -> failed status with actionable details
   - query/UI tests if the repo already has patterns for them

   Cover at least:
   - supported upload enters normal lifecycle
   - unsupported file returns or persists actionable unsupported state
   - parser/processing exception marks document failed with actionable message
   - tenant A cannot read tenant B document failure details

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run tests before and after changes:
   - `dotnet test`

3. If migrations are added, verify they apply cleanly using the repository’s normal workflow.

4. Manually validate the main scenarios:
   - upload a supported file and confirm normal status progression
   - upload an unsupported file type and confirm actionable error response/state
   - simulate a processing/parsing failure and confirm:
     - document status becomes `failed`
     - actionable message is persisted
     - no raw stack trace is shown to the user
   - verify document list/detail or API response includes failure details
   - verify tenant scoping on document retrieval endpoints

5. Confirm logs include operational context for failures:
   - document ID
   - company/tenant ID
   - correlation ID if available

6. Summarize in the final implementation notes:
   - what statuses/errors were added
   - whether unsupported files are rejected early or persisted as failed
   - any migration added
   - any follow-up gaps discovered

# Risks and follow-ups
- The repository may not yet have a complete ST-301 ingestion pipeline; if so, implement the smallest coherent slice needed for actionable failure states without inventing unrelated architecture.
- If there is no existing document UI, prioritize API/application-layer support and note UI follow-up explicitly.
- Be careful not to leak internal exception details or storage paths in user-facing messages.
- Avoid duplicating supported-file-type rules across API, application, and worker layers; centralize them.
- If retries already exist, ensure permanent failures are not retried endlessly.
- If schema changes are needed, keep them additive and nullable to reduce migration risk.
- Follow-up candidates to note if not implemented now:
  - explicit retry/reprocess action for failed documents
  - richer failure taxonomy and localization
  - virus scanning result states
  - audit events for document ingestion failures
  - admin/operator diagnostics separate from end-user messaging