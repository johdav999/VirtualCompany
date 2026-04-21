# Finance dashboard cash metric rules

The finance dashboard cash widgets use the selected company and `asOfUtc` context on every query.

- Current cash balance: sum posted ledger entry lines for cash-classified accounts where `posted_at` (or `entry_at` when `posted_at` is null) is on or before `asOfUtc`.
- Expected incoming cash: for each open receivable, use pending incoming payment allocations scheduled inside the upcoming window when they exist; otherwise use the remaining open receivable balance when the receivable due date falls inside the same window.
- Expected outgoing cash: for each open payable, use pending outgoing payment allocations scheduled inside the upcoming window when they exist; otherwise use the remaining open payable balance when the payable due date falls inside the same window.
- Overdue receivables: sum remaining open receivable balances with due dates before `asOfUtc`.
- Upcoming payables: sum remaining open payable balances with due dates on or after `asOfUtc` and before the upcoming-window end.

Remaining open balances are calculated as:

- document amount minus completed payment allocations posted on or before `asOfUtc`
- clamped to zero so partially over-allocated seed data cannot produce negative balances

Pending scheduled allocations replace, rather than supplement, the unscheduled remainder in expected incoming and outgoing metrics so the forecast does not double count both the scheduled installment and the full document balance.

## Finance agent query resolvers

- `resolve_finance_agent_query` is a live-computed, tenant-scoped finance tool. It does not use pre-aggregated projection tables in the current implementation.
- Seeded multi-company integration coverage validates each supported resolver against a 2 second local threshold per query. Live computation remained within that threshold, so no summary-table migrations or aggregate rebuild jobs were introduced for TASK-26.4.2.
- Supported deterministic phrases are `what should i pay this week`, `which customers are overdue`, and `why is cash down this month`.
- Resolver routing is explicit and phrase-normalized. Unsupported phrasing fails instead of falling back to free-form interpretation.
- `what should i pay this week` uses the company timezone from `companies.timezone`, falls back to `UTC`, and evaluates a Monday-start company week. It returns payable items with bill ids, related allocation ids, and calculation components for original amount, completed allocations, remaining balance, and scheduled payments in the week window.
- `which customers are overdue` uses the company timezone from `companies.timezone`, falls back to `UTC`, and computes days overdue from the local company date. It returns invoice ids, customer ids, aging buckets, and remaining-balance components derived from completed incoming allocations.
- `why is cash down this month` compares current month-to-date cash-account movements against the same number of elapsed company-local days in the prior month. It returns ranked metric components and transaction ids for the categories that moved cash the most.
- Every supported response includes exact provenance through `sourceRecordIds` and `metricComponents`. Bill and invoice answers surface document ids plus allocation ids; month-to-date cash explanations surface the transaction ids that contributed to each component delta.
- Because these agent resolver metrics are live-computed from finance source tables, refresh behavior matches source-of-truth writes immediately after the underlying transaction, invoice, bill, payment, or allocation records are committed. No delayed refresh window, projection lag, or backfill catch-up cycle applies to these resolver answers in the current implementation.