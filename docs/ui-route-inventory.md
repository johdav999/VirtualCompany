# UI Route Inventory

This inventory records the route topology after the UI consolidation. Canonical routes are the destinations used by current navigation. Compatibility routes remain authorized by their existing page or redirect to a canonical view with company context preserved.

## Primary And Settings Routes

| Purpose | Canonical route | Compatibility or contextual routes |
| --- | --- | --- |
| Overview | `/dashboard` | `/` resolves through the existing home flow; Today is the default. `companyId` selects company context, `period=month` selects Monthly, `lens=company|finance|sales|marketing|customers` selects an authorized responsibility lens, and optional `year` plus `month` select a reporting month together. |
| Agent team | `/agents/staff` | `/agents`, `/agents/{AgentId}`, `/agents/{AgentId}/chat` remain canonical profile/chat context |
| Finance | `/finance` | See Finance table |
| Accountant portfolio | `/accountant/portfolio` | Available only when the signed-in user has an explicit accountant membership; company detail stays on the route with `companyId` context and engagement deep links carry the explicit company and fiscal period into the shared close workspace |
| Sales | `/app/sales` | See Sales table |
| Support | `/support` | `/support/cases` |
| Work | `/work` | `/tasks`, `/approvals`, `/inbox`, `/outbound-review-queue`, `/queue` remain compatibility/detail routes |
| History | `/history` | `/activity-feed`, `/audit`, `/audit/{AuditEventId}` |
| Settings | `/settings` | Focused settings routes below |

## Agent And Company Settings

- `/settings/responsibilities` (company-scoped responsibility matrix and size-preset setup; all active members may read, while owner/admin mutation is enforced by the API)
- `/settings/agents`
- `/agents/manage`
- `/agents/mailboxes/connect`
- `/agents/automation`
- `/onboarding`
- `/briefing-preferences`
- `/workflows`
- `/settings/profile`
- `/finance/settings`
- `/finance/settings/email-settings`
- `/finance/settings/integrations/{ProviderKey}`
- `/support/settings/sla`

`/agents/manage` retains existing `companyId`, `agentId`, and stable anchors for roster, brief, capabilities, access, team inboxes, and operating profile. `/settings/agents` is the canonical settings entry point.

## Finance

| Area | Canonical routes | Compatibility routes |
| --- | --- | --- |
| Overview | `/finance` | None |
| Ask Laura | `/finance/workbench` | Governed Finance conversation and supervision workspace; may receive an authorized visible record reference from a Finance detail page |
| Cash | `/finance/cash-position`, `/finance/balances`, `/finance/monthly-summary` | None; balances and monthly reporting are contextual Cash views |
| Customer invoices | `/finance/invoices`, `/finance/invoices/{InvoiceId}` | `/finance/reviews`, `/finance/reviews/{InvoiceId}` are contextual review views |
| Supplier bills | `/finance/supplier-bills`, `/finance/supplier-bills/{BillId}` | `/finance/bills`, `/finance/bills/{BillId}`, `/finance/bill-inbox`, `/finance/bill-inbox/{BillId}` |
| Supplier review | `/finance/supplier-bills/review`, `/finance/supplier-bills/review/{BillId}` | Bill inbox aliases above |
| Payments | `/finance/payments`, `/finance/payments/{PaymentId}` | None |
| Transactions | `/finance/transactions`, `/finance/transactions/{TransactionId}` | `/finance/activity`, `/finance/activity/{TransactionId}` |
| Accounting | `/finance/accounting/close-workspace`, `/finance/accounting/setup`, `/finance/accounting/accounts`, `/finance/accounting/periods`, `/finance/accounting/journals`, `/finance/accounting/reconciliation`, `/finance/accounting/reports`, `/finance/accounting/compliance-calendar`, `/finance/accounting/audit-packages`, `/finance/accounting/report-definitions`, `/finance/accounting/year-end`, `/finance/accounting/advanced`, `/finance/accounting/currency-rates`, `/finance/accounting/dimensions`, `/finance/accounting/schedules`, `/finance/accounting/fixed-assets`, `/finance/accounting/revaluation` | The close workspace is the evidence-led operational entry point; `/finance/accounting/reports?view=revaluation`, `?view=dimensions`, `?view=schedules`, and `?view=assets` remain compatibility views with company and drill-down context preserved |
| Issues | `/finance/issues`, `/finance/issues/{AnomalyId}` | `/finance/anomalies`, `/finance/anomalies/{AnomalyId}` |
| Supporting detail | `/finance/counterparties`, `/finance/alerts/{AlertId}` | Contextual routes, not local navigation |
| Legacy mailbox | `/finance/mailbox` | Configuration is now entered through Settings |

## Sales

- `/app/sales`
- `/app/sales/prospects`
- `/app/sales/pipeline`
- `/app/sales/campaigns`
- `/app/sales/deals/{DealId}`
- `/app/sales/contacts/{ContactId}`

Compatibility:

- `/app/sales/prospecting` renders the canonical Prospects surface.
- `/app/sales/leads` redirects to `/app/sales/prospects?view=leads` while preserving `companyId`.

## Support

- `/support` and `/support/cases`
- `/support/cases/{CaseId}`
- `/support/knowledge`

Compatibility:

- `/support/knowledge-gaps` renders the Knowledge gaps view.
- `/support/memory` renders the governed Memory view and links within the Knowledge area.
- `/support/settings/sla` remains an authorized settings route entered from Settings.

## Restricted And Public Routes

Restricted routes retain their existing environment and authorization checks:

- `/simulation-lab`
- `/system/admin/transparency-events`
- `/system/admin/transparency-events/{EventId}`
- `/system/admin/tool-registry`
- `/system/admin/tool-executions`
- `/system/admin/tool-executions/{ExecutionId}`

Public routes remain separate from the authenticated application information architecture:

- `/company`
- `/contact`

## Context Preservation Rules

- Company-scoped navigation carries `companyId`.
- Today and Monthly persist the authorized responsibility view with `lens`; an unavailable lens falls back to the server-selected default without exposing the requested area.
- Monthly stays on `/dashboard` with `period=month`. Explicit calendar navigation preserves `companyId`, `lens`, `year`, and `month`; returning to Today removes the monthly period parameters.
- Detail routes preserve their typed record identifier.
- Work selection uses `tab`, `taskId`, or `itemId`.
- Sales Prospects uses `view=leads` for inbound lead state.
- Legacy routes use the same underlying authorized page or a replace-navigation redirect; they do not bypass target authorization.
