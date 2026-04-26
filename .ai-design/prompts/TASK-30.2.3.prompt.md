# Goal

Implement backlog task **TASK-30.2.3 — Add attachment and body source-type classification for PDF, DOCX, and email-body invoices** for story **US-30.2 Build bill detection and attachment classification pipeline for untrusted email input**.

Deliver a production-ready implementation in the existing **.NET modular monolith** that:

- Adds deterministic **likely bill candidate detection** for inbound email messages.
- Creates `EmailMessageSnapshot` and `EmailAttachmentSnapshot` records **only** for messages that pass candidate rules.
- Classifies invoice source types as:
  - **PDF attachment**
  - **DOCX attachment**
  - **Email body-only**
- Stores only the minimum required mailbox-derived data:
  - message/attachment reference identifiers
  - metadata
  - MIME type
  - content hash
  - extracted text needed for downstream processing
- Treats all extracted text as **untrusted data** that must **never** influence workflow policy, approval thresholds, or approval behavior.
- Excludes non-candidates from downstream extraction while still counting them in ingestion metrics.

Use deterministic, testable application/domain logic. Do not introduce LLM-based classification for this task.

# Scope

In scope:

- Domain/application/infrastructure changes needed to support:
  - candidate detection rules
  - source-type classification
  - snapshot persistence for candidate messages only
  - attachment metadata persistence
  - extracted text persistence as untrusted content
  - ingestion metrics for both candidate and non-candidate messages
- Database schema/migrations for any new tables/columns/enums needed.
- Background worker or pipeline updates for inbox/email ingestion flow.
- Unit/integration tests covering acceptance criteria.

Out of scope unless required by existing code structure:

- Full OCR support for image PDFs.
- New UI screens beyond minimal diagnostics already exposed by existing APIs.
- Changing approval/policy engine behavior beyond explicitly preventing untrusted extracted text from affecting policy inputs.
- Persisting full raw mailbox bodies or full attachment binaries in SQL if current architecture already uses object storage or transient processing.

Implementation constraints:

- Follow the architecture: **ASP.NET Core modular monolith**, **PostgreSQL**, background workers, tenant-scoped processing.
- Keep mailbox content minimization explicit.
- Prefer strongly typed enums/value objects over magic strings where patterns already exist.
- Preserve tenant isolation on all persisted records.
- Keep the pipeline idempotent where possible.

# Files to touch

Inspect the solution first and then update the most relevant files in these areas.

Likely projects:

- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:

1. **Domain**
   - Email/inbox/bill-detection entities, enums, value objects, and policies
   - Add or extend:
     - `EmailMessageSnapshot`
     - `EmailAttachmentSnapshot`
     - source-type enum/classification model
     - candidate detection result model
     - untrusted text marker/flag model if needed

2. **Application**
   - Inbox ingestion commands/handlers/services
   - Bill candidate detection service
   - Attachment/body classification service
   - DTOs/contracts for snapshot persistence
   - Metrics recording interfaces/events

3. **Infrastructure**
   - EF Core entity configurations
   - repositories
   - migrations
   - email provider adapter mapping
   - document text extraction services for:
     - text-based PDF
     - DOCX
     - email body
   - hashing utilities
   - object storage references if attachments are temporarily staged there

4. **API / worker host**
   - DI registration
   - background job wiring
   - any ingestion endpoints/webhook handlers if they participate in the pipeline

5. **Tests**
   - unit tests for deterministic candidate rules
   - unit tests for source-type classification
   - integration tests for persistence behavior
   - tests proving non-candidates do not create snapshots
   - tests proving untrusted extracted text does not feed policy decisions

Also inspect:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`

to align with repo conventions for migrations and local validation.

# Implementation plan

1. **Discover the existing email ingestion and bill-processing flow**
   - Find current modules/services/entities related to:
     - inbox processing
     - email sync/webhook ingestion
     - attachment handling
     - bill/invoice extraction
     - metrics/telemetry
   - Identify where candidate filtering should happen so that non-candidates are stopped before snapshot creation and downstream extraction.
   - Identify whether `EmailMessageSnapshot` / `EmailAttachmentSnapshot` already exist; if they do, extend rather than duplicate.

2. **Model deterministic candidate detection**
   - Implement a deterministic rule evaluator that combines the required signals:
     - sender/domain matching
     - folder or label filtering
     - keyword matching
     - attachment presence
   - Make the evaluator return a structured result, e.g.:
     - `IsCandidate`
     - matched rules/reasons
     - confidence/category if already modeled, but deterministic only
   - Ensure the logic requires a combination of signals, not a single weak signal.
   - Keep rules configurable only if the existing architecture already supports config; otherwise implement sensible defaults in a clearly isolated policy class.

3. **Add source-type classification**
   - Introduce a source-type enum/value such as:
     - `PdfAttachment`
     - `DocxAttachment`
     - `EmailBody`
   - Classification rules:
     - If candidate has supported text-based PDF attachment(s), classify those attachments as `PdfAttachment`.
     - If candidate has DOCX attachment(s), classify those as `DocxAttachment`.
     - If candidate has no supported invoice attachment but body content qualifies for invoice extraction, classify as `EmailBody`.
   - If multiple supported sources exist, preserve enough metadata to process each candidate source deterministically. If the current downstream model expects one primary source, choose a deterministic precedence and document it in code/tests.

4. **Persist snapshots only for candidates**
   - Update the ingestion pipeline so `EmailMessageSnapshot` and `EmailAttachmentSnapshot` are created only after candidate detection passes.
   - For non-candidates:
     - do not create snapshot records
     - do not enqueue downstream extraction
     - do increment ingestion metrics
   - Ensure idempotency if the same message is seen twice.

5. **Minimize persisted mailbox content**
   - Persist only necessary message-level fields, for example:
     - tenant/company id
     - provider message reference/id
     - thread/conversation reference if already used
     - sender metadata
     - subject
     - received timestamp
     - folder/label metadata
     - candidate classification metadata
   - Persist only necessary attachment-level fields, for example:
     - provider attachment reference/id
     - filename
     - MIME type
     - size
     - content hash
     - source type
     - storage/object reference if used
   - Do **not** persist unnecessary full mailbox content. If body text or attachment text is needed for extraction, store the extracted text in the designated untrusted-text field/table only.

6. **Store extracted text as untrusted**
   - Add explicit storage and handling for extracted document text from:
     - PDF text extraction
     - DOCX text extraction
     - email body normalization
   - Mark or model this text as **untrusted** in a way that is obvious in code and persistence.
   - Ensure downstream consumers cannot accidentally use this text to alter:
     - workflow policy
     - approval thresholds
     - approval routing
     - approval decisions
   - If there is an existing policy engine input model, make sure extracted text is excluded from policy input contracts.

7. **Implement extraction support**
   - PDF:
     - support text-based PDFs only
     - if extraction service can detect image-only/no-text PDFs, mark unsupported or no-text accordingly without pretending success
   - DOCX:
     - extract text from `.docx`
   - Email body:
     - normalize plain text / HTML body into extracted text suitable for downstream invoice extraction
   - Compute and persist content hashes for supported sources.

8. **Record metrics**
   - Add or extend ingestion metrics to count at minimum:
     - total messages ingested
     - candidate messages
     - non-candidate messages
     - candidate messages by source type
     - unsupported/failed extraction cases if metrics infrastructure already exists
   - Ensure non-candidates are counted in metrics only and excluded from downstream extraction.

9. **Database changes**
   - Add/modify EF entities and migrations for:
     - source type
     - candidate classification metadata
     - attachment metadata and content hash
     - extracted untrusted text storage
     - reference identifiers
   - Follow repo migration conventions from the docs.
   - Keep schema tenant-aware.

10. **Tests**
    - Add focused unit tests for candidate detection combinations:
      - sender/domain + keyword + attachment
      - folder/label + keyword + attachment
      - sender/domain only should not pass if rules require more signals
      - keyword only should not pass
      - attachment only should not pass
    - Add classification tests:
      - PDF attachment candidate => `PdfAttachment`
      - DOCX attachment candidate => `DocxAttachment`
      - body-only invoice candidate => `EmailBody`
      - unsupported attachment types are not classified as supported source types
    - Add persistence/integration tests:
      - candidate creates message + attachment snapshots
      - non-candidate creates no snapshots
      - metadata/hash/MIME/reference ids are stored
      - unnecessary full mailbox content is not stored
    - Add safety tests:
      - extracted text is marked/stored as untrusted
      - policy/approval evaluation ignores extracted text fields

11. **Keep implementation clean**
    - Prefer small, composable services:
      - `IEmailBillCandidateDetector`
      - `IInvoiceSourceTypeClassifier`
      - `IAttachmentTextExtractor`
      - `IEmailBodyTextExtractor`
      - `IContentHashService`
    - Keep orchestration in the application layer and provider/file-format specifics in infrastructure.
    - Add comments only where needed to explain security-sensitive decisions around untrusted data.

# Validation steps

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of the repo workflow, generate/apply them per repo convention and verify they compile cleanly.

4. Manually verify with representative test fixtures or existing integration tests:
   - Message matching deterministic candidate rules with PDF attachment:
     - snapshots created
     - source type = PDF
     - metadata/hash/MIME/reference ids stored
   - Message matching deterministic candidate rules with DOCX attachment:
     - snapshots created
     - source type = DOCX
   - Message with invoice details in body and no supported attachment:
     - candidate accepted if rules allow
     - source type = EmailBody
   - Message failing candidate rules:
     - no snapshots
     - no downstream extraction
     - ingestion metrics incremented
   - Extracted text handling:
     - text persisted only in untrusted-text storage/fields
     - no policy/approval path consumes it as policy input

5. Confirm no unnecessary mailbox content persistence:
   - inspect entity mappings and tests to ensure raw full mailbox body/content is not stored unless explicitly required and minimized
   - if body text is stored for extraction, ensure it is normalized extracted text and marked untrusted

6. Confirm idempotency/replay behavior if the same provider message is processed more than once.

# Risks and follow-ups

- **Ambiguity in existing email domain model**: if inbox processing is only partially implemented, you may need to add minimal supporting abstractions. Keep changes narrowly scoped to this task.
- **PDF extraction library limitations**: support text-based PDFs only; do not add OCR in this task.
- **Policy contamination risk**: the most important safety requirement is preventing untrusted extracted text from influencing policy/approval behavior. Be explicit in code boundaries and tests.
- **Snapshot minimization**: avoid the easy path of storing raw full message bodies or full attachments in SQL. Prefer metadata, hashes, references, and extracted text only where needed.
- **Multiple candidate sources in one message**: if the current downstream pipeline only supports one source, implement deterministic precedence and leave a clear TODO/follow-up for multi-source extraction orchestration.
- **Metrics shape may be immature**: if no structured ingestion metrics exist yet, add the smallest viable counters/hooks and note any richer observability as follow-up.
- **Follow-up candidates**:
  - OCR/image-PDF support
  - richer admin-configurable candidate rules
  - quarantine/review flow for ambiguous messages
  - provider-specific folder/label normalization
  - explicit audit events for candidate detection outcomes