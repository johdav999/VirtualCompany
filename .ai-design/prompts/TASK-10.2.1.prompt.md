# Goal
Implement backlog task **TASK-10.2.1** for **ST-402 — Workflow definitions, instances, and triggers** by adding backend support for **workflow templates/definitions with versioned JSON definitions** in the existing .NET modular monolith.

This task should establish the **domain, persistence, application, and API foundations** for workflow definitions so the system can:
- store workflow definitions as JSON,
- version them safely,
- distinguish system/global templates from company-owned definitions,
- query active/latest versions,
- preserve version immutability for in-flight workflow compatibility later.

Focus on a clean first implementation that fits the architecture and backlog direction:
- PostgreSQL primary store
- shared-schema multi-tenancy with `company_id`
- CQRS-lite application layer
- JSONB-backed flexible definitions
- versioned workflow definitions as a first-class workflow subsystem concept

Do **not** overreach into full workflow execution unless required to support definition/version persistence.

# Scope
In scope:
- Add or complete the `workflow_definitions` model aligned with architecture:
  - `id`
  - `company_id` nullable for system templates
  - `code`
  - `name`
  - `department`
  - `version`
  - `trigger_type`
  - `definition_json`
  - `active`
  - timestamps
- Enforce versioned definition behavior:
  - multiple versions per `(company_id, code)` allowed
  - each version immutable after creation, except possibly activation metadata if your existing patterns require it
  - version numbers increment predictably
- Add application use cases for:
  - create workflow definition
  - create new version from an existing code
  - get workflow definition by id
  - list workflow definitions with latest/active filtering
- Add server-side validation for JSON definition payload shape at a pragmatic level:
  - must be valid JSON object
  - must include minimum required fields for a workflow definition contract
  - reject malformed or empty definitions
- Add persistence configuration and migration for PostgreSQL/EF Core
- Add tenant-aware authorization/scoping in handlers/repositories/controllers/endpoints
- Add tests covering versioning and tenant isolation

Out of scope unless already trivial in this codebase:
- full workflow runner
- schedule engine
- event trigger dispatcher
- workflow instance progression
- UI workflow builder
- arbitrary drag/drop workflow design
- background worker execution beyond any minimal plumbing already present
- deep JSON schema engine unless there is already an established validation framework in the repo

If the repository already contains partial workflow entities or handlers, extend/refactor them instead of duplicating.

# Files to touch
Touch only the files needed to implement this task cleanly. Likely areas:

- `src/VirtualCompany.Domain/...`
  - workflow definition entity/aggregate
  - value objects or enums for trigger type/status if used
  - domain validation/helpers
- `src/VirtualCompany.Application/...`
  - commands/queries for workflow definitions
  - DTOs/contracts
  - validators
  - handlers/services/interfaces
- `src/VirtualCompany.Infrastructure/...`
  - EF Core entity configuration
  - DbContext updates
  - repository implementation if applicable
  - migration(s)
- `src/VirtualCompany.Api/...`
  - endpoints/controllers for workflow definitions
  - request/response contracts if API owns them
  - authorization/tenant resolution wiring
- `src/VirtualCompany.Shared/...`
  - shared contracts/enums only if this solution already centralizes them here
- `tests/...` or existing test projects
  - domain/application/integration tests for versioning, validation, and tenant scoping

Also inspect before coding:
- `README.md`
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`
- existing task/workflow/tenant patterns in the solution

Do not create new architectural layers or patterns inconsistent with the current codebase.

# Implementation plan
1. **Inspect existing workflow and tenancy patterns**
   - Search for:
     - workflow-related entities, DTOs, endpoints, migrations
     - tenant-scoped base entities/interfaces
     - command/query handler conventions
     - validation approach (FluentValidation, data annotations, custom)
     - API style (Minimal APIs vs Controllers)
   - Reuse existing conventions exactly.

2. **Define the workflow definition domain model**
   - Add or update a `WorkflowDefinition` entity in the domain layer.
   - Include fields from the architecture model.
   - Represent `trigger_type` as an enum/string-backed enum if the codebase already uses that pattern.
   - Add domain invariants:
     - `code` required, normalized consistently
     - `name` required
     - `version >= 1`
     - `definition_json` required and must represent a JSON object
     - `company_id` nullable only for system templates
   - Prefer immutable version records:
     - creating a new version creates a new row
     - avoid mutating prior version payloads

3. **Define a minimal JSON contract for `definition_json`**
   - Since acceptance criteria are sparse, implement a pragmatic minimum contract for validation.
   - Require the JSON object to contain at least:
     - a root object
     - a `steps` array
   - Optionally require:
     - `name` or `displayName` if not redundant with DB fields
     - trigger metadata section if useful
   - Keep validation intentionally lightweight and extensible.
   - Document assumptions in code comments where appropriate.

4. **Persistence mapping**
   - Add EF Core configuration for `workflow_definitions`.
   - Map JSON payload to PostgreSQL `jsonb`.
   - Add indexes/constraints appropriate for versioning and lookup, for example:
     - unique on `(company_id, code, version)` if PostgreSQL null semantics are handled correctly
     - index on `(company_id, code)`
     - index on `(company_id, active)`
   - Be careful with nullable `company_id` for system templates:
     - if using a unique index, ensure system templates are also protected from duplicate `(null, code, version)` cases, potentially via filtered indexes or normalized uniqueness strategy depending on current migration style.
   - Add timestamps per existing conventions.

5. **Application layer use cases**
   Implement CQRS-lite operations:
   - **CreateWorkflowDefinition**
     - creates version `1` for a new `(company_id, code)` or system template code
     - rejects duplicates for same `(scope, code, version)`
   - **CreateWorkflowDefinitionVersion**
     - loads latest version for `(company_id, code)`
     - creates next version number
     - stores new JSON definition
     - optionally supports setting new version active and deactivating prior active version if that matches existing patterns
   - **GetWorkflowDefinitionById**
   - **ListWorkflowDefinitions**
     - support filters like:
       - company scope
       - code
       - active only
       - latest only
       - include system templates if relevant to current API design
   - Return DTOs that include:
     - id, companyId, code, name, department, version, triggerType, active, createdAt, updatedAt
     - definitionJson if appropriate for detail endpoint

6. **Versioning rules**
   Implement explicit rules:
   - Version numbers are sequential per `(company_id, code)`.
   - Existing versions are not overwritten.
   - New versions do not break old ones because old rows remain intact.
   - Decide activation behavior and keep it consistent:
     - either allow multiple active versions temporarily, or
     - enforce one active version per `(company_id, code)` and deactivate previous active version when a new one is activated.
   - Prefer **one active version per scope/code** if easy to enforce and consistent with backlog intent.
   - If enforcing one active version, do it in application logic first; add DB support only if straightforward in current stack.

7. **Tenant isolation**
   - Ensure all company-owned workflow definition operations are scoped by resolved tenant/company context.
   - Prevent cross-tenant reads/writes.
   - For system templates (`company_id = null`), expose them read-only unless there is already an admin/system seeding pattern.
   - Do not allow a tenant user to mutate another tenant’s definitions or global templates unless existing authorization explicitly supports platform admin behavior.

8. **API surface**
   Add or extend endpoints for:
   - `POST /api/workflows/definitions`
   - `POST /api/workflows/definitions/{id}/versions` or `POST /api/workflows/definitions/{code}/versions`
   - `GET /api/workflows/definitions/{id}`
   - `GET /api/workflows/definitions`
   Follow existing API conventions for:
   - tenant resolution
   - auth attributes/policies
   - problem details / validation responses
   - pagination if already standard

   Suggested request shape for create:
   - `code`
   - `name`
   - `department`
   - `triggerType`
   - `definitionJson`
   - `active`

   Suggested request shape for new version:
   - `name` optional
   - `department` optional
   - `triggerType`
   - `definitionJson`
   - `active`

9. **Migration**
   - Add EF migration for `workflow_definitions` if not already present.
   - Ensure PostgreSQL-compatible JSONB and indexes.
   - If the table already exists but differs, create a safe migration to align it rather than replacing it destructively.

10. **Tests**
   Add tests at the appropriate layers:
   - creating initial version stores version `1`
   - creating a new version increments version
   - prior version remains unchanged
   - invalid JSON/root array/null payload rejected
   - missing required `steps` rejected
   - tenant A cannot read/write tenant B definitions
   - list latest-only returns only newest version per code
   - active filtering behaves as expected
   - system template behavior works per chosen authorization model

11. **Seed/future-proofing**
   - If the repo already has seed infrastructure, consider adding one or two system workflow templates only if low effort and clearly aligned.
   - Otherwise leave seeding for a separate task and keep this task focused on platform capability.

12. **Implementation quality bar**
   - Keep code cohesive and minimal.
   - Prefer explicit naming over generic “config” abstractions.
   - Add concise comments only where business rules are non-obvious.
   - Do not introduce speculative workflow execution engine code.

# Validation steps
Run the relevant local validation after implementation:

1. Restore/build
   - `dotnet build`

2. Run tests
   - `dotnet test`

3. If migrations are used in the normal workflow:
   - generate/apply migration as appropriate for the repo conventions
   - verify migration compiles and database updates cleanly

4. Manually verify API behavior if there is an API test harness/Swagger:
   - create workflow definition for tenant A
   - create second version for same code
   - confirm version increments and old version remains queryable
   - confirm list endpoint can return latest-only and/or active-only
   - confirm tenant B cannot access tenant A definition
   - confirm malformed JSON or missing `steps` returns validation error

5. Confirm persistence details:
   - `definition_json` stored as JSONB
   - uniqueness/index behavior works for version lookup
   - timestamps populated per project conventions

Include in your final implementation summary:
- files changed
- assumptions made about JSON contract
- versioning rules implemented
- any follow-up gaps intentionally left for later tasks

# Risks and follow-ups
Risks:
- Existing codebase may already have partial workflow models; avoid conflicting duplicates.
- Nullable `company_id` uniqueness for system templates can be tricky in PostgreSQL; implement carefully.
- Over-validating JSON now could block future workflow shapes; keep validation minimal and extensible.
- Activation semantics may affect future workflow instance startup behavior; document the chosen rule.
- If tenant resolution is inconsistently implemented across the repo, this task may expose broader authorization cleanup needs.

Follow-ups to note if not completed here:
- workflow instance creation from selected definition version
- schedule trigger registration and distributed locking
- internal event trigger dispatch
- blocked/failed step exception surfacing
- richer JSON schema/version compatibility validation
- predefined workflow template seeding
- audit events for definition creation/versioning
- UI management screens for workflow definitions