# Virtual Company UI Consolidation Implementation Prompts

## Shared Execution Contract

Every prompt in this document must be implemented as production work, not as analysis or scaffolding.

Before implementation:

- Read and follow `AGENTS.md`.
- Read and follow `production-implementation.md`.
- Read and follow `ui-instructions.md`.
- Read and follow `docs/design.md`.
- Read and follow `docs/ui-updated.md`.
- For architecture-sensitive route, authorization, API, or application-contract changes, read and follow `docs/architecture-rules.md`. The repository currently has no `architecture-inst.md`; do not invent rules in its place.
- Inspect the current implementation and current dirty worktree before editing. Existing implementation wins over stale planning documents.
- Preserve unrelated user changes.

For every significant page or major component redesign:

1. Write the image-generation prompt explicitly.
2. Generate the required reference screenshot with the approved OpenAI image model.
3. Save it under `docs/design/references/` using the filename specified by the prompt.
4. Implement against the reference.
5. Compare the running page with the reference using Playwright screenshots.
6. Verify desktop, tablet, and mobile layouts, including text fit and non-overlap.

General preservation rules:

- Preserve company scoping, authorization, approval policies, audit behavior, task state, workflow behavior, and external side-effect boundaries.
- Do not add mock production data.
- Do not expose raw enums, internal identifiers, localization keys, or technical failure messages.
- Preserve old routes as redirects or compatibility routes until all internal callers are migrated.
- Use the existing Blazor stack, API clients, presenters, localization system, and design tokens.
- Do not introduce a new UI framework.
- Add backend or database changes only where the requested UI behavior cannot be delivered correctly through existing contracts. Database changes require an EF migration and equivalent local SQL Server and Docker compatibility.

---

## Prompt 1: Establish The Production App Shell And Navigation

### 1. Title And Outcome

Implement the consolidated Virtual Company app shell with six primary destinations, responsive navigation, working icons, and role-aware secondary settings. This gives users a predictable product structure before individual screens are consolidated.

### 2. Current Context

- `src/VirtualCompany.Web/Layout/NavMenu.razor` currently exposes Home, Onboarding, Dashboard, Finance, Simulation Lab, System Administration, Sales, Support, Agent Team, Agents, Workflows, Inbox, Activity, Approvals, Message Review, and Briefing Delivery as peer entries.
- `NavMenu.razor.css` defines only a small subset of the icon classes referenced by the menu.
- `MainLayout.razor.css` uses a fixed desktop sidebar but the narrow-screen expanded menu pushes the workspace away.
- Existing navigation context code already resolves company context, finance access, administrative visibility, localization, and the contextual agent card.
- Target information architecture is defined in `docs/ui-updated.md`.

### 3. Dependencies

None.

### 4. Implementation Requirements

- First generate `docs/design/references/app-shell-navigation-reference.png`.
- Implement primary navigation: Overview, Agent team, Finance, Sales, Support, Work.
- Implement bottom navigation: History and Settings.
- Keep Simulation Lab and System Administration behind their existing environment and authorization rules, visually separated from normal navigation.
- Preserve company ID propagation and active-link behavior.
- Add a real icon source already compatible with the repository or use the existing enabled icon strategy consistently; do not leave empty icon placeholders or draw ad hoc SVGs.
- Implement desktop, tablet, and mobile navigation states.
- Mobile must use an overlay drawer with backdrop, explicit close control, focus management, Escape handling, body scroll lock, and focus return.
- Add badge support for Work when an existing authorized count is available. Do not invent a count.
- Update English and Swedish navigation resources.
- Add route constants or a typed navigation map if this removes real duplication and matches repository conventions.
- Keep old destination routes operational even when removed from the sidebar.
- Add telemetry only through established interaction telemetry abstractions.

### 5. Constraints And Preservation Rules

- Do not change backend authorization merely to show a link.
- Do not expose settings or restricted tools to unauthorized users.
- Do not remove the contextual agent card without replacing its useful context.
- Do not make mobile navigation part of normal document flow when open.
- Keep the main content stable when labels, counts, or localization change.

### 6. Acceptance Criteria

- Given an onboarded normal company user, when the app loads, then exactly six primary destinations are shown.
- Given a system administrator, when restricted navigation is available, then it is visually separated from daily company navigation.
- Given a viewport below the mobile breakpoint, when the menu opens, then it overlays rather than pushes the workspace and can be closed by button, backdrop, or Escape.
- Given Swedish UI language, when navigation renders, then no English labels or resource keys appear.
- Given a legacy route bookmark, when opened, then the route still resolves with existing authorization.

### 7. Verification

- Component or bUnit tests for role-aware visibility and active destinations.
- Navigation tests for company ID preservation.
- Playwright checks at 1440x1000, 1024x768, 768x1024, and 390x844.
- Keyboard and focus checks for the mobile drawer.
- Verify no text overlap, missing icons, horizontal page overflow, or inaccessible hidden content.
- Build `VirtualCompany.Web` with `--no-restore`.

### 8. Definition Of Done

The production shell is implemented, localized, tested, and visually verified against the saved reference. No placeholder icons, temporary duplicate menus, hardcoded company IDs, silent navigation failures, or deferred in-scope TODOs remain.

---

## Prompt 2: Refine The Executive Overview

### 1. Title And Outcome

Redesign `/dashboard` as the single executive company overview, clearly distinct from the Agent Team Kanban. It should summarize company health and direct the user to the few actions that matter now.

### 2. Current Context

- `Pages/Dashboard.razor` currently contains a finance strip, latest briefing, summary, top actions, and a finance reset action.
- `Pages/AgentStaffOverview.razor` also presents company KPIs above operational work.
- Existing dashboard API clients and components already provide company, finance, briefing, and action data.
- The dashboard must not become another task board or a passive analytics page.

### 3. Dependencies

Prompt 1.

### 4. Implementation Requirements

- First generate `docs/design/references/executive-overview-reference.png`.
- Retain revenue, costs, result, available cash, pipeline value, and support SLA risk when authorized and available.
- Present no more than three to five prioritized actions.
- Show a concise daily briefing and named agent recommendations with direct routes.
- Present missing or unauthorized metrics with plain-language states rather than zero-like false data.
- Remove finance reset and destructive administration controls from the normal dashboard and place them in the appropriate restricted setting without changing their authorization.
- Ensure Dashboard and Agent Team have distinct navigation labels, descriptions, and responsibilities.
- Reuse or extract shared KPI and recommendation components where that reduces current dashboard/module duplication.
- Preserve existing API behavior unless a focused read-model adjustment is required.

### 5. Constraints And Preservation Rules

- Do not duplicate the Agent Team Kanban.
- Do not fill missing data with simulation values.
- Do not show finance values to users without FinanceView access.
- Do not make recommendations executable without existing policy and approval enforcement.
- Keep dashboard loading bounded and avoid serial API waterfalls where existing aggregate reads are available.

### 6. Acceptance Criteria

- Given an authorized company user with data, when Overview loads, then company health, top priorities, and recommended next actions are visible in the first viewport.
- Given missing sales or support data, when Overview loads, then it explains what is unavailable without presenting misleading zero values.
- Given a recommended action, when selected, then it opens the correct company-scoped operational record.
- Given a normal user, when Overview loads, then no finance reset or system administration action is visible.

### 7. Verification

- Unit tests for KPI and recommendation presentation.
- Authorization tests for restricted metrics and administration actions.
- Playwright screenshots for populated, partial-data, empty, loading, and error states.
- English and Swedish localization verification.
- Responsive checks across desktop, tablet, and mobile.
- Web build and relevant API/read-model tests if contracts change.

### 8. Definition Of Done

Overview is an action-oriented executive cockpit, not a duplicate work board. It is production-backed, authorized, localized, responsive, and visually verified with no mock data or destructive controls in the normal experience.

---

## Prompt 3: Preserve And Productionize The Agent Team Kanban

### 1. Title And Outcome

Retain and refine `/agents/staff` as the primary Agent Team Kanban for supervising Finance, Sales, Support, and future agent work.

### 2. Current Context

- `AgentStaffOverview.razor` already renders a company KPI strip, rows by agent, Planned, In progress, Waiting for human approval, and Completed columns, selectable task cards, and a right-side details panel.
- The backend read model already assigns tasks to stages and exposes approval routes.
- Previous defects have shown that approval-related work can be misclassified as In progress, so backend status and approval relationships remain authoritative.
- `/tasks` is a separate detailed task list and must not replace this board.

### 3. Dependencies

Prompts 1 and 2.

### 4. Implementation Requirements

- First generate `docs/design/references/agent-team-kanban-reference.png`.
- Keep the four stages and agent rows.
- Keep company summary KPIs above the board, using the same shared presentation conventions as Overview.
- Preserve the right-side task details panel and make card selection obvious, keyboard-accessible, and URL-safe if selection is persisted.
- Show agent avatar, role, status, task priority, due date, concise context, and approval state in plain language.
- Ensure tasks with a pending human approval appear in Waiting for human approval based on authoritative backend state.
- Add bounded automatic refresh or real-time update using existing infrastructure, with pause/cancellation on disposal and without layout shifts.
- Preserve selected task where possible across refresh.
- Provide appropriate empty states per agent and stage.
- On narrow screens, use a horizontally scrollable board with stable columns or a stage-focused mode; do not compress cards into unreadable widths.
- Keep a route to the full task detail and approval detail.
- Add operationally useful error and stale-data messaging.

### 5. Constraints And Preservation Rules

- Do not implement task-stage business rules in Razor.
- Do not infer approval state from card titles.
- Do not permit drag-and-drop status changes unless a real authorized backend command and policy exist.
- Do not merge the Kanban into `/tasks`.
- Preserve tenant isolation, task authorization, approval enforcement, and audit behavior.

### 6. Acceptance Criteria

- Given work assigned to Laura, Alex, and Ben, when Agent Team loads, then each task appears in exactly one authoritative stage and one agent row.
- Given a pending approval-backed task, when the board loads, then it appears under Waiting for human approval with a Review approval action.
- Given a selected card, when data refreshes, then selection remains if the task still exists and the board does not jump.
- Given a mobile viewport, when the board is used, then agent identity, stage, and task text remain readable without overlap.
- Given an empty company, when the board loads, then it provides a direct Manage agents action.

### 7. Verification

- Integration tests for stage classification, pending approvals, completed tasks, cross-company isolation, and hidden unauthorized work.
- Component tests for selection, refresh, empty states, and task details.
- Playwright desktop/mobile interaction tests and screenshots.
- Verify polling or real-time subscriptions are disposed.
- Verify no duplicate tasks after refresh.
- Web and affected API project builds.

### 8. Definition Of Done

The Agent Team Kanban remains the primary agent supervision screen and works with real task and approval data. It is stable under refresh, accessible, responsive, localized, and backed by authoritative server decisions.

---

## Prompt 4: Create The Consolidated Work Center

### 1. Title And Outcome

Create a single Work destination that consolidates Tasks, Approvals, Message review, and Notifications while preserving their distinct policies and detail behavior.

### 2. Current Context

- `/queue`, `/tasks`, `/approvals`, `/inbox`, and `/outbound-review-queue` expose overlapping work.
- Each page currently has separate filters, selection/detail behavior, company resolution, and localized or hardcoded labels.
- Approval and outbound message execution have authoritative backend policy, approval, and delivery boundaries that must remain unchanged.
- Agent Team provides cross-agent Kanban supervision; Work is the current user's decision and follow-up list.

### 3. Dependencies

Prompts 1 and 3.

### 4. Implementation Requirements

- First generate `docs/design/references/work-center-reference.png`.
- Add a company-scoped Work route with tabs: Tasks, Approvals, Messages, Notifications.
- Use a shared list-and-details layout with URL-addressable selected item and tab state.
- Reuse current typed API clients and detail actions.
- Preserve task filtering, approval decisions, outbound message review, and notification navigation.
- Show tab counts only from authorized real data.
- Present source agent, reason, due/age, risk, and required user action in plain language.
- Keep destructive or external actions behind existing confirmation, approval, idempotency, outbox, and delivery behavior.
- Convert old top-level routes into compatibility routes or redirects that retain query parameters, selected IDs, company ID, and safe return URLs.
- Update internal links, dashboard actions, Kanban actions, notifications, and capability links to target Work where appropriate.
- Localize all changed content in English and Swedish.

### 5. Constraints And Preservation Rules

- Do not combine backend aggregates merely because the UI uses tabs.
- Do not bypass approval checks or direct-send outbound messages.
- Do not expose one company's work under another company context.
- Preserve browser back/forward behavior and deep links.
- Do not remove legacy routes until all tests and internal callers are migrated.

### 6. Acceptance Criteria

- Given a user with tasks, approvals, messages, and notifications, when Work opens, then each item appears in the correct tab without duplication.
- Given a legacy approval URL, when opened, then the same approval is reachable with company context preserved.
- Given an approved message action, when executed, then existing durable delivery behavior is used.
- Given a user without access to a work type, when Work loads, then the tab or data is hidden according to existing policy without leaking counts.
- Given Swedish UI language, then all Work labels and statuses are localized.

### 7. Verification

- Authorization and tenant-isolation integration tests for every tab.
- Regression tests for approval decisions and outbound review actions.
- Route compatibility tests with selected IDs and return URLs.
- Component tests for tabs, filtering, selection, loading, empty, and error states.
- Playwright screenshots and keyboard checks at desktop and mobile widths.
- Build affected Web, API, Application, and capability projects.

### 8. Definition Of Done

Work is the single daily decision center. Existing execution policies, routes, audit behavior, and external side-effect boundaries remain correct. No legacy top-level link, duplicate work item, mock count, or untranslated status remains.

---

## Prompt 5: Decompose Agent Configuration

### 1. Title And Outcome

Replace the oversized Agent Management experience with focused Agent settings for team roles, briefs, capabilities, access, connections, autonomy, and automation.

### 2. Current Context

- `Pages/Agents.razor` is more than 3,000 lines and combines roster, hiring, briefs, documents, AI tools, capabilities, analysis, handoffs, memory, shared sources, mailbox setup, access scopes, and configuration.
- `/agents`, `/agents/{id}`, `/agents/{id}/chat`, `/agents/manage`, and `/agents/mailboxes/connect` overlap in purpose.
- Agent Team is now the primary operational surface and must remain separate from configuration.
- Existing agent APIs, authorization, document upload/indexing, mailbox OAuth/hosted mail, and capability policies are production behavior.

### 3. Dependencies

Prompts 1, 3, and 4.

### 4. Implementation Requirements

- First generate `docs/design/references/agent-settings-reference.png`.
- Create Settings > Agents with focused sections or routes:
  - Team and roles
  - Briefs and documents
  - Capabilities
  - Access and data scope
  - Connections
  - Autonomy and approvals
  - Automation
- Break `Agents.razor` into cohesive components with independent loading, saving, error, and unsaved-change states.
- Preserve canonical agent profiles and contextual chat.
- Keep mailbox secrets and provider authentication out of agent profile records.
- Retain document upload, processing status, deduplication, indexing, generation, and save behavior.
- Ensure capability restrictions explain missing access or configuration in plain language and route to the exact settings section.
- Preserve role-aware access to configuration.
- Add safe redirects from old roster/management anchors and query parameters.
- Remove the duplicate roster presentation after callers migrate, while keeping canonical profile routes.

### 5. Constraints And Preservation Rules

- Do not merge operational Kanban work into settings.
- Do not expose mailbox credentials, tokens, or secrets.
- Do not relax capability, tool, data-scope, or autonomy authorization.
- Do not silently discard unsaved briefs.
- Do not recreate the current giant page as one giant tab component.

### 6. Acceptance Criteria

- Given a company administrator, when Settings > Agents opens, then each configuration concern has a clear dedicated destination.
- Given a restricted capability, when Configure is selected, then the exact access or connection section opens.
- Given an uploaded brief document, when processing completes and the page refreshes, then it remains visible exactly once.
- Given unsaved brief text, when navigating away, then the user receives a clear warning.
- Given a non-administrator, when opening agent settings, then unauthorized controls and data are not returned.

### 7. Verification

- Component tests for section routing, unsaved changes, processing states, and capability actions.
- Integration tests for agent authorization, tenant isolation, document deduplication, and mailbox connection routing.
- Playwright screenshots for each primary settings section at desktop and mobile widths.
- Verify no secrets appear in HTML, logs, URLs, or API responses.
- English and Swedish localization checks.
- Build and focused agent/mailbox tests.

### 8. Definition Of Done

Agent configuration is decomposed into production-ready, focused settings while Agent Team remains the operational Kanban. Existing briefs, documents, capabilities, mailbox connections, policies, profiles, and chat continue to work without duplicate roster UI or lost state.

---

## Prompt 6: Consolidate The Finance Experience

### 1. Title And Outcome

Consolidate Finance into a consistent overview and six operational areas, reducing duplicate review and settings screens while preserving accounting and Fortnox behavior.

### 2. Current Context

- Finance currently exposes overview, balances, cash position, invoices, invoice reviews, supplier bills, supplier bill review, payments, transactions/activity, counterparties, issues/anomalies, monthly summary, mailbox, settings, simulation, and transparency routes.
- Several routes are aliases and several pages represent separate stages of one record workflow.
- Finance uses production local SQL Server/Docker-compatible data, Fortnox references, approval policies, reconciliation, and background processing.
- Existing `FinancePageLayout`, `FinanceSectionNav`, route helpers, API clients, and detail pages should be reused or improved.

### 3. Dependencies

Prompts 1, 2, and 4.

### 4. Implementation Requirements

- First generate:
  - `docs/design/references/finance-consolidated-overview-reference.png`
  - `docs/design/references/finance-supplier-bills-reference.png`
- Implement local Finance navigation: Overview, Cash, Customer invoices, Supplier bills, Payments, Transactions, Issues.
- Merge Balances into Cash.
- Merge supplier bill review into Supplier bills using explicit queue/filter states and contextual detail.
- Integrate invoice review into Customer invoices and Work > Approvals.
- Move mailbox and integration setup to Settings > Connections.
- Keep monthly reporting accessible contextually from Overview or Finance reports without adding another global destination.
- Preserve existing detail routes and route aliases through redirects or compatibility wrappers.
- Standardize headers, KPI strips, filters, dense tables, details panels, actions, and data states.
- Lead with overdue, blocked, unreconciled, approval-required, and provider-failed work before secondary metrics.
- Preserve source attribution and explain whether records come from Fortnox, mailbox intake, simulation, or manual entry where relevant.

### 5. Constraints And Preservation Rules

- Do not change accounting eligibility, posting, approval, payment, sync, or reconciliation decisions in Razor.
- Preserve Fortnox external references and local/Docker SQL Server compatibility.
- Do not mix simulation records into normal company data without explicit simulation state.
- Do not infer paid, posted, or synchronized state from display labels.
- Keep provider failures operator-visible and safe.

### 6. Acceptance Criteria

- Given a Finance user, when entering Finance, then seven or fewer local destinations cover the complete normal workflow.
- Given a supplier bill needing review, when Supplier bills opens, then it is reachable through a queue/filter without requiring a separate top-level page.
- Given a legacy bill review or transaction alias URL, when opened, then the correct company-scoped record remains reachable.
- Given Fortnox and simulation records, when a list renders, then source and state are accurate and not conflated.
- Given a user without Finance access, when Finance routes are requested, then existing authorization behavior remains enforced.

### 7. Verification

- Finance authorization and tenant-isolation tests.
- Regression tests for bills, invoices, payments, approvals, Fortnox source filtering, sync references, and reconciliation.
- Route compatibility tests.
- Playwright screenshots for overview, populated tables, review queue, detail, empty, loading, restricted, and error states.
- English/Swedish and locale-formatting verification.
- Build Web, API, Application, Finance Infrastructure, Persistence, and migrations project if applicable.

### 8. Definition Of Done

Finance has one coherent production UI without losing accounting behavior, source traceability, provider integration, approvals, deep links, or database compatibility. No duplicate normal-work navigation or mock finance data remains.

---

## Prompt 7: Consolidate Sales Around Prospects And Pipeline

### 1. Title And Outcome

Create a focused Sales experience centered on Prospects, Pipeline, and follow-up actions while keeping campaign, deal, and contact capabilities.

### 2. Current Context

- Sales currently provides Overview, Leads, Prospecting, Pipeline, Campaigns, deal detail, and contact profile.
- Leads and Prospecting represent overlapping early-stage customer acquisition work.
- Sales Overview currently presents KPIs, conversion/forecast analytics, campaign performance, recommendations, and movement.
- Alex is the named Sales agent and should remain visibly responsible for recommendations and work.

### 3. Dependencies

Prompts 1, 2, and 4.

### 4. Implementation Requirements

- First generate:
  - `docs/design/references/sales-overview-reference.png`
  - `docs/design/references/sales-prospects-reference.png`
- Implement local Sales navigation: Overview, Prospects, Pipeline, Campaigns.
- Merge Leads and Prospecting into Prospects with clear lifecycle filters and saved URL state.
- Preserve deal and contact detail routes.
- Lead Overview with deals and prospects requiring action, pipeline value, forecast, and Alex's top recommendation.
- Move conversion, campaign variant, and forecast distribution analytics below operational follow-up.
- Use one list/details interaction pattern across Prospects and Pipeline where appropriate.
- Preserve message review, approval, mailbox, and outbound automation boundaries.
- Update legacy Lead and Prospecting links to the consolidated route while preserving meaningful query state.

### 5. Constraints And Preservation Rules

- Do not merge contacts, leads, and deals into one backend entity.
- Do not bypass consent, review, approval, or outbound delivery policy.
- Do not promise or display fabricated forecast confidence.
- Preserve company scoping and role access.
- Keep real campaign and mailbox data separate from empty/demo states.

### 6. Acceptance Criteria

- Given a new lead or prospect, when Prospects opens, then it appears once with a clear lifecycle and next action.
- Given a deal requiring follow-up, when Sales Overview opens, then it is visible before secondary analytics.
- Given a legacy Leads or Prospecting URL, when opened, then the equivalent Prospects view loads with company context preserved.
- Given a message requiring review, when selected, then it routes to Work > Messages and retains existing policy.

### 7. Verification

- Sales authorization and tenant-isolation tests.
- Regression tests for lead/prospect lifecycle, deal pipeline, campaign analytics, and outbound review.
- Route and filter-state tests.
- Playwright screenshots and interaction checks for Overview, Prospects, Pipeline, Campaigns, and detail routes.
- English/Swedish localization and locale formatting checks.
- Build Web, API, Application, and Sales Infrastructure projects.

### 8. Definition Of Done

Sales is action-led and coherent, with one Prospects surface and preserved production pipeline, campaign, contact, consent, and outbound behavior. No duplicate lead presentation or mock sales data remains.

---

## Prompt 8: Consolidate Support Around Cases And Knowledge

### 1. Title And Outcome

Make Support a queue-first operational workspace and combine Knowledge gaps and governed memory into one Knowledge destination.

### 2. Current Context

- `/support` currently combines action buttons, six KPI cards, search/filter controls, SLA analytics, learning analytics, and the case table.
- `/support/knowledge-gaps` and `/support/memory` are separate but related support-learning surfaces.
- `/support/settings/sla` is configuration rather than daily case work.
- Support case detail includes customer messages, internal notes, grounded draft generation, answerability, source evidence, review, approval, and delivery.
- Ben is the named Support agent.

### 3. Dependencies

Prompts 1, 2, 4, and 5.

### 4. Implementation Requirements

- First generate:
  - `docs/design/references/support-cases-reference.png`
  - `docs/design/references/support-knowledge-reference.png`
- Implement local Support navigation: Cases and Knowledge.
- Make the case queue, SLA risk, and actions visible before analytics.
- Keep concise queue KPIs but move detailed SLA and learning analytics into secondary sections.
- Combine knowledge gaps and governed support memory into Knowledge with clear tabs or states.
- Move SLA configuration to Settings > Support.
- Preserve Support case detail and improve its hierarchy without weakening grounding, answerability, source evidence, human review, approval, or durable delivery.
- Keep Ben visible as owner of prioritization and draft recommendations.
- Preserve mailbox-ingested message identity, deduplication, threading, and source evidence.
- Add route compatibility for old knowledge, memory, and SLA settings links.

### 5. Constraints And Preservation Rules

- Do not allow AI drafts to send outside existing approval and delivery policy.
- Do not treat low answerability as a completed answer.
- Do not expose unapproved memory as approved knowledge.
- Do not mix support data across companies.
- Do not hide mailbox ingestion or delivery failures.

### 6. Acceptance Criteria

- Given open support cases, when Support opens, then the case queue and SLA-risk work are visible before learning analytics.
- Given an approved product source, when Ben drafts a supported answer, then source evidence remains visible and the draft follows existing review policy.
- Given a repeated knowledge gap, when Knowledge opens, then related gap and memory information can be reviewed without switching unrelated top-level pages.
- Given a legacy memory or SLA settings URL, when opened, then the correct target section resolves.
- Given Swedish UI language, then filters, statuses, evidence labels, and actions contain no English fallback keys.

### 7. Verification

- Support authorization and tenant-isolation integration tests.
- Regression tests for mailbox ingestion, case creation, grounding, answerability, memory governance, approval, and durable reply delivery.
- Route compatibility tests.
- Playwright populated, empty, loading, error, restricted, draft, and evidence-state screenshots.
- Responsive and keyboard checks for filters, tables, details, and draft controls.
- Build Web, API, Application, Mailbox, Support, and Operations projects as affected.

### 8. Definition Of Done

Support is a production queue-first experience with a coherent Knowledge area. Existing safety, grounding, memory governance, mailbox, approval, and delivery behavior remains intact with no hidden failures or mock cases.

---

## Prompt 9: Relocate Settings, Preserve Routes, And Complete The Consistency Pass

### 1. Title And Outcome

Finish the UI consolidation by implementing grouped Settings, relocating administrative screens, preserving legacy routes, completing localization, and removing obsolete UI only after migration.

### 2. Current Context

- Onboarding, Workflows, Audit, Briefing Delivery, Finance mailbox/settings, Support SLA settings, Simulation Lab, transparency tools, and user preferences currently exist as separate routes.
- Several remain valid capabilities but should not be primary navigation.
- Prompts 1-8 introduce the target shell and consolidated business workspaces.
- Existing bookmarks, notifications, workflow tasks, emails, tests, and agent capability links may still target old routes.

### 3. Dependencies

Prompts 1 through 8.

### 4. Implementation Requirements

- First generate `docs/design/references/settings-hub-reference.png`.
- Implement grouped Settings sections:
  - Company setup
  - Agents
  - Connections
  - Automation
  - Briefings
  - Department settings
  - Security and audit
  - User preferences
- Move links to Onboarding, Workflows, Audit, Briefing Delivery, mailbox connections, Finance integrations, and Support SLA configuration into the correct group.
- Keep Simulation Lab and System Administration restricted and visually separated.
- Inventory every `@page` route and every internal link before removing obsolete UI.
- Implement redirects or compatibility pages that preserve company ID, selected record, filters, source, and safe return URL.
- Update notification, approval, task, dashboard, agent capability, and email links.
- Remove obsolete duplicate Razor/CSS implementations only after target routes and regression tests pass.
- Consolidate shared UI tokens and components and remove truly unused module CSS without broad unrelated rewrites.
- Complete English and Swedish localization for all changed and shared operational pages.
- Update `docs/design.md`, navigation documentation, and any route inventories to describe the resulting product.

### 5. Constraints And Preservation Rules

- Do not delete backend capabilities merely because their page moved.
- Do not redirect across authorization boundaries.
- Do not create redirect loops or discard query state.
- Do not expose simulation, audit, transparency, or system tools to normal users.
- Do not remove a legacy route while code, tests, notifications, or persisted task routes still target it.

### 6. Acceptance Criteria

- Given a normal onboarded user, when the sidebar renders, then only the target primary and bottom navigation appear.
- Given a company administrator, when Settings opens, then configuration is grouped by user intent rather than implementation subsystem.
- Given an authorized legacy route, when opened, then it resolves to the equivalent target with context preserved.
- Given an unauthorized legacy route, when opened, then it does not bypass target authorization.
- Given English or Swedish UI language, when traversing all primary and settings destinations, then no localization keys, mixed-language labels, or raw enums appear.
- Given the final CSS bundle, then removed duplicate screens have no retained unused styles proven by route/component usage checks.

### 7. Verification

- Full route inventory and redirect test matrix.
- Authorization and tenant-isolation tests for every Settings group and restricted tool.
- Playwright navigation smoke tests covering every primary destination, Settings group, and representative legacy route.
- Desktop, tablet, and mobile screenshot comparison against all generated references.
- Accessibility smoke checks for headings, landmarks, focus order, labels, contrast, and keyboard use.
- English and Swedish localization scan.
- `dotnet build` for the solution with `--no-restore`.
- Run focused module suites plus the existing navigation, authorization, approvals, agent, finance, sales, support, mailbox, workflow, and audit regression tests.

### 8. Definition Of Done

The application presents the target information architecture with the Agent Team Kanban intact, grouped production settings, compatible deep links, complete localization, responsive layouts, and no obsolete duplicate UI or in-scope TODOs. All required builds, tests, and browser checks pass.
