# Laura Finance Manager Assessment

This document assesses what is currently implemented for Laura, the Finance Manager agent, and what remains to make her an independent SME financial manager for the company. The assessment is based on repository inspection of the finance, agent orchestration, workflow, approval, integration, dashboard, sales, support, and direct-chat implementation.

## Executive Summary

Laura is implemented as a real seeded Finance Manager agent, not just a UI label. The repository has a strong finance foundation: tenant-scoped finance data, ledger and reporting entities, invoice and bill workflows, approvals, Fortnox integration, dashboard finance snapshots, finance insights, direct chat, activity/audit records, and some cross-agent handoff paths.

However, Laura is not yet an independent SME finance manager. Today she behaves mostly as a bounded finance workflow/operator persona over deterministic tools, rules, approvals, dashboards, and background jobs. She can inspect and act inside finance boundaries, but she does not yet run a proactive finance operating cadence, reason like a controller/CFO across the business, or independently coordinate finance work with sales, support, marketing, and the CEO.

Estimated maturity:

- Platform foundation for Laura: 60-70%.
- Independent SME finance-manager behavior: 25-35%.

## What Is Implemented

### Laura Agent Identity and Governance

Laura is auto-seeded for each company as:

- Name: Laura.
- Role: Finance Manager.
- Department: Finance.
- Autonomy level: Guided.
- Seniority: Senior.
- Persona: conservative, precise, evidence-first finance operator.
- Scope: finance.
- Default capabilities: cash balance review, transaction categorization review, invoice approval review, profit and loss summary, finance risk detection.

The seed data includes objectives, KPIs, tool permissions, approval thresholds, escalation rules, working-hours metadata, and communication profile.

Key file:

- `src/VirtualCompany.Infrastructure/Companies/LauraFinanceAgentSeedData.cs`

### Finance Domain Foundation

The finance domain is one of the strongest areas of the repository. It includes persistence and application services for:

- Finance accounts.
- Bank transactions.
- Finance transactions.
- Invoices.
- Supplier bills.
- Payments.
- Payment allocations.
- Counterparties.
- Assets.
- Balances.
- Ledger entries and ledger lines.
- Fiscal/reporting periods.
- Financial statement mappings.
- Financial statement snapshots.
- Finance policy configuration.
- Finance agent insights.
- Supplier bill review state.
- Supplier invoice payment proposals.
- Fortnox connections, sync state, external references, audit events, and write-command records.

This means Laura has a meaningful financial substrate to work from. The system is not limited to invoice lists; it has the building blocks for cash analysis, payables, receivables, reporting, reconciliations, financial statements, and operational finance workflows.

Key files:

- `src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContext.cs`
- `src/VirtualCompany.Domain/Entities/FinanceEntities.cs`
- `src/VirtualCompany.Domain/Entities/LedgerReportingEntities.cs`
- `src/VirtualCompany.Domain/Entities/FinanceAgentInsight.cs`

### Finance Tools Available to Laura

The static company tool registry exposes finance tools that Laura can use through the agent tool boundary:

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

Tool execution includes:

- Tenant membership validation.
- Agent runtime profile resolution.
- Policy guardrail evaluation.
- Tool permission/data-scope checks.
- Approval request creation when required.
- Denial handling.
- Structured tool execution attempts.
- Audit events.

Key files:

- `src/VirtualCompany.Infrastructure/Companies/StaticCompanyToolRegistry.cs`
- `src/VirtualCompany.Infrastructure/Companies/CompanyAgentToolExecutionService.cs`
- `src/VirtualCompany.Infrastructure/Finance/FinanceToolProviders.cs`

### Deterministic Finance-Agent Queries

Laura has a deterministic query resolver for three business questions:

- "what should i pay this week"
- "which customers are overdue"
- "why is cash down this month"

These queries are tenant-scoped and live-computed from finance source tables. They include provenance through source record ids and metric components.

Implemented behavior includes:

- Current-week payable selection.
- Overdue customer receivables.
- Month-to-date cash movement comparison against prior month.
- Timezone-aware company week/date logic.
- Source record traceability.

Key files:

- `src/VirtualCompany.Infrastructure/Finance/CompanyFinanceReadService.cs`
- `docs/finance-dashboard-cash-metrics.md`

### Invoice Review and Approval Workflow

Laura is connected to invoice review workflows. The workflow can:

- Load invoice and policy context.
- Evaluate invoice risk.
- Classify invoices.
- Determine whether human approval is required.
- Create Laura-assigned review tasks.
- Create approval requests for finance approvers.
- Persist workflow output.
- Attach rationale and confidence.

The review logic currently appears deterministic/rule-based. It considers invoice amount, currency mismatch, approval threshold, due date, overdue state, due-soon state, and related payment activity.

Key file:

- `src/VirtualCompany.Infrastructure/Finance/CompanyInvoiceReviewWorkflowService.cs`

### Supplier Bill and Mailbox Workflows

Laura is integrated into supplier bill handling:

- Connected mailbox scans can be started automatically after mailbox connection.
- Scan tasks are attributed to Laura.
- Supplier invoice candidates can be detected.
- Bill review states and actions exist.
- Supplier invoice enrichment can be suggested.
- Approval is required before supported Fortnox sync changes.

Key files:

- `src/VirtualCompany.Infrastructure/Mailbox/ConnectedMailboxInboxScanOrchestration.cs`
- `src/VirtualCompany.Infrastructure/Finance/CompanyFinanceBillInboxService.cs`
- `src/VirtualCompany.Infrastructure/Finance/SupplierInvoiceEnrichmentService.cs`

### Finance Insights and Dashboard Integration

Finance insights exist as persisted records and dashboard inputs. The finance dashboard and executive cockpit can surface:

- Cash position.
- Expected incoming/outgoing cash.
- Overdue receivables.
- Upcoming payables.
- Finance risk level.
- Grouped finance insights.
- Top finance actions.
- Drilldown-oriented finance alerts.

There are background services for finance insight refresh and startup refresh.

Key files:

- `src/VirtualCompany.Infrastructure/Finance/CompanyDashboardFinanceSnapshotService.cs`
- `src/VirtualCompany.Infrastructure/Finance/CompanyFinanceReadService.Insights.cs`
- `src/VirtualCompany.Infrastructure/Finance/FinanceAnalyticsStartupRefreshBackgroundService.cs`
- `src/VirtualCompany.Infrastructure/Finance/FinanceInsightsSnapshotBackgroundService.cs`

### Fortnox Integration

Fortnox integration is substantial, not a placeholder. The repo includes:

- OAuth connection flow.
- Token storage.
- Token refresh/reconnect handling.
- Sync history.
- External references.
- Provider status.
- Startup sync.
- Outbound write-command tracking.
- Approval-oriented write flows.
- Supplier/customer invoice-related actions.
- Operational runbook.

Key files/docs:

- `src/VirtualCompany.Infrastructure/Finance/FortnoxSyncService.cs`
- `src/VirtualCompany.Infrastructure/Finance/FortnoxFinanceIntegrationOAuthService.cs`
- `src/VirtualCompany.Infrastructure/Finance/FinanceIntegrationWriteApprovalService.cs`
- `docs/integrations/fortnox.md`
- `docs/runbooks/fortnox-integration.md`

### Sales-to-Finance Handoff

Sales has a finance handoff path where Laura is referenced in the operating flow. For won deals, the sales side can request approval before a Fortnox draft is created. Audit records describe Laura preparing or retrying the approved Fortnox draft request.

This is one of the stronger cross-agent/cross-department connections.

Key file:

- `src/VirtualCompany.Infrastructure/Sales/SalesOperationsService.cs`

### Support-to-Finance Foundation

Support has a domain model and finance-adjacent entities, including refund requests with invoice/payment references, approval request ids, and finance action references.

This provides a foundation for support-to-finance workflows, but the full Laura-owned refund/credit execution loop is not yet clearly complete.

Key files:

- `src/VirtualCompany.Domain/Entities/SupportEntities.cs`
- `src/VirtualCompany.Infrastructure/Support/SupportServices.cs`

### Multi-Agent Collaboration

Multi-agent collaboration exists through an explicit manager-worker coordinator. It supports:

- Explicit collaboration plans.
- Coordinator and worker agents.
- Bounded fan-out.
- Max worker/depth/runtime/step limits.
- Worker subtask creation.
- Single-agent execution for each worker.
- Consolidated response and audit events.

This is useful infrastructure, but it is not yet autonomous cross-agent collaboration. The plan is explicit and bounded; Laura does not independently decide to coordinate with sales/support/marketing unless something invokes that plan or workflow.

Key files:

- `src/VirtualCompany.Infrastructure/Companies/MultiAgentCoordinator.cs`
- `src/VirtualCompany.Application/Orchestration/MultiAgentCollaborationContracts.cs`

### Direct Chat

Direct chat exists and routes messages through the shared single-agent orchestration engine. Conversations and messages are persisted, sanitized, and can be linked to tasks.

The current direct-chat response, however, is mostly deterministic. For chat intent, the shared orchestration service builds a generic response that introduces the agent, summarizes role/context, repeats the user message, and suggests clarifying outcome / next concrete action / task tracking.

This means "Message Laura" exists as a workflow entry point, but not yet as a rich finance-aware SME conversation experience.

Key files:

- `src/VirtualCompany.Infrastructure/Companies/CompanyDirectChatService.cs`
- `src/VirtualCompany.Infrastructure/Companies/SingleAgentOrchestrationService.cs`
- `src/VirtualCompany.Web/Pages/AgentChat.razor`

## Main Limitations

### 1. Laura Is Guided, Not Independent

Laura is seeded with `AgentAutonomyLevel.Guided`. Her execute actions are constrained by approval thresholds and policy guardrails. This is good for safety, but it means she is not yet independently running finance.

Current behavior is closer to:

- "Laura can inspect, recommend, create tasks, and request approval."

Target behavior should be:

- "Laura runs the finance operating system within configured autonomy boundaries, escalates exceptions, and asks the CEO only for decisions that require human judgment or exceed policy."

### 2. No Full Autonomous Finance Operating Cadence

The platform has scheduled triggers and background services, but Laura is not yet seeded with an end-to-end finance cadence such as:

- Daily cash review.
- Daily payables review.
- Daily overdue receivables review.
- Weekly payment plan.
- Weekly collections plan.
- Weekly cash forecast refresh.
- Monthly close readiness.
- Monthly P&L and balance-sheet review.
- Budget variance review.
- Tax/VAT reminders.
- Exception digest to CEO.

### 3. No Rich SME Reasoning Loop

The repository has a prompt builder and orchestration envelope, but the inspected Laura path does not show a general model-backed finance reasoning loop for chat/task execution. OpenAI is used in specific places such as sales email intent extraction and finance document OCR, but Laura's core orchestration output is deterministic.

The result is that Laura can use deterministic finance tools and workflows, but she does not yet behave like a controller/CFO-level SME who can synthesize ambiguous financial context, ask targeted clarifying questions, compare alternatives, and produce nuanced recommendations.

### 4. Cross-Agent Collaboration Is Infrastructure, Not Operating Behavior

Sales-to-finance handoff exists, and support refund foundations exist, but the business operating contracts are incomplete.

Laura still needs formal collaboration contracts with:

- Sales: won deals, invoice readiness, contract/payment terms, revenue forecast, collection risk, customer payment friction.
- Support: refunds, credits, disputes, compensation, customer churn risk, invoice/payment context for cases.
- Marketing: spend plans, CAC, campaign ROI, budget pacing, finance approval for campaign spend.
- CEO: decision briefing, approval escalation, cash runway warnings, exception summaries.

### 5. AP/AR Ownership Is Partial

The system has invoices, bills, payments, allocations, and query tools. What is missing is complete ownership behavior:

- Supplier payment run planning.
- "Pay now / defer / negotiate" recommendations.
- Payment proposal approval and execution flow.
- Customer collection reminders.
- Overdue escalation sequencing.
- Promise-to-pay tracking.
- Dispute handling.
- CEO escalation for high-risk receivables/payables.

### 6. Month-End and Accounting Controls Need a Laura-Owned Workflow

The repo has ledger entries, ledger lines, reporting periods, statement mappings, snapshots, and reporting locks. Laura still needs:

- Month-end close checklist.
- Reconciliation status.
- Missing receipt/document detection.
- Accrual/prepayment suggestions.
- Period close validation.
- Variance sign-off.
- Close-readiness score.
- CEO-facing close summary.

### 7. CEO Supervision UX Needs a Laura Control Surface

There are finance dashboards, executive cockpit finance signals, and approvals, but Laura needs a dedicated Finance Manager cockpit that answers:

- What is Laura watching today?
- What does Laura recommend paying?
- Who should be chased for payment?
- What approvals does the CEO need to decide?
- What changed since yesterday?
- What risks are emerging?
- Which other agents are waiting on finance?
- What has Laura already done automatically?

### 8. Production Integration Dependencies Remain

Fortnox and mailbox integrations are substantial, but real independence depends on tenant setup and external availability:

- Fortnox scopes and license/modules.
- Fortnox write permissions.
- Token refresh/reconnect behavior.
- Mailbox OAuth setup.
- Bank feed/open banking coverage.
- Payment execution/export flow.
- Idempotency and retry behavior in production.
- Clear user-safe fallback when integrations fail.

## What Is Left To Make Laura an Independent SME Finance Manager

### Phase 1: Define Laura's Finance Operating Model

Create a first-class operating model for Laura:

- Responsibilities.
- Authority boundaries.
- Daily/weekly/monthly cadence.
- Escalation thresholds.
- CEO approval rules.
- Collaboration contracts with each department.
- Evidence and audit requirements.
- Autonomy levels and allowed actions per level.

This should live in application/domain concepts, not only UI copy.

### Phase 2: Add Laura's Scheduled Finance Cadence

Seed and execute scheduled triggers for Laura:

- Daily cash position review.
- Daily overdue receivables review.
- Daily payables pressure review.
- Weekly payment plan.
- Weekly collections plan.
- Weekly forecast/risk review.
- Monthly close readiness review.

Each scheduled run should create tasks, insights, approvals, notifications, or CEO briefing sections as appropriate.

### Phase 3: Build a Finance SME Reasoning Service

Add a model-backed, auditable reasoning layer for Laura that can:

- Use structured finance context.
- Call deterministic tools.
- Cite source records.
- Produce recommendations with confidence.
- Ask clarifying questions when evidence is insufficient.
- Avoid unsupported claims.
- Create structured next actions.

The deterministic services should remain the source of calculations. The reasoning layer should synthesize and explain, not invent numbers.

### Phase 4: Expand Laura's Tool Surface

Laura needs more tools to manage finance end to end:

- Create payment proposal.
- Update payment proposal.
- Submit payment proposal for approval.
- Create collection task.
- Draft customer payment reminder.
- Send reminder when policy permits.
- Request support/sales input on disputed receivable.
- Create refund/credit approval request.
- Create Fortnox draft invoice/supplier invoice action.
- Sync approved Fortnox write command.
- Start month-end close checklist.
- Mark close step complete.
- Request missing document.
- Generate CEO finance briefing.

Each execute tool should remain policy-gated, tenant-scoped, audited, and idempotent.

### Phase 5: Complete AP/AR Workflows

Implement Laura-owned workflows for:

- Payables prioritization.
- Supplier payment run review.
- Payment approval and execution/export.
- Receivables collections.
- Customer reminder sequencing.
- Dispute routing to support/sales.
- Bad-debt/escalation recommendations.

### Phase 6: Complete Cross-Agent Handoffs

Implement typed handoff workflows:

- Sales to Laura: won deal to invoice/draft, payment terms validation, customer credit risk.
- Laura to Sales: overdue customer risk, payment blocker on strategic account, invoice/payment-term mismatch.
- Support to Laura: refund/credit/dispute approval.
- Laura to Support: account hold, invoice dispute evidence, refund outcome.
- Marketing to Laura: campaign budget request, spend pacing, ROI/CAC review.
- Laura to Marketing: spend freeze/warning, budget approval, campaign profitability feedback.
- Laura to CEO: decision requests, risk summaries, exception digests.

### Phase 7: Build Laura's Finance Manager Cockpit

Create a dedicated operational view for Laura:

- Today's finance priorities.
- Cash position and runway.
- Recommended payments.
- Overdue customers.
- Pending finance approvals.
- Open anomalies.
- Fortnox/mailbox sync health.
- Cross-agent handoffs.
- Laura activity and audit trail.
- Message Laura with finance-aware responses.

### Phase 8: Strengthen Production Readiness

Before Laura can be trusted as independent:

- Validate Docker SQL Server restore path after schema changes.
- Verify Fortnox production setup and scopes.
- Add integration health checks.
- Add retry/idempotency tests for external writes.
- Add audit completeness tests for Laura actions.
- Add tenant-isolation tests for every new workflow/tool.
- Add approval bypass prevention tests.
- Add failure-mode UX for disconnected integrations.

## Recommended Near-Term Implementation Order

1. Add Laura operating model contracts and seed defaults.
2. Seed scheduled daily/weekly/monthly Laura finance triggers.
3. Implement Laura daily finance review job that creates insights/tasks/briefing output.
4. Expand direct chat to route finance questions to `resolve_finance_agent_query` and finance read tools.
5. Add payment-plan and collections-plan workflows.
6. Add typed support-refund and sales-invoice handoff workflows.
7. Build Laura Finance Manager cockpit.
8. Add model-backed SME synthesis behind a strict tool/evidence boundary.

## Bottom Line

The repository already contains a strong finance platform and a real Laura agent identity. Laura can participate in finance workflows, approvals, mailbox bill scanning, deterministic finance analysis, Fortnox-related flows, and some sales handoff activity.

The remaining work is to turn Laura from a guided finance workflow persona into a proactive finance manager: scheduled operating cadence, richer reasoning, broader finance tools, AP/AR ownership, month-end controls, typed cross-agent collaboration, CEO supervision UX, and production-grade integration reliability.
