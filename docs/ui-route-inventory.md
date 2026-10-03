# UI Route Inventory

This inventory records the route topology after the UI consolidation. Canonical routes are the destinations used by current navigation. Compatibility routes remain authorized by their existing page or redirect to a canonical view with company context preserved.

## Primary And Settings Routes

Sales presentation authoring lives at `/app/sales/presentation-presets`: PowerPoint source, reusable scripts, approved cached narration and presentation settings are edited there. The canonical `/app/sales/meeting-invitations/{InvitationId}/prepare` route is a preset selector and meeting launcher. The former editor remains only at the explicit `/legacy-prepare` compatibility suffix; current navigation does not link to it. Attendee consent is evaluated in the actual meeting, never inherited as preset approval.


| Purpose | Canonical route | Compatibility or contextual routes |
| --- | --- | --- |
| Overview | `/dashboard` | `/` resolves through the existing home flow; Today is the default. `companyId` selects company context, `period=month` selects Monthly, `lens=company|finance|sales|marketing|customers` selects an authorized responsibility lens, and optional `year` plus `month` select a reporting month together. |
| Priority evidence | `/dashboard/priorities` | `companyId`, authorized `lens`, typed `key`, and validated exact Overview `returnUrl`; fresh recorded evidence only. Company risks can also carry a validated same-company `healthReturnUrl` retaining the report department filter. Owning module actions receive separate same-company `priorityReturnUrl` to restore evidence after reload. |
| Company health | `/dashboard/company-health` | Company perspective with `companyId`, optional `department=finance|sales|marketing|customers`, and exact Overview `returnUrl`. Dated operational projection, source coverage and recorded plan comparison; unauthorized departments remain gaps. Read-only report; no report export. Risk follow-up and decision commands remain in existing Work services. |
| Agent team work board | `/agents/staff` | `companyId`, authorized `responsibility`, participating `agentId`, `objective`, `state`, and `skip` retain filtered work context. Seven lifecycle projections; shared company outcomes appear once. `/agents`, `/agents/{AgentId}`, `/agents/{AgentId}/chat` retain profile/chat context. |
| Durable agent work detail | `/agents/work/{Kind}/{Id}` | Typed `task`, `initiative`, `case`, or `deal` identity with `companyId`; validated `boardReturnUrl`, `recordReturnUrl` and Overview `returnUrl`. Linked Work/Sales/Support records receive separate `agentWorkReturnUrl`. Permission-filtered evidence and owning lifecycle; reads do not execute actions. |
| Monthly agent company summary | `/agents/staff/summary` | Retained existing authorized monthly measures and roster; `companyId`, optional `year` and `month`. Current work links to canonical board. |
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
- `/settings/document-repositories` (company-admin eight-step Microsoft 365 connection with callback resume through an opaque setup handle, root/output-folder selection, explicit agent grants, server-derived review, durable provisioning, customer-managed advanced fallback, and existing import/sync health; remains under Settings rather than primary navigation)
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
| Operational reports | `/finance/receivables-aging`, `/finance/payables-aging`, `/finance/cash-forecast` | UTC cutoff, currency, bucket and horizon are durable query filters. `financeSource` retains operational/Fortnox review scope; `financeReturnUrl` and `recordReturnUrl` retain validated company-local report/list/record context. CSV refreshes the same authorized API scope before browser handoff. |
| Customer invoices | `/finance/invoices`, `/finance/invoices/{InvoiceId}` | `/finance/reviews`, `/finance/reviews/{InvoiceId}` are contextual review views |
| Supplier bills | `/finance/supplier-bills`, `/finance/supplier-bills/{BillId}` | `/finance/bills`, `/finance/bills/{BillId}`, `/finance/bill-inbox`, `/finance/bill-inbox/{BillId}` |
| Supplier review | `/finance/supplier-bills/review`, `/finance/supplier-bills/review/{BillId}` | Bill inbox aliases above |
| Payments | `/finance/payments`, `/finance/payments/{PaymentId}` | None |
| Transactions | `/finance/transactions`, `/finance/transactions/{TransactionId}` | `/finance/activity`, `/finance/activity/{TransactionId}` |
| Accounting | `/finance/accounting/close-workspace`, `/finance/accounting/setup`, `/finance/accounting/accounts`, `/finance/accounting/periods`, `/finance/accounting/journals`, `/finance/accounting/reconciliation`, `/finance/accounting/reports`, `/finance/accounting/compliance-calendar`, `/finance/accounting/audit-packages`, `/finance/accounting/report-definitions`, `/finance/accounting/year-end`, `/finance/accounting/advanced`, `/finance/accounting/currency-rates`, `/finance/accounting/dimensions`, `/finance/accounting/schedules`, `/finance/accounting/fixed-assets`, `/finance/accounting/revaluation` | P07 carries periodId/source/Overview and bounded accountingReturnUrl through close and source views. Separate reports use view=profit-loss or balance-sheet with period/comparison/snapshot/accountCode selection; journalId opens exact posted detail. Work uses existing financeReturnUrl. Revaluation/dimensions/schedules/assets report aliases and owning lock/reopen/export/provider policies remain retained |
| Issues | `/finance/issues`, `/finance/issues/{AnomalyId}` | `/finance/anomalies`, `/finance/anomalies/{AnomalyId}` |
| Supporting detail | `/finance/counterparties`, `/finance/alerts/{AlertId}` | Contextual routes, not local navigation |
| Legacy mailbox | `/finance/mailbox` | Configuration is now entered through Settings |

## Sales

- `/app/sales`
- `/app/sales/prospects`
- `/app/sales/pipeline`
- `/app/sales/campaigns`
- `/app/sales/presentation-presets` (company-scoped reusable presentation library and immutable version workflow)
- `/app/sales/deals/{DealId}`
- `/app/sales/contacts/{ContactId}`

Compatibility:

- `/app/sales/prospecting` renders the canonical Prospects surface.
- `/app/sales/leads` redirects to `/app/sales/prospects?view=leads` while preserving `companyId`.

## Support

- `/support` and `/support/cases`
- `/support/cases/{CaseId}`
- `/support/reports?view=sla|backlog|unresolved|aging` — current scoped operational reports and authorized CSV
- `/support/reports?view=all&contactId={id}` (or `customerCompanyId={id}`) — same-company customer case history
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
- The shell company selector lists active memberships only. Switching company starts Today without the previous lens or reporting month; the authenticated server resolver selects the new company's default.
- Overview and business links carry a validated local `returnUrl` with company, authorized lens, and selected Monthly year/month. Sidebar Overview/brand restores this origin within the current circuit and from the URL after reload; switching company clears it. Inner module links without a return URL preserve it within that circuit but start Today in a fresh circuit.
- Today and Monthly persist the authorized responsibility view with `lens`; an unavailable lens falls back to the server-selected default without exposing the requested area.
- Monthly stays on `/dashboard` with `period=month`. Explicit calendar navigation preserves `companyId`, `lens`, `year`, and `month`; returning to Today removes the monthly period parameters.
- Detail routes preserve their typed record identifier.
- Work selection uses `tab`, `taskId`, or `itemId`.
- Sales Prospects uses `view=leads` for inbound lead state.
- Legacy routes use the same underlying authorized page or a replace-navigation redirect; they do not bypass target authorization.

## Browser meeting routes

- `/sales/rooms/{RoomId}` is the guest surface. It uses a room-scoped credential and the minimal public layout; it never grants company membership. Initial and same-page invitation fragments are consumed and removed before media is enabled.
- `/app/sales/rooms/{RoomId}?companyId={CompanyId}` is the organizer surface linked from Sales lead scheduling and meeting preparation. Server-side company membership and organizer authorization protect every host projection and command. Private host controls are separate from the customer surface.
- Existing Teams and single-user browser voice routes remain unchanged.


### Browser room closing review (Prompt 9)

- Route: `/app/sales/rooms/{RoomId:guid}/review`, company organizer only; entered from the finished host room.
- Page/component: `Pages/Sales/SalesBrowserClosing.razor` / `Components/Sales/SalesRoomClosingReview.razor`.
- Typed API: `SalesBrowserRoomApiClient.CaptureReviewAsync`, plus existing capture, closing and change-proposal clients. Guest endpoints expose no closing/private content.
- States: loading/retry, forbidden, live/partial/expired, explicit excerpt review, customer draft/review/approval, private notes, delivery approval/queued/error, completed.
- Design and UAT evidence: `docs/verification/browser-sales-room/prompt9-uat.md`.

### P04 Sales operational reports and durable journey

- `/app/sales/pipeline/report`: current open opportunities, stage/currency filters, separate currency totals and stage-weighted value. The existing `/app/sales/pipeline` board retains stage commands.
- `/app/sales/activities?status=overdue|pending|completed|all&dealId={optional}`: internal follow-up due/review state and meeting invitation delivery states. Completed means reviewed internal work; it does not prove customer attendance/delivery.
- `/app/sales/forecast?days=30|60|90&currency={optional}&stageId={optional}`: current risk-adjusted expected revenue and included opportunity evidence; not a historical aggregate snapshot drill-down.
- These reports require the existing Sales responsibility/executive authorization and expose only authorized company data. Refresh-and-prepare CSV rechecks access and exports the exact refreshed filter/row set.
- Sales links preserve exact Overview `returnUrl`, validated `priorityReturnUrl`/`healthReturnUrl`, report `salesReturnUrl` and record `recordReturnUrl`. Preset/version selections use `presetId` and `versionId`. Return URLs do not confer access. Public guest room routes remain separate.

P04 verification and provider limits: `docs/verification/vcscreens/P04/implementation.md` and `uat.md`.

## Marketing campaign journey and operational reports (P05)

| Purpose | Canonical route | Context and authority |
| --- | --- | --- |
| Existing workspace | `/marketing` | Durable `section`, `planId`, `briefId`, `assetId`, `actionId`, company and validated Overview/record return; retained portfolios and governance |
| Campaign/content/asset review | `/marketing/review` | `companyId`, campaign or brief, optional immutable variant/asset IDs; exact record/priority/health return. Content approval and launch authority are separate |
| Campaign delivery | `/marketing/reports/delivery` | UTC date window, campaign, currency, delivery state; current state/updated-in-period semantics and authorized refreshed CSV |
| Spend versus budget | `/marketing/reports/spend` | Same company/window/campaign/currency; immutable known cost, separate currencies, explicit missing budget/unknown spend |
| Available lead results | `/marketing/reports/leads` | Whole nonoverlapping observations, source evidence and coverage; incomplete attribution does not imply causal success |

Operational rows drill to the owning campaign/content and restore the exact filtered report. Existing Marketing infrastructure in Infrastructure.Sales owns commands and calculations; existing Work approval/outbox/provider execution boundaries remain authoritative. Report/review/asset/CSV reads enforce the current responsibility resolver.
