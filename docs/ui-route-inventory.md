# UI Route Inventory

This inventory records the route topology after the UI consolidation. Canonical routes are the destinations used by current navigation. Compatibility routes remain authorized by their existing page or redirect to a canonical view with company context preserved.

## Primary And Settings Routes

Sales presentation authoring lives at `/app/sales/presentation-presets`: PowerPoint source, reusable scripts, approved cached narration and presentation settings are edited there. The canonical `/app/sales/meeting-invitations/{InvitationId}/prepare` route is a preset selector and meeting launcher. The former editor remains only at the explicit `/legacy-prepare` compatibility suffix; current navigation does not link to it. Attendee consent is evaluated in the actual meeting, never inherited as preset approval.

| Purpose | Canonical route | Compatibility or contextual routes |
| --- | --- | --- |
| Overview | `/dashboard` | `/` resolves through the existing home flow; Today is the default. `companyId` selects company context, `period=week` selects Week and optional `week=yyyy-MM-dd` selects its company-local containing week; `period=month` selects Monthly. `lens=company|finance|sales|marketing|customers` selects an authorized responsibility; optional `year` plus `month` select a reporting month together. `snapshot={id}` reproduces an authorized saved monthly revision with exact source returns. |
| Weekly source detail | `/dashboard?period=week&metric=...` | Canonical `companyId`, authorized `lens`, `week` start, typed measure `metric`, `prior=true|false` and `sourcePage` select the included current/prior source rows. Exact validated same-company Overview detail context survives native record return. Activity, balance and current due schedule meanings remain distinct. |
| Company health | `/dashboard/company-health` | Company perspective with `companyId`, optional `department=finance|sales|marketing|customers`, and exact Overview `returnUrl`. Dated operational projection, source coverage and recorded plan comparison; unauthorized departments remain gaps. Read-only report; no report export. Risk follow-up and decision commands remain in existing Work services. |
| Agent team work board | `/agents/staff` | `companyId`, authorized `responsibility`, participating `agentId`, `objective`, `state`, and `skip` retain filtered work context. Seven lifecycle projections; shared company outcomes appear once. `/agents`, `/agents/{AgentId}`, `/agents/{AgentId}/chat` retain profile/chat context. |
| Durable agent work detail | `/agents/work/{Kind}/{Id}` | Typed `task`, `initiative`, `case`, or `deal` identity with `companyId`; validated `boardReturnUrl`, `recordReturnUrl` and Overview `returnUrl`. Linked Work/Sales/Support records receive separate `agentWorkReturnUrl`. Permission-filtered evidence and owning lifecycle; reads do not execute actions. |
| Collaboration flow/list/artifact | `/agents/work/{Kind}/{Id}/collaboration` | P11 uses the existing authorized owning work identity, `companyId`, optional `artifactId`, `view=list`, validated `boardReturnUrl`, `recordReturnUrl` and Overview `returnUrl`. Exact contribution versions/typed handoffs; restricted inputs also withhold derived output. Existing Work approval uses `itemId`. Source/business/Work returns preserve selected artifact/view; no execution/decision command. |
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

ublic routes remain separate from the authenticated application information architecture:

- `/company`
- `/contact`

## Context Preservation Rules

- Company-scoped navigation carries `companyId`.
- The shell company selector lists active memberships only. Switching company starts Today without the previous lens or reporting month; the authenticated server resolver selects the new company's default.
- Overview and business links carry a validated local `returnUrl` with company, authorized lens, and selected Monthly year/month. Sidebar Overview/brand restores this origin within the current circuit and from the URL after reload; switching company clears it. Inner module links without a return URL preserve it within that circuit but start Today in a fresh circuit.
- Today and Monthly persist the authorized responsibility view with `lens`; an unavailable lens falls back to the server-selected default without exposing the requested area.
- Monthly stays on `/dashboard` with `period=month`. Explicit calendar navigation preserves `companyId`, `lens`, `year`, and `month`; returning to Today removes the monthly period parameters.
- Week stays on `/dashboard` with `period=week`. Previous/current/completed week selection preserves company and available role; date selection canonicalizes to configured company-local week start. Source drill-down and return preserve `week`, `metric`, `prior` and `sourcePage`. Marketing campaign sources use `/app/sales/campaigns?companyId=...&campaignId=...`, selecting only a campaign present in that company. Today/Monthly transitions remove weekly source parameters.
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
- Sales links preserve exact Overview `returnUrl`, validated `healthReturnUrl`, report `salesReturnUrl` and record `recordReturnUrl`. Preset/version selections use `presetId` and `versionId`. Return URLs do not confer access. Public guest room routes remain separate.

04 verification and provider limits: `docs/verification/vcscreens/P04/implementation.md` and `uat.md`.

## Marketing campaign journey and operational reports (P05)

| Purpose | Canonical route | Context and authority |
| --- | --- | --- |
| Existing workspace | `/marketing` | Durable `section`, `planId`, `briefId`, `assetId`, `actionId`, company and validated Overview/record return; retained portfolios and governance |
| Campaign/content/asset review | `/marketing/review` | `companyId`, campaign or brief, optional immutable variant/asset IDs; exact record/health return. Content approval and launch authority are separate |
| Campaign delivery | `/marketing/reports/delivery` | UTC date window, campaign, currency, delivery state; current state/updated-in-period semantics and authorized refreshed CSV |
| Spend versus budget | `/marketing/reports/spend` | Same company/window/campaign/currency; immutable known cost, separate currencies, explicit missing budget/unknown spend |
| Available lead results | `/marketing/reports/leads` | Whole nonoverlapping observations, source evidence and coverage; incomplete attribution does not imply causal success |

Operational rows drill to the owning campaign/content and restore the exact filtered report. Existing Marketing infrastructure in Infrastructure.Sales owns commands and calculations; existing Work approval/outbox/provider execution boundaries remain authoritative. Report/review/asset/CSV reads enforce the current responsibility resolver.

## P12 consistent decision review


Canonical `/work?companyId={c}&tab=approvals&itemId={approvalId}` and compatibility `/approvals?companyId={c}&approvalId={approvalId}` use shared DecisionReview and the existing company approval GET/decision APIs. Token-bound detail, expiry/reviewer availability, retained history, unavailable/conflict/retry and owning execution status are explicit. No new top-level route or authorization grant. Evidence source tasks use taskId; bounded `decisionReturnUrl` accepts only same-company exact approval routes. P11 agentWorkReturnUrl/recordReturnUrl preserves selected artifact/version/list and board.

## P13 contextual business evidence

P13 extends record supervision through a shared contextual evidence panel in `/app/sales/deals/{id}` (proposal/Finance tabs), `/support/cases/{id}`, `/finance/reviews/{invoiceId}`, `/finance/bill-inbox/{detectedBillId}` and its review alias, `/finance/supplier-bills/{billId}` (operational detail), and `/marketing/review` with campaignId or exact briefId. Native record commands remain in their owning modules. Evidence links use existing work, collaboration artifact/version and Work decision IDs; returning preserves company, selected record, Sales tab and Finance source. New authenticated typed read endpoint: `/api/companies/{company}/agent-work/business/{deal|case|invoice|bill|campaign|brief}/{id}`. Six nullable task associations and their migration retain actual owning identities; intake and operational bill identities are distinct. See `docs/verification/vcscreens/P13/README.md` and the register's P13 contract for scope, limits and local verification. Human/deployed/provider acceptance remains separate.

## P14 configured and effective authority

New read-only page `/settings/agents/authority` takes companyId, optional agentId and optional exact workKind/workId context. `workReturnUrl` accepts only the same-company canonical Work record with its original filtered-board return. Standalone agent selection survives reload in agentId. The agent settings hub exposes View current authority while retaining its existing autonomy configuration destination. Durable `/agents/work/{kind}/{id}` details show the same shared authority panel.

New scoped no-store GET `/api/companies/{company}/authority-explanation` adapts current company, capability/actor/guardrail, operating dispatch and Finance grant policy owners. It has no POST/apply/execute endpoint. Four disabled native company-intent radio positions carry consequences; effective checks, restrictions, review, bounds and expiry are separate. No P15 policy-editing controls are included. See `docs/verification/vcscreens/P14/README.md` for accepted local browser/source checks and independent release gates.

## P16 execution controls and recovery

`/settings/agents/execution` accepts companyId and optional agentId. Scope selection survives reload. The settings hub links to it; exact task returns retain executionReturnUrl. The typed no-store `/api/companies/{company}/execution-controls` GET, POST preview and POST apply resolve active membership/responsibility and current versions. Global mutation requires authorized executive scope; scoped mutation requires an authorized visible agent. Native controls preview the observed impact before apply, disclose cooperation/lag and keep immutable actor/revision history.

Canonical recovery remains task detail, Support case, Finance workbench and payment batch detail, with existing authorization and provider reconciliation. Company pause includes queued payment submissions, which lack agent attribution; polling and reconciliation continue. Scoped controls apply to attributed agent work and persisted delegation. Current policy/approval and owning outcome remain authoritative; resume never grants new authority or replays uncertainty. Reference and honest browser-runtime blocker are in `docs/verification/vcscreens/P16/`.

## P17 supervision reports

`/agents/staff/reports` is reached from Agent team. URL filters are companyId, from/to inclusive company-local dates, responsibility, taskType, optional agentId, view (`work`, `outcomes`, `bottlenecks`, `authority`) and optional metric. Active Owner/Admin/Manager and existing responsibility/Finance policies govern all sources, counts, filter options and CSV. Shared work is suppressed as a whole when a collaborator/source is restricted. Current status is explicitly observed now; event timestamps use half-open UTC boundaries. Missing blocked-duration, comparable budget or payment attribution history stays unavailable.

Typed read and fresh export: `/api/companies/{company}/agent-supervision` and `/export`, with the same filters. One definition owner supplies measures/cohorts, permitted exact rows, source IDs, scope/coverage and CSV snapshot. Export response replaces the displayed report before the existing browser download handoff. Detail navigation opens actual task/initiative, retained P11 artifact ID, Support case, Finance batch or Work approval (`tab=approvals&itemId={approval}`), with allowlisted same-company `supervisionReturnUrl`. Work, collaboration, decision and native record destinations retain that filtered return. No reporting table or fabricated backfill is introduced. References, accepted evidence and blocked browser gate are in `docs/verification/vcscreens/P17/`.

## P15/P18 task-policy integration

`/settings/agents/task-policies` preserves companyId and agentId and exposes the existing five-entry native catalogue, saved mode/limits, authority-bound no-effect preview, apply/revocation/history and current record/goal queue. Typed `/api/companies/{company}/task-policies` GET, catalogue, records, preview, apply and queue use current manager/company/agent authority. Queue navigates only to its actual retained `/agents/work/task/{id}`; Finance stays in its owning template/grant/trigger/run path. No generic policy enables customer delivery, publication, payment, posting or final close.

Reviewed native catalogue work queues in the existing outbox after approval commits. The same Work decision (`tab=approvals&itemId=...`) says Queued for internal execution; exact task/detail links retain that decision. Current policy, material, actor, tool and pause/admission are checked before native execution. Ambiguous admission requires owning reconciliation. `docs/verification/vcscreens/release-2/` is the evidence/reviewer package, not a product route or release approval. Final browser acceptance remains blocked in the P18 packet.

P21 Sales management: /app/sales/reports/management with companyId, year, month and optional currency; snapshot and lens reproduce an authorized P20 retained Sales report. /app/sales/reports/capacity carries the same period/currency and optional proposal revision identity. Native Sales journey navigation retains exact salesReturnUrl and original Overview context through opportunity/detail and planning revision reload. These are module report/detail routes and add no top-level navigation group.

P22 Marketing management: `/marketing/reports/management` has companyId, year, month, optional currency, explicit modelId/version, campaignId and segmentVersionId. The fixed management/budget routes take precedence over the existing `/marketing/reports/{Kind}` operational route. Entry is Marketing workspace and authorized monthly review. Campaign and exact segment-version drill-down carry bounded same-company recordReturnUrl and original Overview context. `snapshot`/`lens` opens the original P20 report; `proposalEvidence` opens a private retained proposal report, with incompatible filters rejected. `/marketing/reports/budget` carries month/currency/report filters and optional `proposal` identity, exposes original assumptions/results and fresh append-only revision save, and links back to retained evidence. No additional top-level group is added. Typed `/api/marketing/management`, `/export`, `/proposals` GET/POST and `/proposals/{id}` require company membership/context plus current Marketing responsibility. Proposal history/open are reviewer-private. CSV is fresh server-authorized report evidence; retained report snapshots do not offer current export. See docs/verification/vcscreens/P22 for native, SQL Server, rendered, typed and disposable headless Edge evidence and independent provider/deployed/physical/human gates.

P23 Finance planning: `/finance/reports/variance`, `/finance/reports/rolling-forecast` and `/finance/reports/forecast-comparison` extend native Finance workspace/journey navigation. Report/editor context carries companyId, year, month, months, explicit budgetVersion/forecastVersion, currency, financeAccountId, costCenterId and fiscalPeriodId; configured fiscal boundaries must exactly match native UTC monthly planning. Editor `version` reopens original immutable assumptions/results; comparison `earlier`/`later` select retained versions. Same-company `financeReturnUrl` and original Overview context survive native journal/source/revision links. Variance `snapshot`/`lens` reproduces P20 retained Finance evidence and rejects incompatible filters; it offers no live CSV. `/internal/companies/{companyId}/finance/planning` exposes `/analysis`, `/export`, `/explanations` POST, `/preview` POST, `/versions` GET/POST, `/versions/{id}`, `/compare` and `/compare/export`. All require current Finance responsibility and tenant context, including retained report/history/export access. Forecast versions are shared within authorized company Finance, with actor audit. Explanations and future forecast saves append records; native actuals, journal posting, period closing and payments retain existing owners. See docs/verification/vcscreens/P23 for source, authorization, SQL and disposable-browser evidence.

### Support quality and capacity (P24)

| Surface | Route | Context and behavior |
| --- | --- | --- |
| Quality cohorts | `/support/reports/quality` | Company/year/month/category, native case drill-down and exact Support return; optional snapshot/lens or proposalEvidence opens original authorized retained inputs. |
| Recurring issue evidence | `/support/reports/issues` | Same cohort plus optional group/caseId; native suggested groups and audited reviewed corrections with source/hash/previous revision validation. |
| Capacity proposal | `/support/reports/capacity` | Source cohort, optional proposal identity; explicit native-calendar assumptions, deterministic preview, private immutable named revisions, original source evidence/export and current Support authorization. |

P25: company-scoped Quarter overview (/dashboard?period=quarter&year=&quarter=&review=) and editor (/dashboard/planning/quarter). Native API /api/companies/{companyId}/planning/quarters: options/history GET; preview/save/open POST. Open reproduces protected snapshot evidence; quarter planning grants no execution authority.
P26: /dashboard?period=year and /dashboard/planning/year use the native company shell. Annual API api/companies/{companyId}/planning/years: GET options/versions; POST preview/save/open/review. Review binds the canonical approval, with no execution authority. Evidence: docs/verification/vcscreens/P26.

P27: /dashboard?period=multiyear and /dashboard/planning/scenarios share the native company planning workspace. API api/companies/{companyId}/planning/scenarios: GET options and paged versions; POST preview/save/open/duplicate/compare. Exact scenario and comparison IDs survive reload. Native annual and Finance source links retain their versions; scenario funding grants no execution authority. Evidence: docs/verification/vcscreens/P27.

P28: `/work/create-from-decision?companyId=&kind=&versionId=&itemKey=` enters from a saved monthly finding, quarter objective, annual decision/objective or scenario checkpoint. Kind is `month`, `quarter`, `annual` or `scenario`; the exact retained version/item survives reload. `/work/source?companyId=&taskId=` opens the frozen owned-work snapshot and original source; native Work task/approval routes retain their owners. Company-manager membership and original source permissions apply to every read and command. API `/api/companies/{companyId}/decision-work`: GET `context` with kind/versionId/itemKey, POST `preview` and confirmation at the root, POST `tasks/{taskId}/open` and `tasks/{taskId}/review`. Confirmation uses the existing task command and durable typed provenance/idempotency; canonical owner review is required before starting/completing human follow-up. Proposed context grants no agent, sending, publication or payment authority. No new top-level navigation group or export is added. Evidence: `docs/verification/vcscreens/P28`.

## P29 — Timely role briefings

The existing `/briefing-preferences?companyId=` route now offers Schedule, Preview and Absence routing. The connected native owner supports morning, end-of-day, shift, weekly, monthly and configured quarterly/annual reminders, working/quiet periods, timezone/DST, editable authorized role focus, grouped/unchanged updates and separate urgent rules. `/briefings?companyId=&deliveryId=` opens a delivered briefing through current recipient work/source permissions. Notification links expose no restricted snapshot body. Existing company scheduler, update jobs, outbox and inbox remain the execution owners; unconfigured preferences retain legacy daily/weekly behavior.

Absence routing selects independently eligible delegates and an accountable fallback, with generation/dispatch/open permission checks and a safe owner escalation when no permitted recipient remains. It confers no record, approval or agent execution authority. Evidence and channel/production/human acceptance limits are in `docs/verification/vcscreens/P29/README.md`.
