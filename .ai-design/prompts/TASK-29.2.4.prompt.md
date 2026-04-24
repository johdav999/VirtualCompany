# Goal

Implement backlog task **TASK-29.2.4 — Expose normalized insight query endpoints for dashboard and contextual entity views** for story **US-29.2 Unified financial check framework and insight persistence**.

Deliver a coding change in the existing .NET solution that exposes **tenant-scoped query APIs** returning a **single normalized insight response shape** for:
- dashboard/company-wide financial insights
- contextual entity views (for example customer/vendor/account/invoice-related entity pages, depending on current domain model)

The implementation must align with the acceptance criteria by ensuring the API surface is built around the already-normalized/persisted `FinanceAgentInsight` model and does **not** require check-specific branching in consumers.

# Scope

In scope:
- Add or extend application query models for normalized finance insights.
- Add API endpoints for:
  - dashboard insight listing
  - entity-scoped insight listing
- Ensure both endpoints return the **same response DTO shape**.
- Support filtering/sorting appropriate for dashboard and entity pages, such as:
  - active vs resolved
  - severity
  - status
  - entity reference
  - created/updated timestamps
  - optional check type/category if already present in persistence model
- Enforce tenant/company scoping on all queries.
- Map persisted `FinanceAgentInsight` records into a normalized API contract containing at least:
  - id
  - severity
  - message
  - recommendation
  - entity reference
  - status
  - createdAt
  - updatedAt
- Add tests covering endpoint behavior, tenant isolation, and normalized response shape consistency.

Out of scope unless required by compilation:
- Reworking the financial check execution pipeline itself.
- Rebuilding persistence if `FinanceAgentInsight` already exists and is queryable.
- UI changes in Blazor or MAUI.
- Introducing separate response contracts per check type.
- Adding speculative fields not supported by the current domain model.

If the current codebase does not yet contain `FinanceAgentInsight` query infrastructure, implement the minimum necessary query/repository/read-model support to expose it cleanly.

# Files to touch

Prefer touching only the minimal set needed, likely within these areas:

- `src/VirtualCompany.Api/**`
  - finance insight controller/endpoints
  - request/response contracts if API-local
  - route registration if minimal APIs are used
- `src/VirtualCompany.Application/**`
  - query objects/handlers
  - normalized DTO/read models
  - mapping logic
  - authorization/tenant-scoped query services
- `src/VirtualCompany.Domain/**`
  - only if a shared insight contract/value object/entity reference abstraction is missing and truly needed
- `src/VirtualCompany.Infrastructure/**`
  - EF Core query/repository implementation
  - entity configuration if needed for read access
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests
  - serialization/contract consistency tests
  - tenant isolation tests

Also inspect:
- existing finance module structure
- existing CQRS/query patterns
- existing tenant resolution patterns
- existing controller conventions
- existing pagination/filter request patterns
- existing test fixtures for authenticated tenant-scoped API tests

Do not modify unrelated mobile/web code unless shared contracts require it.

# Implementation plan

1. **Inspect existing finance insight implementation**
   - Find the `FinanceAgentInsight` entity/model, related enums, and persistence configuration.
   - Identify whether there is already a finance insights repository/query service.
   - Confirm what fields exist for entity reference. If entity reference is modeled as type/id or similar, preserve that structure in the API contract.
   - Identify current API style:
     - MVC controllers
     - minimal APIs
     - MediatR/CQRS handlers
     - custom result wrappers/pagination envelopes

2. **Define a normalized response contract**
   - Create a single DTO used by both dashboard and entity endpoints, for example:
     - `FinanceInsightDto`
   - Include only stable normalized fields backed by persistence:
     - `id`
     - `severity`
     - `message`
     - `recommendation`
     - `entityReference`
     - `status`
     - `createdAt`
     - `updatedAt`
   - If the persisted model includes a useful normalized discriminator such as `insightType`, `category`, or `sourceCheck`, include it only if it does not force client branching.
   - Model `entityReference` as a normalized nested object if possible, e.g.:
     - `entityType`
     - `entityId`
     - optional `displayName` only if already available without expensive joins

3. **Add application-layer queries**
   - Implement a dashboard query, e.g. `GetFinanceInsightsQuery`, supporting:
     - tenant/company id
     - optional status filter
     - optional severity filter
     - optional pagination
     - sort by most recently updated/created
   - Implement an entity-scoped query, e.g. `GetEntityFinanceInsightsQuery`, supporting:
     - tenant/company id
     - entity type
     - entity id
     - optional status/severity filters
   - Ensure both handlers return the same DTO collection/envelope shape.

4. **Implement infrastructure query access**
   - Add EF Core/repository query logic against `FinanceAgentInsight`.
   - Enforce `company_id` filtering first.
   - Ensure entity filtering uses indexed/filterable fields if available.
   - Avoid loading unrelated navigation graphs.
   - Prefer projection directly to DTO/read model where consistent with current architecture.

5. **Expose API endpoints**
   - Add endpoints under the existing finance/insights route conventions, for example:
     - `GET /api/finance/insights`
     - `GET /api/finance/insights/by-entity` or `GET /api/entities/{entityType}/{entityId}/finance-insights`
   - Use the project’s established route style if one already exists.
   - Both endpoints must return the same normalized response shape.
   - Apply authentication and tenant/company resolution consistently with the rest of the API.
   - Return appropriate status codes:
     - `200 OK` for successful queries
     - `400 Bad Request` for invalid filter/entity inputs
     - `403/404` according to existing tenant authorization conventions

6. **Keep dashboard and entity responses branch-free**
   - Ensure the response contract is identical between endpoints.
   - Do not expose check-specific payload fragments.
   - Do not return polymorphic result bodies.
   - If metadata is needed, keep it generic and normalized.

7. **Add tests**
   - Add API tests covering:
     - dashboard endpoint returns normalized insight list
     - entity endpoint returns normalized insight list
     - both endpoints serialize the same item shape
     - tenant isolation prevents cross-company access/data leakage
     - status filtering includes active/resolved behavior correctly from persisted records
     - entity filtering returns only matching entity references
   - If there are existing integration-style API tests with seeded DB data, follow that pattern.
   - Add at least one test proving resolved insights are returned as persisted records rather than requiring special check-specific logic.

8. **Document assumptions in code comments only where necessary**
   - Keep comments concise.
   - If entity reference semantics are ambiguous in the current model, document the chosen mapping in the DTO or handler.

9. **Build and refine**
   - Run build/tests.
   - Fix nullability, serialization naming, and enum formatting issues.
   - Ensure timestamps are emitted consistently in existing API JSON conventions.

# Validation steps

1. Inspect and compile:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify endpoint behavior manually or through tests:
   - dashboard endpoint returns finance insights for the authenticated tenant only
   - entity endpoint returns only insights for the specified entity within the authenticated tenant
   - both endpoints return the same normalized JSON item structure
   - active and resolved insights are both queryable according to filters
   - no check-specific branching fields are required by the response

4. Validate contract shape:
   - Confirm JSON uses the project’s standard casing conventions.
   - Confirm required fields are present:
     - severity
     - message
     - recommendation
     - entity reference
     - status
     - createdAt
     - updatedAt

5. Validate non-functional expectations:
   - Query path is read-only and efficient
   - No tenant leakage
   - No unnecessary joins or N+1 behavior in query handlers

# Risks and follow-ups

- **Risk: `FinanceAgentInsight` may not yet exist or may be incomplete**
  - If missing, implement only the minimum read model/entity support needed and note the gap clearly.
- **Risk: entity reference may be inconsistently modeled**
  - Normalize it in the API contract without inventing domain semantics; follow persisted fields.
- **Risk: existing API conventions may differ**
  - Match the repository’s established patterns rather than introducing a new style.
- **Risk: enum/string serialization mismatches**
  - Reuse existing JSON and enum serialization settings to avoid breaking clients.
- **Risk: pagination/envelope conventions may already exist**
  - Reuse them if present so the new endpoints fit the platform.

Follow-ups after this task, if not already covered elsewhere:
- add Redis caching for dashboard insight queries if volume grows
- add richer filtering/grouping for cockpit widgets
- add UI consumption in Blazor dashboard/entity pages
- consider indexes on `company_id`, `status`, `severity`, `entity_type`, `entity_id`, and `updated_at` if query performance needs tuning