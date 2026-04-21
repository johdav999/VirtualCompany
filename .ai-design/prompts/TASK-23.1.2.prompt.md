# Goal
Implement `TASK-23.1.2` for story `US-23.1 Map ledger accounts to financial statement sections and reporting classifications` in the existing .NET modular monolith.

Deliver company-scoped backend support for financial statement account mappings, including:

- a persistence model linking each company account to:
  - statement type
  - report section
  - line classification
- CRUD-style APIs for:
  - list mappings by company
  - create mapping
  - update mapping
- a validation API that returns all unmapped or conflicting accounts for a company
- deterministic validation/error codes
- database constraints enforcing one active mapping per company account and statement type
- enforcement that active + reportable accounts cannot be left unmapped

Keep implementation aligned with:
- ASP.NET Core API
- clean architecture boundaries
- shared-schema multi-tenancy via `company_id`
- CQRS-lite application layer
- PostgreSQL persistence
- policy/tenant-safe API behavior

# Scope
In scope:

- Add domain model(s) for financial statement account mappings
- Add EF Core/PostgreSQL persistence configuration and migration
- Add application commands/queries/DTOs for:
  - listing mappings
  - creating mappings
  - updating mappings
  - validating company mappings
- Add API endpoints under a company-scoped route
- Add deterministic validation result/error codes for:
  - unmapped active reportable accounts
  - duplicate/conflicting active mappings
  - invalid account/company references
  - invalid enum/code values
- Enforce uniqueness at DB level for one active mapping per company account and statement type
- Ensure validation endpoint returns stable, machine-readable results
- Add tests covering application/API/persistence behavior

Out of scope unless required by existing patterns:

- Blazor UI
- mobile app changes
- background jobs
- audit/event fan-out beyond minimal existing conventions
- broad accounting domain redesign outside what is necessary for this task

If prerequisite account/company entities already exist, integrate with them. If naming differs, adapt to the existing domain rather than inventing parallel models.

# Files to touch
Inspect the solution first and then touch the appropriate files in these areas.

Likely locations:

- `src/VirtualCompany.Domain/...`
  - add domain entity/value objects/enums for financial statement mappings
- `src/VirtualCompany.Application/...`
  - commands
  - queries
  - validators
  - DTOs/contracts
  - result/error code definitions
- `src/VirtualCompany.Infrastructure/...`
  - EF Core entity configuration
  - repository/query implementations
  - migration(s)
- `src/VirtualCompany.Api/...`
  - controller or minimal API endpoint registration
  - request/response contracts if API-layer specific
- `tests/VirtualCompany.Api.Tests/...`
  - endpoint/integration tests
- possibly shared constants/contracts in:
  - `src/VirtualCompany.Shared/...`

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
- existing company-scoped API patterns
- existing tenant resolution/authorization patterns
- existing migration conventions

# Implementation plan
1. **Discover existing accounting and tenant patterns**
   - Search for:
     - company-scoped controllers/endpoints
     - existing account/ledger/chart-of-accounts entities
     - existing validation/result code conventions
     - EF Core migration patterns
   - Reuse existing naming and folder structure.
   - Do not create duplicate concepts if account models already exist.

2. **Define the domain model**
   - Introduce a financial statement mapping entity tied to:
     - `CompanyId`
     - `AccountId` or equivalent company account FK
     - `StatementType` enum/code
     - `ReportSection` enum/code
     - `LineClassification` enum/code
     - `IsActive`
     - timestamps
   - Ensure the model supports Balance Sheet, Profit & Loss, and Cash Flow.
   - If the account entity already carries `IsActive` and `IsReportable`, use those fields for validation logic.
   - Prefer strongly typed enums/value objects where consistent with the codebase.

3. **Design deterministic validation/error codes**
   - Add stable machine-readable codes, for example:
     - `ACCOUNT_MAPPING_UNMAPPED_ACTIVE_REPORTABLE`
     - `ACCOUNT_MAPPING_CONFLICT_DUPLICATE_ACTIVE`
     - `ACCOUNT_MAPPING_ACCOUNT_NOT_FOUND`
     - `ACCOUNT_MAPPING_ACCOUNT_COMPANY_MISMATCH`
     - `ACCOUNT_MAPPING_INVALID_STATEMENT_TYPE`
     - `ACCOUNT_MAPPING_INVALID_REPORT_SECTION`
     - `ACCOUNT_MAPPING_INVALID_LINE_CLASSIFICATION`
   - Use existing error/result abstractions if present.
   - Keep codes stable and explicit in API responses.

4. **Add persistence and DB constraints**
   - Create EF configuration for the mapping table.
   - Include `company_id` and FK relationships.
   - Add a filtered unique index/constraint enforcing one active mapping per company account and statement type.
     - Example intent: unique on `(company_id, account_id, statement_type)` where `is_active = true`
   - Add supporting indexes for company-scoped listing and validation queries.
   - Generate a PostgreSQL migration following repo conventions.

5. **Implement application layer commands and queries**
   - Add:
     - `ListAccountMappingsQuery`
     - `CreateAccountMappingCommand`
     - `UpdateAccountMappingCommand`
     - `ValidateAccountMappingsQuery`
   - Validation rules should include:
     - account exists
     - account belongs to company
     - account is eligible if required by business rules
     - enum/code values are valid
     - no duplicate active mapping for same company account + statement type
   - Validation query should return:
     - all active + reportable accounts missing required mappings
     - all conflicting accounts/mappings
     - deterministic codes and enough identifiers for clients to act on

6. **Clarify unmapped rule implementation**
   - Acceptance criteria says active + reportable accounts cannot be left unmapped.
   - Implement validation so that for each active + reportable account, required mappings are checked.
   - Unless existing domain rules say otherwise, treat required statement coverage as:
     - Balance Sheet mapping required where applicable
     - Profit & Loss mapping required where applicable
     - Cash Flow mapping required where applicable
   - Before coding, inspect whether account type/category already determines applicable statement types.
   - If no such rule exists, implement the minimal deterministic rule consistent with current domain and document assumptions in code comments/tests.

7. **Implement company-scoped API endpoints**
   - Add endpoints under an existing company route pattern, e.g. similar to:
     - `GET /api/companies/{companyId}/financial-statement-mappings`
     - `POST /api/companies/{companyId}/financial-statement-mappings`
     - `PUT /api/companies/{companyId}/financial-statement-mappings/{mappingId}`
     - `GET /api/companies/{companyId}/financial-statement-mappings/validation`
   - Enforce tenant/company authorization using existing membership/policy patterns.
   - Ensure cross-company access returns forbidden/not found per existing conventions.

8. **Shape API contracts**
   - List response should include:
     - mapping id
     - company id
     - account id
     - account code/name if available
     - statement type
     - report section
     - line classification
     - isActive
     - timestamps
   - Create/update requests should accept the minimum required fields.
   - Validation response should include:
     - company id
     - summary counts
     - issues array
     - each issue with:
       - code
       - account id
       - mapping id if relevant
       - statement type if relevant
       - message
   - Keep response ordering deterministic, e.g. sort by account code/name/id then statement type then code.

9. **Handle conflict and constraint failures cleanly**
   - Catch DB unique constraint violations and translate them into deterministic API/application errors.
   - Do not leak raw PostgreSQL errors.
   - Preserve idempotent, predictable behavior for repeated conflicting requests.

10. **Add tests**
   - Add integration or API tests for:
     - listing mappings scoped to company
     - creating a valid mapping
     - updating a valid mapping
     - rejecting cross-company account references
     - rejecting duplicate active mapping for same account + statement type
     - validation endpoint returning unmapped active reportable accounts
     - validation endpoint returning conflicts deterministically
     - deterministic ordering and codes in validation response
   - Add persistence test coverage if the repo already tests migrations/index behavior.

11. **Keep implementation aligned with existing architecture**
   - Respect module boundaries:
     - API -> Application -> Domain/Infrastructure
   - Avoid direct DB logic in controllers.
   - Reuse existing base abstractions for:
     - tenant context
     - result handling
     - validation
     - endpoint registration

12. **Document assumptions in code**
   - If statement applicability rules are not fully modeled in the current domain, add concise comments and tests describing the chosen rule.
   - Do not add speculative complexity beyond this task.

# Validation steps
1. Restore/build and inspect baseline:
   - `dotnet build`

2. Run tests before changes if useful:
   - `dotnet test`

3. After implementation:
   - run migration/build/tests
   - ensure the new migration is included correctly
   - verify no compile errors across projects

4. Validate API behavior with tests or manual requests:
   - list endpoint returns only mappings for the requested company
   - create endpoint persists valid mapping
   - update endpoint modifies allowed fields only
   - validation endpoint returns deterministic issue codes and ordering
   - duplicate active mapping attempts fail with translated conflict error

5. Confirm DB constraint behavior:
   - verify unique active mapping per `(company_id, account_id, statement_type)`
   - verify inactive historical mappings do not violate the filtered uniqueness rule if supported by the design

6. Final verification:
   - `dotnet build`
   - `dotnet test`

# Risks and follow-ups
- **Risk: existing account model may differ from assumptions**
  - Mitigation: inspect current accounting/account entities first and adapt naming/relationships.

- **Risk: acceptance criteria around “cannot be left unmapped” may require statement-specific applicability rules not yet modeled**
  - Mitigation: implement the narrowest deterministic rule supported by current domain and capture assumptions in tests/comments.

- **Risk: PostgreSQL filtered unique index syntax/config may vary with current EF setup**
  - Mitigation: follow existing migration conventions and verify generated SQL.

- **Risk: tenant authorization conventions may already require specific route or policy patterns**
  - Mitigation: mirror an existing company-scoped API implementation exactly.

- **Follow-up likely needed**
  - UI for managing mappings
  - seed/reference data for valid report sections/classifications
  - richer accounting rules for statement applicability by account type
  - audit events for mapping changes if not already covered elsewhere