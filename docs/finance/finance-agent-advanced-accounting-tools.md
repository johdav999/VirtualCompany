# Finance agent advanced-accounting tools

Prompt 4 adds ten read tools and four recommendation tools under
`finance-advanced-accounting-agent-v1`. They call the owning Finance application services and do not duplicate
reconciliation scoring, exchange-rate selection, allocation settlement, schedule calculations, or fixed-asset
depreciation.

## Supported reads

| Tool | Authoritative state |
| --- | --- |
| `finance.advanced.read_statement_imports` | Import jobs, rows, issues, checksums, parser/message versions, balances, and imported transaction identities |
| `finance.advanced.read_reconciliation` | Native bank reconciliation detail or advanced reconciliation groups, graph evidence, rule/group versions, confidence, exceptions, matches, and history |
| `finance.advanced.read_subledger_settlement` | Receivable/payable allocations, settlement currency, functional amounts, rate identity, ledger identity, status, and version |
| `finance.advanced.read_payment_batches` | Supplier payment proposals, native payment batches, validations, approvals, execution attempts, acknowledgements, remittances, and settlement metadata |
| `finance.advanced.read_exchange_rates` | Currency definitions, sources, bounded rate sets, observation evidence, approval state, freshness, and readiness |
| `finance.advanced.read_revaluation` | Revaluation population, exact rate bindings, evidence checksums, proposal/reconciliation totals, approval state, and version |
| `finance.advanced.read_dimensions` | Dimension types/members, policies, combination rules, mappings/conflicts, and allocation-template versions |
| `finance.advanced.read_schedules` | Schedule versions/hashes, evidence, deterministic occurrences, exceptions, reconciliation, and optional owning-service preview |
| `finance.advanced.read_fixed_assets` | Asset register, source/version, dimensions, components, lifecycle/disposal history, and optional owning-service depreciation preview |
| `finance.advanced.read_inventory_boundary` | Explicit unsupported boundary for inventory quantity, valuation, and COGS accounting |

Payment execution reads deliberately remove the provider authorization URI. They return status and evidence metadata,
not a link that can authorize or release money.

## Recommendations

The four recommendation tools explain reconciliation evidence, identify stale or unapproved FX evidence, surface
schedule/asset review needs, and prioritize settlement exceptions. They return `review_required` freshness and never
apply a match or allocation, approve a rate, release a payment, post a revaluation, generate an occurrence, calculate
tax depreciation, or mutate an asset.

If a rate is missing or unapproved, the response contains no invented rate or amount. Schedule and depreciation
previews are returned exactly from `IAccountingScheduleService` and `IFixedAssetService`. Commerce records are not
used as a substitute for an inventory subledger.

## Bounds and provenance

- Every list accepts at most 100 items; nested collections are also capped at 100.
- Depreciation preview ranges are limited to 366 days.
- Responses declare truncation and return at most 2,000 source IDs while preserving the complete source-ID count.
- Currency, period, source record, source version, checksums, rate-set/observation identities, and ledger links remain
  in owning DTOs or response provenance.
- Reads are company-scoped by every owning application query. Missing or cross-company objects return the same safe
  not-found boundary.

All tools require `finance.view` and Finance scope. The family contains no execute tool. Payment release, statement
commit, reconciliation acceptance, allocation changes, rate approval, revaluation posting, schedule activation or
generation, and asset lifecycle commands remain in their existing human/policy-governed workflows.
