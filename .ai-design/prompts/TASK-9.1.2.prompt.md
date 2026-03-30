# Goal
Implement backlog task **TASK-9.1.2 — Files are stored in object storage and document metadata in PostgreSQL** for story **ST-301 Company document ingestion and storage** in the existing .NET solution.

Deliver a vertical slice for company document upload persistence such that:

- uploaded file binaries are stored in **object storage**
- document metadata is stored in **PostgreSQL**
- the design is **tenant-aware**
- ingestion status is tracked on the metadata record
- the implementation fits the project’s **modular monolith / clean architecture** structure
- the solution leaves clean extension points for later processing steps like virus scanning, chunking, embeddings, and failure handling

No UI-heavy implementation is required unless already scaffolded; prioritize backend domain, application, infrastructure, persistence, and API endpoints needed to support the task.

# Scope
Implement only what is necessary for this task, aligned to ST-301 and the architecture/backlog context.

In scope:

- Add or complete the **knowledge document metadata model** in PostgreSQL
- Add object storage abstraction and concrete implementation suitable for the current stack
- Implement a **document upload application flow** that:
  - accepts tenant/company-scoped uploads
  - stores file content in object storage
  - persists metadata in PostgreSQL
  - records ingestion status
- Persist metadata fields consistent with architecture guidance, including:
  - `company_id`
  - `title`
  - `document_type`
  - `source_type`
  - `storage_url` or storage key/reference
  - `metadata_json`
  - `access_scope_json`
  - `ingestion_status`
  - timestamps
- Add validation for supported file types based on ST-301 notes:
  - start with common text / PDF / doc formats only
- Return actionable error responses for unsupported upload attempts
- Leave a clear hook for future virus scanning and async ingestion pipeline stages

Out of scope unless required by existing code patterns:

- chunking and embeddings
- semantic retrieval
- full document processing pipeline
- advanced UI workflows
- actual virus scanning implementation
- background worker processing beyond placeholder status/hook integration
- enterprise-grade object storage provisioning scripts

Assumptions to follow:

- Use shared-schema multi-tenancy with `company_id` enforcement
- Prefer existing project conventions for CQRS, DI, EF Core, API routing, and auth
- If object storage provider is not already present, implement via a provider-agnostic abstraction and a pragmatic local/dev-backed implementation plus configuration hooks for cloud storage

# Files to touch
Inspect the solution first and then update only the files needed. Likely areas:

- `src/VirtualCompany.Domain/**`
  - knowledge/document entity or aggregate
  - enums/value objects for document type, source type, ingestion status
- `src/VirtualCompany.Application/**`
  - upload command + handler
  - DTOs/contracts
  - validation
  - repository/storage interfaces
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - DbContext updates
  - migration for `knowledge_documents`
  - object storage implementation
  - repository implementation
  - DI registration
- `src/VirtualCompany.Api/**`
  - upload endpoint/controller/minimal API
  - request model binding for multipart upload
  - auth/tenant enforcement wiring
  - configuration binding
- `README.md`
  - brief setup/config notes if new storage settings are introduced

Also check for and reuse any existing equivalents before creating new files:

- tenant context abstractions
- current user/company resolution
- file storage abstractions
- result/error patterns
- shared constants/enums
- migrations folder and EF conventions

# Implementation plan
1. **Inspect current architecture in code**
   - Identify:
     - existing knowledge module structures
     - EF Core DbContext and migration patterns
     - tenant/company resolution approach
     - API style used in the solution
     - whether object storage abstraction already exists
   - Reuse existing patterns instead of introducing parallel ones.

2. **Model document metadata in the domain/application layers**
   - Add or complete a `KnowledgeDocument` entity matching the architecture intent.
   - Include fields equivalent to:
     - `Id`
     - `CompanyId`
     - `Title`
     - `DocumentType`
     - `SourceType`
     - `SourceRef` if useful and consistent
     - `StorageUrl` and/or storage key
     - `MetadataJson`
     - `AccessScopeJson`
     - `IngestionStatus`
     - `CreatedAt`
     - `UpdatedAt`
   - Use enums or constrained string constants for:
     - document type
     - source type
     - ingestion status
   - Initial ingestion status should reflect successful upload but not yet processing, e.g. `uploaded`.

3. **Add persistence mapping for PostgreSQL**
   - Configure EF Core mapping for the document table.
   - Ensure tenant-owned records include `CompanyId`.
   - Use PostgreSQL-friendly types for flexible fields, ideally JSONB where project conventions allow:
     - `MetadataJson`
     - `AccessScopeJson`
   - Add indexes that are useful now:
     - by `CompanyId`
     - optionally by `IngestionStatus`
     - optionally by `CreatedAt`
   - Create and include a migration.

4. **Introduce object storage abstraction**
   - Add an interface in application/infrastructure boundary, e.g. `IObjectStorage` or `IFileObjectStorage`.
   - Required capability:
     - upload stream/content
     - return storage reference (URL/key/path)
   - Keep the abstraction provider-neutral.
   - Include metadata support if easy, but do not overdesign.

5. **Implement concrete object storage**
   - If the repo already has a storage provider, integrate with it.
   - Otherwise implement a pragmatic version:
     - local filesystem-backed storage for development, exposed as object-storage-like abstraction
     - configuration shape that can later support Azure Blob / S3 / GCS
   - Generate deterministic tenant-aware storage paths, for example:
     - `companies/{companyId}/documents/{documentId}/{safeFileName}`
   - Do not trust raw file names; sanitize them.

6. **Implement upload command/use case**
   - Create an application command/handler for document upload.
   - Flow:
     1. resolve tenant/company context
     2. validate request fields
     3. validate file presence, size if applicable, and supported extension/content type
     4. create document ID
     5. upload binary to object storage
     6. persist metadata record in PostgreSQL
     7. return created document details
   - Persist enough metadata for future processing:
     - original file name
     - content type
     - file size
     - optional checksum if easy
   - Store these in `MetadataJson` if no dedicated columns exist.

7. **Validate supported file types**
   - Support a minimal allowlist aligned to ST-301 notes, such as:
     - `.txt`
     - `.md`
     - `.pdf`
     - `.doc`
     - `.docx`
   - Validate by extension and content type where practical.
   - Reject unsupported files with clear, actionable error messages.

8. **Add ingestion status handling**
   - On successful upload + metadata persistence, set status to `uploaded`.
   - If the design already includes downstream processing hooks, leave a clear transition path to:
     - `processing`
     - `processed`
     - `failed`
   - If upload to object storage fails, do not persist a misleading successful metadata record.
   - If metadata persistence fails after object upload, attempt cleanup of the uploaded object if feasible; otherwise log clearly for follow-up reconciliation.

9. **Expose API endpoint**
   - Add a tenant-aware authenticated endpoint for multipart/form-data upload.
   - Request should include:
     - file
     - title
     - document type
     - access scope metadata
   - Ensure company scoping is enforced from authenticated tenant context, not client-supplied trust alone.
   - Return created metadata payload including document ID and ingestion status.

10. **Add extension hooks for future pipeline**
   - Leave a placeholder or interface boundary for:
     - virus scanning
     - async ingestion event/job dispatch
   - A simple comment/TODO is not enough by itself; prefer a small explicit seam such as:
     - `IDocumentIngestionPipeline.Enqueue(documentId)`
     - or domain event/outbox hook if patterns already exist
   - It is acceptable for this seam to be a no-op implementation for now if consistent with current architecture.

11. **Configuration and DI**
   - Add configuration section for object storage settings.
   - Register storage implementation and any related services in DI.
   - Keep secrets/config externalized.

12. **Tests**
   - Add or update tests covering:
     - successful upload stores file and metadata
     - unsupported file type is rejected
     - company scoping is preserved
     - ingestion status defaults correctly
   - Prefer application/integration tests following existing repo patterns.

13. **Document minimal setup**
   - If new config is required, update `README.md` with concise local setup notes.

# Validation steps
Run the relevant checks after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration generation/application if applicable:
   - ensure the new migration exists and is included correctly
   - apply it using the project’s normal EF workflow if available

4. Manually verify the upload flow:
   - call the upload endpoint with a supported file
   - confirm:
     - object is written to configured storage
     - metadata row exists in PostgreSQL
     - `company_id` is populated correctly
     - `ingestion_status` is `uploaded`
     - metadata/access scope fields are persisted

5. Negative-path verification:
   - upload unsupported file type and confirm a clear validation error
   - simulate storage failure if practical and confirm no false-success metadata record remains

6. Code quality verification:
   - confirm no direct infrastructure leakage into API/UI layers beyond established patterns
   - confirm tenant scoping is enforced server-side
   - confirm storage paths do not expose unsafe file names unchecked

# Risks and follow-ups
- **Tenant isolation risk:** document records and storage paths must be company-scoped; avoid any endpoint behavior that trusts a caller-supplied company ID without authorization checks.
- **Consistency risk:** object storage and PostgreSQL are separate systems; partial failure can orphan blobs or metadata. Mitigate with best-effort cleanup and clear logging, and note reconciliation as a follow-up.
- **Provider mismatch risk:** if no real cloud object storage exists yet, a local/dev implementation should remain behind an abstraction so it can be swapped later without changing application logic.
- **Validation risk:** extension-only validation is weak; follow up with stronger MIME/content sniffing and malware scanning integration.
- **Schema flexibility risk:** JSONB fields are useful now, but avoid hiding critical queryable fields inside JSON if they are likely to be filtered often later.
- **Pipeline follow-up:** ST-302 will need the next stage to transition documents from `uploaded` to `processed/failed` through chunking and embeddings.
- **Operational follow-up:** add health checks for object storage if not already present, aligning with ST-104.
- **Security follow-up:** add virus scanning and signed/private object access patterns before broader production rollout.