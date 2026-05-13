# UI direction

Design the product as a calm, web-first executive control room for running a company through named AI agents. The interface should make the company feel alive and organized, not like a generic chatbot or admin console.

The core UI model should be:

- **Executive cockpit first**
- **Agent-led operations second**
- **Workflow and approvals always visible**
- **Auditability built into the interface**
- **Plain-English business language throughout**

Primary product surfaces to design around:

1. **Dashboard**
   - Daily briefing
   - KPI row
   - agent updates
   - pending approvals
   - issues needing attention
   - recent activity
   - suggested next actions

2. **Agents**
   - roster of AI employees
   - individual agent profile
   - current status, autonomy level, workload, permissions summary
   - direct message / assign task actions

3. **Department modules**
   - Finance
   - Sales
   - Marketing
   - Support
   - Operations
   - each with overview + list/detail operational pages

4. **Tasks / Workflows**
   - queue of assigned work
   - workflow progress
   - blocked items
   - escalations
   - approvals

5. **Inbox / Activity / Audit**
   - communication feed
   - operational event history
   - rationale summaries
   - data sources used
   - override actions

6. **Setup / Admin**
   - company setup
   - integrations
   - permissions
   - autonomy policies
   - agent configuration

The product should visually communicate three things at all times:

- what is happening now
- what needs attention
- what the user should do next

Per the required workflow, each major screen should first have a generated reference screenshot saved under `/docs/design/references/`, then be implemented to match that visual target.

Suggested first screenshot prompts to generate:

- `dashboard-overview-reference.png`
- `agents-roster-reference.png`
- `agent-profile-reference.png`
- `finance-overview-reference.png`
- `approvals-queue-reference.png`
- `workflow-detail-reference.png`
- `activity-audit-reference.png`
- `company-setup-reference.png`

Example prompt structure for a dashboard reference:

> Create a clean modern SaaS dashboard screenshot for a Virtual Company App executive cockpit. Use a light background, white rounded cards, soft borders, subtle shadows, blue primary accents, and generous spacing. Include a fixed left sidebar with navigation for Home, Onboarding, Dashboard, Finance, Simulation Lab, System/Admin, Agents, Workflows, Inbox, Activity, Approvals, Audit, Briefing delivery. In the main content area show a page header with small uppercase module label, large title, and short subtitle. Add a KPI row, a daily briefing card, an agent insights panel with named agents like Laura and Maya, a pending approvals card, an issues needing attention card, and a recent activity panel. Use plain-English labels, calm executive control center styling, and clear visual hierarchy. Max content width around 1240px.

# Audience and tone

Primary audience:

- founders
- owner-operators
- small business managers
- startup operators
- lean leadership teams

These users are busy, cross-functional, and not looking for technical complexity. The UI should feel:

- calm
- trustworthy
- operational
- proactive
- human-readable
- controlled, not autonomous in a scary way

Tone rules for the interface:

- use plain English
- describe outcomes, not system mechanics
- emphasize responsibility and review
- avoid internal AI or platform jargon

Prefer:

- Needs review
- Waiting for approval
- Laura recommends
- Cash looks healthy
- 3 issues need attention
- Draft ready to send

Avoid:

- orchestration event
- trigger source
- anomaly identifier
- tenant-scoped
- execution policy object
- enum-like labels

The named-agent model should be visible in the UI. Users should feel they are supervising a team:

- Laura owns finance
- Alex owns sales
- Maya owns marketing
- Ben owns support
- Nina owns operations
- Eva coordinates executive work

This means agent identity should appear in:

- dashboard insight cards
- task ownership
- approval requests
- activity feed
- side contextual card
- profile pages
- chat entry points

# Layout system

Use the design system’s standard desktop shell:

- **fixed left sidebar**
- **main content area**
- **light app background**
- **max content width 1200–1280px**
- **page padding 24–32px**

## App shell

### Sidebar
- width: about 240px
- white background
- thin right border
- vertically scrollable
- logo/company switcher at top
- primary nav in middle
- settings/admin near bottom
- contextual agent card near bottom above settings

### Main content
- background: `#F7F9FC`
- content aligned consistently
- sections separated by 24px to 32px
- cards arranged in a 12-column grid where needed

## Page patterns

### 1. Executive dashboard
Recommended structure:

- page header
- KPI row: 4 cards
- daily briefing / executive summary: full-width hero card
- two-column row:
  - left: agent insights / action queue
  - right: pending approvals / issues
- bottom row:
  - recent activity
  - department scorecards or workflow health

### 2. List/detail operational pages
Use for:

- invoices
- supplier bills
- payments
- tasks
- approvals
- issues
- activity items

Layout:

- left list card: 60–65%
- right detail panel: 35–40%

The right panel should support:
- selected item details
- rationale summary
- timeline
- related actions
- empty state when nothing selected

### 3. Agent roster
Use a card grid, not a dense table.

Structure:
- page header
- filter chips or segmented controls by department/status
- search/filter bar in a white card
- responsive grid of agent cards

Each agent card should show:
- avatar
- name
- role
- department
- status
- autonomy level
- workload summary
- one-line current focus
- quick actions

### 4. Agent profile
Recommended layout:

- top summary card with avatar, role, status, autonomy, quick actions
- tab/chip navigation:
  - Overview
  - Tasks
  - Permissions
  - Memory
  - Activity
  - Settings
- overview content in two columns:
  - left: objectives, KPIs, current work
  - right: permissions, thresholds, escalation rules
- lower sections:
  - recent actions
  - rationale examples
  - connected tools

### 5. Setup and settings
Use step-based cards for onboarding and grouped settings cards for admin.

For onboarding:
- left progress rail or top stepper
- one primary task per step
- clear next action
- optional template selection cards

For settings:
- avoid giant forms
- group into cards:
  - Company profile
  - Branding
  - Region and currency
  - Integrations
  - Permissions
  - AI policies

# Navigation and workflows

## Primary navigation

Use the required sidebar items:

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

Active item:
- blue background
- white icon and text
- rounded rectangle

Inactive item:
- muted text
- line icon
- hover background tint

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

## Core workflow patterns

### Dashboard to action
The dashboard should always let the user move directly into action:
- click approval card → approval detail
- click agent insight → agent profile or related task
- click issue → issue detail
- click suggested action → workflow/task screen

### Agent interaction
From any agent surface, support:
- Message agent
- Assign task
- View activity
- Adjust autonomy
- Pause agent

### Approval flow
Approval items should open into a detail panel with:
- what the agent wants to do
- why it needs approval
- threshold or policy reason
- data used
- approve / reject / ask for changes

### Audit flow
Every important action should be traceable from:
- dashboard issue
- task detail
- approval detail
- activity feed
to:
- rationale summary
- data sources used
- related workflow
- final outcome

# Components

## 1. Page header
Standard structure:
- small uppercase module label
- large title
- short subtitle
- optional top-right actions

Example:
- `DASHBOARD`
- `Executive cockpit`
- `See what your company is doing, what needs attention, and what to review next.`

## 2. Contextual agent card
Place near bottom of sidebar.

Contents:
- avatar
- agent name
- role
- short message
- primary button

Example:
- Laura
- Finance Manager
- I'm here to watch your finances and let you know what needs attention.
- `Message Laura`

This card should change by module context.

## 3. KPI cards
Use 4-up on desktop.

Contents:
- icon in soft circle
- label
- large value
- support text or trend
- optional status tone

Examples:
- Cash on hand
- Monthly spending
- Open approvals
- Active workflows

## 4. Daily briefing card
A signature component for the product.

Structure:
- title: Today’s briefing
- timestamp
- short summary paragraph
- bullet updates grouped by agent
- primary CTA: Review priorities
- secondary CTA: Open full briefing

This should feel like the product’s “morning report.”

## 5. Agent insight cards
Compact cards or stacked list items showing:
- avatar
- agent name and role
- one-line update
- urgency badge if needed
- CTA such as View details or Message

## 6. Action queue / needs attention card
A prioritized list of actionable items:
- title
- count badge
- rows with severity, owner, due timing, CTA

Use plain labels:
- Needs review
- Waiting for approval
- Overdue
- Ready to send

## 7. List rows
For tasks, approvals, invoices, issues, activity.

Structure:
- icon circle
- primary title
- secondary metadata line
- right-aligned status/amount/date
- chevron

Selected row:
- blue-tinted background
- stronger border or inset ring

## 8. Detail panel
For selected item detail.

Sections:
- summary
- what this means
- timeline
- related records
- actions

For AI-generated recommendations, include:
- recommendation
- why this was flagged
- data used
- approval status
- override options

## 9. Status badges
Rounded pills with soft backgrounds.

Friendly labels:
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

## 10. Filter bar
Always inside a white card.

Contents may include:
- search
- status dropdown
- department dropdown
- date range
- assigned agent
- primary apply button
- secondary clear button

## 11. Agent cards
For roster view.

Contents:
- avatar
- name
- role
- department
- autonomy level badge
- status badge
- current focus
- KPI snippet
- quick actions

## 12. Timeline / audit event list
For activity and audit pages.

Each event row should show:
- actor avatar or icon
- actor name
- action summary
- target
- timestamp
- outcome badge
- expandable rationale summary

## 13. Empty states
Use centered icon + title + guidance text + optional CTA.

Examples:
- No approval selected  
  Select an approval request to see details.
- No recent issues  
  Your agents have not flagged anything urgent.
- No workflows running  
  Start a workflow or wait for a scheduled process.

## 14. Forms
Use grouped cards with clear labels and helper text.

For agent setup, break into sections:
- Identity
- Role and department
- Objectives and KPIs
- Permissions
- Approval limits
- Escalation rules
- Autonomy level

Avoid long unstructured forms.

# Visual style

Follow the provided design system closely.

## Core look
- light background
- white rounded cards
- soft borders
- subtle shadows
- blue primary accents
- restrained use of green, orange, red
- generous whitespace
- minimal decoration

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

Soft fills:
- success soft: `#DCFCE7`
- warning soft: `#FEF3C7`
- danger soft: `#FEE2E2`
- info soft: `#DBEAFE`
- neutral soft: `#F1F5F9`

## Typography
Use Inter or equivalent.

Scale:
- page title: 28–32px / 600
- section title: 16–18px / 600
- card title: 14–16px / 600
- body: 14px
- support text: 13–14px
- values: 22–30px / 600