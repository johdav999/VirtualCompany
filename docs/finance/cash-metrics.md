# Cash Analytics Metric Inventory

This inventory covers the finance dashboard cash endpoints and the finance agent cash query tool surface introduced for US-26.4.

| Metric or query | Consumer surface | Source tables | Computation mode | Cache or summary layer | Refresh behavior | Rebuild or backfill path | Explainability payload |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Current cash balance | `/api/dashboard/finance-snapshot`, `/internal/companies/{companyId}/finance/dashboard/current-cash-balance`, `/internal/companies/{companyId}/finance/dashboard/cash-metrics` | `finance_accounts`, `ledger_entries`, `ledger_entry_lines` | Live | None | Reflects new posted ledger entries immediately after commit | None required | Dashboard value only |
| Expected incoming cash | Same dashboard endpoints | `finance_invoices`, `payments`, `payment_allocations` | Live | None | Reflects invoice, payment, and allocation writes immediately after commit | None required | Dashboard value only |
| Expected outgoing cash | Same dashboard endpoints | `finance_bills`, `payments`, `payment_allocations` | Live | None | Reflects bill, payment, and allocation writes immediately after commit | None required | Dashboard value only |
| Overdue receivables | Same dashboard endpoints | `finance_invoices`, `payments`, `payment_allocations` | Live | None | Reflects invoice and allocation writes immediately after commit | None required | Dashboard value only |
| Upcoming payables | Same dashboard endpoints | `finance_bills`, `payments`, `payment_allocations` | Live | None | Reflects bill and allocation writes immediately after commit | None required | Dashboard value only |
| What should I pay this week | `resolve_finance_agent_query` with phrase `what should i pay this week` | `companies`, `finance_bills`, `payments`, `payment_allocations` | Live | None | Reflects bill, payment, allocation, and company timezone writes immediately after commit | None required | `sourceRecordIds` plus metric components for original amount, completed allocations, remaining balance, and scheduled outgoing payments |
| Which customers are overdue | `resolve_finance_agent_query` with phrase `which customers are overdue` | `companies`, `finance_invoices`, `payments`, `payment_allocations` | Live | None | Reflects invoice, allocation, and company timezone writes immediately after commit | None required | `sourceRecordIds` plus metric components for original amount, completed allocations, remaining balance, days overdue, and aging bucket |
| Why is cash down this month | `resolve_finance_agent_query` with phrase `why is cash down this month` | `companies`, `finance_accounts`, `finance_transactions` | Live | None | Reflects transaction and company timezone writes immediately after commit | None required | `sourceRecordIds` plus ranked metric components for net cash movement, inflows, outflows, and category deltas |

## Current implementation boundary

- `CompanyDashboardFinanceSnapshotService` computes dashboard cash metrics directly from tenant-scoped finance tables. It does not read Redis, summary tables, projection rows, or materialized aggregates.
- `CompanyFinanceReadService.ResolveAgentQueryAsync` computes all three supported finance agent cash queries directly from tenant-scoped finance tables. It does not read Redis, summary tables, projection rows, or materialized aggregates.
- `financial_statement_snapshots` and `trial_balance_snapshots` already exist for closed-period statement reporting, but they are not part of the cash analytics read path documented here.

## Refresh and rebuild behavior

- Because these cash metrics are live queries, there is no separate cache invalidation, aggregate refresh, or projection rebuild operation for TASK-26.4.3.
- The only operational prerequisite is that a company is in the seeded finance state before agent-query reads are allowed through the finance tool boundary.
- If a future optimization introduces a cache or aggregate for these queries, it must remain tenant-scoped and preserve `sourceRecordIds` plus `metricComponents` for drill-through.

## Tenant scoping

- Dashboard queries filter every table read by `company_id`.
- Agent cash queries enforce the requested company id and also reject mismatched active company context when one is present.
- The latency validation harness in `tests/VirtualCompany.Api.Tests/CashAnalyticsLatencyIntegrationTests.cs` seeds multiple companies and asserts that supported responses do not surface known source ids from another company.
