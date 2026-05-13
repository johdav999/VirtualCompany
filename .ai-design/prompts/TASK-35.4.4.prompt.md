# Goal
Implement backlog task **TASK-35.4.4** for story **US-35.4 Implement conversion analytics, deal intelligence signals, and revenue forecasting dashboard** in the existing .NET modular monolith.

Deliver a tenant-scoped sales analytics capability that:
- records per-message and per-sequence performance metrics
- supports A/B message variants within sequence steps
- detects and stores deal intelligence signals from inbound replies/conversation history
- recalculates active-deal pipeline risk scores at least daily
- exposes dashboard and deal-detail API data for:
  - campaign performance
  - conversion funnel
  - risk distribution
  - expected revenue forecast for next 30/60/90 days
- uses **tenant production data** rather than mock/demo-only data

Keep implementation aligned with the architecture:
- ASP.NET Core modular monolith
- PostgreSQL primary store
- CQRS-lite application layer
- background workers for scheduled recalculation
- tenant isolation on all reads/writes
- dashboard/query-first design with clean separation from UI/controllers

# Scope
Include:
- domain and persistence support for sales engagement analytics, message events, sequence metrics, variant assignment, intelligence signals, and risk snapshots
- application commands/services to record message lifecycle and conversion timestamps
- application queries for dashboard widgets and deal detail API
- scheduled/background recalculation for active-deal risk scores
- signal detection pipeline for ghosting, price resistance, and buying signals with confidence scores
- Blazor dashboard updates for revenue insights widgets/visualizations
- API exposure for dashboard data and deal detail risk/intelligence data
- tests covering tenant scoping, metric aggregation, forecast calculations, variant comparisons, and daily risk refresh behavior

Do not include:
- unrelated mobile work unless existing shared APIs require no-op compatibility
- external BI tooling
- speculative microservice extraction
- full ML forecasting infrastructure; use deterministic/statistical forecasting appropriate for current architecture and production data availability
- raw chain-of-thought storage from LLM/signal analysis

Assume existing sales/deal/campaign/conversation entities may need extension. First inspect the current codebase and adapt to actual module boundaries and naming.

# Files to touch
Inspect first, then update the appropriate files under these likely areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add or modify:
- domain entities/value objects/enums for:
  - message performance events/status
  - sequence step variant assignment
  - deal intelligence signal
  - pipeline risk score snapshot
  - revenue forecast snapshot or query DTOs
- EF Core persistence:
  - DbContext mappings
  - migrations
  - indexes for tenant/date/deal/campaign/sequence queries
- application layer:
  - commands/handlers for recording message events and conversions
  - services for analytics aggregation
  - services for signal detection orchestration
  - services for risk scoring and forecast calculation
  - queries/handlers for dashboard widgets and deal detail API
- infrastructure:
  - scheduled job/background worker for daily risk recalculation
  - optional inbox/reply processing integration point for signal extraction
- API endpoints/controllers:
  - sales dashboard analytics endpoint(s)
  - deal detail endpoint updates
- web UI:
  - sales dashboard widgets/charts/tables for campaign performance, funnel, risk distribution, and 30/60/90 forecast
  - variant comparison presentation
- tests:
  - unit tests for aggregation/scoring/forecast logic
  - integration/API tests for tenant-scoped responses and acceptance criteria

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
for migration/build conventions before creating schema changes.

# Implementation plan
1. **Inspect current sales analytics surface area**
   - Find existing modules/entities for:
     - deals
     - campaigns
     - sequences
     - sequence steps
     - messages/email events
     - conversations/inbound replies
     - dashboard queries
   - Identify current tenant scoping patterns, CQRS conventions, background job patterns, and migration workflow.
   - Reuse existing naming and module boundaries instead of inventing parallel structures.

2. **Design the minimal domain model extensions**
   Add or extend entities to support the acceptance criteria. Prefer normalized relational tables plus lightweight snapshots where needed.

   Required capabilities:
   - per-message metrics:
     - sent timestamp
     - delivered timestamp
     - bounced timestamp
     - opened timestamp when available
     - replied timestamp
     - deal created timestamp
     - conversion timestamp
   - per-sequence and per-sequence-step aggregation
   - A/B variant assignment within a sequence step
   - intelligence signals:
     - type: `ghosting`, `price_resistance`, `buying_signal`
     - confidence score
     - source reply/message/conversation reference
     - detected timestamp
   - active-deal risk score snapshots/history:
     - score
     - contributing factors/reasons summary
     - calculated timestamp
   - forecast support:
     - expected revenue over next 30/60/90 days based on active deals and current probabilities/risk-adjusted expectations

   Suggested relational shape if no equivalent exists:
   - message engagement/outcome table keyed by tenant + message/deal/sequence/campaign
   - sequence step variant assignment table or variant columns on existing step/message records
   - deal intelligence signals table
   - deal risk score snapshots table
   - optional materialized/snapshot table only if needed for performance; otherwise compute via query services first

3. **Add persistence and migration**
   - Update EF Core mappings and create migration(s).
   - Ensure all tenant-owned tables include tenant/company ID and foreign keys.
   - Add indexes for:
     - `(company_id, created_at)`
     - `(company_id, campaign_id, created_at)`
     - `(company_id, sequence_id, created_at)`
     - `(company_id, deal_id, calculated_at desc)`
     - `(company_id, signal_type, detected_at desc)`
   - Preserve migration conventions used in repo.

4. **Implement message and sequence metric recording**
   - Add application commands/services to record lifecycle events from outbound/inbound processing.
   - Ensure idempotent updates where events may arrive multiple times.
   - Record timestamps for:
     - sent
     - delivered
     - bounced
     - opened
     - replied
     - deal created
     - conversion
   - Aggregate per-message and per-sequence metrics through query services or maintained counters.
   - If existing outbox/inbox processors already handle message events, integrate there rather than duplicating pipelines.

5. **Implement A/B variant assignment and reporting**
   - Extend sequence step model to support multiple message variants.
   - Add assignment logic when a contact/deal enters a step:
     - deterministic/randomized assignment per tenant conventions
     - persist assigned variant on the message/send record
   - Add comparative reporting:
     - reply rate per variant
     - conversion rate per variant
   - Ensure calculations use actual assigned sends, not sequence template counts.

6. **Implement deal intelligence signal detection**
   - Add a service that analyzes inbound replies and relevant conversation history.
   - Detect and store:
     - ghosting
     - price resistance
     - buying signals
   - Persist:
     - signal type
     - confidence score
     - source references
     - short rationale/summary safe for audit/UI
     - detected timestamp
   - If LLM-assisted detection exists elsewhere, reuse orchestration/policy patterns and store only structured outputs.
   - Ghosting may also be rule-assisted (e.g., no reply after threshold) if that fits current architecture better; combine deterministic and AI-assisted logic if useful.

7. **Implement daily active-deal risk recalculation**
   - Create a background worker/scheduled job that recalculates risk for active deals at least daily.
   - Risk inputs may include:
     - recency of last inbound/outbound activity
     - ghosting signals
     - price resistance signals
     - buying signals
     - stage aging
     - reply/open engagement
     - bounce/delivery issues
   - Persist latest score and optionally history snapshots.
   - Expose latest risk score and contributing factors through:
     - dashboard query
     - deal detail API
   - Make job idempotent and tenant-scoped.

8. **Implement revenue forecast and dashboard query services**
   Build application queries that return tenant production-data-backed widgets for:
   - campaign performance
     - sends, deliveries, bounces, opens, replies, deals created, conversions
     - grouped by campaign/sequence as supported by current model
   - conversion funnel
     - sent → delivered → opened (when available) → replied → deal created → converted
   - risk distribution
     - counts/value buckets by risk band for active deals
   - expected revenue
     - next 30 days
     - next 60 days
     - next 90 days

   Forecast guidance:
   - Use deterministic expected revenue from active deals:
     - amount × probability × risk adjustment × expected close timing window
   - If expected close date exists, bucket by 30/60/90-day windows.
   - If not, derive a conservative estimate from stage/progression/history already available.
   - Document assumptions in code comments and keep logic testable.

9. **Expose API endpoints**
   - Add or extend tenant-scoped API endpoints for:
     - sales dashboard revenue insights
     - campaign performance
     - funnel metrics
     - risk distribution
     - forecast summary
     - deal detail risk/intelligence data
     - variant comparison metrics
   - Follow existing API patterns and authorization conventions.
   - Return DTOs tailored for UI consumption, not EF entities.

10. **Update Blazor sales dashboard**
   - Add widgets/visualizations for:
     - campaign performance
     - conversion funnel
     - risk distribution
     - expected revenue 30/60/90
     - A/B variant comparison
   - Use existing dashboard component patterns/styles.
   - Handle empty states and partial data gracefully:
     - e.g. open rate unavailable when tracking is unavailable
   - Ensure tenant production data is loaded from real API/query services.

11. **Add tests**
   Cover acceptance criteria with a mix of unit and integration tests.

   Minimum test coverage:
   - message event recording stores all required timestamps correctly
   - sequence aggregation computes sent/delivered/bounced/opened/replied/deal-created/conversion metrics
   - variant assignment persists and comparative reply/conversion rates are correct
   - signal detection stores ghosting/price resistance/buying signals with confidence scores
   - daily risk recalculation updates active deals and is exposed via API
   - dashboard returns campaign performance, funnel, risk distribution, and 30/60/90 expected revenue using seeded tenant data
   - tenant isolation prevents cross-company analytics leakage
   - idempotency for duplicate message/reply events where applicable

12. **Document assumptions inline**
   - If the current codebase lacks some sales primitives, implement the smallest coherent extension and note assumptions in code comments or concise developer-facing docs.
   - Do not add broad architectural docs unless necessary.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of normal workflow:
   - generate/apply the new EF Core migration per repo conventions
   - verify schema updates succeed locally

4. Manually validate API behavior with seeded/local data:
   - create or use tenant-scoped production-like sales data
   - verify dashboard endpoints return:
     - campaign performance
     - funnel metrics
     - risk distribution
     - expected revenue for 30/60/90 days
   - verify deal detail API includes latest risk score and intelligence signals

5. Manually validate UI:
   - open Blazor sales dashboard
   - confirm widgets render with real tenant data
   - confirm empty states for missing open tracking or low data volume
   - confirm A/B comparison displays reply and conversion rates per variant

6. Validate scheduled behavior:
   - trigger or run the daily risk recalculation job
   - confirm active deals receive updated scores and timestamps
   - confirm dashboard/API reflect recalculated values

7. Validate tenant isolation:
   - seed at least two companies
   - confirm one tenant cannot access another tenant’s analytics, signals, risk scores, or forecasts

# Risks and follow-ups
- **Existing model mismatch:** Sales/campaign/sequence entities may differ from assumptions. Adapt to actual codebase and avoid duplicate concepts.
- **Forecast quality:** If historical close-date/probability data is sparse, use a conservative deterministic forecast and clearly encode assumptions.
- **Open tracking availability:** Acceptance says “opened when available”; ensure funnel/reporting tolerates null/unavailable open data.
- **Signal detection ambiguity:** Ghosting may require time-window rules in addition to reply-text analysis. Prefer a hybrid deterministic + AI-assisted approach if needed.
- **Performance risk:** Dashboard aggregates over production data may need indexes/caching. Add targeted indexes first; only add snapshots/cache if query performance requires it.
- **Idempotency:** Message lifecycle events and inbound reply processing may be delivered multiple times. Ensure deduplication/update semantics.
- **Background scheduling:** Daily recalculation must be reliable and tenant-safe; use existing worker/locking patterns.
- **UI charting dependencies:** Reuse existing chart components/libraries already in the repo rather than introducing a new dependency unless necessary.
- **Follow-up candidates:** materialized analytics snapshots, more advanced forecasting models, drill-down filters by date range/owner/stage, and explainability details for risk scoring.