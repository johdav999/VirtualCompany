# Goal
Implement backlog task **TASK-32.3.1** by adding a production-ready, typed **FortnoxApiClient** for the .NET backend that can call the real Fortnox API at `https://api.fortnox.se/3`, handle token-aware authenticated HTTP requests, retries, rate limiting, pagination, and safe error translation, and support the real sync/read path needed for Fortnox-backed finance data.

This work must enable the application to:
- call real Fortnox endpoints for:
  - company information
  - customers
  - suppliers
  - invoices
  - supplier invoices
  - vouchers
  - accounts
  - articles
  - projects
- support authenticated `GET`, `POST`, `PUT`, and `DELETE`
- support query parameters including:
  - `lastmodified`
  - `fromdate`
  - `todate`
  - `sortby`
  - `sortorder`
  - `page`
  - `limit`
- translate Fortnox API failures into:
  - safe user-facing messages
  - detailed internal logs
- provide pagination primitives and response models suitable for incremental sync jobs
- fit the existing modular monolith architecture and tenant-aware integration model

Do **not** implement fake/simulated-only behavior in this client. The client should be designed for real API usage and safe production operation.

# Scope
In scope:
- Add or complete a typed Fortnox API client abstraction in the Infrastructure/Application layers.
- Add request/response DTOs and endpoint-specific methods for the required Fortnox resources.
- Add token-aware `HttpClient` configuration using the real Fortnox base URL.
- Add retry handling for transient failures.
- Add rate-limit aware handling for Fortnox throttling responses.
- Add pagination support, including helper models for paged reads.
- Add safe error translation from Fortnox error payloads/status codes into internal exception/result types.
- Add structured logging with tenant/integration context where available.
- Wire the client into DI.
- Add tests for:
  - auth header behavior
  - query parameter generation
  - pagination parsing
  - retry/rate-limit behavior
  - error translation

Also in scope if needed to satisfy acceptance criteria:
- Small supporting abstractions for Fortnox auth/token resolution if the project already has integration credential storage patterns.
- Minimal sync-facing contracts needed so downstream incremental sync work can consume this client cleanly.

Out of scope unless absolutely required by existing code structure:
- Full end-to-end sync orchestration for all entities
- UI work for finance pages
- broad refactors unrelated to Fortnox integration
- replacing existing simulation services beyond introducing the real client path
- schema changes for sync state/history unless directly necessary and already partially present

# Files to touch
Inspect the solution first and then update the most appropriate files. Expected areas include:

- `src/VirtualCompany.Infrastructure/**`
  - integration clients
  - HTTP handlers/policies
  - DI registration
  - Fortnox DTOs/models
  - Fortnox error translation
- `src/VirtualCompany.Application/**`
  - integration abstractions/interfaces
  - sync-facing contracts if the client is consumed from application services
- `src/VirtualCompany.Domain/**`
  - only if shared domain-safe error/result types already live here
- `src/VirtualCompany.Api/**`
  - only if registration/config binding lives here
- `tests/VirtualCompany.Api.Tests/**`
  - add focused tests for API client behavior
- potentially `README.md` or relevant docs if there is an integration setup section

Before coding, locate any existing Fortnox-related files, finance integration models, sync state entities, and HTTP client conventions. Reuse existing patterns rather than inventing parallel ones.

# Implementation plan
1. **Discover existing integration structure**
   - Search for:
     - `Fortnox`
     - `FinanceIntegration`
     - `FinanceIntegrationSyncStates`
     - `FinanceExternalReferences`
     - existing `HttpClient` registrations
     - token storage/credential providers
     - retry/polly usage
   - Identify whether there is already:
     - a Fortnox interface stub
     - simulation service
     - sync job skeleton
     - finance entity mapping layer
   - Follow existing naming and layering conventions.

2. **Define the typed client contract**
   - Add an interface such as `IFortnoxApiClient` in the appropriate Application or Infrastructure abstraction layer.
   - Include endpoint methods for:
     - company information
     - customers
     - suppliers
     - invoices
     - supplier invoices
     - vouchers
     - accounts
     - articles
     - projects
   - Include generic support for:
     - `GetAsync`
     - `PostAsync`
     - `PutAsync`
     - `DeleteAsync`
     only if that matches project conventions; otherwise expose resource-specific methods only.
   - Add request option models for query parameters:
     - `lastmodified`
     - `fromdate`
     - `todate`
     - `sortby`
     - `sortorder`
     - `page`
     - `limit`
   - Prefer strongly typed option objects over raw dictionaries.

3. **Implement token-aware HttpClient**
   - Register a named or typed `HttpClient` with base address:
     - `https://api.fortnox.se/3`
   - Add a delegating handler or equivalent mechanism that resolves tenant/integration-specific credentials/tokens at request time.
   - Ensure required Fortnox auth headers are applied according to the project’s credential model.
   - Do not hardcode secrets.
   - Ensure the client is safe for multi-tenant use:
     - no static mutable auth state
     - no token leakage across tenants
   - If the app already uses per-request tenant context, integrate with that pattern.

4. **Add DTOs and endpoint wrappers**
   - Create typed request/response DTOs for the required resources.
   - Model Fortnox envelope/paging structures accurately enough for real API responses.
   - Add endpoint methods such as:
     - `GetCompanyInformationAsync`
     - `GetCustomersAsync`
     - `GetSuppliersAsync`
     - `GetInvoicesAsync`
     - `GetSupplierInvoicesAsync`
     - `GetVouchersAsync`
     - `GetAccountsAsync`
     - `GetArticlesAsync`
     - `GetProjectsAsync`
   - Add create/update/delete methods where acceptance criteria require authenticated `POST`, `PUT`, `DELETE`.
   - Keep serialization options consistent with existing project JSON conventions.

5. **Implement query parameter builder**
   - Build query strings from typed option objects.
   - Only emit supported/non-null parameters.
   - Ensure correct formatting for dates/timestamps expected by Fortnox.
   - Add tests to verify exact query output for combinations of:
     - `lastmodified`
     - `fromdate`
     - `todate`
     - `sortby`
     - `sortorder`
     - `page`
     - `limit`

6. **Implement pagination support**
   - Add a reusable paged response model that captures:
     - items
     - page
     - limit
     - total/pages/metadata if available from Fortnox
     - next-page capability if inferable
   - Add helper methods for iterating paged endpoints safely.
   - Do not auto-fetch unbounded pages by default unless there is already a project pattern for async enumeration.
   - Make the client usable by incremental sync jobs that need deterministic page-by-page processing.

7. **Implement retry and rate-limit handling**
   - Add resilience for transient failures:
     - network failures
     - 5xx responses
     - possibly 408/429 as appropriate
   - Respect Fortnox rate limiting:
     - detect `429 Too Many Requests`
     - honor `Retry-After` if present
     - otherwise use bounded backoff with jitter
   - Keep retries bounded and log each retry attempt at appropriate level.
   - Do not retry non-transient 4xx errors except explicit throttling cases.

8. **Implement safe error translation**
   - Parse Fortnox error payloads where available.
   - Map common failure classes into internal safe exceptions/results, for example:
     - unauthorized/expired credentials
     - forbidden/missing scope
     - not found
     - validation/business rule errors
     - throttling
     - upstream unavailable
   - User-facing messages must be safe and actionable, e.g.:
     - “Fortnox connection needs attention.”
     - “The connected Fortnox account does not have permission for this data.”
     - “Fortnox is temporarily unavailable. Please try again shortly.”
   - Internal logs should include:
     - status code
     - Fortnox error code/message if present
     - endpoint
     - tenant/company/integration identifiers where available
     - correlation/request IDs if available
   - Do not expose secrets, raw tokens, or overly detailed upstream internals in user-facing messages.

9. **Wire into DI and consuming services**
   - Register the typed client and any handlers/providers in DI.
   - Update any existing Fortnox integration service to consume the new client.
   - If there is a simulation service, preserve compatibility while enabling the real client path for synced records.
   - Keep changes minimal and aligned with acceptance criteria.

10. **Add tests**
   - Add unit/integration-style tests using mocked `HttpMessageHandler` or existing test infrastructure.
   - Cover:
     - base URL usage
     - auth header injection
     - query parameter serialization
     - pagination parsing
     - retry on transient failure
     - rate-limit handling with `Retry-After`
     - safe error translation for 401/403/404/422/429/5xx
   - If there is an existing test pattern for typed clients, follow it.

11. **Document assumptions and gaps**
   - If Fortnox auth header requirements depend on existing credential storage not yet implemented, integrate with the current abstraction and leave a concise TODO only where unavoidable.
   - If some endpoints have slightly different envelope shapes, model them explicitly rather than forcing one brittle generic parser.

# Validation steps
1. **Codebase inspection**
   - Confirm the chosen implementation matches existing architecture and naming conventions.
   - Confirm no duplicate Fortnox client abstractions were introduced.

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Behavior verification**
   - Verify the typed client is registered in DI.
   - Verify the configured base address is exactly:
     - `https://api.fortnox.se/3`
   - Verify required Fortnox auth headers are attached per request using the tenant/integration credential source.
   - Verify query parameters are emitted correctly and omitted when null.
   - Verify paged endpoints deserialize correctly from representative Fortnox payloads.
   - Verify retry behavior occurs for transient failures and not for permanent validation/auth failures.
   - Verify `429` handling respects `Retry-After` when present.
   - Verify translated exceptions/messages are safe for end users and detailed in logs.

5. **Acceptance criteria traceability**
   - In your final implementation notes or PR summary, explicitly map the code to these acceptance criteria:
     - real Fortnox endpoint support
     - authenticated GET/POST/PUT/DELETE
     - query parameter support
     - safe error translation
     - pagination support
     - readiness for incremental sync and deduplicated external references

# Risks and follow-ups
- **Auth model ambiguity:** Fortnox may require specific header combinations depending on the app setup. Reuse existing credential abstractions and do not guess beyond what the current integration model supports.
- **Envelope differences across endpoints:** Fortnox resources may not all share identical response shapes. Prefer explicit DTOs per resource where needed.
- **Rate-limit semantics:** If Fortnox returns custom headers in addition to `Retry-After`, capture them in logs and leave the implementation extensible.
- **Incremental sync dependency:** This task should prepare the client for sync usage, but full cursor persistence and deduplication may depend on adjacent tasks if not already present.
- **Simulation coexistence:** Be careful not to break current finance pages or tests that still rely on simulation paths; introduce the real client path cleanly.
- **Logging sensitivity:** Never log tokens, authorization headers, or raw secret material.
- **Follow-up likely needed:** after this client lands, the next work will likely wire entity-specific sync jobs, cursor persistence in `FinanceIntegrationSyncStates`, mapping into local finance models, and UI read paths that prefer synced Fortnox-backed records.