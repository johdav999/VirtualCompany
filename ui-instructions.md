# Purpose

This file gives Codex concrete UI implementation rules for the Virtual Company App.

Build the product as a calm, web-first executive control room where named AI agents help run the company and the user supervises decisions. The UI must feel operational, trustworthy, and human-readable.

Always optimize every screen to answer:

- What is happening now
- What needs attention
- What should the user do next

Before any significant new UI or major redesign, Codex must first generate and save a reference screenshot under `/docs/design/references/`, then implement the UI to closely match that reference.

---

# UI rules

- Follow the existing design system in `design.md` as the source of truth.
- Use plain-English business language throughout.
- Present the product as a company run with named AI agents, not as a generic chatbot or technical admin console.
- Keep agent identity visible across the UI:
  - dashboard insight cards
  - task ownership
  - approval requests
  - activity feed
  - sidebar contextual agent card
  - agent profile pages
  - message entry points
- Prefer operational clarity over decorative visuals.
- Prefer action-oriented layouts over passive reporting.
- Prefer reusable existing components unless reuse would clearly reduce UI quality.
- Preserve existing backend logic and routing unless explicitly asked to change them.
- Do not expose raw enums, internal identifiers, policy object names, trigger terminology, or tenant/platform jargon.
- Do not add mock production data.
- Do not mix simulation/dev tooling into normal user-facing business pages.
- Do not introduce a new UI framework without approval.

## Mandatory screenshot-first workflow

For any new page, major component, dashboard, modal, or significant redesign:

1. Write the screenshot prompt explicitly before generation.
2. Generate a visual reference screenshot using the approved OpenAI image model.
3. Base the prompt on the design system and the specific page requirements.
4. Include:
   - current product context
   - clean modern SaaS styling
   - layout structure
   - cards, panels, spacing, tables/forms if relevant
   - empty states if relevant
5. Save the image in `/docs/design/references/`.
6. Use a descriptive filename such as:
   - `dashboard-overview-reference.png`
   - `agents-roster-reference.png`
   - `agent-profile-reference.png`
   - `finance-overview-reference.png`
   - `approvals-queue-reference.png`
   - `workflow-detail-reference.png`
   - `activity-audit-reference.png`
   - `company-setup-reference.png`
7. Implement the UI using the screenshot as the visual target.
8. Match the reference closely in:
   - layout
   - spacing
   - typography
   - card structure
   - colors
   - hierarchy
   - empty states
   - responsive behavior
9. Do not use the screenshot itself as a shipped UI asset.
10. If the screenshot conflicts with `design.md`, `design.md` wins.

## Product surface rules

### Dashboard
Must feel like an executive cockpit, not a reporting page.

Include:
- page header
- KPI row
- daily briefing
- agent updates
- pending approvals
- issues needing attention
- recent activity
- suggested next actions

Avoid:
- chart-heavy passive dashboards
- generic analytics-only layouts
- technical system summaries without action paths

### Agents
Support:
- roster of AI employees
- individual agent profile
- status
- autonomy level
- workload
- permissions summary
- direct actions like message, assign task, adjust autonomy, pause

### Department modules
Support modules such as:
- Finance
- Sales
- Marketing
- Support
- Operations

Each module should have:
- overview page
- operational list/detail pages
- contextual agent presence for that department

### Tasks / Workflows
Show:
- assigned work
- workflow progress
- blocked items
- escalations
- approvals

### Inbox / Activity / Audit
Show:
- communication feed
- event history
- rationale summaries
- data sources used
- override actions where relevant

### Setup / Admin
Use:
- step-based onboarding
- grouped settings cards
- clear next actions
- no giant unstructured forms

## Language rules

Prefer phrases like:
- Needs review
- Waiting for approval
- Laura recommends
- Cash looks healthy
- 3 issues need attention
- Draft ready to send
- What this means
- Data used
- Review details

Avoid phrases like:
- orchestration event
- trigger source
- anomaly identifier
- tenant-scoped
- execution policy object
- generated records
- enum-like labels

Use named agents consistently:
- Laura owns finance
- Alex owns sales
- Maya owns marketing
- Ben owns support
- Nina owns operations
- Eva coordinates executive work

---

# Layout and navigation

## App shell

Use the standard desktop shell:

- fixed left sidebar
- main content area
- light app background
- max content width 1200px to 1280px
- page padding 24px to 32px

Main content background:
- `#F7F9FC`

Use consistent alignment and section spacing.

## Sidebar

Sidebar rules:
- width around 240px
- white background
- thin right border
- vertically scrollable if needed
- logo or company switcher at top
- primary navigation in the middle
- settings/admin near bottom
- contextual agent card above settings where possible

Primary navigation items:
- Home
- Onboarding
- Dashboard
- Finance
- Simulation Lab
- System / Admin
- Agents
- Workflows
- Inbox
- Activity
- Approvals
- Audit
- Briefing delivery

Active nav item:
- blue background
- white text and icon
- rounded rectangle

Inactive nav item:
- muted text
- line icon
- subtle hover tint

## Contextual sidebar agent card

Show a reusable contextual agent card near the bottom of the sidebar.

Behavior:
- Finance pages: Laura, Finance Manager
- Sales pages: Alex, Sales Manager
- Marketing pages: Maya, Marketing Manager
- Support pages: Ben, Support Manager
- Operations pages: Nina, Operations Manager
- Global or unknown pages: Eva or Company Assistant

Card contents:
- avatar
- agent name
- role
- short helpful message
- primary button: `Message {AgentName}`

## Page header

Use a standard page header on major screens:
- small uppercase module label
- large title
- short subtitle
- optional top-right actions

Example:
- `DASHBOARD`
- `Executive cockpit`
- `See what your company is doing, what needs attention, and what to review next.`

## Secondary navigation

Use pill chips below the page header for module-level navigation.

Examples:

### Finance
- Overview
- Invoices
- Supplier bills
- Payments
- Activity
- Issues
- Settings
- optional: Cash & liquidity

### Agents
- All agents
- Finance
- Sales
- Marketing
- Support
- Operations
- Executive

### Workflows
- All workflows
- Running
- Waiting for approval
- Blocked
- Completed
- Templates

Chip states:
- active: blue text with blue border or light blue fill
- inactive: white/transparent with grey border and dark text

Chips must scroll horizontally on smaller screens.

## Page patterns

### Executive dashboard
Recommended structure:
- page header
- 4-up KPI row
- full-width daily briefing hero card
- two-column row:
  - left: agent insights and/or action queue
  - right: pending approvals and issues
- bottom row:
  - recent activity
  - department scorecards or workflow health

### List/detail operational pages
Use for:
- invoices
- supplier bills
- payments
- tasks
- approvals
- issues
- activity items

Layout:
- left list card: 60% to 65%
- right detail panel: 35% to 40%

Right panel supports:
- selected item details
- rationale summary
- timeline
- related actions
- empty state when nothing is selected

### Agent roster
Use a card grid, not a dense table.

Structure:
- page header
- filter chips or segmented controls
- search/filter bar in a white card
- responsive grid of agent cards

### Agent profile
Structure:
- top summary card
- tab/chip navigation
- two-column overview content
- lower sections for recent actions, rationale examples, connected tools

### Setup and settings
- onboarding: stepper or progress rail, one primary task per step
- settings: grouped cards, not one giant form

---

# Component behavior

## Cards
Default card style:
- white background
- 1px soft border
- 14px to 16px radius
- 16px to 24px padding
- subtle shadow
- consistent internal spacing

## KPI cards
Use 4-up on desktop where possible.

Include:
- icon in soft circle
- label
- large value
- support text or trend
- optional tone

Examples:
- Cash on hand
- Monthly spending
- Open approvals
- Active workflows

## Daily briefing card
This is a signature component.

Include:
- title: `Today’s briefing`
- timestamp
- short summary paragraph
- bullet updates grouped by agent
- primary CTA: `Review priorities`
- secondary CTA: `Open full briefing`

It should feel like a morning executive report.

## Agent insight cards
Show:
- avatar
- agent name
- role
- one-line update
- urgency badge if needed
- CTA such as `View details` or `Message`

## Action queue / needs attention
Use a prioritized list.

Each row should show:
- severity
- owner
- due timing or freshness
- CTA

Use friendly labels such as:
- Needs review
- Waiting for approval
- Overdue
- Ready to send

## Agent cards
For roster view, each card should show:
- avatar
- name
- role
- department
- autonomy level badge
- status badge
- workload summary
- one-line current focus
- KPI snippet if useful
- quick actions

Quick actions may include:
- Message
- Assign task
- View profile

## Agent profile summary card
Top card should include:
- avatar
- name
- role
- department
- status
- autonomy level
- workload summary
- permissions summary
- quick actions:
  - Message agent
  - Assign task
  - Adjust autonomy
  - Pause agent

## List rows
Use for tasks, approvals, invoices, issues, activity.

Structure:
- icon circle
- primary title
- secondary metadata line
- right-aligned status, amount, or date
- chevron

States:
- hover background
- selected row with blue-tinted background and stronger emphasis
- no raw enum labels

## Detail panel
For selected item detail, include sections such as:
- summary
- what this means
- timeline
- related records
- actions

For AI recommendations, include:
- recommendation
- why this was flagged
- data used
- approval status
- override options

## Approval detail behavior
Approval detail panels should clearly show:
- what the agent wants to do
- why approval is needed
- threshold or policy reason in plain English
- data used
- actions:
  - Approve
  - Reject
  - Ask for changes

## Timeline / audit event list
Each event row should show:
- actor avatar or icon
- actor name
- action summary
- target
- timestamp
- outcome badge
- expandable rationale summary

Auditability should be visible in the UI, not hidden.

## Filter bar
Always place filters inside a white card.

Possible controls:
- search
- status dropdown
- department dropdown
- date range
- assigned agent
- primary apply button
- secondary clear button

Avoid loose filters floating directly on the page.

## Forms
Use grouped cards with helper text.

For agent setup, break into sections like:
- Identity
- Role and department
- Objectives and KPIs
- Permissions
- Approval limits
- Escalation rules
- Autonomy level

Avoid long unstructured forms.

## Status badges
Use rounded pill badges with soft fills.

Friendly labels include:
- Active
- Paused
- Restricted
- Draft ready
- Waiting for approval
- Approved
- Completed
- Blocked
- Overdue
- All good

Map status tone appropriately:
- success
- warning
- danger
- info
- neutral

## Empty states
Empty states must be centered inside cards and include:
- icon
- title
- guidance text
- optional CTA

Examples:
- No approval selected  
  Select an approval request to see details.
- No recent issues  
  Your agents have not flagged anything urgent.
- No workflows running  
  Start a workflow or wait for a scheduled process.

## Navigation-to-action behavior
Important surfaces must lead directly to action:
- dashboard approval card → approval detail
- dashboard issue → issue detail
- agent insight → agent profile or related task
- suggested action → workflow/task screen

---

# Visual language

## Overall tone
The UI should feel:
- calm
- trustworthy
- operational
- proactive
- controlled
- human-readable

It should not feel:
- futuristic
- cyber
- noisy
- overly autonomous
- like a chatbot-first interface
- like a generic admin console

## Style rules
Use:
- light background
- white rounded cards
- soft borders
- subtle shadows
- blue primary accents
- restrained green/orange/red states
- generous whitespace
- minimal decoration
- clear hierarchy

Avoid:
- heavy gradients
- dark futuristic styling on normal business pages
- dense technical tables by default
- decorative effects that compete with content

## Color tokens
Use:
- app background: `#F7F9FC`
- card background: `#FFFFFF`
- primary: `#2563EB`
- primary hover: `#1D4ED8`
- border: `#E5E7EB`
- text primary: `#0F172A`
- text secondary: `#64748B`
- success: `#16A34A`
- warning: `#F59E0B`
- danger: `#DC2626`
- info: `#2563EB`

Soft fills