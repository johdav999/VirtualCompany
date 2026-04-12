# Goal
Implement **TASK-11.5.2** for **ST-505 Daily briefings and executive summaries** so that generated briefings **aggregate alerts, approvals, KPI highlights, anomalies, and notable agent updates** and are persisted for dashboard/in-app consumption.

This task should fit the existing architecture:
- ASP.NET Core modular monolith
- PostgreSQL-backed tenant-scoped data
- Background-worker-friendly design
- CQRS-lite application layer
- Communication + Analytics/Cockpit + Approval + Task modules collaborating through typed application services
- Audit-safe, concise summaries without exposing chain-of-thought

Focus on delivering the **briefing aggregation capability** behind scheduled/generated summaries, not a full notification-delivery system overhaul.

# Scope
Implement the minimum vertical slice needed for briefing aggregation:

1. **Domain/application model for briefing aggregate content**
   - A structured DTO/model representing:
     - alerts
     - pending approvals
     - KPI highlights
     - anomalies
     - notable agent updates
   - Include enough metadata to support:
     - rendering in dashboard/mobile later
     - linking back to underlying entities where possible
     - concise summary generation

2. **Tenant-scoped briefing aggregation service**
   - Build an application service that, for a given company and time window, gathers:
     - active/recent alerts or exception-like items
     - pending approvals
     - KPI highlight cards/metrics
     - anomaly indicators
     - notable agent updates/activity
   - The service should return a deterministic structured result suitable for:
     - storage in messages/notifications
     - later LLM summarization or template-based summary text

3. **Initial aggregation logic**
   - Use existing persisted data where available.
   - If some source areas are not fully implemented yet, provide safe fallback behavior:
     - empty sections instead of failures
     - clear internal structure for future enrichment
   - Prefer query composition over hardcoded mock data.

4. **Briefing summary composition**
   - Produce a concise user-facing summary/body from the aggregate result.
   - This can be template/rule-based unless the repo already has an established summarization pipeline.
   - Summary should mention the major sections only when data exists.

5. **Persistence integration**
   - Store generated briefing output in the existing communication model, most likely as a `messages` record with:
     - `message_type` appropriate for summary/briefing
     - structured payload containing the aggregate sections and references
   - Ensure company scoping is enforced.

6. **Tests**
   - Add unit/integration tests for:
     - aggregation behavior
     - empty-state behavior
     - tenant isolation
     - inclusion of approvals/alerts/agent updates when present

Out of scope unless already trivial and clearly supported by existing code:
- Full weekly-summary implementation if daily and weekly are separate pipelines
- Mobile UI work
- Full dashboard UI redesign
- Email/push delivery
- New anomaly-detection engine beyond simple available signals
- Large schema redesign unrelated to briefing aggregation

# Files to touch
Inspect the solution first and adjust to actual project structure, but expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - Add briefing aggregate models/value objects if domain-owned
- `src/VirtualCompany.Application/**`
  - Commands/queries/services for briefing aggregation and generation
  - DTOs/view models for briefing sections
  - Interfaces for data providers if needed
- `src/VirtualCompany.Infrastructure/**`
  - EF Core/query implementations/repositories
  - Persistence mapping for any new entities or message payload handling
- `src/VirtualCompany.Api/**`
  - Endpoint wiring only if there is already an API surface for generating/fetching briefings
- `src/VirtualCompany.Web/**`
  - Only minimal changes if an existing dashboard/briefing view needs the new payload shape
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests
- Potentially:
  - `tests/**` in application/infrastructure test projects if present

Also inspect:
- existing message/conversation models
- approval query services
- dashboard/analytics aggregate services
- background job/scheduler patterns
- tenant context abstractions
- audit/event patterns

Do **not** create parallel patterns if the repo already has established conventions.

# Implementation plan
1. **Discover existing briefing and messaging flow**
   - Search for:
     - `briefing`
     - `summary`
     - `message_type`
     - `conversation`
     - `approval`
     - `dashboard`
     - `alert`
     - `anomaly`
     - `notification`
   - Identify:
     - whether ST-505 already has partial implementation
     - how messages are persisted
     - whether there is an existing scheduled job or generation command
     - where tenant-scoped dashboard aggregates live

2. **Define a structured briefing aggregate contract**
   - Add a model such as `BriefingAggregateResult` with sections like:
     - `Alerts`
     - `PendingApprovals`
     - `KpiHighlights`
     - `Anomalies`
     - `AgentUpdates`
   - Each item should include:
     - title/label
     - concise summary
     - severity/status where relevant
     - source entity type/id where available
     - timestamps
   - Keep payload JSON-friendly and stable for future UI/mobile use.

3. **Implement section-level aggregation providers**
   - Create a central service, e.g. `BriefingAggregationService`, that orchestrates section gathering.
   - Back it with focused query methods/providers:
     - approvals: pending approvals for the company in the target window
     - alerts: workflow failures, escalations, blocked tasks, or existing alert records if present
     - KPI highlights: top metrics from analytics/cockpit aggregates if available
     - anomalies: use existing anomaly/exception indicators if present; otherwise derive simple anomaly-like items from failures/spikes only if supported by current data
     - agent updates: recent completed/blocked/high-impact agent tasks or status changes
   - All queries must be filtered by `company_id`.

4. **Use graceful fallback behavior**
   - If a section has no backing data yet, return an empty list.
   - Do not throw because one section is unavailable.
   - Keep the aggregate generation resilient and deterministic.

5. **Compose a concise briefing body**
   - Implement a formatter/composer that turns the structured aggregate into a readable summary.
   - Example shape:
     - headline sentence
     - bullets or short paragraphs for non-empty sections
   - Keep it concise, executive-friendly, and reference counts/highlights.
   - Avoid chain-of-thought or speculative language.

6. **Persist the generated briefing**
   - Reuse existing conversation/message persistence.
   - Store:
     - body text
     - structured payload JSON with all aggregate sections
     - message type indicating briefing/summary if supported, otherwise use existing summary type
   - If the system has a dedicated executive/company conversation channel, use it; otherwise follow current communication conventions.
   - Ensure idempotency or duplicate-safe behavior if generation can run more than once for the same company/day.

7. **Wire into existing generation flow**
   - If there is already a scheduled daily briefing command/job, plug the aggregation service into it.
   - If not, add the smallest appropriate application command/handler to generate a daily briefing for a company and persist it.
   - Keep scheduling concerns separate from aggregation logic.

8. **Add tests**
   - Unit test the aggregation service:
     - returns all sections
     - empty sections when no data exists
     - includes pending approvals
     - includes notable agent updates from recent task/activity data
   - Integration/API tests:
     - generated briefing is persisted as a tenant-scoped message
     - one company cannot see another company’s briefing content
   - If anomaly/KPI sources are partial, test current implemented behavior explicitly rather than inventing unsupported expectations.

9. **Keep implementation aligned with architecture**
   - CQRS-lite in application layer
   - typed contracts, no direct DB access from controllers/UI
   - tenant isolation enforced in query/repository layer
   - structured payloads for future dashboard/mobile rendering
   - audit-safe summaries only

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add/verify automated coverage for:
   - briefing aggregate includes approvals/alerts/agent updates when seeded
   - empty company data still generates a valid briefing payload
   - tenant isolation across companies
   - persisted message contains structured payload and readable body

4. Manual verification if an API or UI path exists:
   - Generate a briefing for a seeded company
   - Confirm stored message/summary includes:
     - alerts
     - approvals
     - KPI highlights
     - anomalies
     - notable agent updates
   - Confirm links/references to underlying entities are present where supported
   - Confirm no cross-tenant leakage

5. If EF migrations are required:
   - create/update migration using the repo’s existing migration workflow
   - verify schema changes apply cleanly
   - ensure JSON payload columns/types are mapped correctly

# Risks and follow-ups
- **Risk: source data for some sections may not exist yet**
  - Mitigation: implement empty-section fallback and structure for future enrichment.

- **Risk: “alerts” and “anomalies” may not yet have first-class tables**
  - Mitigation: derive from existing blocked/failed/escalated workflow/task data only if consistent with current architecture; otherwise leave section empty.

- **Risk: duplicate daily briefings**
  - Mitigation: check for an existing briefing for the same company/time window before inserting, or add an idempotency key if a pattern exists.

- **Risk: payload shape drift**
  - Mitigation: define a stable DTO/contract now and serialize consistently.

- **Risk: summary generation becomes tightly coupled to current UI wording**
  - Mitigation: keep structured aggregate separate from rendered body text.

Follow-ups after this task:
- weekly summary specialization
- configurable delivery preferences and notification fan-out
- richer KPI/anomaly providers from analytics module
- dashboard/mobile rendering of structured briefing sections
- source attribution links and drill-down UX
- caching of expensive aggregate queries for scheduled generation