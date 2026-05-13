# Goal
Implement backlog task **TASK-35.4.1** for story **US-35.4** by adding a production-ready conversion analytics persistence layer centered on:

- `IConversionAnalyticsService`
- `SalesMessagePerformance` persistence and related entities
- tenant-scoped storage and aggregation for:
  - campaign metrics
  - sequence metrics
  - step metrics
  - contact metrics
  - A/B variant metrics

The implementation must ensure the system can record and query message lifecycle and conversion performance data for sales outreach, including:

- sent
- delivered
- bounced
- opened when available
- replied
- deal created
- conversion timestamps

It must also persist enough normalized data to support downstream dashboard/reporting work for:

- campaign performance
- conversion funnel
- risk distribution
- expected revenue windows at 30/60/90 days
- comparative reply/conversion rates by message variant

This task is primarily **domain + application + infrastructure persistence**. Build the service and storage model so later dashboard/API work can consume it without schema rework.

# Scope
In scope:

- Add or complete domain models for sales/conversion analytics persistence.
- Define `IConversionAnalyticsService` in the application layer.
- Implement the service in infrastructure/application composition as appropriate for the current solution structure.
- Persist message performance records at the lowest useful grain and support rollups for:
  - campaign
  - sequence
  - step
  - contact
  - variant
- Capture timestamps for lifecycle/conversion events.
- Support tenant isolation on all persisted analytics records.
- Add EF Core mappings/configuration and migrations if this repo uses EF migrations.
- Add repository/query methods needed by the service.
- Add tests for:
  - event recording
  - idempotent updates/upserts
  - tenant scoping
  - variant metric aggregation
  - conversion timestamp persistence

Out of scope unless required to compile/integrate cleanly:

- Full dashboard UI implementation
- Full forecasting model sophistication
- Full deal intelligence NLP/signal extraction pipeline
- Daily risk recalculation scheduler
- Public API/controller endpoints beyond what is minimally needed for integration
- Backfilling historical production data

However, design the persistence model so acceptance criteria for dashboard, risk, and intelligence can be built on top of it without major redesign.

# Files to touch
Inspect the solution first and adapt to actual conventions, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/`
  - analytics domain entities/value objects/enums
  - sales/deal-related entities if analytics links to existing sales models
- `src/VirtualCompany.Application/`
  - service interface for `IConversionAnalyticsService`
  - DTOs/commands/results for recording analytics events and querying summaries
- `src/VirtualCompany.Infrastructure/`
  - EF Core entity configurations
  - DbContext updates
  - service implementation
  - repositories/query services
  - migrations or SQL scripts if applicable
- `src/VirtualCompany.Api/`
  - DI registration if composition root lives here
  - optional internal endpoints only if already patterned in repo
- `tests/VirtualCompany.Api.Tests/` and/or other test projects
  - integration tests for persistence and service behavior

Likely file additions may include names similar to:

- `IConversionAnalyticsService.cs`
- `ConversionAnalyticsService.cs`
- `SalesMessagePerformance.cs`
- `SalesMessagePerformanceVariant.cs` or equivalent
- `CampaignPerformanceSnapshot.cs` or equivalent if snapshots are already a pattern
- `ConversionAnalyticsEventType.cs`
- `SalesMessagePerformanceConfiguration.cs`
- migration files

Use existing naming, foldering, and module boundaries in the repo over these suggestions.

# Implementation plan
1. **Discover existing sales/analytics model before coding**
   - Search for existing concepts such as:
     - campaign
     - sequence
     - sequence step
     - contact
     - deal
     - variant
     - analytics
     - performance
   - Reuse existing entities/IDs where possible.
   - Do not introduce duplicate campaign/sequence abstractions if they already exist.
   - Identify current persistence approach:
     - EF Core code-first
     - SQL scripts
     - repository pattern
     - MediatR/CQRS-lite patterns
   - Follow current tenant-scoping conventions exactly.

2. **Define the persistence model around a canonical per-message record**
   Implement a normalized record that can answer all required rollups. Prefer a base entity like `SalesMessagePerformance` with fields along these lines:

   - `Id`
   - `CompanyId` / tenant key
   - `CampaignId` nullable
   - `SequenceId` nullable
   - `SequenceStepId` nullable
   - `ContactId`
   - `DealId` nullable
   - `MessageId` or outbound communication reference
   - `VariantKey` / `VariantId` nullable
   - event timestamps:
     - `SentAt`
     - `DeliveredAt`
     - `BouncedAt`
     - `OpenedAt`
     - `RepliedAt`
     - `DealCreatedAt`
     - `ConvertedAt`
   - derived flags/counters:
     - `IsSent`
     - `IsDelivered`
     - `IsBounced`
     - `IsOpened`
     - `IsReplied`
     - `IsDealCreated`
     - `IsConverted`
   - optional revenue/risk support fields if available from existing domain:
     - `ExpectedRevenueAmount`
     - `ExpectedRevenueCurrency`
     - `PipelineRiskScore`
     - `LastRiskCalculatedAt`
   - audit metadata:
     - `CreatedAt`
     - `UpdatedAt`

   Notes:
   - Prefer nullable timestamps as source of truth; derive booleans from them if possible.
   - Add unique/index constraints to support idempotent upsert by tenant + message identity.
   - If the system already models message events separately, you may persist both:
     - immutable event log
     - current performance projection
     But only add the event log if it fits existing architecture and does not over-scope the task.

3. **Support A/B variant analytics explicitly**
   Ensure the model can attribute each message to a variant at the sequence-step level.

   Minimum requirement:
   - each performance record stores `VariantId` or stable `VariantKey`
   - aggregation methods can compute:
     - sent count
     - reply count
     - conversion count
     - reply rate
     - conversion rate
   grouped by:
   - campaign
   - sequence
   - step
   - variant

   If variant assignment is modeled elsewhere, reference that entity rather than duplicating it.

4. **Design `IConversionAnalyticsService` around recording and querying**
   Add an application-layer interface with methods similar to:

   - record/upsert message lifecycle events
   - record reply/conversion/deal-created events
   - get campaign performance summary
   - get sequence/step summary
   - get variant comparison summary
   - optionally get contact-level performance history

   Example shape only; adapt to repo conventions:

   - `Task RecordMessageSentAsync(...)`
   - `Task RecordMessageDeliveryAsync(...)`
   - `Task RecordMessageBounceAsync(...)`
   - `Task RecordMessageOpenAsync(...)`
   - `Task RecordMessageReplyAsync(...)`
   - `Task RecordDealCreatedAsync(...)`
   - `Task RecordConversionAsync(...)`
   - `Task<CampaignPerformanceSummaryDto> GetCampaignPerformanceAsync(...)`
   - `Task<IReadOnlyList<VariantPerformanceSummaryDto>> GetVariantPerformanceAsync(...)`

   Prefer a small number of cohesive methods if the codebase favors command objects, e.g.:
   - `RecordMessagePerformanceEventAsync(RecordMessagePerformanceEventCommand command)`

   Requirements:
   - idempotent behavior for repeated webhook/event delivery
   - tenant-scoped access
   - safe handling of partial event arrival order

5. **Implement idempotent event application**
   The service must handle out-of-order and duplicate events safely.

   Rules:
   - if a record does not exist, create it
   - if it exists, only set a timestamp if:
     - it is currently null, or
     - the incoming timestamp is earlier/later according to the business rule you define consistently
   - never double-count duplicate events
   - preserve first-known or canonical timestamp semantics consistently and document them in code comments/tests

   Recommended default:
   - keep the earliest timestamp for each event type unless existing domain semantics require latest provider timestamp.

6. **Add efficient query/aggregation support**
   Implement query methods or repository projections for rollups needed by acceptance criteria.

   At minimum support:
   - campaign totals:
     - sent
     - delivered
     - bounced
     - opened
     - replied
     - deal created
     - converted
   - funnel rates:
     - delivery rate
     - open rate when available
     - reply rate
     - conversion rate
   - variant comparison:
     - per variant counts and rates
   - time-window expected revenue placeholders if existing deal data supports it

   Use SQL/EF projections rather than loading raw rows into memory for aggregation.

7. **Prepare for revenue insights and risk distribution**
   Even if full dashboard logic is not implemented here, persist or expose enough fields to support:
   - expected revenue next 30/60/90 days
   - risk distribution for active deals
   - linkage between message performance and deal outcomes

   If existing deal entities already contain:
   - expected close date
   - amount
   - risk score
   then query them through joins/projections rather than duplicating data unnecessarily.

   If no analytics-facing projection exists, add a lightweight query model or DTO that can later feed the dashboard.

8. **Keep intelligence signal persistence extensible**
   Acceptance criteria mention ghosting, price resistance, and buying signals with confidence scores. Do not fully implement NLP extraction unless already present, but avoid blocking it.

   If there is no existing persistence for signals, consider adding a minimal extensibility point only if it naturally fits current models, such as:
   - a related `DealIntelligenceSignal` entity with:
     - `CompanyId`
     - `DealId`
     - `ContactId` nullable
     - `SignalType`
     - `ConfidenceScore`
     - `DetectedAt`
     - `SourceMessageId` nullable
     - `SourceConversationId` nullable
     - `MetadataJson`
   Only do this if it is low-cost and aligned with current architecture; otherwise leave a clear TODO in code comments and note in tests/follow-up.

9. **Add persistence configuration and migration**
   - Update DbContext.
   - Add indexes for common filters/grouping:
     - tenant + campaign
     - tenant + sequence
     - tenant + step
     - tenant + contact
     - tenant + variant
     - tenant + message unique key
   - Add foreign keys where corresponding tables exist.
   - Keep nullable relationships where upstream entities may not always be present.
   - Generate migration using repo-standard approach.

10. **Register dependencies**
   - Wire `IConversionAnalyticsService` into DI.
   - Ensure any repositories/query services are registered.
   - Keep composition in the existing startup/program/module registration style.

11. **Add tests**
   Add automated tests covering:
   - creating a new performance record from first event
   - updating an existing record with later events
   - duplicate event does not double-count
   - out-of-order event application still yields correct final state
   - tenant A cannot read/update tenant B analytics
   - variant aggregation returns correct reply/conversion rates
   - campaign summary counts are correct
   - conversion timestamp is persisted and queryable

   Prefer integration tests against the real persistence layer if the repo already supports them.

12. **Document assumptions in code**
   Add concise comments where needed for:
   - idempotency semantics
   - timestamp precedence
   - open tracking being optional
   - variant attribution rules
   - how expected revenue/risk are sourced

# Validation steps
1. Inspect the solution structure and existing sales/domain models:
   - search for campaign/sequence/contact/deal/message/variant entities
   - confirm DbContext and migration workflow

2. Build after implementation:
   - `dotnet build`

3. Run tests:
   - `dotnet test`

4. If migrations are used, generate/apply them per repo convention and verify schema compiles cleanly.

5. Manually validate with a focused test scenario:
   - create tenant A and tenant B context
   - record for tenant A:
     - sent
     - delivered
     - opened
     - replied
     - deal created
     - converted
   - record duplicate sent/replied events
   - verify one persisted performance row per message identity
   - verify counts remain correct
   - verify tenant B cannot access tenant A data

6. Validate variant reporting:
   - create two variants on the same step
   - persist several message records across both variants
   - verify reply rate and conversion rate calculations per variant

7. Validate aggregation queries:
   - campaign summary
   - sequence summary
   - step summary
   - contact history if implemented

8. Confirm indexes/constraints exist for:
   - unique message identity per tenant
   - common analytics grouping/filter paths

# Risks and follow-ups
- **Risk: existing domain model mismatch**
  - Campaign/sequence/step/variant entities may already exist under different names. Reconcile carefully instead of creating parallel models.

- **Risk: unclear message identity**
  - If outbound messages lack a stable unique ID, define a canonical composite identity and document it. This is critical for idempotency.

- **Risk: over-scoping into dashboard/forecasting**
  - Keep this task focused on persistence and service/query foundations. Do not build full UI unless required for compile/integration.

- **Risk: event ordering from providers**
  - Delivery/open/reply webhooks may arrive out of order or multiple times. Tests must lock down behavior.

- **Risk: optional open tracking**
  - Some channels/providers may not support opens. Ensure open metrics are nullable/optional and do not break funnel calculations.

- **Risk: tenant leakage in aggregates**
  - Aggregation queries are especially prone to missing tenant filters. Verify every query is tenant-scoped.

Follow-ups to note after implementation:
- add dashboard query endpoints and UI cards for campaign funnel/r