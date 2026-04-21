# Goal
Implement backlog task **TASK-24.2.3 — Implement payment detail and list views in the finance workspace** for story **US-24.2 Implement payment records for incoming and outgoing cash movements**.

Deliver an end-to-end vertical slice across domain, persistence, API, seed/bootstrap, and Blazor finance workspace UI so that tenant-scoped incoming and outgoing payments can be created, listed, and inspected.

# Scope
In scope:
- Add a **Payment** domain entity with required fields:
  - `paymentType`
  - `amount`
  - `currency`
  - `paymentDate`
  - `method`
  - `status`
  - `counterpartyReference`
- Enforce database constraints for:
  - `amount > 0`
  - valid `paymentType` values only
- Add PostgreSQL migration for payment storage and indexes on:
  - tenant/company identifier
  - status
  - paymentDate
- Implement tenant/company-scoped APIs for:
  - create payment
  - list payments
- Support both incoming and outgoing payments
- Implement finance workspace UI:
  - payment list view
  - payment detail/inspection view
  - show header fields, status, amount, currency, linked counterparty reference
- Extend seed/bootstrap logic to generate realistic payment records for existing companies
- Ensure seed/bootstrap is idempotent and does not fail with duplicate key errors
- Add/update tests covering domain, persistence, API, and UI-facing query behavior where practical

Out of scope unless required by existing patterns:
- Editing/deleting payments
- Approval workflows for payments
- Advanced filtering beyond what existing finance list patterns already support
- Mobile UI
- External accounting integrations

# Files to touch
Inspect the solution first and adapt to actual project structure, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - payment entity/value objects/enums
- `src/VirtualCompany.Application/**`
  - commands/queries/DTOs/validators/handlers for create and list
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - DbContext
  - migrations
  - repositories/query services
  - seed/bootstrap logic
- `src/VirtualCompany.Api/**`
  - payment endpoints/controllers/minimal API mappings
  - request/response contracts if API-owned
- `src/VirtualCompany.Web/**`
  - finance workspace payment list page/component
  - payment detail page/component
  - navigation links if needed
  - API client/service calls
- `src/VirtualCompany.Shared/**`
  - shared contracts if this solution centralizes DTOs here
- `tests/VirtualCompany.Api.Tests/**`
  - API integration tests
- Other relevant test projects if present for application/infrastructure/web

Also inspect:
- existing tenant/company scoping patterns
- existing finance workspace pages
- existing migration naming conventions
- existing seed/bootstrap entry points
- existing list/detail UI patterns for similar entities such as invoices, expenses, tasks, or approvals

# Implementation plan
1. **Discover existing patterns before coding**
   - Find how the solution currently models:
     - tenant/company-owned entities
     - enum/string constrained fields
     - EF Core configurations and migrations
     - API endpoint registration
     - Blazor finance workspace routing/layout
     - seed/bootstrap execution
   - Reuse established conventions over inventing new ones.

2. **Add the Payment domain model**
   - Create a `Payment` entity in the appropriate domain module/namespace.
   - Include:
     - `Id`
     - `CompanyId` or equivalent tenant/company foreign key
     - `PaymentType`
     - `Amount`
     - `Currency`
     - `PaymentDate`
     - `Method`
     - `Status`
     - `CounterpartyReference`
     - standard audit timestamps if the project uses them
   - Represent `PaymentType` as a constrained enum or strongly validated string with allowed values for incoming/outgoing.
   - Keep the model aligned with acceptance criteria and existing domain style.

3. **Configure persistence**
   - Add EF Core mapping/configuration for `Payment`.
   - Map to a `payments` table using existing naming conventions.
   - Add database constraints:
     - check constraint for `amount > 0`
     - check constraint for valid `payment_type` values
   - Add indexes on:
     - `company_id` or tenant/company identifier
     - `status`
     - `payment_date`
   - Add any necessary FK to `companies`.

4. **Create migration**
   - Generate or hand-author a migration that creates payment storage and required indexes/constraints.
   - Ensure migration is deterministic and matches project migration style.
   - Do not modify archived migration docs except if the repo explicitly requires updating migration documentation.

5. **Implement application layer create/list flows**
   - Add command/query models and handlers for:
     - create payment
     - list payments by company
     - get payment detail by id and company
   - Validate:
     - positive amount
     - required fields
     - valid payment type
     - tenant/company scoping
   - Return DTOs suitable for both API and UI consumption.
   - Keep CQRS-lite style consistent with architecture guidance.

6. **Implement API endpoints**
   - Add tenant/company-scoped endpoints for:
     - `POST` create payment
     - `GET` list payments
     - `GET` payment detail
   - Follow existing auth and company resolution patterns.
   - Ensure cross-tenant access returns forbidden/not found according to existing conventions.
   - Support both incoming and outgoing payments through the same model and endpoint contract.

7. **Implement finance workspace UI**
   - Add/update finance workspace navigation to expose payments.
   - Build a payment list view showing at minimum:
     - payment type
     - payment date
     - status
     - amount
     - currency
     - counterparty reference
   - Build a payment detail/inspection view showing:
     - header fields
     - status
     - amount
     - currency
     - linked counterparty reference
   - Use existing finance workspace layout/components/styles.
   - Prefer SSR-first Blazor patterns already used in the app.
   - If there is an existing finance API client/service abstraction, extend it instead of calling HTTP directly from components.

8. **Extend seed/bootstrap logic**
   - Add realistic incoming and outgoing payment generation for existing companies.
   - Use deterministic/idempotent seeding patterns already present in the repo.
   - Avoid duplicate key errors by:
     - checking for existing seeded records
     - using stable identifiers or natural uniqueness strategy already used elsewhere
   - Generate plausible values for:
     - amount
     - currency based on company/default currency if available
     - payment date
     - method
     - status
     - counterparty reference

9. **Add tests**
   - Add tests for:
     - DB constraint behavior where feasible
     - create payment API success
     - list payments scoped to company
     - invalid amount rejection
     - invalid payment type rejection
     - payment detail not accessible across tenants
   - If UI tests are not already established, at least cover the query/DTO shape that the UI depends on.
   - Keep tests aligned with existing test infrastructure.

10. **Polish**
   - Ensure naming is consistent across domain/API/UI.
   - Ensure formatting/currency/date display follows existing app conventions.
   - Verify no analyzer/build warnings are introduced if the repo treats warnings seriously.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of normal workflow, verify the new migration is included and applies cleanly.

4. Manually validate API behavior:
   - create an incoming payment for company A
   - create an outgoing payment for company A
   - list payments for company A and confirm both appear
   - verify company B cannot access company A payments
   - verify invalid `amount <= 0` is rejected
   - verify invalid `paymentType` is rejected

5. Manually validate UI:
   - open finance workspace payments list
   - confirm list renders seeded or created payments
   - open a payment detail page
   - confirm header fields, status, amount, currency, and counterparty reference are visible

6. Validate seed/bootstrap:
   - run bootstrap/seed path once
   - run it again
   - confirm no duplicate key errors
   - confirm realistic payment records exist for existing companies

7. If possible, inspect generated SQL/schema or migration snapshot to confirm:
   - payment table exists
   - indexes exist on company identifier, status, payment date
   - check constraints exist for amount and payment type

# Risks and follow-ups
- The repo may already have finance-related entities or partial payment concepts; avoid duplicating overlapping models.
- Tenant scoping may be implemented differently across API, application, and infrastructure layers; follow the existing enforcement path exactly.
- If enums are serialized as strings in API but ints in DB, ensure consistency and explicit mapping.
- Seed logic is a common source of duplicate key failures; prefer idempotent upsert/check-before-insert patterns already used in the codebase.
- UI routing and finance workspace composition may already have conventions for list/detail pages; integrate rather than creating isolated pages.
- If there is no existing payment counterparty entity yet, treat `counterpartyReference` as a display/reference field only for this task.
- Follow-up candidates after this task:
  - payment filtering/search
  - edit/cancel flows
  - reconciliation views
  - links from counterparties/invoices to payments
  - dashboard summaries for incoming vs outgoing cash movements