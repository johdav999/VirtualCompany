# Goal

Implement `DocumentExtractionService` for **text-based PDF, DOCX, and email body parsing** to support **US-30.3 Extract normalized bill data with evidence capture, validation, duplicate checks, and confidence scoring**.

The implementation must produce normalized bill candidates from supported document inputs and persist or return enough structured data for downstream validation, duplicate detection, review gating, and auditability.

Key outcome:

- For each detected bill candidate, produce a normalized bill object with:
  - `supplierName`
  - `supplierOrgNumber`
  - `invoiceNumber`
  - `invoiceDate`
  - `dueDate`
  - `currency`
  - `totalAmount`
  - `vatAmount`
  - `paymentReference`
  - `bankgiro`
  - `plusgiro`
  - `iban`
  - `bic`
  - `confidence`
  - `sourceEmailId`
  - `sourceAttachmentId`
- For each extracted field, capture evidence metadata:
  - source document
  - page or section reference
  - text span or locator
  - extraction method
  - field-level confidence
- Apply validation rules that reject or flag records when:
  - amount is missing
  - due date is invalid
  - invoice number is duplicated
  - bankgiro or IBAN format is invalid
  - VAT values are implausible
- Perform duplicate detection using at minimum:
  - tenant
  - supplier
  - invoice number
  - total amount
- Persist duplicate detection result in a `BillDuplicateCheck` record
- Assign confidence as `high`, `medium`, or `low` based on deterministic validation outcomes and supplier matching signals
- Mark `low` and `medium` confidence bills as requiring review and ensure they are not eligible for approval proposal until validation status is persisted

Use the existing modular monolith structure and keep the implementation deterministic, testable, and tenant-aware.

# Scope

In scope:

- Add or complete an application/domain service named `DocumentExtractionService`
- Support parsing from:
  - text-based PDF
  - DOCX
  - email body text/html converted to text
- Normalize extracted bill candidates into a structured contract
- Capture field-level evidence metadata
- Add deterministic validation logic for extracted bills
- Add duplicate detection logic and persistence contract for `BillDuplicateCheck`
- Add confidence scoring logic
- Set review-required state for medium/low confidence candidates
- Add unit/integration tests for extraction, validation, duplicate detection, and confidence classification
- Wire service into the appropriate application/infrastructure layers

Out of scope unless already partially scaffolded:

- OCR for scanned/image PDFs
- LLM-based extraction
- UI changes beyond what is required to compile
- Full workflow/approval implementation outside the flags/statuses needed by this task
- Broad ingestion pipeline redesign

Implementation constraints:

- Follow clean architecture boundaries
- Keep parsing/extraction logic behind interfaces
- Prefer deterministic extraction heuristics and validators
- Keep tenant scoping explicit
- Do not introduce direct DB access from parsing components
- If persistence models for bills/duplicate checks already exist, use them; otherwise add the minimum required domain/application/infrastructure pieces consistent with the solution structure

# Files to touch

Inspect the solution first and then touch only the files needed. Expect to work primarily in:

- `src/VirtualCompany.Domain`
  - bill-related entities/value objects/enums
  - evidence metadata models
  - validation result models
  - duplicate check models
  - confidence/review status enums
- `src/VirtualCompany.Application`
  - `DocumentExtractionService`
  - interfaces/contracts for extraction, validation, duplicate checking
  - DTOs/commands/results for normalized bill candidates
  - orchestration logic for parsing -> extraction -> validation -> duplicate check -> confidence scoring
- `src/VirtualCompany.Infrastructure`
  - PDF/DOCX/email text extraction adapters
  - repository implementation for duplicate checks if needed
  - persistence mappings/configurations
- `src/VirtualCompany.Api`
  - DI registration only if needed
  - endpoint wiring only if already part of an existing ingestion flow
- `tests/VirtualCompany.Api.Tests`
  - integration tests if API/application flow is exposed there
- Add test projects/files in the existing test structure if there is a more appropriate application/domain test location

Also inspect:

- existing bill/invoice/accounting-related models
- existing document ingestion abstractions
- existing email ingestion models
- existing persistence/migrations approach
- existing enum/status naming conventions
- existing repository and service registration patterns

If schema changes are required, add them using the repository’s established migration approach. Do not invent a new migration mechanism.

# Implementation plan

1. **Inspect the current codebase before coding**
   - Find any existing:
     - bill/invoice entities
     - document ingestion services
     - email message/attachment models
     - validation frameworks
     - duplicate check records
     - confidence/review status fields
     - repository abstractions
   - Reuse existing naming and patterns wherever possible
   - Identify whether this task should return extracted candidates only or also persist them as bill records; if both are possible, implement the minimum needed to satisfy acceptance criteria without overreaching

2. **Define or align the normalized bill extraction contract**
   - Create or update a DTO/model for a normalized extracted bill candidate containing all required fields:
     - `supplierName`
     - `supplierOrgNumber`
     - `invoiceNumber`
     - `invoiceDate`
     - `dueDate`
     - `currency`
     - `totalAmount`
     - `vatAmount`
     - `paymentReference`
     - `bankgiro`
     - `plusgiro`
     - `iban`
     - `bic`
     - `confidence`
     - `sourceEmailId`
     - `sourceAttachmentId`
   - Add supporting structures for:
     - field evidence metadata
     - field-level confidence
     - extraction method
     - page/section reference
     - text span/locator
     - validation findings
     - duplicate check outcome
     - review-required flag
     - approval-eligibility gate if represented in the model
   - Prefer strongly typed enums over free-form strings where the codebase already does that

3. **Model evidence metadata explicitly**
   - For every extracted field, store evidence metadata in a reusable structure, e.g. one evidence object per field
   - Include:
     - field name
     - extracted value
     - source document identifier/type
     - page number or section reference
     - text span, snippet, or locator
     - extraction method
     - field confidence
   - Ensure evidence can be persisted or at least returned in a structured way for downstream audit/explainability

4. **Implement document text extraction adapters**
   - Add infrastructure adapters for:
     - PDF text extraction
     - DOCX text extraction
     - email body extraction
   - Keep them behind interfaces such as:
     - `IPdfTextExtractor`
     - `IDocxTextExtractor`
     - `IEmailBodyTextExtractor`
     - or a unified `IDocumentTextExtractor`
   - Only support text-based PDFs; if no extractable text exists, return a clear unsupported/empty-text result rather than attempting OCR
   - Normalize extracted text into a common internal representation with:
     - plain text
     - page/section segmentation
     - source references for evidence mapping

5. **Implement deterministic bill candidate extraction**
   - Build `DocumentExtractionService` to:
     - accept tenant context and source metadata
     - detect one or more bill candidates from a document/email body
     - extract required fields using deterministic heuristics/regex/pattern matching
   - Suggested extraction heuristics:
     - supplier name from top-of-document/header patterns and known sender/signature context
     - org number from Swedish org number patterns if applicable
     - invoice number from labels like invoice/invoice no/fakturanummer/reference
     - dates from labeled date fields
     - due date from labeled due/payment due fields
     - currency from symbols/codes (`SEK`, `EUR`, etc.)
     - total amount from labels like total/amount to pay/att betala
     - VAT from labels like VAT/moms
     - payment reference from OCR/reference/meddelande/reference labels
     - bankgiro/plusgiro/IBAN/BIC from labeled payment sections
   - Support multilingual label variants if easy to add, especially likely Swedish/English invoice terms
   - If multiple candidates are found in one source, return all of them

6. **Implement field-level confidence scoring**
   - For each field, assign confidence based on deterministic signals such as:
     - labeled match vs inferred match
     - uniqueness of match
     - format validity
     - consistency with nearby context
   - Then compute overall bill confidence:
     - `high` when key fields are present and valid, duplicate check is clean, and supplier matching signals are strong
     - `medium` when extraction is plausible but one or more non-fatal ambiguities/flags exist
     - `low` when key fields are weak/ambiguous or validation issues materially reduce trust
   - Keep the scoring deterministic and explainable in code comments or result metadata

7. **Implement validation rules**
   - Add validation logic that rejects or flags records when:
     - `totalAmount` is missing
     - `dueDate` is invalid
     - invoice number is duplicated
     - `bankgiro` format is invalid
     - `iban` format is invalid
     - VAT values are implausible
   - Distinguish between:
     - hard rejection
     - warning/flag requiring review
   - VAT plausibility should be deterministic and conservative, for example:
     - VAT cannot be negative unless domain rules already allow credit notes
     - VAT should not exceed total amount
     - if both total and VAT exist, VAT ratio should be within a plausible range unless explicitly flagged
   - Reuse existing validation abstractions if present

8. **Implement duplicate detection**
   - Add a duplicate detection service/repository flow that checks at minimum:
     - tenant
     - supplier
     - invoice number
     - total amount
   - Persist the result in a `BillDuplicateCheck` record
   - If the entity/table does not exist, add the minimum domain model and persistence mapping required
   - Duplicate detection should return:
     - whether duplicate was found
     - matched record identifiers if available
     - criteria used
     - timestamp
   - Ensure tenant isolation in all queries

9. **Apply review gating and approval eligibility rules**
   - When confidence is `low` or `medium`, mark the bill as requiring review
   - Ensure such bills are not eligible for approval proposal until validation status is persisted
   - If the codebase already has workflow/approval flags, integrate with them
   - If not, add minimal status fields/flags in the extraction result or persisted bill aggregate to represent:
     - validation status
     - requires review
     - approval proposal eligibility

10. **Persist or expose results through the correct layer**
   - If the existing architecture persists extracted bill candidates immediately:
     - save normalized bill data
     - save field evidence
     - save validation findings
     - save duplicate check result
   - If the architecture returns extraction results for later persistence:
     - ensure the result object contains all required data for downstream persistence
   - In either case, acceptance criteria require duplicate check persistence, so implement that persistence path explicitly

11. **Register dependencies**
   - Add DI registrations for:
     - `DocumentExtractionService`
     - text extraction adapters
     - validators
     - duplicate checker
     - confidence scorer
   - Keep registrations in the existing composition root pattern

12. **Add tests**
   - Add focused tests for:
     - PDF text-based extraction
     - DOCX extraction
     - email body extraction
     - evidence metadata population
     - missing amount validation
     - invalid due date validation
     - invalid bankgiro validation
     - invalid IBAN validation
     - implausible VAT validation
     - duplicate detection persistence
     - confidence classification high/medium/low
     - medium/low confidence review gating
   - Use small representative fixtures or inline text samples
   - Prefer deterministic tests over broad snapshot tests

13. **Keep implementation quality high**
   - Follow existing code style and naming
   - Add concise XML/docs/comments only where helpful
   - Avoid speculative abstractions beyond this task
   - Ensure nullability is handled correctly
   - Keep parsing logic modular so OCR/scanned-PDF support can be added later

# Validation steps

1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add or update automated tests to verify acceptance criteria:
   - normalized bill object contains all required fields
   - each extracted field has evidence metadata
   - validation flags/rejects the required failure cases
   - duplicate detection checks tenant + supplier + invoice number + total amount
   - duplicate detection persists a `BillDuplicateCheck`
   - confidence scoring returns `high`, `medium`, or `low`
   - medium/low confidence bills are marked as requiring review
   - approval proposal eligibility is blocked until validation status is persisted

4. Manually verify with representative fixtures if a test harness exists:
   - text-based PDF invoice
   - DOCX invoice
   - email body containing invoice/bill details
   - duplicate invoice scenario
   - malformed payment details scenario

5. If schema changes were made:
   - apply migrations using the repo’s standard process
   - verify the app still builds and tests pass

# Risks and follow-ups

- **Risk: existing bill domain may already exist under different naming**
  - Mitigation: inspect first and align instead of duplicating concepts

- **Risk: PDF/DOCX parsing libraries may not yet be referenced**
  - Mitigation: use existing packages if present; if adding a package, keep it minimal and compatible with the solution

- **Risk: acceptance criteria imply persistence details not yet modeled**
  - Mitigation: add the smallest consistent domain/persistence surface needed, especially for `BillDuplicateCheck` and evidence metadata

- **Risk: supplier matching signals may not yet exist**
  - Mitigation: implement a simple deterministic supplier matching signal based on extracted supplier identifiers/name consistency