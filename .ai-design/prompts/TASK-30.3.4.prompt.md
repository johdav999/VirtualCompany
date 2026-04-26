# Goal
Implement backlog task **TASK-30.3.4 — Create DetectedBill and DetectedBillField schemas with evidence references** for story **US-30.3 Extract normalized bill data with evidence capture, validation, duplicate checks, and confidence scoring**.

The coding agent should add the domain, persistence, and migration support needed to represent:
- a normalized detected bill candidate
- per-field extracted values with evidence metadata
- duplicate check persistence via `BillDuplicateCheck`
- validation and review-related state required by the acceptance criteria

This task is focused on **schema/model implementation**, not full extraction pipeline behavior. However, the schema must be designed so later application services can satisfy all listed acceptance criteria without requiring breaking changes.

# Scope
In scope:
- Add domain entities/value objects/enums for:
  - `DetectedBill`
  - `DetectedBillField`
  - `BillDuplicateCheck` if not already present; otherwise extend it to support this story
- Model all normalized bill fields required by the acceptance criteria:
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
- Add per-field evidence metadata on `DetectedBillField`:
  - source document
  - page or section reference
  - text span or locator
  - extraction method
  - field-level confidence
- Add validation/review/eligibility state on `DetectedBill` sufficient to support:
  - rejected/flagged validation outcomes
  - duplicate check result linkage
  - confidence banding high/medium/low
  - requires-review behavior
  - approval proposal eligibility gating until validation status is persisted
- Add EF Core configurations and PostgreSQL migration(s)
- Preserve tenant isolation with `company_id`/tenant-scoped ownership patterns consistent with the existing architecture
- Add tests for entity mapping and migration/model expectations where the repo patterns support it

Out of scope unless clearly required by existing compile-time architecture:
- OCR/LLM extraction logic
- validation engine implementation
- duplicate detection algorithm implementation
- approval workflow implementation
- API/UI endpoints
- background jobs

# Files to touch
Inspect the solution structure first and then update the appropriate files in the established patterns. Likely areas:

- `src/VirtualCompany.Domain/...`
  - add `DetectedBill.cs`
  - add `DetectedBillField.cs`
  - add supporting enums/value objects if the domain uses them
  - add or update `BillDuplicateCheck.cs`
- `src/VirtualCompany.Infrastructure/...`
  - EF Core entity configurations for the new entities
  - `DbContext` / model registration
  - migration files
- `src/VirtualCompany.Application/...`
  - only if shared contracts or read models are already defined here and needed for compile consistency
- `tests/...`
  - mapping/model tests
  - migration/schema tests if the repo already has that pattern

Before editing, search for:
- existing invoice/bill/accounting entities
- existing duplicate-check entities
- base entity abstractions
- tenant-owned entity conventions
- enum conventions
- EF configuration conventions
- migration naming conventions

# Implementation plan
1. **Inspect existing architecture and conventions**
   - Find the main `DbContext`, entity base classes, and EF configuration registration pattern.
   - Check whether tenant ownership is represented as `CompanyId`, `TenantId`, or similar.
   - Search for existing finance-related entities to avoid duplicating concepts.
   - Search for `BillDuplicateCheck`; if it exists, extend it rather than creating a conflicting model.

2. **Design the `DetectedBill` aggregate/schema**
   - Create a tenant-owned entity representing one detected bill candidate from an email attachment/document.
   - Include identifiers and source references:
     - `Id`
     - `CompanyId` or equivalent tenant key
     - `SourceEmailId`
     - `SourceAttachmentId`
   - Include normalized bill properties from the acceptance criteria:
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
   - Include confidence and workflow state:
     - raw numeric confidence if the codebase prefers scores
     - confidence band enum/string: `High`, `Medium`, `Low`
     - validation status enum/string
     - review status / `RequiresReview`
     - approval proposal eligibility flag, default false until validation persisted
   - Include duplicate check linkage:
     - optional FK/reference to `BillDuplicateCheck`
     - or one-to-one/one-to-many relation depending on existing duplicate-check design
   - Include timestamps/audit fields consistent with project conventions.

3. **Design the `DetectedBillField` schema**
   - Create one row per extracted field for a `DetectedBill`.
   - Include:
     - `Id`
     - `CompanyId` if tenant-owned child entities also carry tenant key in this codebase
     - `DetectedBillId`
     - `FieldName` or enum identifying which normalized field this row represents
     - extracted/normalized value storage
   - Prefer a flexible representation that supports heterogeneous field types without overcomplication:
     - `NormalizedValue` as string/text for canonical storage
     - optional raw value text if useful and consistent with conventions
   - Add evidence metadata required by acceptance criteria:
     - `SourceDocument` or `SourceDocumentRef`
     - `PageReference`
     - `SectionReference`
     - `TextSpan`
     - `Locator`
     - `ExtractionMethod`
     - `FieldConfidence`
   - If the architecture prefers JSONB for flexible evidence payloads, keep the required fields queryable and explicit where possible; do not hide all evidence in opaque JSON unless that is already the repo standard.
   - Add uniqueness/indexing so a bill cannot have duplicate rows for the same logical field unless the design intentionally supports multiple candidates. For this task, prefer one field row per normalized field unless existing extraction patterns require otherwise.

4. **Model validation and duplicate-check persistence support**
   - Ensure `DetectedBill` can persist validation outcomes for:
     - missing amount
     - invalid due date
     - duplicated invoice number
     - invalid bankgiro or IBAN format
     - implausible VAT values
   - This does **not** require implementing the validator, but the schema must support:
     - overall validation status
     - machine-readable validation issues, either as child records, JSONB, or status fields consistent with repo conventions
   - Ensure duplicate detection persistence can store at minimum:
     - tenant/company
     - supplier
     - invoice number
     - total amount
     - result status / matched bill reference(s)
   - If `BillDuplicateCheck` does not exist, add it with enough structure to support later duplicate detection services.

5. **Define enums/value objects carefully**
   Add enums or constrained string fields for:
   - `DetectedBillConfidenceLevel`: `High`, `Medium`, `Low`
   - `DetectedBillValidationStatus`: e.g. `Pending`, `Valid`, `Flagged`, `Rejected`
   - `DetectedBillReviewStatus`: e.g. `NotRequired`, `Required`, `Completed`
   - `DetectedBillFieldName`
   - `ExtractionMethod` if the codebase uses enums for this kind of metadata
   - duplicate check result status if needed

   Follow existing project conventions:
   - use enums + EF conversions if common
   - otherwise use constrained strings

6. **Add EF Core configurations**
   - Map tables with explicit names matching project conventions.
   - Configure required vs optional fields based on acceptance criteria:
     - `TotalAmount` should likely be nullable at schema level to allow persistence of invalid/flagged candidates, even though validation may reject/flag it
     - same for other extracted fields
   - Configure precision for monetary values, e.g. `numeric(18,2)` or existing project standard.
   - Configure max lengths for identifiers like invoice number, org number, currency, bankgiro, plusgiro, IBAN, BIC.
   - Configure relationships:
     - `DetectedBill` -> many `DetectedBillField`
     - `DetectedBill` -> duplicate check relation
   - Add indexes for likely lookup and duplicate detection:
     - `(company_id, source_email_id)`
     - `(company_id, source_attachment_id)`
     - `(company_id, supplier_name, invoice_number, total_amount)` or normalized equivalent
     - unique or semi-unique index for child field names per bill
   - Ensure delete behavior is explicit and safe.

7. **Create migration**
   - Add a migration with clear naming, e.g. `AddDetectedBillSchemas`.
   - Verify generated SQL types are appropriate for PostgreSQL.
   - Ensure migration includes indexes and foreign keys.
   - If using snake_case naming conventions, ensure generated names align with the rest of the schema.

8. **Add tests**
   Depending on existing test patterns, add:
   - domain/entity construction tests if entities enforce invariants
   - EF model tests verifying:
     - required relationships
     - enum conversions
     - precision/column types for money
     - indexes/unique constraints where practical
   - migration smoke test or `DbContext` model build test
   - If there are snapshot/schema tests, update them

9. **Keep implementation minimal but future-proof**
   - Do not implement extraction services or validators in this task.
   - Do not over-model with too many child tables unless the repo already uses that style.
   - Favor a schema that supports later workflows without forcing another migration for obvious acceptance-criteria fields.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are managed in the normal EF workflow, ensure the migration compiles and the model snapshot updates correctly.

4. Manually verify the schema/model against acceptance criteria:
   - `DetectedBill` contains all normalized bill fields listed in the task
   - `DetectedBillField` stores evidence metadata:
     - source document
     - page/section reference
     - text span/locator
     - extraction method
     - field confidence
   - validation state can represent rejected/flagged outcomes
   - duplicate check persistence exists via `BillDuplicateCheck`
   - confidence banding and review gating fields exist
   - low/medium confidence can be marked as requiring review
   - approval proposal eligibility is blocked until validation status is persisted

5. Review generated migration for:
   - correct FK relationships
   - correct indexes
   - correct numeric precision
   - nullable choices that allow storing imperfect extraction candidates for review

# Risks and follow-ups
- **Risk: existing finance schema overlap**
  - There may already be invoice/bill entities. Avoid creating conflicting concepts; `DetectedBill` should represent extraction-stage candidates, not approved accounting records.

- **Risk: tenant key conventions**
  - The repo may use `CompanyId` everywhere rather than `TenantId`. Match existing conventions exactly.

- **Risk: enum persistence conventions**
  - If the project stores enums as strings, do not introduce integer-backed enums inconsistently.

- **Risk: over-constraining nullability**
  - Acceptance criteria require validation to reject/flag bad records, which implies invalid candidates may still be persisted. Do not make fields non-nullable if that would prevent storing flagged extraction results.

- **Risk: duplicate-check relationship ambiguity**
  - If `BillDuplicateCheck` already exists with a different ownership model, adapt to it rather than forcing a new one-to-one design.

Follow-ups after this task:
- implement extraction pipeline to populate `DetectedBill` and `DetectedBillField`
- implement validation issue generation and persistence
- implement duplicate detection service writing `BillDuplicateCheck`
- implement confidence scoring logic and review gating
- expose query/API surfaces for review workflows