Assessment based on source inspection, not runtime verification. I did not run the full app or tests for this pass.

**Overall Shape**
Virtual Company is a multi-tenant SaaS-style business operations app with:
- ASP.NET Core API backend
- Blazor web frontend
- .NET MAUI mobile companion app
- SQL-backed domain model
- background workers
- integrations with Fortnox, Gmail/Microsoft mailbox providers, and optional OpenAI services

Core references:
[README.md](</C:/Users/Johan/source/repos/Virtual Company/README.md:1>), [NavMenu.razor](</C:/Users/Johan/source/repos/Virtual Company/src/VirtualCompany.Web/Layout/NavMenu.razor:1>), [VirtualCompanyDbContext.cs](</C:/Users/Johan/source/repos/Virtual Company/src/VirtualCompany.Infrastructure/Persistence/VirtualCompanyDbContext.cs:1>), [DependencyInjection.cs](</C:/Users/Johan/source/repos/Virtual Company/src/VirtualCompany.Infrastructure/DependencyInjection.cs:1>).

**Main Functional Areas**
| Area | Existing functionality |
|---|---|
| Company / tenancy | Companies, memberships, invitations, selected company context, onboarding, company dashboard, notes, role-aware access. |
| Agents | Agent roster, templates, profiles, scheduling, direct chat, task links, tool permissions, approval thresholds, escalation rules, working hours, autonomy settings. |
| Tasks | Work tasks, subtasks, status updates, execution, manager-worker collaboration flows. |
| Workflows | Workflow definitions, versions, triggers, started instances, instance state changes, exceptions, review handling, scheduled/condition-triggered execution. |
| Approvals | General approval requests/tasks, finance approvals, notification-linked approval decisions, approval auditability. |
| Finance | Invoices, supplier bills, bills inbox, payments, counterparties, transactions, balances, bank accounts, budgets, forecasts, reporting periods, ledger entries, trial balances, financial statement snapshots, anomalies, finance insights, cash position, monthly summary, P&L/balance-sheet style reporting. |
| Fortnox | OAuth connection, token storage, sync history, external references, sync state, integration status, outbound write-command tracking, invoice/supplier invoice related actions, approval-oriented write flow. |
| Mailbox | Gmail/Microsoft 365 connection support, OAuth callback handling, mailbox scanning, message/attachment snapshots, email ingestion runs. |
| Bill ingestion | Email/document ingestion, bill detection, field extraction, duplicate checks, review state/actions, supplier bill draft/payment/correction/enrichment/source-document flows. |
| Sales | Leads, contacts, customer companies, pipeline stages, deals, sales activities, campaign execution, outbound review queue, sales email processing, follow-up recommendations, risk scoring, revenue forecast snapshots, finance handoff. |
| Knowledge / documents | Company document upload, local storage, text extraction, chunking, embedding/indexing pipeline, semantic search, grounded context retrieval. |
| Memory | Company memory items and customer memory profiles, including conversation/deal/engagement/preference/price/industry signals. |
| Briefings | Company briefings, sections, contributions, preferences, severity rules, scheduled update jobs, mobile/latest briefing endpoints. |
| Notifications / activity / audit | Notification inbox, unread/status handling, activity feed, SignalR activity hub, audit list/detail, action insights, proactive messages. |
| Executive dashboard | Dashboard focus, finance snapshot, briefing summary, executive cockpit widgets, KPIs, finance alert detail. |
| Mobile companion | Mobile summary, mobile briefing, mobile notification inbox, mobile chat-related endpoints. Companion scope, not full admin parity. |
| Admin / transparency | Tool registry, tool executions, transparency events, queue/system/admin views. |

**Finance Depth**
The finance module is one of the most developed areas. It has both transactional entities and analytical/reporting entities:
- operational records: invoices, bills, payments, bank transactions, counterparties
- accounting records: ledger entries, ledger lines, account mappings, fiscal periods
- analysis records: balances, budgets, forecasts, anomalies, insights, statement snapshots
- integration records: Fortnox connection, external references, write commands, sync states

The app appears designed to support real finance analysis, not just invoice lists. The presence of ledger lines, trial balance snapshots, financial statement mappings, reporting period locks, and drilldown APIs means the system has the building blocks for P&L, balance sheet, account movement, and variance analysis.

Finance references:
[FinanceContracts.cs](</C:/Users/Johan/source/repos/Virtual Company/src/VirtualCompany.Application/Finance/FinanceContracts.cs:1>).

**AI Usage**
AI is present, but not everywhere.

Clear AI-backed areas:
- Sales email intent extraction via OpenAI-compatible chat completions: [OpenAiSalesEmailIntentExtractionService.cs](</C:/Users/Johan/source/repos/Virtual Company/src/VirtualCompany.Infrastructure/Sales/OpenAiSalesEmailIntentExtractionService.cs:1>)
- PDF/image OCR for finance documents via OpenAI Responses API when configured: [DocumentTextExtractors.cs](</C:/Users/Johan/source/repos/Virtual Company/src/VirtualCompany.Infrastructure/Finance/DocumentTextExtractors.cs:1>)
- embeddings and semantic retrieval for company knowledge/documents
- grounded context retrieval for agent-style responses

Many “agent” and finance insight features appear to be deterministic workflow/tool/rule systems rather than pure LLM analysis. The agent orchestration layer builds context, checks policy, executes tools, writes audit, and coordinates responses, but the inspected path does not prove every agent response is LLM-generated.

Agent reference:
[AgentContracts.cs](</C:/Users/Johan/source/repos/Virtual Company/src/VirtualCompany.Application/Agents/AgentContracts.cs:1>), [SingleAgentOrchestrationService.cs](</C:/Users/Johan/source/repos/Virtual Company/src/VirtualCompany.Infrastructure/Companies/SingleAgentOrchestrationService.cs:1>).

**Integration Maturity**
Fortnox and mailbox integrations are substantial, not placeholders. They include connection management, OAuth state, token storage, sync tracking, external references, provider clients, and write-command auditing.

That said, these integrations depend on external configuration, API scopes/licenses, and provider availability. The code supports the flows, but runtime completeness depends on credentials, tenant setup, Fortnox permissions, mailbox OAuth setup, and sync scheduling.

Mailbox reference:
[MailboxConnectionContracts.cs](</C:/Users/Johan/source/repos/Virtual Company/src/VirtualCompany.Application/Mailbox/MailboxConnectionContracts.cs:1>).

**Important Gaps / Caveats**
- The README appears partly stale: it mentions PostgreSQL/pgvector, while current infrastructure defaults to SQL Server and can switch provider by configuration.
- `docs/architecture-overview.md` was referenced by workspace instructions but is missing.
- Some features are clearly backend-capable but may not have complete frontend UX.
- Some AI features degrade or skip when OpenAI is not configured.
- Fortnox write automation likely requires correct API scopes, company license/modules, and approval policy configuration.
- I did not verify migrations, seed data, or runtime behavior in this assessment.

**Bottom Line**
Virtual Company is already a broad business-operations platform. The strongest implemented areas are finance operations/analysis, Fortnox/mailbox integration, agent/task/workflow orchestration, sales pipeline automation, audit/activity/notifications, and knowledge-grounded context.

The app is not just a UI prototype. It has a large persistent domain model, many API surfaces, background workers, provider integrations, and partial AI-assisted workflows. The biggest remaining question is runtime completeness: which of these flows are production-ready end-to-end versus implemented as backend capability with partial UI or configuration dependency.
