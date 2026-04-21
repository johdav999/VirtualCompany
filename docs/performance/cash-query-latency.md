# Cash Query Latency Validation

TASK-26.4.3 keeps the current cash analytics implementation on the live-query path and adds a seeded latency gate instead of introducing summary tables without measurement.

## Thresholds

- Dashboard cash snapshot: median latency for `/api/dashboard/finance-snapshot` must remain at or below `2000 ms` after one warm-up request.
- Finance agent cash queries: median latency for each supported `resolve_finance_agent_query` phrase must remain at or below `2000 ms` after one warm-up request.

## Executable evidence

- Test file: `tests/VirtualCompany.Api.Tests/CashAnalyticsLatencyIntegrationTests.cs`
- Dataset shape:
  - fixed `asOfUtc` of `2026-04-17T12:00:00Z`
  - multiple companies with deterministic ids
  - posted cash ledger entries for dashboard balance calculations
  - open invoices and bills plus completed and pending payment allocations
  - current-month and prior-month finance transactions for the cash-down explanation
- Handler coverage:
  - dashboard latency test calls the authenticated HTTP endpoint used by the dashboard surface
  - agent latency test calls `IInternalCompanyToolContract` for `resolve_finance_agent_query`, then verifies `sourceRecordIds` and `metricComponents`

## Operational note

- No cash analytics summary or projection table is required by the current measurements, so TASK-26.4.3 does not add a finance aggregate migration, backfill worker, or rebuild command.
- If future seeded latency gates fail, add the smallest tenant-scoped aggregate necessary and preserve source attribution in the read model.
