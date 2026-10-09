# Finance insight reads and refresh commands

Finance insight queries consume the last reconciled projection. They do not run
financial checks, create or resolve insights, queue work, publish cache entries,
or remove expired cache entries.

## Read behavior

- `GET /api/companies/{companyId}/finance/insights` serves a matching, unexpired
  snapshot when eligible. On a cache miss or expiry it reads persisted insight
  records. Entity and resolved-status filters apply to those records.
- An uninitialized insight projection returns an empty items collection. Viewing
  a page does not initialize it. Existing finance initialization/access checks
  still apply.
- `refreshSnapshot=true` on insight GET and the legacy
  `refreshInsightsSnapshot=true` on analytics GET bypass the snapshot cache.
  Both remain read-only. The latter no longer performs reconciliation.
- Date/window parameters select the snapshot key; a database fallback represents
  the last reconciled records, not a new historical calculation. Use an explicit
  refresh with the desired parameters to recompute. Item `observedAt` and
  `updatedAt` identify the evidence and reconciliation timestamps;
  `generatedAt` identifies response generation (or cached snapshot generation).

## Refresh ownership

`IFinanceInsightRefreshService`, implemented by `FinanceInsightRefreshService`,
owns both immediate refresh and durable queued refresh. These operations have
been removed from `IFinanceReadService`.

The existing owner/admin endpoint remains:

`POST /internal/companies/{companyId}/finance/insights/refresh`

Its request shape, company authorization and immediate/background behavior are
unchanged. Immediate refresh evaluates checks, reconciles stable insight
identities and resolution state, then publishes the snapshot. Background refresh
queues the existing company/descriptor-keyed execution for the existing worker.
The worker retains its claim, tenant scope, attempt and retry behavior.

Startup refresh and Finance workflow triggers now invoke the command service.
They continue to maintain freshness without requiring a user to visit a page.
Deployments that disable those workers must explicitly refresh to initialize or
update insights. Cache expiry alone does not schedule reconciliation.

The read service retains pure calculation helpers used by the refresh service
and analytics. No persistence model, migration, route or response shape changes
are required for this separation.

## Regression coverage

`FinanceInsightReadBoundaryTests` rejects saves and cache mutations during empty,
cached, uncached, expired, filtered and analytics reads. It also verifies explicit
refresh resolution, stable identity, refreshed cache publication and tenant
rejection. API tests cover read-state preservation, refresh authorization,
cross-company denial and the existing immediate refresh endpoint. Existing
workflow, queue deduplication, persistence and insight-generation tests remain.

Verified on 9 October 2026:

- Eight Finance read-boundary cases passed.
- The expanded API/workflow/domain-event/DI selection passed 35 cases initially;
  its remaining DI expectation was corrected to include the new service's scoped
  lifetime, and that case passed on a focused rerun (36 distinct API cases).
- API and Web builds succeeded. Existing compiler warnings remain.
- EF `migrations has-pending-model-changes` reported no model changes.
- `git diff --check` passed.

TRX results are under `artifacts/finance-insight-refactor-tests`. This is automated
local verification; running application hosts were not restarted and no schema
changes were applied.
