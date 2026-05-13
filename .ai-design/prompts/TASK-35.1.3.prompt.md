# Goal
Implement backlog task **TASK-35.1.3** for story **US-35.1 Implement outbound campaigns and multi-step sales sequence execution** by building the **web UI at `/app/sales/campaigns`** and the supporting application/API/domain/infrastructure pieces needed for:

- campaign creation
- audience selection from existing contacts, past customers, and imported contact lists
- sequence builder with at least 4 steps
- campaign preview
- start / pause / stop controls
- live UI state updates without page reload
- scheduling sequence executions for eligible contacts
- tenant policy enforcement
- cancellation of pending steps on reply or deal creation
- real email integration usage with rate limiting, bounce handling, delivery persistence, and reply correlation

This must fit the existing **modular monolith .NET architecture**, preserve **tenant isolation**, and follow **CQRS-lite**, **background worker**, and **outbox/reliable side effect** patterns already described in the architecture.

# Scope
Include only what is necessary to satisfy this task end-to-end in a production-credible way.

## In scope
- Add or complete the sales campaigns page and related UI components in the Blazor web app.
- Add backend endpoints/handlers/services/query models required by the page.
- Add domain entities/value objects/enums needed for campaigns, sequence steps, audience selection, executions, and state transitions.
- Add persistence mappings and migrations for campaign-related data.
- Implement validation for required campaign fields and sequence step requirements.
- Implement campaign launch logic that creates scheduled sequence executions for eligible contacts.
- Enforce tenant policy constraints:
  - outbound enabled flag
  - max emails per day
  - approval requirements
- Implement pause/stop/start state transitions with immediate UI refresh.
- Implement background processing for scheduled sends.
- Implement cancellation of pending future steps within 1 minute when:
  - a reply is received
  - a deal is created for a contact in an active sequence
- Integrate with the real email integration abstraction already present or create the proper adapter seam if missing.
- Persist delivery status, bounce status, and reply correlation to campaign + sequence step.
- Add tests for core application/domain behavior and API/UI integration where practical.

## Out of scope
- Building a generic workflow builder for all domains.
- Replacing the existing email integration architecture.
- Full analytics/reporting dashboards beyond what is needed for preview/status display.
- Mobile app support.
- Large-scale refactors unrelated to sales campaigns.
- New contact import functionality if imported lists already exist; only consume existing imported list data sources.

# Files to touch
Inspect the solution first and adjust to actual project structure, but expect to touch files in these areas.

## Web
- `src/VirtualCompany.Web/...`
  - routing/nav for `/app/sales/campaigns`
  - campaigns page/component
  - create/edit campaign form components
  - audience selector component
  - sequence builder component
  - preview panel/component
  - campaign list/status controls
  - any DTO/viewmodel bindings used by the page
  - optional SignalR or polling-based live refresh wiring if the app already has a pattern

## API
- `src/VirtualCompany.Api/...`
  - campaign endpoints/controllers/minimal APIs
  - reply/deal-triggered cancellation endpoints or event ingestion hooks if exposed here
  - email webhook endpoints if this task needs to complete correlation flow through API

## Application
- `src/VirtualCompany.Application/...`
  - commands:
    - create campaign
    - update campaign
    - start campaign
    - pause campaign
    - stop campaign
    - cancel pending steps for contact
  - queries:
    - list campaigns
    - get campaign detail
    - get audience sources/options
    - preview eligible audience / schedule summary
  - validators
  - policy enforcement services
  - orchestration/scheduling services
  - event handlers for reply received / deal created
  - DTOs and mapping

## Domain
- `src/VirtualCompany.Domain/...`
  - campaign aggregate/entity
  - sequence step entity/value object
  - campaign status enum
  - execution/scheduled step entities
  - domain rules for valid transitions and cancellation behavior
  - domain events if used

## Infrastructure
- `src/VirtualCompany.Infrastructure/...`
  - EF Core configurations
  - repositories
  - migrations
  - background workers for scheduled sends / cancellation processing
  - email integration adapter usage
  - rate limiting persistence/coordination
  - outbox/event dispatch wiring
  - webhook/reply correlation persistence
  - bounce/delivery status persistence

## Tests
- `tests/VirtualCompany.Api.Tests/...`
- any existing application/domain/web test projects if present

Also review:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

# Implementation plan
1. **Inspect the existing codebase before changing anything**
   - Find current patterns for:
     - Blazor page composition and forms
     - API style
     - MediatR/CQRS usage if present
     - EF Core entity configuration and migrations
     - tenant resolution and authorization
     - background jobs/workers
     - outbox/event handling
     - email integration and webhook processing
     - contacts/customers/deals/imported lists data model
   - Reuse existing conventions exactly.

2. **Model the sales campaign domain**
   - Add campaign concepts with tenant ownership.
   - Minimum fields should support:
     - `Id`
     - `Tenant/CompanyId`
     - `Name`
     - `Status` (`Draft`, `Active`, `Paused`, `Stopped`, optionally `Completed`)
     - audience source definition
     - created/updated timestamps
     - launch/start/pause/stop metadata
   - Add sequence step model with:
     - step order
     - delay in days
     - template subject/body or equivalent content fields
     - AI personalization enabled flag
   - Enforce at least 4 steps at validation level.
   - Add execution models for:
     - campaign contact enrollment
     - scheduled step execution per contact
     - execution status
     - cancellation reason
     - correlation to outbound email message/provider identifiers

3. **Design persistence**
   - Add EF entities/configurations and migration(s) for campaign tables.
   - Ensure all tenant-owned tables include `company_id` and are query-filtered/scoped.
   - Suggested relational shape:
     - `sales_campaigns`
     - `sales_campaign_steps`
     - `sales_campaign_contacts` or enrollments
     - `sales_campaign_step_executions`
     - optional `sales_email_events` if needed for provider webhook correlation
   - Add indexes for:
     - tenant + campaign status
     - tenant + scheduled send time + execution status
     - contact + active enrollment
     - provider message id / thread id correlation

4. **Implement application commands and validation**
   - Create command handlers for create/update/start/pause/stop.
   - Validation must show field-level errors for missing required fields, including:
     - campaign name
     - audience selection
     - minimum 4 sequence steps
     - required content per step
     - valid non-negative delays
   - Start command must:
     - verify campaign is launchable
     - resolve eligible contacts from selected audience source(s)
     - enforce tenant outbound policy constraints
     - create scheduled executions
     - create approval request instead of direct launch if required by policy
   - Pause/stop commands must update DB state and affect future processing immediately.
   - Stop should cancel all pending unsent future steps for the campaign.

5. **Implement audience selection**
   - Build a query/service that can resolve audience options from:
     - existing contacts
     - past customers
     - imported contact lists
   - In the UI, allow selecting source type and specific source/list/filter.
   - In preview, show:
     - estimated eligible contacts
     - excluded contacts count/reasons if practical
     - sequence summary
   - Keep audience resolution tenant-scoped.

6. **Build the Blazor UI at `/app/sales/campaigns`**
   - Add a campaigns page with:
     - campaign list/grid
     - create/edit panel or page
     - audience selector
     - sequence builder
     - preview section
     - start/pause/stop controls
   - UX requirements:
     - validation errors visible inline
     - no page reload required for state changes
     - status updates reflected immediately after command completion
   - Prefer existing app patterns:
     - SSR + interactive components if already used
     - form components with `EditForm`/validation summaries
   - Sequence builder must support at least 4 editable steps and allow more if easy.

7. **Implement live state reflection**
   - Use the project’s existing real-time pattern if present.
   - If none exists, use pragmatic no-reload refresh:
     - optimistic local state update after successful command
     - re-query campaign detail/list
     - lightweight polling only if necessary
   - Do not require full page reload for start/pause/stop.

8. **Implement launch scheduling**
   - On campaign start, create scheduled sequence executions for all eligible contacts.
   - Respect:
     - outbound enabled flag
     - max emails per day
     - approval requirements
   - If max emails/day is a tenant-level throughput constraint, schedule sends across days rather than over-enrolling unsafely.
   - Ensure idempotency so repeated start requests do not duplicate enrollments/executions.

9. **Implement background send processing**
   - Add/extend a worker that picks due scheduled executions.
   - Before sending each email:
     - re-check campaign/contact execution state
     - ensure not cancelled due to reply/deal
     - enforce rate limiting
     - use real email integration abstraction
   - Persist:
     - send attempt
     - provider message id
     - delivery status
     - bounce status
     - failure reason
   - Correlate each send to:
     - campaign
     - contact
     - sequence step

10. **Implement reply correlation and cancellation**
    - Hook into existing inbound email/reply webhook or inbox processor.
    - When a reply is received for a contact in an active sequence:
      - correlate to originating campaign/step via provider message/thread/reference ids
      - cancel all pending future steps for that contact
      - complete within 1 minute via event handler/background worker
    - Persist audit/status updates.

11. **Implement deal-created cancellation**
    - Subscribe to or handle internal deal-created events.
    - When a deal is created for a contact in an active sequence:
      - cancel all pending future steps for that contact within 1 minute
   - Ensure idempotent cancellation logic shared with reply-triggered cancellation.

12. **Implement policy and approval integration**
    - Reuse existing policy/approval modules if available.
    - Launch/send behavior must honor:
      - outbound enabled
      - max emails/day
      - approval required
    - If approval is required:
      - create approval request
      - prevent actual launch/send until approved
    - Keep policy decisions auditable.

13. **Add auditability and operational logging**
    - Record business audit events for:
      - campaign created
      - campaign started/paused/stopped
      - executions scheduled
      - reply-triggered cancellation
      - deal-triggered cancellation
      - send/bounce/delivery updates
    - Include tenant context and correlation IDs in technical logs.

14. **Test thoroughly**
    - Domain tests:
      - valid/invalid state transitions
      - cancellation behavior
    - Application tests:
      - create validation
      - start scheduling
      - policy enforcement
      - idempotent start
      - reply/deal cancellation
    - API tests:
      - tenant scoping
      - command/query responses
    - If practical, component/integration tests for Blazor form validation and state updates.

15. **Keep implementation clean**
    - Do not introduce direct DB access from UI.
    - Do not bypass application layer for policy checks.
    - Keep external email calls behind infrastructure adapters.
    - Keep code incremental and aligned with existing module boundaries.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. After implementation, run:
   - `dotnet build`
   - `dotnet test`

4. Verify database migration generation/application follows repo conventions.
   - If migrations are committed in-source, add the new migration and ensure it is consistent with existing naming/style.

5. Manually verify the UI flow:
   - Navigate to `/app/sales/campaigns`
   - Create a campaign with missing fields and confirm inline validation errors
   - Create a valid campaign selecting each supported audience source type
   - Define at least 4 sequence steps with delays, content, and AI personalization toggles
   - Save and reopen to confirm persistence
   - Preview audience and schedule summary
   - Start campaign and confirm:
     - DB state changes
     - UI updates without reload
     - scheduled executions are created
   - Pause campaign and confirm pending sends stop being processed
   - Resume/start again if supported by design and confirm correct behavior
   - Stop campaign and confirm pending future steps are cancelled

6. Verify policy enforcement scenarios:
   - outbound disabled tenant cannot launch/send
   - max emails/day is respected in scheduling/processing
   - approval-required tenant creates approval instead of immediate launch/send

7. Verify reply cancellation:
   - Simulate/provider-test a reply to an active sequence email
   - Confirm pending future steps for that contact are cancelled within 1 minute
   - Confirm correlation to campaign and step is persisted

8. Verify deal cancellation:
   - Create a deal for a contact in an active sequence
   - Confirm pending future steps for that contact are cancelled within 1 minute

9. Verify email lifecycle persistence:
   - send status
   - delivery updates
   - bounce handling
   - reply correlation

10. Verify tenant isolation:
   - campaign lists/details/actions must not cross tenant boundaries

# Risks and follow-ups
- **Existing data model uncertainty:** contacts, customers, imported lists, deals, and email integration may already exist under different names. Adapt to actual structures rather than forcing new parallel models.
- **Approval semantics ambiguity:** acceptance criteria mention approval requirements but do not fully define whether approval gates campaign launch, individual sends