# Goal
Implement backlog task **TASK-ST-301 — Company document ingestion and storage** for story **ST-301 Company document ingestion and storage** in the existing .NET modular monolith.

Deliver a first-pass, production-shaped document ingestion flow that allows tenant-scoped users to upload company knowledge documents, stores file binaries in object storage, persists document metadata in PostgreSQL, tracks ingestion lifecycle status, and surfaces actionable failures for unsupported or failed files.

This implementation should align with the architecture and backlog guidance for:
- **EP-3 Knowledge, memory, and retrieval**
- **Knowledge & Memory Module**
- **Shared-schema multi-tenancy with `company_id` enforcement**
- **Object storage for uploaded files**
- **PostgreSQL metadata persistence**
- **Background-worker-friendly ingestion pipeline design**
- **Future chunking/embedding pipeline in ST-302**

# Scope
Implement only what is needed for **ST-301**.

Include:
- Tenant-scoped domain model and persistence for company knowledge documents
- Upload API/application flow for documents with metadata:
  - `title`
  - `document_type`
  - `access_scope`
- File storage in object storage abstraction
- Metadata persistence in PostgreSQL
- Ingestion status tracking with states such as:
  - `uploaded`
  - `processing`
  - `processed`
  - `failed`
  - optionally `unsupported`
- Validation for supported file types (start with common text/PDF/doc formats only)
- Actionable error reporting for unsupported or failed files
- Pipeline design that leaves a clear hook for:
  - virus scanning
  - async processing/chunking later
- Minimal query/read endpoint(s) needed to verify uploaded documents and statuses

Do not implement unless already trivial and necessary:
- Full chunking/embedding generation
- Semantic retrieval
- Full UI workflow beyond minimal integration if the project already has a clear pattern
- Real virus scanning
- Complex document parsing
- Full background processing unless needed to transition status cleanly
- Broad document management features like delete/versioning unless already scaffolded

If the codebase already contains partial knowledge/document infrastructure, extend it rather than duplicating patterns.

# Files to touch
Inspect the solution structure first and then touch the appropriate files across these likely projects:

- `src/VirtualCompany.Domain`
  - Add/extend knowledge document entity, enums/value objects, and domain rules
- `src/VirtualCompany.Application`
  - Add commands/handlers/DTOs/validators for upload and listing
  - Add object storage and ingestion service abstractions
- `src/VirtualCompany.Infrastructure`
  - Add EF Core configuration/migrations support
  - Implement object storage provider or local/dev-backed object storage adapter
  - Implement repository persistence
- `src/VirtualCompany.Api`
  - Add tenant-scoped endpoints/controllers/minimal APIs for upload and retrieval
  - Wire DI and request handling
- `src/VirtualCompany.Shared`
  - Add shared contracts only if this repo uses shared DTOs/contracts there
- `src/VirtualCompany.Web`
  - Only if there is already an established upload page/pattern and a minimal UI is necessary for verification
- `README.md`
  - Update only if setup/configuration for storage or upload behavior must be documented

Also expect to add:
- EF Core migration files if migrations are tracked in source
- Configuration entries/options classes for storage
- Tests in the existing test projects if present in the repo

# Implementation plan
1. **Inspect the existing architecture and conventions**
   - Review `README.md`, project references, startup wiring, and any existing patterns for:
     - CQRS/application handlers
     - tenant resolution
     - authorization
     - EF Core entities/configurations
     - file upload endpoints
     - background jobs
     - object storage abstractions
   - Follow the repository’s established style exactly.

2. **Model the document aggregate in the domain**
   - Add a tenant-owned entity aligned with the architecture’s `knowledge_documents` table.
   - Include fields equivalent to:
     - `id`
     - `company_id`
     - `title`
     - `document_type`
     - `source_type` = upload
     - `source_ref` nullable
     - `storage_url` or storage key/path
     - `metadata_json`
     - `access_scope_json`
     - `ingestion_status`
     - timestamps
     - failure details if the codebase has a standard pattern for operational errors
   - Add enums/constants for:
     - supported document types
     - ingestion statuses
   - Keep the model ready for ST-302 without implementing chunking now.

3. **Define supported file handling rules**
   - Support a conservative initial allowlist for common formats only, such as:
     - `.txt`
     - `.md`
     - `.pdf`
     - `.doc`
     - `.docx`
   - Validate by extension and content type where practical.
   - Reject unsupported files with actionable messages.
   - Leave a clear pre-processing hook/interface for future virus scanning before persistence is finalized or before processing advances.

4. **Add application-layer upload command**
   - Create a command/handler for uploading a company document.
   - Inputs should include:
     - tenant/company context
     - user context if needed for auditing/ownership
     - title
     - document type
     - access scope metadata
     - uploaded file stream/content
     - original filename/content type
   - Validate:
     - required title
     - valid document type
     - valid tenant scope
     - file presence and non-zero length
     - supported file type
   - Flow:
     1. resolve tenant/company context
     2. create document record with initial status (`uploaded` or `processing`, depending on your flow)
     3. store file in object storage using a deterministic tenant-scoped path
     4. persist metadata in PostgreSQL
     5. set status appropriately
     6. if storage/persistence fails, mark as `failed` where possible and return actionable error

5. **Design storage paths for tenant isolation**
   - Use tenant-scoped object keys, for example:
     - `companies/{companyId}/knowledge/{documentId}/{sanitizedFileName}`
   - Do not expose unsafe local paths.
   - Preserve original filename in metadata if useful, but use sanitized storage naming.

6. **Implement object storage abstraction**
   - If an abstraction already exists, reuse it.
   - Otherwise add a minimal interface in Application and implementation in Infrastructure.
   - The implementation should support dev/test operation, potentially via:
     - local filesystem-backed storage for development
     - or an existing cloud storage adapter if already present
   - Return a storage key/URL reference suitable for persistence.
   - Keep the abstraction future-proof for Azure Blob/S3-style providers.

7. **Persist metadata with EF Core**
   - Add EF Core entity configuration for `knowledge_documents`.
   - Ensure:
     - `company_id` is required
     - JSON fields map correctly for metadata/access scope
     - indexes support tenant-scoped listing and status filtering
   - Suggested useful indexes:
     - `(company_id, created_at desc)`
     - `(company_id, ingestion_status)`
   - Add migration if the repo tracks migrations.

8. **Track ingestion lifecycle status**
   - Implement status transitions that make sense for ST-301:
     - on accepted upload: `uploaded`
     - if immediate processing step exists: `processing`
     - if upload/storage completed and no parsing yet: either remain `uploaded` or move to `processed` only if your definition is “stored successfully”
   - Prefer a model that supports ST-302 cleanly:
     - `uploaded` = file stored, awaiting downstream processing
     - `processing` = parser/chunker running
     - `processed` = downstream ingestion complete
     - `failed` = ingestion failed
     - `unsupported` optional if represented distinctly
   - Since ST-301 requires status tracked from uploaded to processed/failed, implement a minimal processing step if needed:
     - after successful storage, mark `uploaded`
     - optionally enqueue or invoke a lightweight ingestion finalizer that validates readiness and marks `processed`
   - If no worker infrastructure exists yet, a synchronous transition to `processed` after successful storage is acceptable, but structure the code so ST-302 can replace this with async processing later.

9. **Surface actionable error states**
   - Ensure API/application responses distinguish:
     - unsupported file type
     - empty file
     - storage failure
     - metadata persistence failure
     - tenant authorization/scope failure
   - Persist failure reason/details in a safe, user-facing form if the domain has a place for it.
   - Add a read/list endpoint so clients can see document status and error state.

10. **Add tenant-scoped API endpoints**
    - Implement endpoints for at least:
      - upload document
      - list documents for current company
      - get document metadata/status by id
   - Enforce company scoping on every query and command.
   - Use existing auth/tenant resolution patterns from ST-101 foundations if present.
   - Return `forbidden`/`not found` appropriately for cross-tenant access.

11. **Keep hooks for future async ingestion**
    - Introduce a seam such as:
      - `IDocumentIngestionOrchestrator`
      - `IDocumentProcessingQueue`
      - or domain/application event after upload
    - For now it may execute inline or no-op after status update.
    - Explicitly leave a placeholder for:
      - virus scanning
      - text extraction
      - chunking
      - embeddings
      - reprocessing

12. **Add tests**
    - Add or extend tests for:
      - upload success persists metadata and stores file
      - unsupported file rejected
      - tenant scoping enforced
      - failed storage results in failed/actionable outcome
      - list/get only returns current company documents
      - status transitions behave as expected
   - Prefer unit tests for handlers/validators and integration tests for API + persistence if the repo supports them.

13. **Document configuration**
    - If new configuration is required, document:
      - storage provider settings
      - local dev storage path/container/bucket
      - max upload size if introduced
      - supported file types

# Validation steps
1. Restore and inspect the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run:
   - `dotnet build`
   - `dotnet test`

4. If migrations are used:
   - generate/apply the migration using the repo’s existing EF workflow
   - verify the `knowledge_documents` table/schema matches the intended model

5. Manually verify the API flow:
   - Upload a supported file for a valid company context
   - Confirm:
     - object storage contains the file
     - PostgreSQL contains the metadata row
     - status is set correctly
   - List documents and verify the uploaded document appears with:
     - title
     - type
     - access scope
     - ingestion status

6. Verify failure behavior:
   - Upload an unsupported file type and confirm actionable validation error
   - Simulate storage failure if feasible and confirm:
     - safe error response
     - document status/failure state is meaningful
   - Attempt cross-tenant access and confirm forbidden/not found behavior

7. If a minimal UI was added, verify from the web app that:
   - upload works
   - status is visible
   - failed/unsupported states are understandable

# Risks and follow-ups
- **Status semantics risk:** ST-301 mentions tracking from uploaded to processed/failed, but ST-302 owns chunking/embedding. Keep status semantics explicit so “processed” does not block future async ingestion design.
- **Object storage ambiguity:** The repo may not yet have a concrete storage provider. If so, implement a clean abstraction with a dev-friendly adapter and avoid coupling to one cloud vendor.
- **Tenant isolation risk:** Every document query and storage path must remain company-scoped. Do not rely on client-supplied company IDs without server-side tenant resolution/authorization.
- **Large file handling:** Keep v1 conservative. If no upload size policy exists, add a reasonable limit or at least structure for one.
- **Security follow-up:** Leave a clear virus scanning hook in the ingestion pipeline even if not implemented now.
- **Metadata evolution:** `access_scope` and document metadata should be flexible JSON-backed structures, but validate enough to avoid garbage data.
- **Future stories:** Ensure this implementation sets up ST-302 and ST-304 cleanly by preserving:
  - stable document IDs
  - storage references
  - ingestion status transitions
  - room for parser/chunking metadata
  - failure reason tracking