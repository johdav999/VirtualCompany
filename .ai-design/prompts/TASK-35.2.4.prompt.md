# Goal
Implement backlog task **TASK-35.2.4 — Integrate memory-aware personalization into campaign draft generation and duplicate-offer suppression checks** for story **US-35.2 Deliver customer memory profiles and personalized message generation**.

Deliver an end-to-end implementation in the existing .NET modular monolith that:

- adds a persistent **customer memory profile** per tenant contact
- surfaces that profile in the **customer profile view** using production tenant data
- injects customer memory into **campaign draft generation**
- stores a **personalized generated draft per contact before send**
- blocks scheduling/sending when the **same offer** was already sent to the same contact within a configurable lookback window
- persists both the **original generated draft** and the **final edited/sent content** for audit and analytics

Keep the implementation tenant-safe, production-oriented, and aligned with the existing architecture: ASP.NET Core backend, PostgreSQL, CQRS-lite application layer, Blazor web UI, background workers, auditability, and shared orchestration/retrieval patterns.

# Scope
In scope:

- Domain and persistence changes for:
  - customer memory profile storage
  - relationship memory / interaction summaries
  - personalized campaign drafts
  - duplicate-offer suppression history checks
  - audit fields for generated-vs-final message content
- Application services/handlers for:
  - building/updating customer memory profiles
  - retrieving customer profile view models
  - generating personalized campaign drafts with memory context
  - checking duplicate-offer suppression before scheduling
  - persisting edited final content alongside original generated content
- Infrastructure/repository/query updates for tenant-scoped reads/writes
- API endpoints or existing command/query surfaces needed by web UI and workers
- Blazor UI updates for customer profile display and draft editing/viewing
- Tests covering core acceptance criteria

Out of scope unless required by existing code paths:

- New external integrations
- Mobile app changes
- Full redesign of campaign architecture
- Large refactors unrelated to this task
- Generic memory platform work beyond what is needed for customer/contact personalization

Assumptions to validate in code before implementing:

- There is already a contact/customer entity and campaign step scheduling flow
- There is already some message generation pipeline or campaign draft generation service
- There are existing deal/history entities or normalized records that can be queried
- There is an existing tenant/company context pattern that must be preserved

If names differ in the codebase, adapt to actual module/entity names rather than forcing new abstractions.

# Files to touch
Inspect the solution first and then update the appropriate files in these likely areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:

## Domain
- Contact/customer aggregate or related entities
- Campaign/campaign step/draft entities
- Deal/history entities if duplicate-offer checks belong in domain logic
- New entities/value objects such as:
  - `CustomerMemoryProfile`
  - `CustomerInteractionSummary`
  - `CampaignMessageDraft` or equivalent extension
  - `OfferSuppressionPolicy` / `OfferHistoryMatch` if needed

## Application
- Commands/queries/handlers for:
  - get customer profile
  - generate campaign drafts
  - schedule campaign steps
  - save edited draft / mark sent
- Services/interfaces such as:
  - memory profile builder
  - personalization context assembler
  - duplicate-offer suppression checker
- DTOs/view models for profile and draft audit data

## Infrastructure
- EF Core entity configurations / DbContext mappings
- Repositories and query services
- SQL migrations
- Background worker hooks if draft generation or scheduling runs async
- LLM prompt/context builder integration for customer memory injection

## API / Web
- Endpoints/controllers/minimal APIs for profile and draft actions
- Blazor pages/components for customer profile view and draft editing/audit display

## Tests
- Unit tests for suppression logic and personalization context assembly
- Integration tests for tenant-scoped retrieval and persistence
- API tests for profile view and draft edit persistence behavior

Also check:
- `README.md`
- any architecture or migration docs if conventions require updates

# Implementation plan
1. **Discover existing campaign/contact/deal architecture**
   - Search the solution for:
     - contact/customer entities
     - campaign step generation/scheduling
     - message draft persistence
     - deal history / prior outreach history
     - audit event creation
   - Identify the current source of truth for:
     - contacts
     - conversations/interactions
     - deals/offers
     - campaign sends
   - Reuse existing module boundaries and naming.

2. **Design the minimal persistent customer memory profile model**
   - Add or extend persistence so each tenant contact has a durable profile containing at least:
     - past conversations summary or references
     - previous deals summary or references
     - preferences
     - price sensitivity indicators
     - industry signals
     - last outreach summary
     - relationship memory summary
     - engagement score
     - timestamps for last refresh/update
   - Prefer a normalized core record plus JSONB for flexible memory fields if that matches project conventions.
   - Ensure all records are tenant/company scoped.
   - If interaction history already exists elsewhere, do not duplicate raw history; store summary/reference data and query raw history separately for the profile view.

3. **Add migration and EF mappings**
   - Create/update PostgreSQL migration(s) for new tables/columns/indexes.
   - Add indexes for:
     - `(company_id, contact_id)` on memory profile
     - campaign/deal history lookups used by duplicate-offer suppression
     - draft retrieval by campaign step/contact
   - Keep migration naming and placement consistent with repo conventions.

4. **Implement customer memory profile builder/updater**
   - Create an application service that composes a contact’s memory profile from production data:
     - prior conversations/messages
     - prior campaign sends
     - previous deals/opportunities
     - contact/company industry data
     - engagement signals
   - The service should:
     - be deterministic/testable
     - avoid storing chain-of-thought
     - store concise operational summaries
     - support refresh on demand and/or during generation/scheduling flows
   - If there is already a memory module, integrate with it instead of duplicating logic.

5. **Implement customer profile query/view model**
   - Build a query returning the selected tenant contact’s profile view with:
     - interaction history
     - AI summary
     - relationship memory
     - past deals
     - engagement score
   - Use production data sources, not mock/sample placeholders.
   - Enforce tenant isolation and not-found/forbidden behavior.
   - Keep query efficient; aggregate where possible.

6. **Integrate memory-aware personalization into campaign draft generation**
   - Update the campaign message generation pipeline so prompt/context assembly includes customer memory context:
     - preferences
     - prior outreach summary
     - deal context
     - price sensitivity
     - industry signals
     - relationship memory
   - Ensure generation happens **per contact** and results in a **stored personalized draft before send**.
   - Persist at minimum:
     - generated draft content
     - structured personalization metadata/context snapshot if appropriate
     - generation timestamp
     - campaign step ID
     - contact ID
     - company ID
     - status
   - If drafts already exist, extend them rather than creating a parallel model.

7. **Persist original generated variant and final edited/sent content**
   - Update draft editing/send flow so:
     - original generated content remains immutable once generated
     - user edits are stored separately as final draft/sent content
     - sent record references both original and final versions
   - Include audit-friendly fields such as:
     - generated content
     - final content
     - edited by user ID
     - edited at
     - sent at
     - send outcome/status
   - If analytics/audit tables already exist, emit or link appropriate audit events.

8. **Implement duplicate-offer suppression before scheduling**
   - Add a configurable lookback window, ideally tenant/company configurable with sensible default if no setting exists.
   - Before scheduling a campaign step, check whether the same offer was already sent to the same contact within the lookback window using:
     - prior campaign history
     - prior deal history
   - Define “same offer” using existing domain identifiers if available:
     - offer ID / template ID / product SKU / normalized offer key
   - Avoid brittle text-only matching unless no structured identifier exists; if needed, create a normalized offer key.
   - On suppression:
     - prevent scheduling
     - return/store a clear reason
     - create audit/event record if consistent with existing patterns
   - Make the check idempotent and testable.

9. **Wire suppression into the actual scheduling path**
   - Ensure the duplicate-offer check runs in the authoritative scheduling command/handler or worker path, not only in UI.
   - If there are multiple scheduling entry points, centralize the guard.
   - Preserve existing approval/policy flow if campaign scheduling already uses it.

10. **Update Blazor customer profile UI**
   - Add/update the customer/contact profile page to display:
     - interaction history
     - AI summary
     - relationship memory
     - past deals
     - engagement score
   - Use real tenant-scoped API/query data.
   - Keep UX simple and production-safe; no fake/demo data.

11. **Update draft UI/API behavior**
   - Ensure users can view/edit generated drafts.
   - Show enough metadata to support auditability, such as:
     - generated version
     - edited/final version
     - personalization summary if already exposed elsewhere
   - Do not expose hidden reasoning; only operational summaries.

12. **Add tests**
   - Unit tests:
     - memory context assembly includes expected profile fields
     - duplicate-offer suppression blocks within lookback window
     - suppression allows scheduling outside lookback window
     - original generated content is preserved after edits
   - Integration/API tests:
     - tenant-scoped customer profile retrieval
     - personalized draft persisted before send
     - final sent content and original generated variant both persisted
     - scheduling path rejects duplicate offer based on prior campaign/deal history
   - Prefer existing test patterns and fixtures.

13. **Validate build and behavior**
   - Run solution build/tests.
   - Fix warnings/errors introduced by the change.
   - Confirm migrations apply cleanly.

Implementation notes:
- Follow clean architecture boundaries already present in the repo.
- Keep commands for writes and queries for reads.
- Use existing audit/event infrastructure where available.
- Do not expose raw LLM reasoning.
- Preserve tenant isolation on every query and write.
- Prefer extending existing entities/services over introducing parallel systems.

# Validation steps
1. Inspect and map existing code paths:
   - contact/customer retrieval
   - campaign draft generation
   - campaign scheduling
   - deal/campaign history lookup
   - draft editing/send persistence

2. Apply migrations and verify schema changes.

3. Run build:
   - `dotnet build`

4. Run tests:
   - `dotnet test`

5. Manually verify or add integration coverage for these scenarios:

   - **Customer memory profile persistence**
     - Given a tenant contact with prior interactions/deals
     - When profile data is built/refreshed
     - Then a persistent memory profile exists with required fields populated

   - **Customer profile view**
     - Given a selected tenant contact
     - When the profile page/query is loaded
     - Then it shows interaction history, AI summary, relationship memory, past deals, and engagement score from production-backed data

   - **Personalized draft generation**
     - Given a campaign step for multiple contacts
     - When drafts are generated
     - Then each contact receives a stored personalized draft before send
     - And the generation context includes customer memory/profile data

   - **Duplicate-offer suppression**
     - Given a prior matching offer in campaign history within lookback
     - When scheduling a new step with the same offer
     - Then scheduling is blocked with a clear suppression reason
     - And no send is queued

   - **Deal-history suppression**
     - Given a prior matching offer in deal history within lookback
     - When scheduling
     - Then scheduling is blocked

   - **Edited draft audit persistence**
     - Given a generated draft
     - When a user edits it and it is later sent
     - Then both original generated content and final sent content are persisted and retrievable

   - **Tenant isolation**
     - Given another tenant’s contact/history
     - When querying profile or suppression history
     - Then data is not visible or used across tenants

6. If there are existing API tests or snapshot tests for campaign/profile endpoints, update them accordingly.

# Risks and follow-ups
- **Unknown existing model names:** The repo may use different terms than contact/customer/offer/draft. Adapt to actual code structure rather than inventing duplicate concepts.
- **Offer identity ambiguity:** If “same offer” is not currently modeled structurally, define a normalized offer key now and document it. Text matching alone is fragile.
- **Performance risk:** Profile assembly and suppression checks may become expensive if they scan large message/deal histories. Add targeted indexes and aggregate queries.
- **Data duplication risk:** Avoid copying full interaction history into memory profile storage; store summaries and references, query raw history separately for the profile view.
- **Concurrency risk:** Scheduling workers may race. Ensure suppression checks happen in the authoritative write path and consider transactional protection/idempotency if needed.
- **Prompt bloat risk:** Customer memory context should be concise and structured; do not dump full histories into generation prompts.
- **Audit/privacy risk:** Persist operational summaries only, not chain-of-thought or unnecessary sensitive content.
- **Config source risk:** If no tenant-level setting exists for lookback window, add a minimal configuration path or use a safe default with a TODO for admin configurability.
- **Follow-up candidates:**
  - admin UI for configuring duplicate-offer lookback window
  - background refresh jobs for customer memory profiles
  - analytics on generated-vs-edited draft deltas
  - richer engagement scoring model
  - vector-backed retrieval for customer memory if current implementation is purely relational