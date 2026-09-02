# Virtual Company Design System

## 🚨 Mandatory UI Design Workflow — Reference Screenshot First

This workflow is REQUIRED for all UI work.

Codex MUST NOT implement UI before completing this workflow.

When creating or significantly redesigning any UI screen, page, dashboard, modal, or major component:

1. First create a visual reference screenshot using OpenAI image generation (`image.2` or the current approved image model).

2. The screenshot prompt MUST:
   - be explicitly written before generation
   - be based on this `design.md`
   - include the specific page requirements
   - include existing product context
   - include the SaaS visual style
   - include layout structure such as cards, panels, spacing, tables, forms, and empty states

3. Store the generated reference image in `/docs/design/references/`.

4. Use a descriptive filename, for example:
   - `finance-overview-reference.png`
   - `supplier-bills-reference.png`
   - `fortnox-settings-reference.png`

5. Then implement the UI using the screenshot as the visual target.

6. The implementation MUST match the reference screenshot in:
   - layout
   - spacing
   - typography
   - card structure
   - colors
   - visual hierarchy
   - empty states
   - responsive behavior

7. Do NOT copy the image as a static asset into the UI. Use it only as a reference.

8. If the generated screenshot conflicts with this `design.md`, this `design.md` wins.

9. If the generated screenshot conflicts with existing reusable components, prefer adapting existing components unless that would clearly reduce UI quality.

10. After implementation, compare the built UI against the reference screenshot and refine until the result is visually close.

---

## Design Philosophy

The UI should feel like an executive control center where AI agents operate the company and the user supervises decisions.

Focus:

- Clarity over decoration
- Actions over data
- Agents over raw metrics

---

## Layout

### Global Layout

- Left sidebar: navigation
- Center: main workspace
- Right panel: AI insights and actions

### Sidebar

- Fixed width: 240px

Sections:

- Dashboard
- Finance
- Sales
- Support
- Agents

---

## Components

### KPI Card

- Padding: 16px
- Border radius: 12px
- Shadow: subtle

Structure:

- Label
- Value, large
- Trend, color-coded

---

### Agent Insight Card

- Avatar: 48px
- Name and role
- Insight text: 1–2 lines

Actions:

- Primary button
- Secondary action

Example:

Laura, Finance Manager  
“Cash flow risk in 14 days”  

[Review payments] [View forecast]

---

### Table

- Dense but readable
- Sticky header
- Inline actions

---

## Colors

### Light Mode

- Background: `#F7F9FC`
- Card: `#FFFFFF`
- Primary: `#2563EB`
- Success: `#16A34A`
- Warning: `#F59E0B`
- Danger: `#DC2626`

### Dark Mode

- Background: `#0F172A`
- Card: `#1E293B`

---

## Typography

- Font: Inter
- Headings: 600 weight
- Body: 400 weight

---

## Interaction Principles

1. Every screen must answer:
   - What is happening?
   - What should I do?

2. Replace passive charts with:
   - Insight plus recommended action

3. Use plain English:
   - Avoid internal or system names

---

## Dashboard Rules

### Do

- Show top 3–5 priorities
- Highlight risks
- Show agent recommendations

### Avoid

- Overloaded charts
- Duplicate information across panels

---

## Current Product Information Architecture

The production app shell uses these primary destinations:

- **Overview**: executive company health, trends, priorities, and agent recommendations.
- **Agent team**: the operational Kanban with Finance, Sales, and Support agent rows and the stages Planned, In progress, Waiting for human approval, and Completed.
- **Finance**: daily finance work organized as Overview, Cash, Customer invoices, Supplier bills, Payments, Transactions, and Issues.
- **Sales**: daily sales work organized around Overview, Prospects, Pipeline, Campaigns, and customer or deal detail.
- **Support**: case queue and case details, with Knowledge as the secondary support workspace.
- **Work**: the shared action center for Tasks, Approvals, Messages, and Notifications.

History and Settings are secondary destinations at the bottom of the navigation. Simulation Lab and System Administration remain restricted utilities and must be visually separated from daily company work.

Do not add retired pages back to the primary navigation. Preserve legacy routes only as compatibility paths or detailed workflow destinations.

The complete canonical and compatibility route map is maintained in `docs/ui-route-inventory.md`.

## Settings Information Architecture

Settings is the entry point for:

- Company setup and onboarding
- Agent roster, role briefs, capabilities, access, and team mailboxes
- Finance and mailbox connections
- Workflow and outbound automation
- Briefing delivery
- Finance and support operating policies
- Audit history
- User language and profile preferences

Administrative settings must not be presented as daily operational actions.

## Reference Screens

The current consolidation references are stored in `docs/design/references/`:

- `app-shell-navigation-reference.png`
- `executive-overview-reference.png`
- `agent-team-kanban-reference.png`
- `work-center-reference.png`
- `agent-settings-reference.png`
- `agent-authority-approval-reference.png`
- `finance-consolidated-overview-reference.png`
- `finance-supplier-bills-reference.png`
- `sales-overview-reference.png`
- `sales-prospects-reference.png`
- `support-cases-reference.png`
- `support-knowledge-reference.png`
- `settings-hub-reference.png`
- `swedish-accounting-setup-reference.png`
- `swedish-vat-statutory-reporting-reference.png`
- `native-invoice-editor-issue-reference.png`
- `native-receivables-collections-reference.png`
- `native-receivables-operations-reference.png`
- `finance-agent-coverage-reference.png`
- `responsibility-driven-today-workspace-reference.png`
- `responsibility-assignments-settings-reference.png`
- `responsibility-driven-monthly-workspace-reference.png`

The responsibility settings reference was generated from
`responsibility-assignments-settings-reference-prompt.md`. It defines the stacked
matrix cards, edit panel, preset preview, explicit replacement confirmation, and
mobile card treatment used by `/settings/responsibilities`.

The monthly workspace reference was generated from
`responsibility-driven-monthly-workspace-reference-prompt.md`. It extends the
canonical `/dashboard` shell with a Today/Monthly switch, explicit calendar and
comparison context, four high-signal result cards, ranked next-month priorities,
compact feature reviews, and a decision/agent outcome rail. Its mobile treatment
stacks those same concepts without introducing a separate route or passive chart
grid.

---

## Agent Design

Each agent must have:

- Visual identity, such as an avatar
- Clear role
- Consistent tone of voice

### Finance Manager

- Strict, analytical
- Focus on risk and compliance

### Sales Manager

- Optimistic, opportunity-driven

### Support Manager

- Empathetic, responsive

---

## Future Extensions

- Voice and talking avatars
- Real-time alerts
- Scenario simulation UI
