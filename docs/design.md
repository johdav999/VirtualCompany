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