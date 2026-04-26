# Goal

Implement backlog task **TASK-30.3.3 — Add deterministic validation, duplicate checks, and confidence scoring persistence** for story **US-30.3 Extract normalized bill data with evidence capture, validation, duplicate checks, and confidence scoring**.

The coding agent should extend the existing bill extraction pipeline so that, for every detected bill candidate, the system:

- produces and persists a normalized bill object with all required fields
- persists field-level evidence metadata
- runs deterministic validation rules
- performs duplicate detection and persists the result in a `BillDuplicateCheck` record
- assigns and persists a deterministic confidence level of `high`, `medium`, or `low`
- marks `medium` and `low` confidence bills as requiring review
- ensures bills are **not eligible for approval proposal until validation status is persisted**

This work must fit the existing **modular monolith / clean architecture** style in the repo and remain **tenant-scoped** throughout.

# Scope

In scope:

- Domain model updates for normalized bill extraction result persistence
- Validation model and deterministic validation rule implementation
- Duplicate detection model and persistence
- Confidence scoring model and persistence
- Review gating flags/status persistence
- Application-layer orchestration updates so validation, duplicate check, and confidence scoring happen before downstream approval eligibility
- Infrastructure persistence updates, including EF Core mappings and migrations if this repo uses them
- Automated tests for domain/application/integration behavior

Required output behavior per acceptance criteria:

1. Persist a normalized bill object containing:
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

2. Persist evidence metadata for each extracted field including:
   - source document
   - page or section reference
   - text span or locator
   - extraction method
   - field-level confidence

3. Deterministic validation must reject or flag records when:
   - amount is missing
   - due date is invalid
   - invoice number is duplicated
   - bankgiro or IBAN format is invalid
   - VAT values are implausible

4. Duplicate detection must check at minimum:
   - tenant
   - supplier
   - invoice number
   - total amount

5. Persist duplicate detection result in a `BillDuplicateCheck` record.

6. Confidence scoring must assign:
   - `high`
   - `medium`
   - `low`

   based on:
   - deterministic validation outcomes
   - supplier matching signals

7. `low` and `medium` confidence bills:
   - must be marked as requiring review
   - must not be eligible for approval proposal until validation status is persisted

Out of scope unless required by existing code structure:

- UI work beyond minimal DTO/API exposure already needed by backend contracts
- LLM prompt changes unrelated to persistence/validation/duplicate logic
- broad workflow redesign
- approval UX changes

# Files to touch

Inspect the solution first and then touch the minimum necessary files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - bill/invoice-related entities, value objects, enums
  - validation result models
  - duplicate check models
  - confidence/review status models

- `src/VirtualCompany.Application/**`
  - extraction pipeline command/handler
  - bill normalization services
  - validation services
  - duplicate detection services
  - confidence scoring services
  - DTOs/contracts

- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories/query services
  - migrations
  - persistence implementations for duplicate checks and evidence metadata

- `src/VirtualCompany.Api/**`
  - only if request/response contracts or endpoints need updating

- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests

Also search for existing bill-related code before implementing:
- `Bill`
- `Invoice`
- `Evidence`
- `Validation`
- `Duplicate`
- `Confidence`
- `Approval`
- `Review`
- `sourceEmailId`
- `sourceAttachmentId`

If there is already a migration strategy documented under `docs/postgresql-migrations-archive/README.md`, follow the repo’s established approach exactly.

# Implementation plan

1. **Discover existing bill extraction flow**
   - Find the current normalized bill extraction pipeline and identify:
     - the entity representing extracted bills
     - where extracted fields are persisted
     - where evidence metadata is stored, if at all
     - where approval eligibility is determined
   - Do not create parallel models if a bill aggregate already exists; extend the existing one.

2. **Add/extend domain models**
   - Ensure the normalized bill persistence model includes all required fields:
     - `SupplierName`
     - `SupplierOrgNumber`
     - `InvoiceNumber`
     - `InvoiceDate`
     - `DueDate`
     - `Currency`
     - `TotalAmount`
     - `VatAmount`
     - `PaymentReference`
     - `Bankgiro`
     - `Plusgiro`
     - `Iban`
     - `Bic`
     - `Confidence`
     - `SourceEmailId`
     - `SourceAttachmentId`
   - Add or extend a field evidence entity/value object for per-field evidence metadata:
     - field name
     - source document
     - page/section reference
     - text span/locator
     - extraction method
     - field-level confidence
   - Add validation persistence structures, for example:
     - overall validation status enum
     - validation issue collection with code/severity/message/field
   - Add `BillDuplicateCheck` entity with at least:
     - id
     - tenant/company id
     - bill id
     - supplier match key/details
     - invoice number checked
     - total amount checked
     - duplicate status/result
     - matched bill id(s) or summary
     - checked at timestamp
   - Add confidence/review fields to the bill aggregate if not already present:
     - confidence enum/string: `high|medium|low`
     - `RequiresReview`
     - `ValidationStatusPersistedAt` or equivalent persisted marker
     - approval eligibility flag or derived state guard

3. **Implement deterministic validation rules**
   - Create a deterministic validator service in the application/domain layer.
   - Rules required:
     - missing amount => reject/flag
     - invalid due date => reject/flag
     - duplicated invoice number => reject/flag
     - invalid bankgiro format => reject/flag
     - invalid IBAN format => reject/flag
     - implausible VAT values => reject/flag
   - Prefer explicit validation issue codes, e.g.:
     - `AMOUNT_MISSING`
     - `DUE_DATE_INVALID`
     - `INVOICE_DUPLICATE`
     - `BANKGIRO_INVALID`
     - `IBAN_INVALID`
     - `VAT_IMPLAUSIBLE`
   - Make validation deterministic and testable:
     - no LLM calls
     - pure functions where possible
   - Define clear severity semantics:
     - blocking/rejecting vs warning/flagging
   - At minimum, acceptance criteria require reject-or-flag behavior; persist the outcome.

4. **Implement duplicate detection**
   - Add a duplicate detection service/repository query that checks existing bills by:
     - tenant/company
     - supplier
     - invoice number
     - total amount
   - Use normalized comparison rules where appropriate:
     - trimmed/case-normalized invoice number
     - normalized supplier identifier if org number exists
     - amount exact match using correct decimal precision
   - Persist the result in `BillDuplicateCheck`.
   - If duplicates are found, ensure validation/flagging reflects that.

5. **Implement supplier matching signals**
   - Reuse existing supplier/vendor matching if present.
   - If not present, implement minimal deterministic supplier matching signals based on available extracted data, such as:
     - exact org number match
     - exact normalized supplier name match
     - weaker name-only match
   - Expose these signals to confidence scoring.
   - Keep this deterministic and persisted enough for auditability if existing patterns support it.

6. **Implement confidence scoring**
   - Add a deterministic confidence scoring service that maps validation outcomes and supplier matching signals to:
     - `high`
     - `medium`
     - `low`
   - Suggested deterministic policy unless existing domain rules already exist:
     - `low` if any blocking validation failure exists, duplicate detected, or supplier match is weak/absent with other issues
     - `medium` if no blocking failures but one or more warnings/flags or only partial supplier match
     - `high` if validations pass cleanly and supplier match is strong
   - Persist:
     - overall confidence
     - any score rationale summary if the model already supports explainability fields

7. **Enforce review and approval gating**
   - Update the extraction persistence flow so that:
     - validation result is persisted
     - duplicate check result is persisted
     - confidence is persisted
     - then review/approval eligibility is computed
   - Ensure:
     - `medium` and `low` confidence => `RequiresReview = true`
     - these bills are not eligible for approval proposal until validation status is persisted
   - If approval eligibility is currently derived elsewhere, add a guard there too so downstream flows cannot bypass this rule.

8. **Persist evidence metadata**
   - Ensure each extracted field stores evidence metadata.
   - If evidence is currently stored as JSON, extend the schema carefully rather than duplicating structures.
   - If evidence is relational, add child rows keyed by bill and field name.
   - Preserve source references for auditability.

9. **Database and mapping updates**
   - Update EF Core configurations and persistence mappings.
   - Add migration(s) for:
     - new bill columns
     - evidence metadata structures
     - validation issue/result structures
     - `BillDuplicateCheck`
     - confidence/review fields
   - Keep tenant/company scoping explicit in schema and indexes.
   - Add useful indexes for duplicate detection, likely on:
     - `company_id`
     - normalized supplier key
     - normalized invoice number
     - `total_amount`

10. **Testing**
   - Add unit tests for:
     - validation rules
     - duplicate detection matching logic
     - confidence scoring
   - Add integration tests for:
     - persistence of normalized bill fields
     - persistence of evidence metadata
     - persistence of `BillDuplicateCheck`
     - review gating for medium/low confidence
     - approval ineligibility before validation status persistence
   - Cover edge cases:
     - missing amount
     - malformed IBAN
     - invalid bankgiro
     - implausible VAT
     - duplicate invoice number within same tenant
     - same invoice number across different tenants should not collide unless existing model says otherwise

11. **Keep implementation aligned with architecture**
   - Respect clean boundaries:
     - domain: rules and entities
     - application: orchestration/use cases
     - infrastructure: persistence and queries
     - api: transport only
   - Keep all operations tenant-scoped.
   - Prefer CQRS-lite patterns already used in the repo.

# Validation steps

Run these steps after implementation:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Targeted verification**
   - Confirm a detected bill candidate persists all required normalized fields.
   - Confirm each extracted field has evidence metadata persisted with:
     - source document
     - page/section reference
     - text span/locator
     - extraction method
     - field-level confidence
   - Confirm validation flags/rejects:
     - missing amount
     - invalid due date
     - duplicate invoice number
     - invalid bankgiro
     - invalid IBAN
     - implausible VAT
   - Confirm duplicate detection checks tenant + supplier + invoice number + total amount.
   - Confirm a `BillDuplicateCheck` record is persisted.
   - Confirm confidence is persisted as one of:
     - `high`
     - `medium`
     - `low`
   - Confirm `medium` and `low` confidence bills are marked `RequiresReview = true`.
   - Confirm bills are not eligible for approval proposal until validation status is persisted.
   - Confirm duplicate detection is tenant-isolated.

4. **Migration verification**
   - If migrations are used, verify the migration applies cleanly and schema matches mappings.

# Risks and follow-ups

- **Existing model mismatch risk:** The repo may already have invoice/bill entities with different naming. Extend existing aggregates instead of introducing conflicting concepts.
- **Migration strategy risk:** The project may use a specific migration/archive workflow. Follow the repo convention exactly.
- **Approval gating risk:** Approval eligibility may be computed in multiple places. Search for all approval proposal entry points and add guards consistently.
- **Duplicate false positives risk:** Supplier matching may be weak if org number is absent. Prefer deterministic normalization and persist enough detail for review.
- **Validation policy ambiguity:** “Reject or flag” leaves room for severity interpretation. If existing domain conventions exist, align with them; otherwise implement explicit severities and document them in code/tests.
- **VAT plausibility rule ambiguity:** Use a deterministic, conservative rule set and encode it in tests. If no business rule exists, implement a minimal plausibility check rather than country-specific tax logic.
- **Evidence storage shape risk:** If evidence is already JSON-based, avoid unnecessary relational redesign unless required by current architecture.
- **Follow-up suggestion:** If not already present, a later task should expose validation issues, duplicate check results, and confidence rationale in audit/explainability views and review queues.