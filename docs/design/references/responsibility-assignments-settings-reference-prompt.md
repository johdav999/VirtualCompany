# Responsibility assignments settings reference prompt

Use case: ui-mockup

Asset type: production-quality SaaS settings screen reference, shown as a large desktop layout with a narrow mobile companion on the right

Primary request: Design the Virtual Company “Responsibilities” settings screen where a company owner assigns accountable people, working AI agents, authority, approval policy, escalation, and executive oversight. The screen must answer who owns each responsibility and what the owner should configure next.

Existing product context: Virtual Company is an executive control center where AI agents operate a company and humans supervise decisions. Preserve the established dark navy 240px left navigation, white/light-gray workspace, blue primary actions, rounded white cards, subtle borders and shadows, Inter-style typography, and plain-English labels used by the existing Settings and Today screens. Settings is a secondary destination, not a daily operations dashboard.

Scene/backdrop: Light `#F7F9FC` application canvas with the existing dark navy product sidebar on desktop. Use white cards, `#2563EB` primary blue, restrained green success, amber warning, and red error accents.

Desktop layout:

- Header with breadcrumb “Settings / Responsibilities”, title “Responsibilities”, concise explanation that responsibility relevance does not grant access or expand agent tools, and a “View Today” secondary link.
- A compact setup card across the top with company-size segmented choices “Micro”, “Small”, and “Medium”; “Micro” selected; a “Preview recommended setup” action.
- Main content uses a readable two-column workspace. The wider left column contains six stacked responsibility rows/cards: “Cash & accounting”, “Compliance”, “Sales”, “Marketing”, “Customer support”, and “Company performance”. Each row shows a one-line purpose, responsible active member, assigned active AI agent with small avatar, authority badge, approval summary, escalation person, and executive-oversight indicator. Use clear warning states for “Unassigned”, “No compatible agent”, and an inactive previous assignee. Avoid a wide conventional data table.
- The narrower right column contains an edit card for the selected “Sales” responsibility. Show labeled selects for responsible person, working agent, authority, approval policy, and escalation person; an executive-oversight readout; field-level validation space; primary “Save assignment” and secondary “Remove assignment” actions. Include helper copy that selecting an agent does not change its permissions or tools.
- Below the edit card, show a “Recommended setup preview” card summarizing exact changes with small badges: “Add”, “Retain”, and “Replace”. Default mode is “Fill missing”. Include a separate “Replace existing” option with an unchecked explicit confirmation control before the primary “Apply setup” action.
- Include a success confirmation area linking to Today.

Mobile layout: Show the same screen in a narrow phone-sized companion. Hide the sidebar behind a header menu. Stack company-size selection, responsibility cards, selected edit form, and preset preview vertically. Each responsibility card must keep its purpose, owner, agent, authority, and status readable without horizontal scrolling. Buttons are full-width or comfortably touch-sized, and no content clips.

Style/medium: realistic shippable product UI mockup, not concept art; executive SaaS control center; dense but calm; strong information hierarchy; restrained decoration.

Composition/framing: 16:9 landscape reference with desktop taking roughly 78% of the width and mobile companion roughly 22%. Show the full desktop page and a representative mobile scroll segment in one image.

Text (verbatim where visible): “Responsibilities”, “Settings / Responsibilities”, “Responsibility determines what appears in Today. Access and agent tools remain governed separately.”, “Company size”, “Micro”, “Small”, “Medium”, “Preview recommended setup”, “Cash & accounting”, “Compliance”, “Sales”, “Marketing”, “Customer support”, “Company performance”, “Responsible person”, “Working agent”, “Authority”, “Approval policy”, “Escalation”, “Executive oversight”, “Fill missing”, “Replace existing”, “Save assignment”, “Apply setup”, “View Today”.

Constraints: Use practical forms and real product structure; no charts; no arbitrary dashboard customization; no decorative activity feed; no horizontal mobile table; no logos other than a simple Virtual Company product mark; no trademarks; no watermark. Keep text concise and legible. If generated details conflict with `docs/design.md`, the design system wins.
