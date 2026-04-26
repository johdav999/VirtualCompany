# Goal
Implement backlog task **TASK-30.3.2 — Build BillInformationExtractor with regex parsing, structured extraction, and normalized field mapping** for story **US-30.3 Extract normalized bill data with evidence capture, validation, duplicate checks, and confidence scoring**.

Create a production-ready bill extraction component in the .NET backend that:
- parses detected bill candidates from source text/documents using deterministic regex and structured extraction rules,
- maps extracted values into a normalized bill object,
- captures field-level evidence metadata,
- validates extracted data,
- performs duplicate detection,
- assigns confidence,
- marks review requirements correctly,
- persists duplicate-check and validation outcomes needed by downstream workflow logic.

The implementation must fit the existing modular monolith / clean architecture style and remain tenant-scoped, deterministic, testable, and auditable.

# Scope
Implement only what is necessary for this task, but ensure the result is end-to-end usable within the application layer and persistence layer.

Required behavior:
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
- Store each extracted field with evidence metadata including:
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
- Assign confidence as:
  - high
  - medium
  - low
  based on deterministic validation outcomes and supplier matching signals
- Ensure low-confidence and medium-confidence bills:
  - are marked as requiring review
  - are not eligible for approval proposal until validation status is persisted

Constraints:
- Prefer deterministic extraction first; do not introduce LLM extraction for this task.
- Keep extraction logic in application/domain services, not controllers/UI.
- Follow tenant-aware patterns.
- Use typed models and explicit enums/value objects where appropriate.
- Add or update tests covering extraction, validation, duplicate detection, and confidence outcomes.
- If bill-related entities/contracts do not exist yet, add the minimum required domain/application/infrastructure pieces cleanly.

Out of scope unless required to compile/integrate:
- Full UI for bill review
- Email ingestion pipeline redesign
- OCR engine implementation
- Approval proposal generation itself
- Broad refactors unrelated to bill extraction

# Files to touch
Inspect the solution first and then touch the minimum necessary files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - add bill extraction domain models, enums, validation result types, evidence models, duplicate-check entity/value objects if missing
- `src/VirtualCompany.Application/**`
  - add `BillInformationExtractor`
  - add extraction contracts/interfaces
  - add validation and confidence scoring services/helpers
  - add duplicate detection service/handler
  - add DTOs/commands/results for normalized bill extraction
- `src/VirtualCompany.Infrastructure/**`
  - persistence for bill extraction results and `BillDuplicateCheck`
  - EF Core configurations/mappings if used
  - repository implementations
- `src/VirtualCompany.Api/**`
  - only if wiring is needed for DI or endpoint integration
- `tests/VirtualCompany.Api.Tests/**` and/or other test projects
  - unit/integration tests for extraction, validation, duplicate checks, persistence, and confidence/review behavior

Also inspect:
- existing bill/invoice/accounting-related models or modules
- existing migration approach and whether new migrations belong in current migration location
- DI registration patterns
- repository/query patterns
- tenant scoping conventions
- audit/explainability patterns that can be reused for evidence metadata

# Implementation plan
1. **Discover existing bill-related architecture**
   - Search the solution for:
     - `Bill`
     - `Invoice`
     - `DuplicateCheck`
     - `Evidence`
     - `ValidationStatus`
     - `Confidence`
     - `sourceEmailId`
     - `sourceAttachmentId`
   - Reuse existing abstractions if present instead of inventing parallel models.
   - Identify whether EF Core, Dapper, or another persistence approach is used.

2. **Define/extend core contracts and models**
   - Add or extend normalized bill models so the extracted result has all required fields.
   - Introduce explicit types/enums where useful, for example:
     - `BillConfidenceLevel` = `High | Medium | Low`
     - `BillValidationStatus` = `Valid | Flagged | Rejected | Pending`
     - `ExtractionMethod` = `Regex | StructuredRule | Derived`
     - `ReviewRequirementStatus` or boolean `RequiresReview`
   - Add field evidence model, e.g. one evidence record per field:
     - field name
     - extracted value
     - source document identifier
     - page/section reference
     - text span/locator
     - extraction method
     - field confidence
   - Add duplicate-check model/entity:
     - tenant/company id
     - supplier identity fields
     - invoice number
     - total amount
     - match result
     - matched bill id if any
     - checked timestamp
     - reason/details

3. **Implement `BillInformationExtractor`**
   - Create a focused application service, e.g.:
     - `IBillInformationExtractor`
     - `BillInformationExtractor`
   - Input should represent a detected bill candidate and its source text/document metadata.
   - Output should include:
     - normalized bill object
     - field evidence collection
     - validation result
     - duplicate-check result
     - confidence result
   - Extraction approach:
     - regex-based extraction for common invoice patterns
     - structured rule parsing for labels like:
       - invoice number / fakturanummer / invoice no
       - invoice date / fakturadatum
       - due date / förfallodatum
       - total / amount due / att betala
       - VAT / moms
       - org number / organisationsnummer
       - OCR / reference / payment reference
       - bankgiro / plusgiro / IBAN / BIC
       - currency markers like SEK/EUR/USD
     - normalized field mapping after extraction
   - Make extraction deterministic and composable:
     - field-specific extractors/helpers
     - normalization helpers for dates, decimals, currency, org numbers, payment refs
   - Prefer best-match selection rules when multiple candidates exist:
     - labeled values over unlabeled values
     - exact format matches over fuzzy matches
     - values near invoice-related headings over generic footer text

4. **Implement evidence capture**
   - For every extracted field, persist evidence metadata.
   - If exact character offsets are available from source text, store them.
   - If not, store the best available locator:
     - page number
     - section/header/footer marker
     - matched text snippet
   - Ensure evidence is attached even for flagged fields when extraction succeeded but validation later failed.

5. **Implement normalization**
   - Normalize:
     - dates to a consistent type/format
     - amounts to decimal using invariant storage
     - currency to ISO-like uppercase code where possible
     - supplier org number to a canonical format if regional rules exist
     - bankgiro/plusgiro/IBAN/BIC stripped of display separators as appropriate
   - Preserve original extracted text in evidence while storing normalized values in the bill object.

6. **Implement validation rules**
   - Add deterministic validation service or validator class.
   - Required checks:
     - missing total amount => reject or flag per acceptance criteria
     - invalid due date => reject or flag
     - duplicate invoice number => flag/reject via duplicate detection result
     - invalid bankgiro format => flag
     - invalid IBAN format => flag
     - implausible VAT => flag
   - VAT plausibility should be deterministic and conservative, for example:
     - negative VAT not allowed unless domain already supports credit notes
     - VAT greater than total amount invalid
     - VAT inconsistent with total/net in obvious cases should flag
   - Persist validation status before any approval eligibility is considered.

7. **Implement duplicate detection**
   - Add repository/query support to check existing bills by:
     - tenant/company
     - supplier
     - invoice number
     - total amount
   - Persist a `BillDuplicateCheck` record for each extraction attempt/result.
   - Ensure duplicate detection is tenant-isolated.
   - If supplier matching is fuzzy in existing code, use only deterministic matching unless a current supplier matcher already exists.
   - Include enough detail in the duplicate-check record for audit/debugging.

8. **Implement confidence scoring**
   - Add deterministic confidence scoring service.
   - Suggested rules:
     - **High**
       - required fields present
       - no critical validation failures
       - supplier match strong/deterministic
       - duplicate check negative
     - **Medium**
       - most required fields present
       - minor validation flags or weaker supplier match
     - **Low**
       - missing important fields, invalid formats, duplicate suspicion, or weak supplier signal
   - Confidence must be derived from validation and supplier matching signals, not arbitrary heuristics.
   - Set `RequiresReview = true` for medium and low confidence.
   - Ensure such bills are not approval-eligible until validation status is persisted.

9. **Persist entities and wire infrastructure**
   - Add EF Core entity configurations and migrations if the project uses EF Core.
   - Persist:
     - normalized bill record
     - field evidence records
     - validation status/details
     - duplicate-check record
   - Follow existing naming and schema conventions.
   - Keep transactional consistency where appropriate.

10. **Wire DI and application flow**
   - Register extractor/validator/scorer/repository services in DI.
   - If there is an existing ingestion pipeline or command handler for bill candidates, integrate there.
   - Avoid controller-heavy logic; orchestration should happen in application services/handlers.

11. **Add tests**
   - Unit tests for regex/structured extraction:
     - Swedish and English invoice labels if relevant from domain context
     - multiple date/amount candidates
     - bankgiro/plusgiro/IBAN/BIC extraction
   - Unit tests for normalization:
     - decimal parsing
     - date parsing
     - currency normalization
   - Unit tests for validation:
     - missing amount
     - invalid due date
     - invalid bankgiro
     - invalid IBAN
     - implausible VAT
   - Unit/integration tests for duplicate detection:
     - same tenant + supplier + invoice number + amount => duplicate
     - different tenant => not duplicate
   - Unit tests for confidence scoring:
     - high/medium/low outcomes
     - medium/low => requires review
     - approval ineligible until validation persisted
   - Persistence/integration tests if infrastructure patterns support them.

12. **Keep implementation auditable and maintainable**
   - Add concise XML/docs/comments only where needed.
   - Reuse existing audit/explainability patterns if available.
   - Do not expose chain-of-thought; store operational evidence and deterministic reasons only.

# Validation steps
Run these after implementation and ensure all pass.

1. **Build and tests**
   - `dotnet build`
   - `dotnet test`

2. **Targeted verification**
   - Confirm a detected bill candidate produces a normalized bill object with all required fields/properties present in the contract.
   - Confirm each extracted field has evidence metadata persisted or returned as designed.
   - Confirm validation flags/rejects:
     - missing amount
     - invalid due date
     - duplicate invoice number
     - invalid bankgiro
     - invalid IBAN
     - implausible VAT
   - Confirm duplicate detection checks tenant + supplier + invoice number + total amount and persists a `BillDuplicateCheck`.
   - Confirm confidence scoring returns only `High`, `Medium`, or `Low`.
   - Confirm `Medium` and `Low` confidence bills are marked `RequiresReview = true`.
   - Confirm medium/low confidence bills are not approval-eligible before validation status persistence.
   - Confirm tenant isolation in duplicate detection and persistence queries.

3. **Code quality checks**
   - Ensure no extraction logic is embedded in controllers or UI.
   - Ensure no direct DB access from extraction logic outside repositories/data access abstractions.
   - Ensure deterministic behavior for the same input.
   - Ensure nullability and parsing failures are handled safely.

4. **If migrations are added**
   - Generate/apply migration according to repo conventions.
   - Verify schema includes any new bill evidence and duplicate-check tables/columns.
   - Verify tests or startup still succeed with the migration in place.

# Risks and follow-ups
- **Existing bill domain may already exist under different names**
  - Reconcile carefully to avoid duplicate concepts like `Invoice` vs `Bill`.
- **Source text quality may vary**
  - Keep extraction resilient but deterministic; do not overfit to one template.
- **OCR/page locator data may be incomplete**
  - Store best-available evidence locator when exact spans are unavailable.
- **VAT plausibility rules can become country-specific**
  - Implement conservative generic rules now and leave extension points for regional validation later.
- **Supplier matching signals may be weak if supplier master data is incomplete**
  - Use deterministic signals only; document assumptions in code/tests.
- **Duplicate detection may need indexing**
  - If adding persistence, consider indexes on tenant/supplier/invoice number/amount combinations.
- **Approval eligibility dependency may touch adjacent workflow code**
  - Keep changes minimal but ensure validation persistence state is the gate.
- **Follow-up candidates**
  - supplier master matching enrichment
  - OCR-aware evidence spans
  - country-specific invoice parsing packs
  - review queue UI
  - richer duplicate heuristics beyond exact