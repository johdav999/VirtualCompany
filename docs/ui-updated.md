# Virtual Company UI Consolidation Plan

## Purpose

This plan reduces navigation and screen duplication while preserving the product's operational capabilities. The target experience is a calm executive control room where users supervise named AI agents, review decisions, and act on company work.

The Agent Team Kanban at `/agents/staff` is a primary product surface and must be retained.

## Current Assessment

The current web application contains approximately 57 Razor page files, 74 routes, and 16 primary sidebar destinations. Many routes are valid detail or administration surfaces, but too many appear as peer-level navigation choices.

The main issues are:

- Global navigation mixes daily work, setup, administration, history, and development tools.
- Queue, Inbox, Tasks, Approvals, Message Review, Workflows, and Agent Team overlap.
- Agent Team, Agent directory, profiles, chat, and Agent Management lack clear boundaries.
- Finance, Sales, Support, Dashboard, and Agents use separate component and styling conventions.
- Mobile navigation pushes the workspace away instead of behaving as an overlay drawer.
- Most sidebar icon classes have no loaded icon implementation.
- Shared operational pages still contain hardcoded English.
- Several screens lead with passive metrics instead of the next decision or action.

## Product Principles

Every main screen must answer:

1. What is happening now?
2. What needs attention?
3. What should the user do next?

Apply these rules:

- Keep named agent identity visible in work ownership, recommendations, approvals, and history.
- Prefer company language over workflow, enum, trigger, execution, or tenant terminology.
- Keep normal business pages separate from simulation and system administration.
- Use consistent page headers, tabs, filters, tables, empty states, details panels, and responsive behavior.
- Preserve authorized deep links and existing backend behavior while changing navigation.
- Do not replace production data with mock UI data.

## Target Information Architecture

### Primary Navigation

1. **Overview**
   - Executive company summary
   - Top three to five priorities
   - Agent recommendations
   - Risks and approvals requiring attention

2. **Agent team**
   - Agent Team Kanban
   - Rows for Finance, Sales, Support, and future agents
   - Planned, In progress, Waiting for human approval, and Completed columns
   - Selectable task cards and a persistent details panel
   - Company KPI strip above the board

3. **Finance**
   - Overview
   - Cash
   - Customer invoices
   - Supplier bills
   - Payments
   - Transactions
   - Issues

4. **Sales**
   - Overview
   - Prospects
   - Pipeline
   - Campaigns
   - Deal and contact detail as contextual routes

5. **Support**
   - Cases
   - Knowledge
   - Case detail as a contextual route

6. **Work**
   - Tasks
   - Approvals
   - Messages requiring review
   - Notifications

### Bottom Navigation

- **History**
  - User-facing activity and rationale
- **Settings**
  - Company setup
  - Agents
  - Connections
  - Automation
  - Briefings
  - Support settings
  - Security and audit
  - User preferences

### Restricted Navigation

- Simulation Lab is visible only in simulation, demo, or development contexts.
- System Administration is visible only to authorized system administrators.
- Audit and transparency tools remain available to authorized users but do not appear as normal operational navigation.

## Screen Decisions

### Keep As Primary Screens

| Current route | Target role | Decision |
| --- | --- | --- |
| `/dashboard` | Overview | Keep and update |
| `/agents/staff` | Agent team | Keep and update; this is the primary Kanban |
| `/finance` | Finance overview | Keep and update |
| `/app/sales` | Sales overview | Keep and update |
| `/support` | Support cases | Keep and update |
| New consolidated route | Work | Create from existing work surfaces |

### Keep As Contextual Or Detail Screens

| Current surface | Decision |
| --- | --- |
| Finance invoice, bill, payment, transaction, issue, and alert details | Keep as deep links |
| Sales deal and contact details | Keep as deep links |
| Support case detail | Keep as a deep link |
| Agent profile | Keep as a deep link |
| Agent chat | Keep, but open contextually from Agent Team or profile |
| Team mailbox connection | Keep under Settings > Connections |
| Public company/contact pages | Keep outside the authenticated shell |

### Consolidate

| Current surfaces | Target |
| --- | --- |
| `/queue`, `/tasks`, `/approvals`, `/inbox`, `/outbound-review-queue` | Work with tabs |
| `/agents` and overlapping roster sections in `/agents/manage` | Settings > Agents |
| Finance balances and cash position | Finance > Cash |
| Supplier bill list and supplier bill review | Finance > Supplier bills with queue/filter states |
| Invoice review and customer invoice work | Finance > Customer invoices plus Work > Approvals |
| Sales leads and prospecting | Sales > Prospects |
| Support knowledge gaps and memory review | Support > Knowledge |
| Activity feed and user-facing operational history | History |

### Remove From Primary Navigation

| Current surface | New access |
| --- | --- |
| Home | Redirect authenticated users to Overview |
| Onboarding | Settings > Company setup |
| Workflows | Settings > Automation |
| Audit | Settings > Security and audit |
| Briefing delivery | Settings > Briefings |
| Finance mailbox | Settings > Connections |
| Support SLA settings | Settings > Support |
| Simulation Lab | Restricted environment-only entry |
| System Administration | Restricted administrator-only entry |

Removing an item from primary navigation does not imply deleting its backend behavior. Preserve existing routes as redirects or authorized contextual routes until callers, bookmarks, tests, and notification links have migrated.

## Screen-Level Direction

### Overview

The Overview is the executive cockpit, not a task board or reporting archive.

Include:

- Revenue, costs, result, available cash, pipeline value, and support SLA risk.
- Top three to five company priorities.
- A concise daily briefing.
- Named agent recommendations.
- Direct links to the relevant operational record.

Move finance reset and other destructive administration actions to restricted settings.

### Agent Team Kanban

The Kanban is the primary place to supervise agent execution.

Preserve:

- Agent rows with avatar, role, and current status.
- Planned, In progress, Waiting for human approval, and Completed stages.
- Real backend task and approval states.
- Selectable cards and the right-side task details panel.
- Company summary KPIs above the board.

Improve:

- Real-time or bounded polling refresh without layout movement.
- Plain-language status and stage labels.
- Clear ownership, due date, priority, rationale, and approval actions.
- Keyboard selection and visible selected state.
- Compact cards that do not truncate the distinguishing information.
- Responsive behavior using a horizontally scrollable board or stage-focused mobile mode.

Do not merge the Kanban into the generic task list.

### Work

Work is the user's decision and follow-up center.

Tabs:

- Tasks
- Approvals
- Message review
- Notifications

Use a shared list-and-details layout. Preserve server-side authorization, source routes, approval policies, and task status rules. A task waiting for approval must appear in the approval stage and relevant Work tab.

### Agent Settings

Separate day-to-day supervision from configuration.

Agent settings should provide focused sections for:

- Team and roles
- Briefs and documents
- Capabilities
- Access and data scope
- Mailbox and provider connections
- Autonomy and approvals
- Automation

Reduce the current large Agent Management page into routed or tabbed components with independent loading and error states.

### Finance

Use one Finance shell and local navigation.

- Merge Balances into Cash.
- Merge supplier bill review into Supplier bills.
- Integrate invoice review into Customer invoices and shared approvals.
- Move mailbox and integrations to Settings > Connections.
- Keep detail routes and aliases compatible.
- Lead each page with exceptions and next actions before secondary metrics.

### Sales

- Merge Leads and Prospecting into Prospects.
- Keep Overview, Pipeline, Campaigns, deal detail, and contact detail.
- Lead Overview with opportunities requiring follow-up and Alex's recommended action.
- Keep analytics secondary to pipeline and follow-up work.

### Support

- Make the case queue the dominant Support screen.
- Move SLA analytics behind an Analytics or performance section.
- Combine Knowledge gaps and Memory into Knowledge.
- Move SLA configuration to Settings > Support.
- Keep Ben's draft, grounding, confidence, and approval information visible in case detail.

### Settings And Administration

Settings should use grouped, task-oriented sections rather than a single giant form.

Sections:

- Company setup
- Agents
- Connections
- Automation
- Briefings
- Department settings
- Security and audit
- User preferences

System-only diagnostics and simulation tools must be visually and permission-wise separated from normal company settings.

## Shared UI Foundation

Standardize:

- App shell and responsive navigation
- Page headers and local tab navigation
- KPI strips
- Agent recommendation panels
- Queue/list and details layouts
- Dense tables
- Filters and search
- Status, priority, and approval badges
- Empty, loading, error, restricted, and configuration-required states
- Toasts and operator-visible failures

Use the tokens and interaction rules in `docs/design.md` and `ui-instructions.md`. Reduce module-specific CSS where shared components can express the same layout without reducing quality.

## Responsive Requirements

- Desktop sidebar remains fixed and approximately 240px wide.
- Tablet may use a compact or collapsible sidebar.
- Mobile uses an overlay drawer with backdrop, close control, body scroll lock, and focus return.
- The Agent Team Kanban must not squeeze cards into unreadable columns.
- Tables must provide a deliberate narrow-screen representation.
- Buttons and labels must wrap without overlap.
- Main content must remain visible when navigation is open or closed.

## Accessibility And Localization

- All controls require accessible names and visible focus states.
- Navigation must support keyboard traversal and announce the active destination.
- Do not rely on color alone for status.
- Avoid nested interactive controls.
- Move hardcoded user-facing strings into the established localization resources.
- Verify English and Swedish for every changed page.
- Use locale-aware money, number, date, and percentage formatting.

## Route Transition Strategy

1. Introduce target screens and navigation without deleting old routes.
2. Add local redirects or compatibility wrappers where a screen is consolidated.
3. Update internal links, notifications, task routes, and agent capability routes.
4. Add authorization and tenant-isolation tests for target routes.
5. Monitor legacy route use before removal.
6. Remove only obsolete UI implementations after their behavior and callers have migrated.

## Delivery Order

1. Shared shell, navigation, responsive behavior, icons, and route map.
2. Overview differentiation and executive action hierarchy.
3. Agent Team Kanban refinement.
4. Consolidated Work area.
5. Agent settings decomposition.
6. Finance consolidation.
7. Sales consolidation.
8. Support consolidation.
9. Settings and administration relocation, legacy redirects, localization, and final consistency pass.

Each major redesign must follow the screenshot-first workflow in `docs/design.md` and `ui-instructions.md`.

## Success Measures

- No more than six primary navigation destinations.
- Normal users do not see simulation or system administration.
- Users can find a task, approval, or message requiring review from one Work destination.
- Agent work remains visible in the Agent Team Kanban.
- Dashboard and Agent Team have distinct purposes.
- Existing authorized deep links continue to resolve.
- Changed screens are usable without overlap at desktop, tablet, and mobile widths.
- English and Swedish display no resource keys or hardcoded fallback labels.
- Core task, approval, mailbox, finance, sales, and support workflows remain behaviorally unchanged.
