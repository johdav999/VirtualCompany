# Goal
Implement backlog task **TASK-9.1.1** for story **ST-301 — Company document ingestion and storage** so that company users can upload documents with **title**, **document type**, and **access scope** metadata, with files stored in object storage and metadata persisted in PostgreSQL in a **tenant-scoped** way.

# Scope
Deliver the minimum end-to-end vertical slice for document upload and metadata persistence in the existing .NET solution, aligned to the modular monolith architecture.

Include:
- A tenant-scoped document upload API/application flow.
- Persistence of document metadata in PostgreSQL using the `knowledge_documents` model.
- File storage in object storage through an abstraction.
- Upload request validation for:
  - title
  - document type
  - access scope
  - supported file types
  - file presence/size
- Initial ingestion lifecycle tracking with statuses such as:
  - `uploaded`
  - `processing`
  - `processed`
  - `failed`
- Actionable error states for unsupported or failed files.
- Basic auditability/logging hooks where natural.
- A simple web UI entry point only if there is already an obvious existing pattern; otherwise prioritize backend and shared contracts.

Do not include unless already trivial from existing patterns:
- Full chunking/embedding pipeline from ST-302.
- Virus scanning implementation, but leave a clear extension point/hook.
- Advanced retrieval/search UX.
- Mobile implementation.
- Full background processing beyond what is needed to represent ingestion status transitions.

Assume no explicit acceptance criteria beyond the story/backlog details, so use the story notes as implementation guidance:
- Support common text/PDF/doc formats only.
- Keep access scope tenant-aware.
- Design the pipeline so virus scanning can be inserted later.

# Files to touch
Inspect the solution first and then touch only the files needed. Likely areas:

- `src/VirtualCompany.Domain/**`
  - Add or extend document domain entity/value objects/enums for knowledge documents and ingestion status.
- `src/VirtualCompany.Application/**`
  - Add command, validator, handler, DTOs, and storage abstraction for document upload.
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configuration/migrations for `knowledge_documents`.
  - Object storage implementation.
  - Repository implementation.
- `src/VirtualCompany.Api/**`
  - Add authenticated tenant-scoped endpoint/controller/minimal API for multipart upload.
- `src/VirtualCompany.Shared/**`
  - Shared request/response contracts if this project is used that way in the solution.
- `src/VirtualCompany.Web/**`
  - Optional: minimal upload page/component only if the app already has a matching pattern and it can be added cheaply.
- `README.md`
  - Only if setup/configuration for object storage or upload limits must be documented.

Also inspect for existing equivalents before creating new files:
- tenant context abstractions
- current user/company resolution
- authorization policies
- storage abstractions
- result/error patterns
- validation framework
- audit event patterns
- outbox/background job patterns

# Implementation plan
1. **Inspect the existing architecture and patterns**
   - Build a quick mental map of:
     - API style used in `VirtualCompany.Api`
     - application command/query pattern
     - domain entity conventions
     - EF Core DbContext and configurations
     - tenant resolution and authorization
     - file/object storage abstractions
   - Reuse existing conventions exactly; do not introduce a parallel pattern.

2. **Model the document aggregate/persistence shape**
   - Implement or extend a `KnowledgeDocument` entity matching the architecture/backlog intent:
     - `Id`
     - `CompanyId`
     - `Title`
     - `DocumentType`
     - `SourceType` = upload
     - `SourceRef` nullable
     - `StorageUrl` or storage key/reference
     - `MetadataJson` or equivalent structured metadata
     - `AccessScopeJson` or equivalent structured scope payload
     - `IngestionStatus`
     - timestamps
   - Add enums/constants for:
     - supported document types, if the codebase uses enums
     - ingestion statuses
     - source type
   - Keep the model future-compatible with ST-302.

3. **Define upload contracts**
   - Add an application/API request contract for multipart upload containing:
     - `title`
     - `documentType`
     - `accessScope`
     - file
   - `accessScope` should be represented in a structured way, not a free-form string if possible.
   - If the system already uses JSON payloads with multipart metadata, follow that pattern.
   - Add response DTO with at least:
     - document id
     - title
     - document type
     - ingestion status
     - created timestamp
     - any actionable error message if relevant

4. **Implement validation**
   - Validate:
     - authenticated user and resolved company context
     - title required and length-bounded
     - document type required and allowed
     - access scope required and valid for tenant context
     - file required
     - file extension/content type in allowed set
     - file size within a reasonable configured limit
   - Supported formats should initially cover common text/PDF/doc formats only, e.g.:
     - `.txt`
     - `.pdf`
     - `.doc`
     - `.docx`
     - optionally `.md`
   - Return clear field-level or problem-details style errors consistent with the codebase.

5. **Add object storage abstraction usage**
   - Reuse an existing storage abstraction if present; otherwise add one in Application and implement it in Infrastructure.
   - Store uploaded files in object storage using a tenant-aware path/key convention, e.g.:
     - `{companyId}/knowledge/{documentId}/{originalFileName}`
   - Persist either:
     - storage URL, or
     - storage key plus resolved URL pattern,
     depending on existing conventions.
   - Keep a clear hook/interface for future virus scanning before finalizing status progression.

6. **Persist metadata in PostgreSQL**
   - Add EF Core mapping for `knowledge_documents`.
   - Ensure tenant-owned records include `CompanyId`.
   - Store access scope in a structured JSON column if that matches the architecture.
   - Add migration if the table/config does not already exist.
   - If the table already exists partially, evolve it minimally and safely.

7. **Implement upload command/handler**
   - Flow should be:
     1. resolve tenant/company context
     2. validate request
     3. create document record with initial status `uploaded`
     4. upload file to object storage
     5. update storage reference and status
     6. if there is no processing pipeline yet, either:
        - leave as `uploaded`, or
        - transition to `processing` only if a worker/event is actually triggered
     7. on known failures, set status `failed` with actionable error metadata
   - Prefer transactional consistency where practical:
     - if DB record is created before storage upload and storage fails, mark document `failed`
     - avoid orphaned successful uploads without DB metadata
   - If the codebase has outbox/events, emit a document-uploaded event for future ingestion processing.

8. **Expose tenant-scoped API endpoint**
   - Add an authenticated endpoint for document upload.
   - Enforce company scoping from membership/tenant context, not from arbitrary client-supplied company id unless that is the established pattern.
   - Use authorization consistent with company user access.
   - Endpoint should accept multipart form-data if uploading binary files.
   - Return a clean success payload and proper error responses for:
     - unsupported file type
     - validation failure
     - storage failure
     - unauthorized/forbidden tenant access

9. **Represent actionable failure states**
   - Add a place to store or return failure reason/message for unsupported or failed files.
   - If the schema already has `metadata_json`, use it carefully for failure details if no dedicated column exists.
   - Ensure the API response surfaces actionable messages such as:
     - unsupported file type
     - file too large
     - upload failed, retry later
   - Do not expose internal stack traces.

10. **Optional minimal web integration**
    - Only if there is already a clear UI pattern:
      - add a simple upload form/page in Blazor Web
      - fields: title, type, access scope, file
      - submit to the new API
      - show success/error state
    - If UI patterns are absent or this would expand scope, skip UI and keep the task backend-complete.

11. **Testing**
    - Add or update tests for:
      - validation rules
      - tenant scoping
      - successful upload flow
      - unsupported file rejection
      - storage failure leading to failed status
      - persistence of metadata and access scope
    - Prefer unit tests for handlers/validators and integration tests for API + persistence if the repo supports them.

12. **Keep implementation future-ready**
    - Leave clear extension points for:
      - virus scanning
      - background ingestion processing
      - chunking/embedding event trigger
      - richer access-scope enforcement in retrieval
    - Do not implement ST-302, but make ST-301 naturally feed it.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run:
   - `dotnet build`
   - `dotnet test`

4. If migrations are used in this repo:
   - generate/apply the migration for document metadata changes
   - verify the `knowledge_documents` table/schema matches the intended fields

5. Manually verify the API:
   - upload a supported file with valid title, type, and access scope
   - confirm:
     - file stored in object storage
     - metadata row created in PostgreSQL
     - `company_id` is set correctly
     - ingestion status is set appropriately
   - upload an unsupported file type and confirm actionable validation/error response
   - simulate storage failure if feasible and confirm document status becomes `failed`

6. Verify tenant isolation:
   - ensure one company cannot access or mutate another company’s document metadata through the endpoint/query path

7. If a web UI was added:
   - manually test form submission and error display in the Blazor app

# Risks and follow-ups
- **Unknown existing patterns:** The repo may already contain partial document, storage, or tenant abstractions. Reuse them rather than creating duplicates.
- **Multipart handling differences:** API conventions may prefer controllers, minimal APIs, or MediatR-style endpoints; follow the established style.
- **Object storage availability:** Local/dev configuration may not have real object storage. If needed, support a local/dev stub implementation behind the same abstraction.
- **Schema ambiguity:** The architecture shows `knowledge_documents`, but the actual repo may not yet have this table. Add only the minimal schema needed for ST-301.
- **Access scope design:** Keep it structured and tenant-aware, but avoid over-designing authorization semantics that belong more fully to retrieval and policy stories.
- **Status transitions:** Without the ST-302 processing worker, avoid pretending documents are fully processed. Use honest lifecycle states.
- **Follow-up stories likely needed:**
  - trigger asynchronous ingestion after upload
  - chunking and embeddings
  - document listing/detail endpoints
  - delete/archive/re-upload/versioning
  - virus scanning integration
  - richer audit events and UI management screens