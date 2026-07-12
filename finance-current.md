# Laura Finance Agent Current Status

This document tracks the current implementation status of Laura, the Finance Manager agent, after the latest Fortnox paid supplier bill expense-posting work. It is a current-state companion to `finance.md`.

## Current Summary

Laura is implemented as a seeded Finance Manager agent with finance-scoped tools, approval guardrails, audit logging, deterministic finance queries, supplier bill workflows, Fortnox integration, and dashboard support.

Laura is still not a fully independent SME finance manager. She is currently a guided finance operator with some real execute capability. The latest implementation expands her from read/recommend/approval actions into a concrete Fortnox supplier bill expense-posting workflow.

Current maturity estimate:

- Finance platform foundation for Laura: 65-75%.
- Independent SME finance-manager behavior: 30-40%.
- Fortnox supplier bill expense-posting capability: implemented for paid draft supplier bills, subject to Fortnox connection/scopes and finance approval policy.

## Implemented Laura Capabilities

### Agent Identity and Governance

Laura is seeded as:

- Name: Laura.
- Role: Finance Manager.
- Department: Finance.
- Autonomy level: guided.
- Finance data scope.
- Approval-aware execute actions.
- Evidence-first finance communication profile.

Key file:

- `src/VirtualCompany.Infrastructure/Companies/LauraFinanceAgentSeedData.cs`

### Finance Tool Surface

Laura currently has access to these finance tools:

- `get_cash_balance`
- `resolve_finance_agent_query`
- `list_transactions`
- `list_uncategorized_transactions`
- `list_invoices_awaiting_approval`
- `get_profit_and_loss_summary`
- `recommend_transaction_category`
- `recommend_invoice_approval_decision`
- `evaluate_transaction_anomaly`
- `categorize_transaction`
- `approve_invoice`
- `post_paid_supplier_bill_expense`

The newest tool is `post_paid_supplier_bill_expense`.

Key files:

- `src/VirtualCompany.Infrastructure/Companies/StaticCompanyToolRegistry.cs`
- `src/VirtualCompany.Infrastructure/Companies/InternalCompanyToolContract.cs`
- `src/VirtualCompany.Infrastructure/Finance/FinanceToolProviders.cs`

## Newly Implemented: Paid Supplier Bill Expense Posting

Laura can now post a paid supplier bill expense through Fortnox when the bill has already been paid but is still a Fortnox draft supplier invoice.

The implemented path:

1. User or Laura starts expense posting for a supplier bill.
2. Backend validates the company tenant context.
3. Backend validates the bill is a supplier invoice.
4. Backend validates the bill is paid.
5. Backend validates the bill is still draft/not already booked.
6. Backend validates Fortnox is connected.
7. Backend checks Fortnox granted scopes include:
   - `bookkeeping`
   - `supplierinvoice`
8. Backend calls the existing Fortnox supplier invoice bookkeeping endpoint path.
9. Draft action status is updated and returned to the UI/tool caller.

New endpoint:

- `POST /internal/companies/{companyId}/finance/bills/{billId}/paid-expense-posting`

New backend service:

- `src/VirtualCompany.Infrastructure/Finance/PaidSupplierBillExpensePostingService.cs`

New contracts:

- `PostPaidSupplierBillExpenseCommand`
- `PaidSupplierBillExpensePostingDto`
- `IPaidSupplierBillExpensePostingService`

Key contract file:

- `src/VirtualCompany.Application/Finance/FinanceContracts.cs`

## Fortnox Support Status

Laura supports the workflow commonly described in Swedish as:

- `bokföra en kostnad i Fortnox`

In this implementation, the English wording is:

- `post an expense`
- more specifically: `post a paid supplier bill as an expense`
- Fortnox/API-specific wording: `bookkeep a supplier invoice`

Current Fortnox permission requirement for this implemented flow:

- `Bookkeeping`
- `Supplier Invoice`

The user screenshot also showed these relevant permissions enabled:

- `Bookkeeping`
- `Payments`
- `Supplier`
- `Supplier Invoice`
- `Project`
- `Invoice`
- `Article`
- `Company Information`
- `Customer`

For the implemented paid supplier bill expense-posting path, `Bookkeeping` and `Supplier Invoice` are the critical scopes. `Cost Center` is only needed if the company requires cost center dimensions on supplier invoice rows.

Important operational note:

- If Fortnox permissions are changed in the Fortnox Developer Portal, the tenant must reconnect Fortnox so the OAuth token receives the new granted scopes.

## UI Status

The finance bills UI now exposes Laura's paid expense-posting action.

When a selected bill is:

- a supplier invoice,
- fully paid,
- still in draft posting status,
- not cancelled/credited/booked,

the Fortnox section shows:

- `Ask Laura to post expense`

Clicking it calls the new backend endpoint, updates the Fortnox draft action state, and reloads the bill list/detail.

Key files:

- `src/VirtualCompany.Web/Pages/Finance/BillsPage.razor`
- `src/VirtualCompany.Web/Pages/Finance/BillsPage.razor.cs`
- `src/VirtualCompany.Web/Services/FinanceApiClient.cs`

## Approval and Safety Status

Laura remains guided and policy-bound.

The new execute tool is registered as an execute finance tool and is included in Laura's approval-required workflow capabilities. The backend endpoint is protected by the existing finance approval policy.

Current safety checks include:

- tenant context enforcement,
- finance approval authorization on the HTTP endpoint,
- finance tool registry schema validation,
- tool permission checks,
- provider boundary through `IFinanceToolProvider`,
- Fortnox connection validation,
- Fortnox granted-scope validation,
- existing draft action/audit behavior.

## Tests and Verification Status

Updated tests/test doubles:

- `tests/VirtualCompany.Api.Tests/FinanceToolProviderBoundaryTests.cs`
- `tests/VirtualCompany.Api.Tests/FinanceToolDefinitionManifestTests.cs`
- `tests/VirtualCompany.Api.Tests/FinanceToolExecutionFlowIntegrationTests.cs`

Verification performed:

- Web project compile succeeded when built to a temporary output directory.

Verification limitation:

- Full solution/API/test builds were blocked by the local environment. The running Web app locked build output files, and API/test restore/build failed inside MSBuild project-reference resolution without compiler diagnostics. No source-level compiler error was emitted in the captured output.

## What Laura Still Cannot Do Independently

Laura still lacks a full autonomous finance operating cadence.

Major gaps remain:

- daily cash review owned by Laura,
- weekly payment plan,
- overdue receivables collections workflow,
- customer payment reminder drafting/sending,
- refund/credit workflow with support,
- marketing spend approval and budget pacing,
- month-end close checklist,
- reconciliation ownership,
- VAT/tax calendar support,
- CEO finance briefing generation,
- finance-aware direct chat beyond deterministic/generic behavior,
- model-backed SME reasoning over finance evidence,
- typed cross-agent workflows with sales, support, marketing, and CEO.

## Recommended Next Implementation Steps

1. Add Laura daily finance review workflow.
2. Add Laura weekly payables/payment plan workflow.
3. Add collections workflow for overdue customer invoices.
4. Add finance-aware direct chat routing to Laura's finance tools.
5. Add typed handoff contracts:
   - sales to Laura for invoice/payment terms,
   - support to Laura for refunds/credits,
   - marketing to Laura for spend approvals,
   - Laura to CEO for exceptions and decisions.
6. Build Laura Finance Manager cockpit.
7. Add model-backed SME synthesis with strict source-record grounding.

## Current Bottom Line

Laura now has a concrete Fortnox-backed ability to post a paid supplier bill as an expense, exposed both through backend/tooling and the finance bills UI.

She is closer to being a functional finance operator, but she is not yet an independent SME finance manager. The next major step is to give her recurring finance ownership: daily/weekly/monthly workflows, cross-agent handoffs, and CEO-facing decision briefings.
