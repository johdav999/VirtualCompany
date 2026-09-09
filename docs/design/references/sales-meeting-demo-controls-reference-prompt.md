# Sales meeting demo controls reference prompt

Use case: ui-mockup

Asset type: production-ready desktop SaaS private meeting-side-panel reference

Primary request: Extend Virtual Company's existing "Alex · Sales sidekick" private meeting panel with a controlled product-demo workflow. The salesperson must be able to see the exact synthetic demo tenant and approved scenario version, preview a destructive reset, confirm and run the reset, start the demo, execute only the next allowlisted typed step, and see deterministic status and validation results. The customer-visible application remains a separate real application surface; this panel is private operator control only.

Existing product context: Preserve the established dark sales-meeting side-panel language: Alex avatar and sync state, meeting status, current presentation context, restrained cards, and a sticky action area. Virtual Company is an executive AI-agent control center with a fixed navigation shell elsewhere; this artifact shows only the private right-side panel.

Style/medium: realistic shippable SaaS product UI, not concept art; Inter-like sans serif; dark mode using #0F172A and #1E293B cards, #2563EB primary, #16A34A success, #F59E0B warning, #DC2626 danger; subtle borders and shadows; 12px card radius; dense but readable.

Composition/framing: portrait desktop side panel about 430px wide. Header with Alex identity, green synced indicator, and an unmistakable blue "DEMO · SYNTHETIC DATA" badge. Below it, a compact scenario card showing "Northstar sales walkthrough", "Version 1", the exact demo tenant "Northstar Demo Company", linked-meeting state, and "External integrations blocked". Then an ordered three-step list: "Qualify lead", "Convert to deal", "Move deal to proposal", with completed/current/locked states. Include status text such as "Ready · Step 1 of 3" and a small validation summary with checked invariants.

Reset interaction: Show a yellow-edged reset preview card, not an executed reset. It identifies the exact tenant and scenario version, lists affected records as "1 customer · 1 contact · 1 lead · 1 deal (if present) · demo activities", states "Audit history is preserved", and shows external integrations "Email, Teams, calendar, payments, accounting providers: blocked". Include a required confirmation checkbox labelled "Reset Northstar Demo Company only" and a restrained danger button labelled "Reset demo". Never present a broad reset or wildcard control.

Primary controls: A blue "Start demo" button when ready, a blue "Run next step" button for the current allowlisted typed command, a secondary "Preview reset" button, and compact status/error space. Controls clearly show disabled/loading states. The command area identifies "Typed command · no cursor automation".

Responsive behavior: On a narrower mobile-width panel, cards stack in the same order, buttons become full width, and the exact tenant/scenario identity remains above the destructive reset control.

Text (verbatim): "Alex · Sales sidekick", "DEMO · SYNTHETIC DATA", "Northstar sales walkthrough", "Version 1", "Northstar Demo Company", "External integrations blocked", "Ready · Step 1 of 3", "Qualify lead", "Convert to deal", "Move deal to proposal", "Typed command · no cursor automation", "Preview reset", "Reset Northstar Demo Company only", "Reset demo", "Start demo", "Run next step", "Audit history is preserved".

Constraints: The screen must answer what is happening and what the salesperson should do next. Make destructive scope explicit. Show real workflow state rather than fabricated analytics. Use clear hierarchy, generous spacing, accessible contrast, obvious focus states, and plain English. No provider logos, no credentials, no personal data, no mouse cursor, no coordinate controls, no arbitrary command input, no arbitrary HTTP/SQL/code controls, no watermark.

