# Goal
Implement `BillDetectionService` for `TASK-30.2.1` to classify inbound email messages as likely bill candidates using deterministic heuristics, and to create `EmailMessageSnapshot` and `EmailAttachmentSnapshot` records only for qualifying candidates.

This work supports `US-30.2 Build bill detection and attachment classification pipeline for untrusted email input` and must preserve the security boundary that all extracted email/document text is treated as untrusted data.

Key outcomes:
- Deterministic candidate detection based on sender/domain, folder/label, keyword, and attachment heuristics.
- Support separate source types for:
  - text-based PDF attachments
  - DOCX attachments
  - email body-only invoices
- Persist only minimal mailbox snapshot data and attachment metadata needed for downstream extraction.
- Exclude non-candidates from downstream extraction while still counting them in ingestion metrics.
- Ensure extracted text cannot influence workflow policy, approval behavior, or execution guardrails.

# Scope
In scope:
- Add or complete a domain/application service named `BillDetectionService`.
- Define deterministic bill-candidate rules and result model.
- Add persistence flow for:
  - `EmailMessageSnapshot`
  - `EmailAttachmentSnapshot`
- Add attachment/source-type classification logic.
- Add ingestion metric updates for candidate vs non-candidate outcomes.
- Add tests covering acceptance criteria and edge cases.
- Wire the service into the inbox/email ingestion pipeline if a suitable integration point already exists.

Out of scope:
- LLM-based classification.
- OCR for image PDFs or scanned documents unless existing infrastructure already supports it.
- Changing workflow policy, approval logic, or orchestration behavior.
- Persisting full raw mailbox content beyond minimal snapshot/reference requirements.
- Broad refactors outside the email ingestion/bill pipeline area.

Implementation constraints:
- Follow modular monolith boundaries.
- Keep logic deterministic and testable.
- Treat all extracted body/attachment text as untrusted content.
- Prefer minimal persistence: metadata, hashes, MIME type, references, extracted text needed for downstream extraction, but not unnecessary full mailbox payloads.

# Files to touch
Inspect the solution and update the actual files that match these responsibilities. Likely areas:

- `src/VirtualCompany.Domain/**`
  - bill detection value objects/enums/results
  - snapshot entities if not already present
  - source type enum(s)
- `src/VirtualCompany.Application/**`
  - `BillDetectionService`
  - interfaces/contracts for email ingestion and snapshot persistence
  - command/handler or pipeline orchestration integration
  - ingestion metrics updates
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations/mappings
  - repositories for message/attachment snapshots
  - content hashing utility
  - MIME/attachment inspection helpers
  - text extraction adapters for PDF/DOCX if already available
- `src/VirtualCompany.Api/**`
  - only if DI registration or worker pipeline wiring lives here
- `tests/**`
  - unit tests for heuristic classification
  - integration tests for persistence behavior and exclusion of non-candidates

Also inspect for existing equivalents before creating new files:
- email ingestion pipeline classes
- mailbox sync/inbox processor workers
- snapshot entities
- metrics abstractions
- attachment/document extraction services

# Implementation plan
1. **Discover existing email ingestion and snapshot model**
   - Search for:
     - email/mailbox/inbox ingestion services
     - snapshot entities
     - attachment metadata persistence
     - document extraction services
     - metrics counters
   - Reuse existing abstractions where possible.
   - If `EmailMessageSnapshot` / `EmailAttachmentSnapshot` do not exist, add them in the appropriate domain/infrastructure layers with tenant-aware persistence.

2. **Define deterministic detection contract**
   - Create a clear result model, e.g.:
     - `IsCandidate`
     - `MatchedRules`
     - `SourceType` or `DetectedSourceTypes`
     - `ReasonSummary`
     - `CandidateAttachments`
   - Add enums/value objects for:
     - `BillSourceType` = `PdfAttachment`, `DocxAttachment`, `EmailBodyOnly`
     - optional rule match types such as `SenderMatch`, `FolderMatch`, `KeywordMatch`, `AttachmentPresent`
   - Keep the service pure/deterministic where possible.

3. **Implement heuristic rules**
   - Candidate detection must combine deterministic rules across these dimensions:
     - sender/domain matching
     - folder or label filtering
     - keyword matching
     - attachment presence
   - Implement a conservative rule set such as:
     - candidate if trusted/known billing sender or billing-like domain AND invoice/bill folder/label
     - candidate if billing keywords present AND supported attachment exists
     - candidate if billing keywords present in body/subject and no attachment but body strongly indicates invoice/bill
   - Use explicit keyword lists for:
     - invoice, bill, statement, payment due, amount due, remit, remittance, due date, account statement, receipt/invoice number
   - Use explicit folder/label indicators for:
     - invoices, bills, finance, accounting, ap, accounts payable
   - Use sender/domain indicators from configured or static deterministic matchers already present in the system; if no config exists, encapsulate defaults so they can be externalized later.
   - Ensure unsupported attachments do not qualify as supported source types.

4. **Implement attachment classification**
   - For each attachment, determine:
     - filename
     - MIME type
     - size
     - content hash
     - reference identifier/source identifier
     - supported type classification
   - Supported source types:
     - text-based PDF attachments
     - DOCX attachments
     - email body-only invoices
   - For PDFs:
     - only classify as supported if text extraction indicates text-based content, not image-only/scanned content.
     - If existing extractor can detect extractable text, use that.
   - For DOCX:
     - classify as supported when MIME/extension and extraction are valid.
   - For body-only:
     - classify only when no qualifying attachment is present and body/subject heuristics meet threshold.

5. **Persist snapshots only for candidates**
   - Enforce acceptance criterion strictly:
     - create `EmailMessageSnapshot` and `EmailAttachmentSnapshot` only when `IsCandidate == true`
   - Snapshot should store minimal required data:
     - tenant/company id
     - external message reference/id
     - sender/from metadata
     - subject
     - received timestamp
     - folder/label metadata
     - message/body reference identifiers
     - extracted body text if needed for downstream extraction
     - classification outcome/source type
   - Attachment snapshot should store:
     - external attachment reference/id
     - message snapshot FK
     - filename
     - MIME type
     - size
     - content hash
     - source type classification
     - extracted text if supported and needed
     - storage/reference identifier, not unnecessary raw mailbox blob
   - Do not persist unnecessary full mailbox content or raw MIME unless already required by an existing minimal-reference pattern.

6. **Enforce untrusted-data boundary**
   - Mark or document extracted body/attachment text as untrusted.
   - Ensure the service only outputs data for downstream document extraction/classification and not policy mutation.
   - Do not feed extracted text into approval thresholds, autonomy decisions, or workflow policy evaluation.
   - If there is a DTO/entity field for extracted text, add comments/tests/guardrails clarifying it is untrusted content only.

7. **Handle non-candidates**
   - For messages failing candidate rules:
     - do not create message or attachment snapshots
     - do increment ingestion metrics/counters
     - return a non-candidate result with reasons for observability/debugging
   - Keep downstream extraction from running for these messages.

8. **Integrate with ingestion pipeline**
   - Update the inbox processor/email ingestion flow so that:
     - raw message metadata enters the detection service
     - candidate result gates snapshot creation and downstream extraction enqueueing
     - non-candidates are short-circuited after metrics recording
   - Preserve idempotency if the pipeline may retry:
     - avoid duplicate snapshots for the same external message/attachment reference
     - use unique constraints or repository checks if patterns already exist

9. **Add tests**
   - Unit tests for deterministic rules:
     - sender/domain + folder/label + keyword + attachment combinations
     - supported PDF attachment candidate
     - supported DOCX attachment candidate
     - body-only invoice candidate
     - non-candidate with unrelated sender/keywords
     - unsupported attachment does not qualify
     - image/scanned/no-text PDF does not qualify as text-based PDF source type
   - Persistence/integration tests:
     - snapshots created only for candidates
     - attachment metadata/hash/MIME/reference persisted
     - non-candidates counted in metrics only
     - extracted text stored as untrusted content path only
     - downstream extraction not invoked for excluded messages

10. **Document assumptions in code**
   - Add concise comments near heuristics and trust boundaries.
   - If configuration is not yet available for sender/domain rules, leave a clear extension point for tenant-specific rule configuration in a future task.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify targeted scenarios with tests or existing integration harness:
   - Candidate with billing sender + invoice PDF attachment creates message and attachment snapshots.
   - Candidate with DOCX invoice attachment creates snapshots and classifies source type correctly.
   - Candidate with no attachment but invoice-like body creates only message snapshot with `EmailBodyOnly` source type.
   - Non-candidate message creates no snapshots and increments ingestion metrics only.
   - Attachment snapshot stores:
     - metadata
     - content hash
     - MIME type
     - external/reference identifiers
   - Unsupported or non-text attachment is excluded from supported source classification.
   - Extracted text is persisted only in the untrusted-content path and does not affect policy/approval logic.

4. If there are EF migrations involved:
   - generate or verify migration if schema changes are required
   - ensure mappings and constraints are valid
   - confirm no duplicate snapshot creation on retry/idempotent reprocessing

# Risks and follow-ups
- **Risk: missing existing email domain model**
  - The repository may not yet contain mailbox ingestion entities/services. If so, add the smallest coherent slice needed and avoid inventing a broad email subsystem.

- **Risk: PDF text detection capability may not exist**
  - If there is no current PDF text extractor, implement classification behind an interface and support only what can be deterministically verified now. Leave scanned/OCR support for a follow-up.

- **Risk: sender/domain heuristics may need tenant configurability**
  - Start with deterministic defaults and existing config sources if present, but isolate matcher logic so tenant-specific billing sender rules can be added later.

- **Risk: over-persisting mailbox content**
  - Be strict about storing references, metadata, hashes, and extracted text only where required. Avoid raw MIME/full body persistence unless already mandated by an existing pattern.

- **Risk: duplicate processing**
  - Retries from background workers may create duplicate snapshots unless uniqueness/idempotency is enforced on external message and attachment references.

Follow-up suggestions:
- Add tenant-configurable billing sender/domain allowlists and keyword packs.
- Add OCR/scanned PDF support in a separate task.
- Add richer ingestion observability dashboards for candidate rates and exclusion reasons.
- Add explicit schema/indexes for external message reference uniqueness and attachment hash lookup.