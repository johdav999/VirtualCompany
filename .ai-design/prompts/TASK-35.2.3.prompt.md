# Goal
Implement backlog task **TASK-35.2.3** for story **US-35.2 Deliver customer memory profiles and personalized message generation** by extending the customer profile experience and supporting application flow so that:

- the customer profile UI renders:
  - relationship memory
  - AI summary
  - engagement score
  - historical interactions
  - past deals
- all displayed data comes from **production tenant-scoped contact data**
- the implementation aligns with the existing **ASP.NET Core + Blazor Web App + PostgreSQL modular monolith**
- the work is delivered in a way that supports the acceptance criteria now, while not overreaching into unrelated campaign-send orchestration unless required to unblock this task

This task is primarily a **web UI + query/application integration** task, but you must inspect the current codebase and implement any missing read models, API/query handlers, DTOs, and persistence mappings needed for the UI to render real data for a selected tenant contact.

# Scope
In scope:

- Discover the existing customer/contact domain, campaign domain, deal/history domain, and any memory/profile-related models already present.
- Extend the **customer/contact profile page** in the Blazor web app to display:
  - interaction history
  - AI summary
  - relationship memory
  - past deals
  - engagement score
- Use **real tenant-scoped data** from the backend, not mock/sample/static placeholders.
- Add or extend backend query endpoints/services/handlers needed to supply a single aggregated customer profile view model.
- Ensure the query path is tenant-aware and authorization-safe.
- If the data model already contains customer memory profile fields, wire them through.
- If the data model is partially missing but clearly required for rendering, add the minimal schema/domain/application/infrastructure support necessary to read and display:
  - past conversations
  - previous deals
  - preferences
  - price sensitivity indicators
  - industry signals
  - last outreach summary
  - AI summary
  - engagement score
- Add tests for the new query/application behavior where practical.

Out of scope unless directly required to make the UI functional:

- Full message generation pipeline implementation
- Full send-time duplicate-offer prevention workflow
- Full audit persistence for edited generated drafts
- Mobile app changes
- Broad redesign of unrelated customer/contact pages
- Introducing new architectural patterns inconsistent with the modular monolith

If you discover that some acceptance criteria belong to adjacent tasks and are not yet implemented, do **not** silently build a large speculative system. Instead:
- implement only the minimal supporting structures needed for this task’s UI/read path
- leave clear TODOs or follow-up notes for remaining send/generation/audit workflow work

# Files to touch
Start by inspecting and then likely touching files in these areas, depending on what already exists:

- `src/VirtualCompany.Web/**`
  - customer/contact profile pages and components
  - shared UI components for cards, timelines, score displays, summaries
- `src/VirtualCompany.Api/**`
  - customer/contact profile endpoints or controllers
  - request/response contracts if API-backed
- `src/VirtualCompany.Application/**`
  - queries, handlers, DTOs, read models
  - tenant-scoped application services
- `src/VirtualCompany.Domain/**`
  - customer/contact entities or value objects
  - customer memory profile concepts if absent/incomplete
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations/repositories/query projections
  - SQL/read-model access
  - migrations if schema additions are required
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests
- potentially other test projects if present for application/infrastructure layers

Also inspect:
- `README.md`
- solution/project structure under `src/`
- any existing migrations guidance in `docs/postgresql-migrations-archive/README.md`

Before changing anything, identify the actual existing names for:
- contact/customer entities
- tenant/company resolution
- campaign step/message draft entities
- deal/opportunity entities
- interaction/conversation/message entities
- memory/profile entities

Use the codebase’s existing naming and module boundaries rather than inventing parallel concepts.

# Implementation plan
1. **Discover existing architecture and domain shape**
   - Inspect the solution structure and locate:
     - customer/contact domain models
     - profile/detail pages
     - conversation/message history
     - deals/opportunities
     - campaign/message generation artifacts
     - tenant/company scoping patterns
   - Determine whether the app uses:
     - direct Blazor-to-application calls
     - API controllers/minimal APIs
     - MediatR/CQRS handlers
     - EF Core DbContext projections
   - Follow existing conventions exactly.

2. **Map acceptance criteria to this task’s actual deliverable**
   - Treat this task as focused on the **customer profile UI and supporting read path**.
   - Ensure the resulting profile view can display, for the selected tenant contact:
     - persistent customer memory profile data
     - interaction history
     - AI summary
     - relationship memory
     - past deals
     - engagement score
   - If message generation and duplicate-offer logic already exist, surface any relevant read-only indicators only if trivial and already modeled.
   - Do not overbuild send orchestration in this task.

3. **Design or extend a tenant-scoped aggregated read model**
   - Create or extend a query such as a `GetCustomerProfileQuery` / `GetContactProfileQuery` and response DTO that aggregates:
     - contact identity/basic details
     - AI summary
     - relationship memory entries or summary
     - engagement score and score metadata
     - historical interactions timeline
     - past deals list
     - preferences
     - price sensitivity indicators
     - industry signals
     - last outreach summary
   - Ensure all data is filtered by the active tenant/company and selected contact ID.
   - Prefer a single profile read model optimized for UI rendering over many chatty calls.

4. **Implement backend query logic**
   - Reuse existing repositories/DbContext/query services where possible.
   - If needed, add efficient projections joining or composing from:
     - contacts/customers
     - conversations/messages/interactions
     - deals/opportunities
     - memory/profile tables
     - campaign history / outreach summaries
   - Handle missing optional data gracefully:
     - empty states instead of null reference failures
     - no fake defaults that imply real data exists
   - Keep the query deterministic and testable.

5. **Add minimal domain/persistence support if required**
   - Only if the current schema lacks fields needed for accepted UI rendering, add the smallest viable support for persistent customer memory profile data.
   - Candidate fields/tables may include:
     - AI summary
     - relationship memory summary/details
     - engagement score
     - preferences
     - price sensitivity indicators
     - industry signals
     - last outreach summary
   - If adding schema:
     - use the project’s existing migration approach
     - keep naming consistent with current domain language
     - include `company_id`/tenant scoping where appropriate
   - Do not create a speculative full memory platform if a simple profile extension suffices.

6. **Extend the Blazor customer profile UI**
   - Update the selected customer/contact profile page to render production-backed sections/cards for:
     - AI summary
     - relationship memory
     - engagement score
     - historical interactions
     - past deals
   - Present data clearly with resilient empty states:
     - “No AI summary available”
     - “No relationship memory recorded yet”
     - “No prior deals”
     - “No interactions found”
   - Keep styling and component patterns consistent with the existing web app.
   - Prefer composable components if the page is already componentized.

7. **Support interaction history rendering**
   - Render a timeline or list of historical interactions using actual stored records.
   - Include useful metadata already available in the model, such as:
     - date/time
     - channel/type
     - summary/snippet
     - campaign/deal association if present
   - Avoid exposing raw chain-of-thought or internal-only AI reasoning.

8. **Support engagement score rendering**
   - Display the engagement score from persisted/calculated production data.
   - If the score has supporting metadata already available, show lightweight context such as:
     - score label/band
     - last updated timestamp
   - Do not invent a new scoring algorithm unless one is already implied by the codebase and needed for display.

9. **Preserve tenant isolation and authorization**
   - Ensure every query path enforces company/tenant scoping.
   - A contact from another tenant must not be retrievable/renderable.
   - Follow existing authorization patterns in API/application/web layers.

10. **Add tests**
   - Add or update tests to cover:
     - tenant-scoped retrieval of customer profile data
     - inclusion of interaction history, AI summary, relationship memory, deals, and engagement score in the response
     - empty-state behavior when optional sections have no data
     - forbidden/not-found behavior for cross-tenant access where applicable
   - Prefer integration-style tests if the project already uses them for API/query validation.

11. **Validate end-to-end**
   - Confirm the selected customer/contact profile page loads and renders real data.
   - Confirm no mock data remains.
   - Confirm build/tests pass.

12. **Document follow-ups**
   - If acceptance criteria around:
     - personalized draft generation
     - duplicate-offer lookback enforcement
     - edited-vs-original draft audit persistence
     are not fully implemented in this task, note them explicitly in code comments only where appropriate and summarize them in your final report as follow-up work, not hidden omissions.

# Validation steps
Run the relevant discovery, build, and test steps after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations were added, verify:
   - migration compiles
   - app starts with the updated schema
   - tenant-scoped queries still work

4. Manually verify in the web app:
   - navigate to a customer/contact profile
   - confirm the page renders:
     - AI summary
     - relationship memory
     - engagement score
     - historical interactions
     - past deals
   - confirm data is real and tenant-scoped
   - confirm empty states render cleanly when data is absent

5. Verify no regressions:
   - existing customer/contact navigation still works
   - no broken serialization/DTO issues
   - no null-reference errors on partially populated contacts

# Risks and follow-ups
- **Risk: domain naming mismatch**
  - The codebase may use `Contact`, `Customer`, `Lead`, or another term. Reuse existing terminology consistently.

- **Risk: missing persistence model**
  - Some required profile fields may not yet exist. Add only minimal schema/domain support needed for this task’s read experience.

- **Risk: overbuilding adjacent acceptance criteria**
  - The story acceptance criteria include message generation, duplicate-offer prevention, and audit persistence. Those may belong to adjacent tasks. Do not implement a broad orchestration system unless the codebase clearly expects it here.

- **Risk: tenant leakage**
  - Aggregated profile queries are easy places to miss tenant filters across joined tables. Validate every source is company-scoped.

- **Risk: performance**
  - A profile page that loads interactions, deals, and memory can become query-heavy. Prefer projection/read models and bounded result sets for history sections.

Follow-ups to call out if not already present in code:
- personalized draft generation per contact using customer memory context
- configurable duplicate-offer lookback enforcement before campaign step scheduling
- persistence of both original generated draft and final edited sent content for audit/analytics