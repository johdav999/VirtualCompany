# Manual journal reference prompts

These prompts were written before implementation and generated with the built-in OpenAI image model. The screenshots are design references only and are not shipped as UI assets.

## Journal list and detail

Use case: `ui-mockup`

Create a shippable Virtual Company Finance screen titled “Journals” for a micro-company using the native internal ledger. Use the existing calm SaaS workspace: light `#F7F9FC` background, white 16px-radius cards with soft borders and subtle shadows, Inter typography, blue `#2563EB` actions, restrained green/amber/red status colors, the fixed 240px app sidebar, Finance secondary navigation, Accounting subnavigation, and a compact Laura Finance Manager guidance card. Use a 62/38 list-detail operational layout. The left side contains a white filter card with search, status, voucher series, source, and posting-date controls followed by a dense accessible journal list showing voucher number, posting date, explanation, source, debit total, and friendly status or correction badges. The selected detail panel shows balanced debit and credit totals, immutable journal lines, source and evidence links, approval, posted-by actor, voucher identity, correction chain, and audit timeline, with actions “Create correction” and “Create adjusting entry”. Include loading, empty, blocked-period, unauthorized, and recoverable error affordances. Small viewports collapse to filters, list, then detail. Do not show Edit or Delete for posted journals, provider or Fortnox concepts, raw enum values, tenant identifiers, storage keys, fake charts, or futuristic styling.

Generated reference: `accounting-journals-reference.png`.

## Manual journal workbench

Use case: `ui-mockup`

Create a shippable Virtual Company Finance page titled “New manual journal”. Reuse the same app shell, Finance and Accounting navigation, calm colors, card language, spacing, typography, and Laura guidance. Build an action-oriented workbench with a compact header status badge “Draft”, a grouped journal-details card for posting date, document date, accounting period, voucher series, currency, explanation, and evidence links; then an accessible editable line grid with account, description, debit, credit, tax code, and dimensions columns, row add/remove controls, and clear keyboard focus. Keep a persistent balance card visible beside or below the grid showing Debit, Credit, Difference, and a prominent balanced/unbalanced state. Include a server-generated preview panel, version and last-saved information, plain-English policy warnings for evidence, restricted accounts, approval thresholds, stale edits, and locked periods, plus actions “Save draft”, “Preview”, “Submit for approval”, and “Post approved journal” with only currently valid actions enabled. Show awaiting approval and recoverable conflict affordances without exposing technical identifiers. Small viewports turn the grid into stacked editable line cards and keep totals visible. Do not show a client approval checkbox, autonomous-agent bypass, mock production data, provider language, raw enums, or futuristic styling.

Generated reference: `manual-journal-workbench-reference.png`.
