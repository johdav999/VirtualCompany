# Year-end rollover workspace reference prompt

Use case: ui-mockup

Asset type: desktop SaaS application reference screenshot for the Virtual Company finance workspace.

Primary request: Design a polished year-end rollover and subsequent-event control workspace for finance administrators. It must make the governed sequence obvious: readiness, retained-earnings proposal, independent approval, execution, opening-balance reconciliation, and completion. The screen must never imply that an incomplete or unapproved year can be finalized.

Existing product context: Virtual Company is a web-first multi-tenant SaaS modular monolith. The UI feels like an executive control center where AI agents operate the company and people supervise evidence-backed decisions. Existing navigation has a fixed 240px left sidebar with Overview, Agent team, Finance, Sales, Support, Work, History, and Settings. This screen is within Finance > Accounting.

Scene/backdrop: 1440×1000 desktop application viewport, light mode, #F7F9FC background, white cards, Inter typography, subtle shadows, restrained blue #2563EB primary accents, green success, amber warning, red danger.

Layout structure:

- Fixed left navigation sidebar with Finance selected.
- Main header containing “Year-end rollover”, company context “Nordic Studio AB”, fiscal year selector “FY 2026”, status chip “Awaiting independent approval”, and a compact refresh action.
- Four KPI cards: closed periods 12/12, blockers 0, proposed transfer SEK 1,284,600, opening balance difference SEK 0.
- A horizontal six-step lifecycle/timeline: Readiness complete, Proposal prepared, Approval pending, Execute locked, Reconcile waiting, Finalize waiting.
- Two-column center workspace. Left wide column has a readiness evidence card with grouped rows for period close, subledgers, reports and tax/compliance, audit package, and required sign-offs. Each row shows status, evidence timestamp, and a safe drill-down action.
- Right column has a “Retained earnings proposal” card showing source result, retained earnings account 2099, target fiscal period Jan 2027, exact evidence hash, preparer, reviewer, and buttons “Submit for approval” and secondary “Review journal preview”. Make the primary action clearly conditional on readiness.
- Below, a dense reconciliation table titled “Opening balances by account” with columns account, closing 2026, opening 2027, currency/dimension, difference, and status. Include a zero-difference success state plus one expandable example row.
- Right-side lower card titled “Subsequent events” showing one disclosed event with decision “Post forward”, linked evidence, owner, and a button “Record subsequent event”. Include explanatory text that prior journals and snapshots remain unchanged.
- Bottom immutable history strip showing prepared, reviewed, executed, and verified events with actors and timestamps; later steps appear muted while pending.
- Include a compact blocked-state callout explaining that execution rechecks authoritative close evidence and commits neither journal if posting fails.

Visual hierarchy: clear title and current state first, then lifecycle, then evidence and decision, then reconciliation/history. Dense but readable tables with sticky-style headers. 12px card radius, 16–24px spacing, concise plain English, accessible contrast, visible focus-ready controls.

Text (verbatim): “Year-end rollover”, “Awaiting independent approval”, “Retained earnings proposal”, “Opening balances by account”, “Subsequent events”, “Prior-year accounting remains unchanged”, “Submit for approval”, “Review journal preview”.

Constraints: show a serious financial control workflow rather than a generic dashboard; company and fiscal-year context must remain explicit; actions must look backend-governed; do not show charts; do not imply automatic legal approval; no logos, no gradients, no glassmorphism, no decorative illustration, no watermark.

Avoid: excessive empty space, oversized headings, ambiguous icon-only actions, editable accounting totals without provenance, green completion before reconciliation, dark mode.
