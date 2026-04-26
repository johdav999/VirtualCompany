# Goal
Implement backlog task **TASK-30.2.2 — Create snapshot storage for candidate emails and attachments with hash-based deduplication** for story **US-30.2 Build bill detection and attachment classification pipeline for untrusted email input**.

The coding agent should add the backend/domain/infrastructure support needed to:

- detect likely bill candidate emails using deterministic rules only
- persist **EmailMessageSnapshot** and **EmailAttachmentSnapshot** records only for candidate messages
- classify candidate source types as:
  - text-based PDF attachment
  - DOCX attachment
  - email body-only invoice
- store only minimal required snapshot data and attachment metadata
- deduplicate attachments by content hash
- ensure extracted text is explicitly treated as **untrusted data**
- exclude non-candidates from downstream extraction while still counting them in ingestion metrics

Keep the implementation aligned with the existing modular monolith, .NET backend, PostgreSQL persistence, and tenant-scoped data model.

# Scope
In scope:

- Add/extend domain entities and enums for:
  - email message snapshots
  - email attachment snapshots
  - candidate classification/source type
  - candidate decision metadata
  - ingestion metrics/status fields as needed
- Add EF Core/PostgreSQL persistence and migrations for snapshot tables
- Implement deterministic candidate detection rules combining:
  - sender/domain matching
  - folder/label filtering
  - keyword matching
  - attachment presence
- Implement attachment hash-based deduplication behavior
- Persist only candidate snapshots, not full mailbox content
- Store:
  - tenant/company reference
  - external message/reference identifiers
  - sender metadata
  - subject
  - selected body text / extracted text needed for downstream processing
  - attachment metadata
  - MIME type
  - content hash
  - reference identifiers
- Mark extracted text and body-derived text as untrusted in code and schema semantics
- Ensure non-candidates are excluded from downstream extraction/classification pipeline
- Add/update tests for candidate detection, deduplication, and persistence behavior

Out of scope unless required by existing code paths:

- full mailbox sync implementation
- OCR for image PDFs
- support for XLS/XLSX or image attachments
- LLM-based candidate detection
- policy/approval workflow changes beyond explicit untrusted-data guardrails
- UI/mobile work

# Files to touch
Inspect the solution first and adapt to actual project structure, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - new snapshot entities/value objects/enums
  - domain constants for untrusted document handling
- `src/VirtualCompany.Application/**`
  - candidate detection service
  - snapshot persistence orchestration
  - DTOs/contracts for email ingestion pipeline
  - downstream gating logic so only candidates proceed
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - DbContext updates
  - repository implementations
  - hashing service / attachment dedup support
  - migration files
- `src/VirtualCompany.Api/**`
  - DI registration if needed
  - worker/job/webhook pipeline wiring if candidate ingestion enters here
- `tests/**`
  - unit tests for deterministic rules
  - unit tests for hash deduplication
  - integration tests for persistence and candidate-only snapshot creation

Also review:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`

to follow existing migration and project conventions.

# Implementation plan
1. **Discover current email ingestion pipeline**
   - Search for existing modules related to:
     - email integrations
     - inbox processing
     - attachment ingestion
     - background workers
     - document extraction
   - Identify current abstractions for:
     - external message DTOs
     - tenant/company scoping
     - persistence patterns
     - metrics collection
   - Reuse existing naming and boundaries rather than inventing a parallel subsystem.

2. **Design the snapshot model**
   Add domain models for candidate-only snapshots. Prefer explicit, minimal entities such as:

   - `EmailMessageSnapshot`
     - `Id`
     - `CompanyId`
     - `ExternalProvider`
     - `ExternalMessageId`
     - `ThreadId` or equivalent external conversation ref if available
     - `MailboxRef` / account ref if available
     - `SenderEmail`
     - `SenderDomain`
     - `FromDisplayName`
     - `Subject`
     - `ReceivedAt`
     - `FolderName` / normalized folder
     - `LabelsJson` or normalized related table if project conventions prefer
     - `BodyTextSnapshot` or `ExtractedBodyText`
     - `CandidateDecision`
     - `CandidateReasonFlags`
     - `SourceType`
     - `HasAttachments`
     - `UntrustedText`
     - timestamps

   - `EmailAttachmentSnapshot`
     - `Id`
     - `CompanyId`
     - `EmailMessageSnapshotId`
     - `ExternalAttachmentId` or provider ref
     - `FileName`
     - `MimeType`
     - `FileSizeBytes`
     - `ContentHash`
     - `StorageRef` or object ref only if current architecture stores candidate attachment payloads externally
     - `ExtractedText`
     - `SourceType`
     - `IsDuplicateByHash`
     - `CanonicalAttachmentSnapshotId` or equivalent optional self-reference if useful
     - timestamps

   Add enums/value objects as appropriate:
   - `EmailCandidateDecision` (`Candidate`, `NotCandidate`)
   - `BillCandidateSourceType` (`PdfTextAttachment`, `DocxAttachment`, `EmailBodyOnly`)
   - flags/reasons for deterministic matching:
     - sender/domain match
     - folder/label match
     - keyword match
     - attachment present

   Important:
   - Do **not** persist unnecessary full mailbox content, raw MIME blobs, or unrelated message bodies.
   - If body text is stored, store only the minimal normalized text needed for extraction and audit of candidate handling.

3. **Define deterministic candidate rules**
   Implement a pure, testable application/domain service, e.g. `BillCandidateDetectionService`, that accepts normalized email input and returns a structured decision.

   Rules must combine deterministic signals:
   - sender/domain matching
   - folder or label filtering
   - keyword matching
   - attachment presence

   Suggested behavior:
   - Candidate if deterministic rule threshold is met by approved combinations, not by probabilistic scoring.
   - Support body-only invoices as a separate source type when:
     - sender/domain and/or folder/label and keyword rules match
     - no supported attachment exists
     - body contains invoice/bill indicators
   - Support attachment-based candidates when:
     - candidate rules match
     - attachment exists with supported MIME/extension for text PDF or DOCX

   Return a structured result containing:
   - `IsCandidate`
   - matched reasons
   - selected `SourceType` or source types
   - whether downstream extraction is allowed

   Keep this logic deterministic and free of LLM/model dependencies.

4. **Implement supported source type classification**
   Add explicit classification for:
   - text-based PDF attachments
   - DOCX attachments
   - email body-only invoices

   Use MIME type plus filename extension fallback where needed.
   Do not add OCR/image-PDF support unless already present.
   If a message has multiple supported attachments, snapshot each candidate attachment and classify each appropriately.

5. **Implement hash-based attachment deduplication**
   Add a hashing service in infrastructure/application:
   - compute a stable content hash from attachment bytes using a standard algorithm already used in the codebase, otherwise SHA-256
   - deduplicate by at least:
     - `CompanyId`
     - `ContentHash`
   - If same hash already exists for the tenant, avoid redundant attachment payload persistence
   - Still allow a new `EmailAttachmentSnapshot` record to reference the same canonical content if the same attachment appears on another candidate message, if that best fits the model

   Prefer this pattern:
   - snapshot row per candidate message attachment occurrence
   - optional shared content/canonical reference for deduped binary/text payload storage

   If introducing a separate canonical blob/content table is too large for this task, implement deduplication at snapshot persistence level with a unique or indexed hash strategy and clear duplicate markers.

6. **Persist candidate snapshots only**
   Update the ingestion pipeline so that:
   - all incoming messages can be evaluated for metrics
   - only candidate messages create `EmailMessageSnapshot`
   - only candidate supported attachments create `EmailAttachmentSnapshot`
   - non-candidates do not create snapshot records and do not proceed to extraction

   Ensure downstream extraction/classification stages are gated on `IsCandidate == true`.

7. **Handle untrusted extracted text explicitly**
   The acceptance criteria require that all extracted document text is stored and processed as untrusted data and must not alter workflow policy or approval behavior.

   Reflect this in implementation by:
   - naming fields/services clearly, e.g. `UntrustedExtractedText`
   - adding comments/docs where appropriate
   - ensuring policy/approval engines do not consume extracted text as configuration or authority
   - keeping candidate detection deterministic and config-driven, not text-authoritative
   - avoiding any code path where extracted invoice text can mutate thresholds, permissions, or approval routing

   If there is an existing workflow/policy subsystem integration, add guard tests to prove extracted text cannot override policy decisions.

8. **Add persistence configuration and migration**
   - Update DbContext
   - Add EF configurations with indexes such as:
     - message lookup by `CompanyId + ExternalProvider + ExternalMessageId`
     - attachment lookup by `CompanyId + ContentHash`
     - foreign key from attachment snapshot to message snapshot
   - Add migration following repository conventions
   - Keep schema tenant-aware with `CompanyId` on all tenant-owned records

9. **Add ingestion metrics behavior**
   Ensure metrics distinguish:
   - total messages ingested/evaluated
   - candidate messages
   - non-candidate messages excluded
   - candidate attachments snapshotted
   - deduplicated attachments

   Reuse existing metrics/audit patterns if present. If no metrics abstraction exists, add the smallest internal counter/reporting structure needed by current pipeline code.

10. **Test thoroughly**
   Add tests covering at minimum:

   **Candidate detection**
   - sender/domain + keyword + attachment => candidate
   - folder/label + keyword + no attachment but invoice-like body => body-only candidate
   - keyword only without other required signals => not candidate if rules require stronger combination
   - unsupported attachment type => not classified as supported attachment source
   - non-candidate excluded from downstream extraction

   **Deduplication**
   - same attachment bytes on two candidate messages in same tenant => same hash / dedup behavior
   - same bytes across different tenants => no cross-tenant dedup coupling
   - duplicate attachment metadata with different bytes => different hash

   **Persistence**
   - candidate message creates message snapshot
   - candidate supported attachments create attachment snapshots
   - non-candidate creates no snapshot rows
   - stored fields exclude unnecessary full mailbox content
   - extracted text fields are marked/handled as untrusted

   **Guardrail behavior**
   - extracted text cannot influence policy/approval configuration or execution path

11. **Keep implementation incremental and idiomatic**
   - Follow existing namespace, folder, and DI conventions
   - Avoid speculative abstractions beyond what this task needs
   - Prefer small composable services with unit-testable rule logic
   - Document any assumptions in code comments and PR-style notes if needed

# Validation steps
1. Restore/build solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of normal workflow, generate/apply or verify migration compiles according to repo conventions.

4. Manually verify code paths:
   - candidate message with supported PDF attachment creates message + attachment snapshot
   - candidate message with DOCX attachment creates message + attachment snapshot
   - candidate body-only invoice creates message snapshot with body-only source type and no attachment snapshot
   - non-candidate message increments ingestion metrics only and does not create snapshots
   - duplicate attachment content in same tenant results in same hash and dedup behavior

5. Review schema/configuration for:
   - tenant scoping on all new tables
   - indexes for external refs and content hash
   - no raw full-mailbox persistence beyond minimal snapshot fields
   - explicit untrusted text handling

6. Include a concise implementation summary in your final output:
   - files changed
   - migration added
   - rule logic implemented
   - tests added
   - any assumptions or follow-up gaps

# Risks and follow-ups
- The repository may already contain partial email ingestion models; avoid duplicating concepts and instead extend existing ones.
- Candidate rule thresholds may not yet be formally defined in config; if missing, implement a conservative deterministic baseline and clearly document it.
- Attachment deduplication may be cleaner with a separate canonical content table, but that may exceed this task; choose the smallest design that still satisfies acceptance criteria.
- If current extraction pipeline assumes all ingested messages proceed downstream, gating changes may affect existing tests and worker flows.
- MIME detection from provider metadata may be unreliable; use extension fallback carefully and test both.
- Be careful not to store excessive body/raw message content while still preserving enough text for candidate extraction.
- If policy/approval code is not directly connected yet, add explicit tests or code comments to enforce the untrusted-data boundary now.
- Follow-up tasks may be needed for:
  - OCR/image PDF support
  - richer provider-specific folder/label normalization
  - canonical attachment content store
  - operational dashboards for ingestion metrics