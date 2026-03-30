# Goal
Implement backlog task **TASK-9.1.7 — Access scope metadata must be tenant-aware** for **ST-301 Company document ingestion and storage**.

Ensure document access scope metadata is explicitly modeled, validated, persisted, and enforced as **company/tenant-scoped** so that uploaded knowledge documents cannot reference or leak scope information across tenants.

# Scope
In scope:
- Update the document ingestion/domain model so `access_scope` metadata is tenant-aware.
- Ensure all create/update/read paths for knowledge document metadata treat access scope as belonging to the current `company_id`.
- Add validation to reject ambiguous, missing, or cross-tenant scope references.
- Update persistence mappings/configuration as needed.
- Add/adjust tests covering tenant isolation and valid/invalid access scope scenarios.

Out of scope:
- Full retrieval-layer policy enforcement beyond what is necessary to keep metadata tenant-aware.
- New UI features beyond any minimal DTO/form binding changes required by the backend contract.
- Broader authorization redesign outside knowledge document access scope handling.
- Multi-region or per-database tenant partitioning changes.

Assumptions to verify in code:
- The solution uses shared-schema multi-tenancy with `company_id` on tenant-owned entities.
- `knowledge_documents.access_scope_json` already exists or is planned as JSON-backed metadata.
- There is an existing tenant/company context abstraction used by application services and repositories.
- Document ingestion APIs/commands already exist for ST-301 or are partially implemented.

# Files to touch
Inspect first, then modify only what is necessary in the relevant layers. Likely candidates:

- `src/VirtualCompany.Domain/**`
  - Knowledge document entity/value objects
  - Access scope model/value object
  - Domain validation rules
- `src/VirtualCompany.Application/**`
  - Commands/handlers for document upload/create/update
  - DTOs/contracts for access scope metadata
  - Validators
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - JSON conversion/mapping for access scope metadata
  - Repository/query enforcement for `company_id`
- `src/VirtualCompany.Api/**`
  - Request models/endpoints if access scope is accepted over HTTP
- `src/VirtualCompany.Web/**`
  - Only if request/response contracts require minimal updates
- Tests in whichever test projects exist
  - Domain tests
  - Application handler tests
  - Integration tests for persistence/API behavior

Also review:
- `README.md`
- Any existing architecture/conventions docs
- Solution-wide tenant abstractions and knowledge module files

# Implementation plan
1. **Discover the current knowledge document model**
   - Locate the `knowledge_documents` entity/model and all related DTOs, commands, handlers, EF mappings, and API contracts.
   - Find how `access_scope_json` is currently represented:
     - raw string/json
     - dictionary
     - strongly typed object
     - nullable/unvalidated blob
   - Identify where `company_id` is attached and how tenant context is resolved in the request pipeline.

2. **Define a tenant-aware access scope contract**
   - Introduce or refine a strongly typed access scope model/value object if one does not already exist.
   - The model should make tenant-awareness explicit. Prefer a shape that can only be interpreted within a company context, for example:
     - scope level/type values that are inherently company-local, and/or
     - scoped references that must belong to the same `company_id`.
   - Avoid designs that allow arbitrary global IDs without tenant validation.
   - If the current schema stores JSON, keep JSON persistence but strongly type it in code.

3. **Add domain/application validation rules**
   - Enforce that access scope metadata:
     - is present when required by the story flow
     - has a valid scope type
     - does not contain cross-tenant references
     - cannot specify another tenant/company explicitly unless the current company matches and that is part of the approved model
     - defaults safely if omitted only when such behavior already matches existing conventions
   - Use default-deny semantics for ambiguous scope metadata.
   - If scope references entities such as agents, roles, departments, or memberships, validate those references against the current company.

4. **Thread tenant context through ingestion flows**
   - In document upload/create/update handlers, ensure the current tenant/company context is used when constructing and validating access scope metadata.
   - Prevent clients from spoofing tenant ownership through request payloads.
   - If request contracts currently accept `companyId` or tenant-identifying scope fields from the client, ignore or reject them in favor of server-resolved tenant context.

5. **Update persistence mapping**
   - Ensure the access scope object serializes/deserializes cleanly to `access_scope_json`.
   - Confirm `knowledge_documents.company_id` remains the authoritative tenant owner.
   - If needed, add migration(s) only if the persisted shape must change.
   - If a migration is required:
     - keep it backward-safe
     - preserve existing records where possible
     - normalize legacy scope payloads into tenant-aware format

6. **Harden query/read behavior**
   - Ensure document reads always filter by `company_id`.
   - If access scope metadata is exposed in responses, return only the tenant-local representation needed by the caller.
   - Do not expose raw cross-tenant identifiers or unvalidated metadata.

7. **Add tests**
   - Domain tests:
     - valid tenant-aware scope accepted
     - invalid/unknown scope rejected
     - cross-tenant references rejected
   - Application tests:
     - upload/create command uses current tenant context
     - spoofed tenant/company values are ignored or rejected
     - same-company scoped references succeed
   - Integration tests if available:
     - persisted JSON round-trips correctly
     - querying from another tenant cannot access the document or its scope metadata

8. **Keep implementation aligned with architecture**
   - Respect clean architecture boundaries.
   - Keep tenant isolation enforced in application/repository layers.
   - Prefer explicit value objects and validators over ad hoc JSON manipulation.
   - Do not introduce direct DB access from API/UI layers.

Implementation notes:
- Since the story note explicitly says **“Access scope metadata must be tenant-aware”**, treat this as the primary behavioral requirement even though no separate acceptance criteria were provided for the task.
- Favor a model where access scope is interpreted relative to `knowledge_documents.company_id`, not as a globally portable scope blob.
- If there is existing retrieval code depending on the old shape, update only the minimum necessary contract adapters to avoid breaking downstream consumers.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects, run those as well for faster iteration.

4. Manually verify the following scenarios through tests or API/integration coverage:
   - Create/upload a document under Company A with valid Company A scope metadata → succeeds.
   - Create/upload a document under Company A with scope referencing Company B-owned entity/data → fails validation.
   - Create/upload a document with missing/ambiguous scope metadata → fails or safely defaults according to existing conventions.
   - Read/list document metadata from another tenant context → not found/forbidden and no metadata leakage.
   - Persisted `access_scope_json` round-trips to the typed model without loss.

5. If migrations are added:
   - Apply migration locally.
   - Verify existing/seeded records still load correctly.

# Risks and follow-ups
- **Risk: existing raw JSON usage**
  - If `access_scope_json` is currently loosely typed in multiple places, refactoring may have wider impact than expected.
- **Risk: hidden cross-tenant references**
  - Scope metadata may reference other entities indirectly; validate all referenced IDs against `company_id`.
- **Risk: contract breakage**
  - API/UI clients may already send a legacy scope shape. Preserve compatibility where reasonable or update callers in the same change.
- **Risk: incomplete enforcement**
  - Fixing ingestion metadata alone may not fully protect retrieval if downstream consumers assume trusted scope JSON. Audit retrieval/filtering paths and note any gaps.

Follow-ups to note in code comments or task summary if not completed here:
- Enforce the same tenant-aware scope semantics in semantic retrieval and context assembly paths for ST-302/ST-304.
- Consider adding reusable tenant-scoped reference validators for knowledge, memory, tasks, and approvals.
- Consider backfilling/normalizing legacy document scope metadata if pre-existing records are not guaranteed tenant-safe.