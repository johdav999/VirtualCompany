# Goal
Implement backlog task **TASK-33.2.1** for story **US-33.2 Build production Fortnox sync pipeline and normalized finance data mapping**.

Deliver a production-ready Fortnox finance integration in the existing .NET modular monolith that:

- Calls real **Fortnox production finance endpoints**
- Supports **resilient HTTP handling**
- Supports **pagination**
- Integrates with existing **token refresh/auth flow**
- Normalizes imported data into existing **internal finance contracts**
- Persists **tenant-scoped external references**
- Ensures **idempotent re-sync behavior**
- Records **sync history, counts, duration, retries, and safe error summaries**
- Translates provider/API errors into **plain-English user-facing messages**
- Preserves detailed diagnostics in **server logs**
- Enforces **tenant isolation and permission checks** on all reads/writes

Do not build a provider-payload-as-source-of-truth design. Fortnox payloads may be used transiently for mapping and diagnostics, but normalized internal records must remain the system of record.

# Scope
In scope:

- Add or complete a **Fortnox API client** in Infrastructure for these entities:
  - company information
  - customers
  - suppliers
  - invoices
  - supplier invoices
  - vouchers
  - accounts
  - articles
  - projects
- Implement:
  - authenticated HTTP requests against Fortnox production endpoints
  - token refresh integration using existing auth/token infrastructure if present
  - retry/backoff for transient failures
  - timeout/cancellation support
  - pagination traversal
  - provider error parsing and translation
- Add or complete application-layer sync orchestration that:
  - fetches provider data
  - maps into existing internal finance contracts/entities
  - upserts records idempotently
  - writes/updates external reference mappings
  - records sync history and per-entity counts
- Ensure all operations are tenant-scoped and permission-checked
- Add tests for client behavior, mapping/upsert behavior, and sync result handling

Out of scope unless required by existing architecture to complete acceptance criteria:

- New UI beyond what is minimally needed for existing manual sync endpoint to function
- Webhook ingestion
- Storing raw Fortnox payloads as canonical records
- Broad refactors unrelated to finance sync
- New provider integrations beyond Fortnox

# Files to touch
Inspect the solution first and then update the exact files that fit the existing architecture. Likely areas:

- `src/VirtualCompany.Infrastructure/**`
  - Fortnox API client(s)
  - HTTP resilience configuration
  - token refresh/auth provider integration
  - provider DTOs
  - repository implementations if sync persistence lives here
- `src/VirtualCompany.Application/**`
  - sync command/handler or service orchestration
  - finance normalization/mapping services
  - permission checks
  - sync history recording
  - user-facing error translation contracts
- `src/VirtualCompany.Domain/**`
  - external reference model/value objects if missing
  - sync history/status models if missing
  - finance entity contracts if small additions are required
- `src/VirtualCompany.Api/**`
  - manual sync endpoint wiring if not already present
  - DI registration
  - authorization/policy enforcement
- `src/VirtualCompany.Shared/**`
  - shared contracts/enums if used across layers
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration-style tests
- Add test projects/files in other existing test folders if present and more appropriate

Before editing, locate existing implementations for:

- tenant resolution / company context
- authorization policies
- integration connection storage
- OAuth/token refresh handling
- sync history
- external reference mapping
- finance domain contracts/entities
- retry/outbox/background job patterns
- typed `HttpClient` / Polly / resilience pipeline usage

Prefer extending existing patterns over introducing parallel abstractions.

# Implementation plan
1. **Discover existing architecture and reuse points**
   - Search for:
     - integration module patterns
     - Fortnox-related code
     - token storage/refresh services
     - sync history entities/tables
     - external reference entities/tables
     - finance contracts/entities
     - manual sync endpoint
     - tenant authorization patterns
   - Identify the canonical place for:
     - provider client
     - sync orchestration
     - normalized finance persistence
     - user-facing integration errors

2. **Define provider client contract**
   - Create or complete a typed Fortnox client abstraction with methods for:
     - `GetCompanyInformation`
     - paged fetches for customers, suppliers, invoices, supplier invoices, vouchers, accounts, articles, projects
   - Keep provider DTOs isolated to Infrastructure/provider boundary.
   - Ensure methods accept:
     - tenant/integration context
     - cancellation token
     - optional page cursor/page number if needed internally

3. **Implement resilient HTTP handling**
   - Use existing resilience stack if present; otherwise use standard .NET resilience patterns already used in the solution.
   - Include:
     - retry for transient network/5xx/429 failures
     - bounded timeout
     - cancellation propagation
     - structured logging with tenant/integration correlation
   - Do not retry:
     - authorization/business-invalid requests that are clearly permanent
   - Parse Fortnox error responses and preserve diagnostics in logs.

4. **Integrate token refresh**
   - Reuse existing token management flow if available.
   - On expired/invalid access token scenarios:
     - attempt refresh through existing token refresh service
     - persist refreshed token through existing secure storage path
     - replay the failed request once when appropriate
   - Avoid infinite refresh loops.
   - If refresh fails, return a user-safe integration/authentication error.

5. **Implement pagination**
   - Support Fortnox pagination for all list endpoints.
   - Fetch all pages safely and deterministically.
   - Guard against:
     - empty pages
     - malformed pagination metadata
     - duplicate page processing
   - Return a complete sequence to the application layer without leaking provider pagination concerns upward unless existing architecture expects streaming/page-wise processing.

6. **Map provider data into normalized internal finance contracts**
   - For each Fortnox entity, map only the fields needed by the existing internal finance model.
   - Do not persist raw provider payloads as the source of truth.
   - If the internal model lacks required fields for acceptance criteria, add minimal targeted extensions.
   - Keep mapping logic explicit and testable.

7. **Implement idempotent upsert + external reference persistence**
   - For each imported record:
     - resolve existing tenant-scoped external reference by:
       - provider name
       - external id
       - entity type
       - tenant/company id
     - if found, update the linked internal record
     - if not found, create the internal record and then create the external reference
   - Persist/update external reference fields:
     - provider name
     - external id
     - internal id
     - entity type
     - last synced timestamp
   - Ensure repeated sync of unchanged records does not create duplicates.
   - If existing architecture supports content hash/version/updated-at comparison, use it to avoid unnecessary writes.

8. **Record sync history**
   - For each manual sync execution, persist:
     - tenant/company id
     - provider/integration
     - start/end timestamps
     - duration
     - final status
     - per-entity counts
     - retry/failure summary as appropriate
     - safe user-facing error summary on failure
   - Log detailed diagnostics separately in server logs.
   - Ensure partial failures are represented clearly if the existing sync model supports them.

9. **Translate provider errors**
   - Add a translation layer from Fortnox/provider errors to plain-English messages suitable for API responses/UI.
   - Examples:
     - auth expired / reconnect required
     - permission missing in Fortnox
     - rate limited / try again later
     - provider unavailable
     - malformed provider response
   - Keep raw provider details out of user-facing responses.
   - Preserve full details in structured logs with correlation IDs.

10. **Enforce tenant isolation and permissions**
    - Ensure manual sync endpoint and downstream services:
      - resolve active tenant/company context
      - verify caller has permission to run finance sync
      - scope all reads/writes to that tenant only
    - Never query or update external references, sync history, or finance records without tenant/company filters.

11. **Wire endpoint/application flow**
    - Ensure the manual sync endpoint triggers the Fortnox sync orchestration using existing CQRS/service patterns.
    - Return a safe response containing:
      - status
      - counts if appropriate
      - sync history id/reference if existing API style supports it
      - user-safe error message on failure

12. **Add tests**
    - Unit tests:
      - pagination traversal
      - retry classification
      - token refresh replay behavior
      - error translation
      - mapping logic
      - idempotent upsert/external reference behavior
    - API/application tests:
      - authorized tenant can trigger sync
      - unauthorized/wrong-tenant access is blocked
      - sync history is recorded
      - repeated sync does not duplicate records
    - Mock provider HTTP responses using the project’s existing test style.

13. **Keep implementation aligned with existing codebase conventions**
    - Follow current naming, DI registration, logging, result/error patterns, and folder structure.
    - Keep changes focused and production-oriented.
    - Add concise comments only where behavior is non-obvious.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there is an existing manual sync endpoint test path, verify:
   - authorized tenant request succeeds
   - unauthorized request is rejected
   - wrong-tenant data is not accessible

4. Verify Fortnox client behavior with tests covering:
   - successful single-page fetch
   - successful multi-page fetch
   - transient failure retry
   - 429 handling
   - token refresh then request replay
   - refresh failure path
   - non-retryable 4xx handling

5. Verify sync persistence behavior:
   - first sync creates normalized internal records
   - external references are created with required fields
   - second sync of unchanged data updates mappings idempotently without duplicates
   - sync history stores status, counts, duration, and safe error summary

6. Verify error handling:
   - user-facing responses contain plain-English safe messages
   - logs retain detailed provider diagnostics
   - provider payloads are not persisted as system-of-record data

7. If local configuration supports it, run a manual smoke test against a Fortnox sandbox/production-compatible setup without committing secrets.

# Risks and follow-ups
- **Unknown existing finance model shape**: internal finance contracts may not yet cover all Fortnox entities cleanly; make minimal schema/domain additions only where necessary.
- **Unknown token infrastructure**: if token refresh is partially implemented, integrate with it rather than replacing it; document any gaps.
- **Pagination contract differences**: Fortnox endpoint pagination may vary by entity; verify each endpoint’s response shape before finalizing abstractions.
- **Rate limiting**: aggressive full syncs may hit provider limits; keep retry bounded and surface safe retry-later messaging.
- **Large sync duration**: if manual sync becomes long-running, a follow-up may be needed to move execution to background jobs with polling/status endpoints.
- **Partial failure semantics**: if one entity type fails mid-run, confirm whether the existing product expects fail-fast or partial-success sync history; implement the pattern already used elsewhere.
- **Schema gaps**: if external reference or sync history persistence does not exist yet, add it in the smallest way consistent with the architecture and note any migration requirements.
- **Observability**: ensure correlation IDs, tenant context, and provider/entity labels are included in logs for supportability.
- **Security**: never log secrets/tokens or expose raw provider error bodies to users.