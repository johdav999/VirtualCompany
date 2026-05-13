# Goal
Implement `IOutboundCampaignService` and `ISequenceExecutionService` for outbound sales campaigns and multi-step sequence execution in the existing .NET modular monolith so that campaigns can be launched, scheduled, rate-limited, integrated with the real email provider, and automatically stopped for a contact when a reply or deal creation occurs.

This task must satisfy `TASK-35.1.2` under `US-35.1 Implement outbound campaigns and multi-step sales sequence execution`.

# Scope
Focus only on the backend/domain/application/infrastructure work required to support the acceptance criteria for campaign launch and execution behavior. Assume UI pages may already exist or be handled in adjacent tasks, but expose the APIs/state/contracts needed so the UI at `/app/sales/campaigns` can function without reload.

Include:
- Domain models, enums, and persistence for:
  - outbound campaigns
  - campaign audiences / eligible contacts
  - sequence definitions with at least 4 steps
  - scheduled sequence executions per contact and step
  - campaign state transitions: draft, active, paused, stopped, completed, etc.
  - delivery status, bounce status, reply correlation, provider message identifiers
- Application services:
  - `IOutboundCampaignService`
  - `ISequenceExecutionService`
- Validation rules for campaign creation and sequence definition
- Tenant policy enforcement:
  - outbound enabled flag
  - max emails per day
  - approval requirements
- Scheduling/background execution for delayed sequence steps
- Stop conditions:
  - cancel pending future steps within 1 minute when a reply is received
  - cancel pending future steps within 1 minute when a deal is created for the contact
- Real email provider integration path, not fake/in-memory sending
- Rate limiting and idempotency protections
- Persistence of delivery/bounce/reply correlation data
- Event-driven or worker-driven processing using the project’s existing architecture patterns
- API/application state updates so campaign start/pause/stop changes are reflected in DB and query models immediately

Do not:
- Build unrelated CRM/contact import features beyond consuming existing contacts/customers/imported lists
- Introduce microservices
- Bypass tenant scoping
- Add speculative abstractions not needed for this story
- Implement fake email sending if a real provider adapter pattern already exists or is expected by architecture

# Files to touch
Inspect the solution first and then update the actual relevant files. Expected areas include:

- `src/VirtualCompany.Domain/**`
  - sales/outbound campaign entities, value objects, enums, domain events
  - tenant policy entities/settings if already present
- `src/VirtualCompany.Application/**`
  - service interfaces and implementations for `IOutboundCampaignService` and `ISequenceExecutionService`
  - commands/queries/DTOs/validators
  - event handlers for reply received / deal created
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories
  - background workers / schedulers
  - email provider adapter integration
  - outbox/event dispatch support
  - rate limiting coordination using Redis if available
- `src/VirtualCompany.Api/**`
  - endpoints/controllers for campaign create/start/pause/stop and any webhook endpoints needed for provider callbacks
- `src/VirtualCompany.Web/**`
  - only if necessary to wire no-reload state refresh contracts already expected by the page
- `tests/**`
  - unit tests for validation, scheduling, stop conditions, policy enforcement
  - integration tests for provider callback correlation, state transitions, and worker execution

Also inspect:
- existing sales/contact/deal modules
- existing background job infrastructure
- existing outbox/eventing patterns
- existing email integration abstractions
- existing tenant context and authorization patterns

# Implementation plan
1. **Discover existing architecture before coding**
   - Find whether outbound campaign, contact, deal, email, workflow, approval, and notification concepts already exist.
   - Reuse existing patterns for:
     - tenant scoping
     - CQRS-lite commands/queries
     - background workers
     - outbox/domain events
     - EF Core mappings
     - API endpoint style
   - Identify where `IOutboundCampaignService` and `ISequenceExecutionService` should live and whether interfaces already exist as stubs.

2. **Model the domain**
   - Add or complete domain entities for:
     - `OutboundCampaign`
     - `CampaignAudience` or audience selection references
     - `SequenceDefinition`
     - `SequenceStep`
     - `SequenceExecution`
     - `SequenceExecutionStep`
     - `EmailDeliveryRecord` or equivalent
   - Add enums/statuses such as:
     - campaign status
     - execution status
     - step status
     - delivery status
     - bounce status
     - stop reason
   - Ensure all tenant-owned entities include tenant/company scoping.
   - Sequence rules:
     - minimum 4 steps
     - each step has delay in days
     - template content
     - AI personalization enabled/disabled
   - Add domain methods for:
     - launch
     - pause
     - stop
     - schedule next step
     - cancel pending future steps
     - mark reply received
     - mark deal created stop condition
     - mark send/delivery/bounce outcomes

3. **Persist the model**
   - Add EF Core configurations and migrations for new/updated tables.
   - Include indexes for:
     - tenant + campaign status
     - tenant + scheduled send time + pending status
     - provider message id
     - contact + active execution
     - correlation keys for reply handling
   - Persist provider identifiers and message threading/correlation fields needed to map replies back to campaign and sequence step.
   - If migrations are used in this repo, create a proper migration in the active migrations location, not the archive docs folder.

4. **Implement `IOutboundCampaignService`**
   - Responsibilities:
     - create campaign with validation
     - update campaign
     - launch campaign
     - pause campaign
     - stop campaign
     - query campaign state/details needed by UI
   - On create/update:
     - validate required fields
     - validate audience selection references existing contacts/customers/imported lists
     - validate sequence has at least 4 steps
   - On launch:
     - resolve eligible contacts from selected audience
     - exclude ineligible contacts based on existing rules/data available in system
     - enforce tenant outbound policy:
       - outbound enabled
       - max emails per day
       - approval requirements
     - create scheduled sequence executions for all eligible contacts
     - if approval is required, create approval records and prevent actual sending until approved
   - On pause:
     - prevent new sends
     - preserve history
     - leave already-sent records intact
   - On stop:
     - cancel all pending executions/steps for the campaign
   - Ensure state changes are persisted immediately and exposed through query/read models so UI can reflect changes without full reload.

5. **Implement `ISequenceExecutionService`**
   - Responsibilities:
     - create per-contact execution plans from a launched campaign
     - schedule steps based on delay in days
     - process due steps
     - send emails through provider integration
     - apply rate limiting and idempotency
     - update delivery state
     - cancel future steps on stop conditions
   - Add methods such as:
     - `ScheduleExecutionsForCampaignAsync(...)`
     - `ProcessDueStepsAsync(...)`
     - `CancelPendingStepsForContactAsync(...)`
     - `HandleReplyReceivedAsync(...)`
     - `HandleDealCreatedAsync(...)`
     - `HandleDeliveryStatusAsync(...)`
     - `HandleBounceAsync(...)`
   - Use UTC persistence and tenant/company timezone only for display/business interpretation if needed.

6. **Scheduling and worker execution**
   - Implement or extend a background worker that polls due sequence steps.
   - Use distributed locking / safe concurrency if the project already uses Redis or DB-based coordination.
   - Process due steps in batches.
   - Ensure idempotency so the same step is not sent twice under retries or concurrent workers.
   - Respect campaign paused/stopped state at execution time, not only at scheduling time.
   - If a step is blocked by approval or policy, mark it clearly and do not send.

7. **Rate limiting**
   - Enforce tenant-level max emails per day before sending.
   - Prefer a robust implementation:
     - count sent/scheduled-for-send records for the tenant/day
     - optionally coordinate with Redis for worker concurrency if available
   - Make sure rate limiting is enforced both:
     - when launching/scheduling
     - when actually sending due steps
   - If the daily limit is reached:
     - defer remaining pending steps to the next valid window or mark as throttled/pending retry according to existing patterns
   - Add tests for boundary conditions and concurrent processing.

8. **Approval requirements**
   - Integrate with the existing approval module if present.
   - If tenant policy requires approval for outbound sends/campaign launch:
     - create approval request(s)
     - block send execution until approved
     - resume scheduling/sending after approval event/decision
   - Reuse existing approval entities and workflows rather than inventing a parallel mechanism.

9. **Real email provider integration**
   - Find the existing email integration abstraction and implement campaign/sequence sending through it.
   - Do not use a fake sender except in tests.
   - Persist:
     - provider message id
     - thread/conversation identifiers if available
     - send timestamp
     - delivery status
     - bounce details
     - reply correlation metadata
   - Ensure outbound messages include enough metadata/headers/custom args to correlate callbacks to:
     - tenant
     - campaign
     - sequence execution
     - sequence step
     - contact
   - Add webhook or callback handling endpoints/services for:
     - delivered
     - bounced
     - replied/inbound correlation if provider supports it directly
   - If inbound replies are processed by a separate inbox processor, integrate with that event flow instead of duplicating logic.

10. **Stop conditions within 1 minute**
    - Wire reply-received and deal-created events into sequence cancellation.
    - On reply received for a contact in an active sequence:
      - cancel all pending future steps for that contact within 1 minute
      - record stop reason
    - On deal created for a contact in an active sequence:
      - same cancellation behavior
    - Prefer event-driven handlers plus background processing if needed, but keep SLA under 1 minute.
    - Add tests that verify pending future steps are cancelled and already-sent steps remain unchanged.

11. **API/application contracts**
    - Expose or update endpoints/handlers for:
      - create campaign
      - validate/save draft
      - launch/start
      - pause
      - stop
      - fetch campaign details/status
      - provider webhook callbacks if applicable
    - Return validation errors in a field-level structure consistent with the rest of the app.
    - Ensure state changes are queryable immediately so the campaigns page can update without reload.

12. **Observability and audit**
    - Add structured logs with tenant and campaign correlation.
    - Emit audit/business events for:
      - campaign created
      - campaign launched
      - campaign paused/stopped
      - step scheduled
      - email sent
      - bounce received
      - reply-correlated stop
      - deal-created stop
      - approval-blocked send
      - rate-limit deferral
    - Reuse existing audit/outbox patterns.

13. **Testing**
    - Unit tests:
      - campaign validation
      - sequence minimum 4-step validation
      - policy enforcement
      - pause/stop behavior
      - reply/deal stop cancellation
      - rate limiting
      - idempotent send processing
    - Integration tests:
      - launch creates executions for eligible contacts
      - due worker sends through provider abstraction
      - webhook/provider callback updates delivery/bounce state
      - reply correlation cancels future steps
      - deal creation event cancels future steps
      - approval-required flow blocks until approved
    - If there are API tests, cover start/pause/stop endpoints and returned state.

14. **Implementation constraints**
    - Keep code aligned with modular monolith and clean boundaries.
    - No direct DB access from controllers/endpoints.
    - No tenant scope leaks.
    - No uncontrolled background loops; use existing worker patterns.
    - Prefer small, composable services over one giant class.
    - If you must add new abstractions, keep them minimal and justified by current task needs.

# Validation steps
1. Restore/build:
   - `dotnet build`
2. Run tests:
   - `dotnet test`
3. If migrations are part of normal workflow, generate/apply and verify schema consistency.
4. Manually verify or integration-test these scenarios:
   - Create a campaign with missing required fields and confirm field-level validation errors.
   - Create a valid sequence with at least 4 steps.
   - Launch a campaign and confirm scheduled executions are created for eligible contacts only.
   - Launch under tenant policy with outbound disabled and confirm launch/send is blocked correctly.
   - Launch/send with max emails per day reached and confirm throttling/deferral behavior.
   - Launch/send when approval is required and confirm approval gate works.
   - Process due steps and confirm real provider integration path is invoked.
   - Receive delivery and bounce callbacks and confirm persistence updates.
   - Receive a reply for a contact in an active sequence and confirm pending future steps are cancelled within 1 minute.
   - Create a deal for a contact in an active sequence and confirm pending future steps are cancelled within 1 minute.
   - Pause and stop a campaign and confirm DB state and returned API/query state update immediately.
5. Include a short implementation note in the final output summarizing:
   - files changed
   - migration added
   - tests added
   - any assumptions or gaps

# Risks and follow-ups
- The repo may not yet contain complete sales/contact/deal/email abstractions; if missing, implement only the minimum required seams and clearly note assumptions.
- Real provider integration details may depend on existing adapters/webhook infrastructure; avoid hardcoding a provider-specific design if an abstraction already exists.
- Rate limiting can be tricky under concurrency; ensure idempotency and locking are covered by tests.
- Reply correlation may require provider-specific headers or inbox processing support; document any dependency on inbound email pipeline.
- Approval requirements may be defined at tenant