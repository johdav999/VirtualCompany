# Goal
Implement backlog task **TASK-35.2.2** for story **US-35.2 Deliver customer memory profiles and personalized message generation** by adding an `ICustomerMemoryService` that aggregates tenant-scoped contact context from production data and makes it reusable for profile views, message generation, scheduling guardrails, and audit persistence.

The implementation should satisfy these outcomes:

- Build and persist a **customer memory profile** per contact containing:
  - past conversations
  - previous deals
  - preferences
  - price sensitivity indicators
  - industry signals
  - last outreach summary
- Expose a reusable service API that returns structured contact context for:
  - customer profile UI/query handlers
  - campaign message generation
  - duplicate-offer prevention checks
- Ensure campaign step message generation includes customer memory context and stores a **personalized draft per contact before send**
- Prevent sending the same offer to the same contact within a configurable lookback window using prior campaign and deal history
- Persist both:
  - the original generated draft
  - the final edited/sent content
  for audit and analytics

Follow existing solution architecture and conventions in the repo. Keep all logic tenant-aware, application-layer orchestrated, and production-data backed.

# Scope
In scope:

- Discover existing domain/application/infrastructure structures for:
  - contacts
  - conversations/messages
  - deals/opportunities
  - campaigns/campaign steps
  - generated drafts / outbound messages
  - audit/history entities
  - tenant scoping patterns
- Add `ICustomerMemoryService` and its implementation in the appropriate application/infrastructure layers
- Define DTOs/models for reusable customer memory context
- Aggregate data from existing persistence models rather than mock/sample data
- Add persistence/update flow for a contact’s customer memory profile if no dedicated profile entity exists yet
- Add duplicate-offer lookback evaluation logic
- Update message generation flow so generated drafts are stored before send and include customer memory context
- Update send/edit persistence so original generated variant and final sent content are both retained
- Add/adjust query handler(s) powering the customer profile view to use the new service
- Add tests covering aggregation, tenant isolation, lookback prevention, and draft audit persistence

Out of scope unless required by existing code structure:

- Large UI redesigns
- New external integrations
- Replacing existing orchestration architecture
- Broad schema redesign unrelated to customer memory
- Fake/demo data paths

# Files to touch
Inspect the solution first and then touch the minimal correct set. Likely areas:

- `src/VirtualCompany.Application/**`
  - service interfaces
  - commands/queries/handlers
  - DTOs/view models
  - campaign/message generation orchestration
- `src/VirtualCompany.Domain/**`
  - customer memory profile entity/value objects if needed
  - campaign/draft/audit domain models if missing required fields
- `src/VirtualCompany.Infrastructure/**`
  - EF Core repositories / DbContext mappings
  - service implementation
  - SQL/query composition
  - migrations if schema changes are required
- `src/VirtualCompany.Api/**`
  - DI registration
  - endpoints if profile/message flows are API-driven
- `src/VirtualCompany.Web/**`
  - only if the profile view contract must be updated to consume new fields
- `tests/**`
  - application tests
  - API/integration tests
  - persistence tests for lookback and draft audit behavior

Also inspect:
- `README.md`
- any architecture/conventions docs
- existing migration guidance under `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Inspect current architecture and locate existing customer-facing modules**
   - Find current models and handlers for contacts, campaigns, deals, conversations, and outbound messaging.
   - Identify whether there is already:
     - a contact profile query
     - a draft generation service
     - a campaign scheduling validator
     - audit entities for generated/sent messages
   - Reuse existing naming and folder conventions.

2. **Design the customer memory contract**
   - Add `ICustomerMemoryService` in the application layer.
   - Define a structured result model, for example:
     - contact identifiers
     - interaction history summary + recent items
     - deal history summary + recent deals
     - inferred/stored preferences
     - price sensitivity indicators
     - industry signals
     - last outreach summary
     - engagement score
     - offer exposure history
     - AI/customer memory summary
     - source references / timestamps where useful
   - Keep the contract deterministic and reusable by both UI and generation flows.

3. **Implement tenant-scoped aggregation**
   - In infrastructure, implement `CustomerMemoryService` using existing repositories/DbContext.
   - Aggregate from production tables only, scoped by tenant/company and contact ID.
   - Pull and normalize:
     - conversation/message history
     - prior campaign interactions
     - prior deals/opportunities/orders if present
     - stored preferences/memory items if present
     - industry/company/contact metadata signals
     - latest outreach/send result
   - Compute or derive:
     - engagement score using available production signals
     - price sensitivity indicators from prior deal/message behavior if explicit fields do not exist
     - offer exposure history for duplicate prevention
   - If a persistent customer memory profile table/entity already exists, update it.
   - If not, add a minimal persistent profile representation aligned with current schema patterns.

4. **Support the customer profile view**
   - Update the relevant query/handler used by the customer profile page.
   - Ensure the view model includes:
     - interaction history
     - AI summary
     - relationship memory
     - past deals
     - engagement score
   - Ensure it loads real tenant contact data, not placeholder values.

5. **Integrate with message generation**
   - Find the campaign step message generation pipeline.
   - Inject `ICustomerMemoryService` and include returned context in prompt/input construction.
   - Before send, persist a generated personalized draft per contact.
   - Ensure the stored draft clearly distinguishes:
     - generated/original content
     - editable current content
     - final sent content
   - Preserve correlation to campaign step/contact/tenant.

6. **Implement duplicate-offer prevention**
   - Add a reusable method on `ICustomerMemoryService` or a closely related policy helper to evaluate whether an offer may be sent.
   - Check prior campaign history and deal history for the same contact and offer within a configurable lookback window.
   - Use existing settings/config patterns for the lookback duration; if none exist, add a minimal configuration path consistent with the codebase.
   - Integrate this check into scheduling/step preparation so blocked sends do not get scheduled.
   - Return a structured reason for audit/logging/UI visibility.

7. **Persist original generated vs final edited/sent content**
   - Inspect current outbound draft/send entities.
   - Add fields/entities/history records as needed so both are retained:
     - original generated variant
     - user-edited final content
     - actual sent content if separately tracked
   - Ensure edits do not overwrite the original generated draft.
   - Add audit/analytics-friendly metadata such as timestamps, editor/sender, and generation source if patterns already exist.

8. **Register dependencies and wire handlers**
   - Add DI registration in API/startup composition root.
   - Update any command/query handlers and background jobs that need the new service.
   - Keep boundaries clean: controllers/endpoints should call handlers/services, not build aggregation logic directly.

9. **Add tests**
   - Unit/integration tests for:
     - customer memory aggregation from real persistence models
     - tenant isolation
     - profile view data composition
     - message generation storing per-contact draft before send
     - duplicate-offer lookback blocking
     - preserving original generated draft after user edits
     - persisting final sent content separately
   - Prefer integration tests where behavior spans EF/query logic.

10. **Schema changes if necessary**
   - Only add migrations if required by missing persistence support.
   - Keep schema additions minimal and aligned with PostgreSQL + existing conventions.
   - If adding tables/columns, include indexes for:
     - tenant/contact lookups
     - offer exposure lookback queries
     - campaign step/contact draft retrieval

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify customer memory aggregation:
   - Confirm a selected tenant contact returns a profile with:
     - conversation history
     - AI summary
     - relationship memory
     - past deals
     - engagement score

4. Verify message generation flow:
   - Generate campaign step messages for multiple contacts
   - Confirm a personalized draft is persisted per contact before send
   - Confirm the generation input includes customer memory context

5. Verify duplicate-offer prevention:
   - Seed or use existing test data where the same offer was previously sent/dealt
   - Confirm scheduling is blocked within the configured lookback window
   - Confirm a structured reason is persisted/returned

6. Verify edit/send audit persistence:
   - Edit a generated draft before send
   - Confirm:
     - original generated variant remains unchanged
     - final edited content is stored
     - final sent content is stored/auditable

7. Verify tenant isolation:
   - Ensure cross-tenant contact/history access is impossible in service queries and tests

# Risks and follow-ups
- The repo may not yet have explicit entities for customer memory profiles, offer exposure, or draft versioning; add only the minimum schema needed.
- “Price sensitivity indicators” and “industry signals” may require heuristic derivation if no explicit fields exist; document the derivation clearly in code comments/tests.
- If campaign scheduling and message generation are split across modules/workers, ensure duplicate-offer checks happen at the scheduling boundary, not only at send time.
- If there is no existing configurable lookback setting, add a small, well-scoped configuration mechanism and note it for future admin UI exposure.
- If engagement score logic is not already standardized, implement a simple deterministic scoring model now and flag future calibration as follow-up.
- Follow-up backlog likely needed for:
  - admin-configurable memory scoring/weights
  - richer profile UI presentation
  - analytics on generated-vs-edited message deltas
  - explicit customer memory refresh/background recomputation jobs