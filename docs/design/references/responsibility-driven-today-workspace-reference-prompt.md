# Responsibility-Driven Today Workspace Reference Prompt

Use case: ui-mockup

Asset type: production visual reference for the Virtual Company responsive Blazor SaaS dashboard

Primary request: Create a high-fidelity reference screenshot for one reusable, responsibility-aware “Today” workspace. Show a large desktop viewport and a narrow mobile viewport side by side in the same reference board. The same stable page structure adapts to a company owner or functional manager through responsibility lenses; do not create separate dashboards.

Product context: Virtual Company is an executive control center where people supervise AI agents operating Finance, Sales, Marketing, and Customer Support. The canonical route is `/dashboard`. Existing left navigation contains Overview, Agent team, Finance, Sales, Support, Work, with History and Settings separated at the bottom. Detailed mutations remain in their existing feature pages.

Desktop composition: fixed 240px dark navy left application sidebar; a spacious light gray central workspace; a contextual right rail. At the top of the central workspace, show “Today” with a local date, company context, a compact freshness indicator, and an accessible segmented responsibility lens picker with Company selected and Finance, Sales, Marketing, and Customers available. Beneath it, show a concise situation summary. Then show four compact KPI cards in one row. Below, show three priority action cards with clear severity accents, concise “what happened” and “why it matters” copy, owner or working-agent context, freshness, and obvious action buttons. Follow with compact typed responsibility sections for Finance, Sales, Marketing, and Customer Support; each section contains a short health summary and one canonical drill-down action. The right rail contains a Decisions card with two approval items and an Agent briefings card with two distinct agent avatars, names, roles, status, update text, freshness, and actions.

Mobile composition: narrow phone-width version of the same page. Hide the fixed sidebar behind a compact app bar; keep Today, date, freshness, and a horizontally scrollable or wrapping lens selector; stack KPI cards two per row; stack priorities and responsibility sections; place Decisions and Agent briefings after main content. No clipped content, internal page scrollbar, hover-only controls, dense table, or tiny touch targets.

Visual style: polished modern B2B SaaS, Inter typography, executive clarity, restrained data density, generous whitespace, 12px card radii, subtle borders and shadows. Light background #F7F9FC, white cards, primary blue #2563EB, success green #16A34A, warning amber #F59E0B, danger red #DC2626, dark navy sidebar. Headings are semibold; body copy is plain English. Prioritize actions over charts and agents over raw metrics.

Exact prominent text: “Today”, “Company”, “Finance”, “Sales”, “Marketing”, “Customers”, “What needs your attention”, “Decisions”, “Agent briefings”, “Review”, “Open workspace”.

State guidance: show realistic populated content, plus one small honest partial-data notice on a responsibility section without obscuring valid content. Do not show a hard-coded persona named Laura. Use distinct neutral professional agent identities.

Constraints: accessible contrast, visible keyboard focus on the selected Company lens, minimum 44px touch targets on mobile, consistent alignment and spacing, no decorative illustration, no pie charts, no large passive graph, no duplicate information, no invented primary route, no watermark, no browser chrome.
