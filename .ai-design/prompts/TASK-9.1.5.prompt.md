# Goal
Implement backlog task **TASK-9.1.5 — Start with common text/PDF/doc formats only** for **ST-301 Company document ingestion and storage** in the existing .NET solution.

The coding agent should update the document upload/ingestion flow so that the system **explicitly allows only a small initial set of common document formats** and **rejects unsupported formats with actionable validation/error messaging**.

This task should align with the story intent:
- users can upload company knowledge documents
- files are stored in object storage
- metadata is stored in PostgreSQL
- ingestion status is tracked
- unsupported files surface clear error states

Because no explicit acceptance criteria were provided for this task, infer a pragmatic v1 implementation that is safe, testable, and consistent with the architecture and backlog notes.

# Scope
In scope:
- Restrict upload acceptance to a defined allowlist of common formats for v1.
- Apply validation at the API/application boundary before storage/ingestion proceeds.
- Ensure the allowed formats are represented in a reusable place, not hardcoded in multiple layers.
- Return clear user-facing validation errors for unsupported file types.
- If document records are created before deep processing, ensure unsupported files end in a clear failed/rejected state or are blocked before persistence, depending on the current flow.
- Update any DTOs, validators, services, and UI hints that expose supported file types.
- Add or update tests covering allowed and rejected formats.

Recommended initial allowlist for this task:
- `.txt`
- `.md`
- `.pdf`
- `.doc`
- `.docx`

If the existing code already has a document parser pipeline, do **not** add support for more formats in this task. Keep the implementation intentionally narrow.

Out of scope:
- Full content extraction/chunking/embedding work from ST-302
- Virus scanning implementation beyond preserving/extending hooks
- MIME sniffing or advanced file signature validation unless already present and easy to extend
- OCR, spreadsheets, presentations, images, archives, email files, or rich media
- Broad refactors unrelated to upload validation

# Files to touch
Inspect the solution first, then touch only the relevant files. Likely areas include:

- `src/VirtualCompany.Api/...`
  - upload endpoint/controller/minimal API for knowledge documents
  - request models or endpoint filters
- `src/VirtualCompany.Application/...`
  - commands/handlers for document upload
  - validators
  - ingestion orchestration service interfaces
- `src/VirtualCompany.Domain/...`
  - document entity/value objects/enums/policies for supported formats
- `src/VirtualCompany.Infrastructure/...`
  - object storage upload service
  - ingestion pipeline implementation
  - parser/format resolver
- `src/VirtualCompany.Web/...`
  - upload form UI hints, accepted extensions, validation messaging
- tests in the corresponding test projects, if present

Also update:
- `README.md` only if there is already a section documenting supported upload formats or local testing behavior

Prefer adding a single reusable source of truth, such as:
- `SupportedDocumentFormats`
- `DocumentFormatPolicy`
- `KnowledgeDocumentFileRules`

# Implementation plan
1. **Inspect the current upload and ingestion flow**
   - Find where ST-301 is currently implemented:
     - API endpoint for upload
     - application command/handler
     - document metadata persistence
     - object storage write
     - ingestion status transitions
   - Determine whether validation currently happens:
     - in UI only
     - in API only
     - in application layer
     - not at all
   - Identify whether the system stores:
     - original filename
     - extension
     - content type
     - document type metadata
     - ingestion failure reason

2. **Introduce a single supported-format policy**
   - Add a reusable policy/value object in Domain or Application, depending on current architecture conventions.
   - It should expose:
     - allowed extensions
     - optional allowed MIME types if the codebase already uses them
     - a method to validate a filename/content type pair
   - Normalize extension comparison to be case-insensitive.
   - Recommended allowlist:
     - `.txt`
     - `.md`
     - `.pdf`
     - `.doc`
     - `.docx`

3. **Enforce validation at the server boundary**
   - Update the upload command/handler or endpoint validation so unsupported files are rejected before expensive work.
   - Prefer application-layer enforcement even if UI validation exists.
   - Return a clear error message such as:
     - “Unsupported file format. Supported formats: .txt, .md, .pdf, .doc, .docx.”
   - If the current architecture uses FluentValidation or similar, add field-level validation there.
   - If the endpoint accepts multiple files, validate each file independently and return structured errors.

4. **Align persistence and ingestion behavior**
   - If unsupported files are blocked before document creation, ensure no object storage upload or ingestion job is started.
   - If the current flow creates a document record first, mark it with an appropriate failure/rejected status and actionable reason.
   - Reuse existing ingestion status values where possible; do not invent a large new state model unless necessary.
   - Ensure tenant-aware metadata handling remains unchanged.

5. **Update UI upload hints**
   - In the Blazor web upload form, set the file input `accept` attribute if applicable.
   - Show supported formats in helper text or validation summary.
   - Do not rely on client-side filtering alone.

6. **Keep parser/ingestion pipeline narrow**
   - If there is a format resolver/parser factory, make sure only the allowed formats are wired for v1.
   - For unsupported formats reaching deeper layers unexpectedly, fail safely with a clear exception/result that maps to a user-safe error.
   - Preserve any virus scanning hook points in the pipeline design.

7. **Add tests**
   - Add unit tests for the supported-format policy:
     - accepts `.txt`, `.md`, `.pdf`, `.doc`, `.docx`
     - rejects unsupported extensions such as `.xls`, `.xlsx`, `.pptx`, `.png`, `.zip`
     - handles uppercase extensions like `.PDF` and `.DOCX`
     - rejects missing/empty filenames or filenames without valid extension if applicable
   - Add application/API tests for upload behavior:
     - supported file proceeds successfully
     - unsupported file returns validation error / bad request
     - unsupported file does not trigger storage or ingestion side effects
   - If UI tests exist, update them minimally to reflect the new accepted formats text.

8. **Keep implementation consistent with architecture**
   - Respect modular monolith boundaries.
   - Keep tenant scoping intact.
   - Do not let infrastructure-specific checks leak everywhere.
   - Prefer CQRS-lite command validation patterns already used in the solution.

9. **Document assumptions in code comments only where needed**
   - If MIME validation is weak because browser-provided content types are unreliable, note that extension allowlisting is the v1 gate and stronger file signature validation can follow later.
   - Keep comments concise.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify upload behavior through the implemented API/UI flow:
   - upload a `.pdf` file → should succeed
   - upload a `.docx` file → should succeed
   - upload a `.txt` file → should succeed
   - upload a `.xlsx` file → should be rejected with clear supported-format messaging
   - upload a `.png` file → should be rejected with clear supported-format messaging

4. Verify no unintended side effects for rejected files:
   - no object storage upload occurs
   - no ingestion job is queued
   - no document record is created, unless the existing design intentionally records failed attempts

5. Verify any persisted status/error behavior:
   - if a record is created before validation completion, confirm unsupported files surface a failed/rejected state with actionable reason

6. Verify UI hints if applicable:
   - upload control shows the expected accepted formats
   - helper text/error text matches server behavior

# Risks and follow-ups
- **Risk: extension-only validation is imperfect.**
  - For v1 this is acceptable, but follow up with MIME/file signature validation if security requirements increase.

- **Risk: browser-provided content types may be inconsistent.**
  - Do not rely solely on MIME type unless the current codebase already has a robust pattern.

- **Risk: parser support may not match the allowlist exactly.**
  - Ensure `.doc`/`.docx` are only allowed if the current ingestion pipeline can safely handle them or intentionally stores them for later processing under ST-302/ST-301 flow. If parser support is absent, still keep the upload contract aligned with actual capability and fail clearly.

- **Risk: validation duplicated across UI/API/application.**
  - Use one reusable source of truth and keep UI as advisory, server as authoritative.

Follow-ups after this task:
- add stronger file signature validation
- add virus scanning integration hook implementation
- expand supported formats later to spreadsheets/presentations/images only when extraction pipeline support exists
- persist explicit ingestion failure reasons if not already modeled
- centralize upload constraints in configuration/options if product wants admin-tunable policies later