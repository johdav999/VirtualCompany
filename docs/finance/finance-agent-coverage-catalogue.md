# Finance agent coverage catalogue

This document is the checked-in baseline for `finance-agent-coverage-v1`. It is derived from
`FinanceAgentCoverageCatalogue.Manifests`; the completeness tests compare the same metadata with the trusted tool
registry, the Finance authority matrix, planner projection metadata, and effective authority. Coverage never grants
authority. `AgentEffectiveAuthorityResolver` and the P0 actor authorization policy remain authoritative at planning
and execution time.

## Generated baseline

Generated from code metadata on 2026-09-01.

| Measure | Count |
| --- | ---: |
| Domain workflow capabilities | 18 |
| Classified operations | 77 |
| Registered and singly owned Finance tools | 64 |
| Implemented read operations | 35 |
| Implemented recommend/draft operations | 17 |
| Implemented execute operations | 12 |
| Configuration-dependent operations | 2 |
| Unsupported operations | 5 |
| Permanently human-only operations | 6 |

| Capability ID | Workflow | Baseline coverage |
| --- | --- | --- |
| `finance.daily_cash` | Daily cash and liquidity | Implemented read |
| `finance.transaction_review` | Transaction review and categorization | Implemented read, recommend, and governed execute |
| `finance.invoice_review` | Invoice review | Implemented read, recommend, and governed execute |
| `finance.management_reporting` | Management reporting | Implemented bounded P&L read |
| `finance.natural_language_queries` | Bounded Finance questions | Implemented deterministic read |
| `finance.role_analysis` | Finance role analysis | Implemented recommendation |
| `finance.paid_bill_accounting` | Paid supplier-bill accounting | Implemented governed execute |
| `finance.accounting_provider_migration` | Accounting-provider migration | Implemented read, recommend, and governed execute |
| `finance.payables_operations` | Payables, bills, and subscriptions | Unsupported broad coverage |
| `finance.customer_billing_receivables` | Customer billing and receivables | Unsupported broad coverage |
| `finance.ledger_financial_reporting` | Ledger, periods, and financial reporting | Implemented bounded authoritative reads |
| `finance.bank_reconciliation` | Banking, imports, reconciliation, and payments | Configuration-dependent/unsupported; payment initiation human-only |
| `finance.advanced_accounting` | Advanced accounting and drafts | Unsupported broad coverage |
| `finance.close_year_end` | Close and year-end | Implemented reads and recommendations; final authority human-only |
| `finance.compliance_audit` | Compliance and audit | Implemented reads and recommendations; final filing/sign-off human-only |
| `finance.administration` | Finance integrations and administration | Configuration-dependent; credential changes human-only |
| `finance.approval_governance` | Approval governance | Self-approval permanently human-only |
| `finance.provider_reconciliation` | Ambiguous provider outcomes | Final ambiguity resolution human-only |

The six permanent human-only operations are payment initiation/release, credential and consent changes, final
statutory filing or sign-off, final close/lock/reopen/year-end authority, self-approval, and resolution of ambiguous
provider outcomes. The planner detects explicit requests for these operations before an AI-provider call and returns
the safe preparation or navigation alternative declared by the catalogue.

## Adding or changing Finance coverage

1. Add or change the trusted Finance tool manifest and its selection metadata.
2. Add that tool to exactly one domain-workflow operation in `FinanceAgentCoverageCatalogue.Manifests`. Declare the
   action class, permission, scope, risk, approval behavior, integrations, sources, support state, reason, and safe
   fallback. Do not derive this metadata from a route.
3. Add execute risk metadata to `FinanceToolRiskPolicyCatalog` and preserve the P0 actor/effective-authority checks.
4. Run `FinanceAgentCoverageCatalogueTests`. An unowned, duplicate, missing, action-mismatched, permission-mismatched,
   or risk-mismatched tool fails the test and is excluded from Laura's effective authority projection.
5. Update this generated baseline when counts or workflows intentionally change. A coverage entry documents product
   support; it does not add a grant to any persisted agent profile.

Future unsupported workflows must first become explicit catalogue operations. Permanent human-only declarations must
not be converted into execute tools merely by increasing agent autonomy.
