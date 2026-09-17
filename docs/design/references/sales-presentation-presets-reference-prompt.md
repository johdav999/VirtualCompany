# Sales presentation presets reference prompt

Use case: ui-mockup

Asset type: high-fidelity desktop SaaS product reference screenshot for the Virtual Company Blazor web app

Primary request: Design the production Sales > Presentation presets library and selected-preset editor. The experience lets sales users create, search, filter, process, preview, publish, version, duplicate, and archive reusable PowerPoint presentation presets before a customer meeting exists.

Existing product context: Virtual Company is an executive control center where AI agents operate the company and users supervise decisions. Use the established shell: a fixed 240 px left navigation with Overview, Agent team, Finance, Sales, Support, and Work; Sales is active and Presentation presets is a secondary Sales destination. The main workspace uses Inter, a #F7F9FC background, white cards, #2563EB primary actions, subtle shadows, and 12 px radii. A narrow right rail provides a Sales Manager insight and recommended next action.

Layout structure: 16:10 desktop application screenshot. Main header with breadcrumb “Sales / Presentation presets”, title “Presentation presets”, concise helper text, and a “New preset” primary button. Below it, a search field and compact filter chips for All, Draft, Processing, Needs review, Published, Failed, and Archived. Use a dense but readable two-column composition: a left library list around 42% width and a right selected-preset detail around 58% width. Library rows show preset name, short description, owner avatar/name, version, status badge, slide count, last updated time, where-used count, and an authoritative primary action. Include Published, Processing, Needs review, and Failed examples without looking crowded.

Selected preset detail: show “Quarterly business review” with a Draft v4 badge and a secondary “Published v3” badge. Put identity fields and reusable defaults in a clear card, visually separate from operational processing status. Include labeled fields for name, description, owner, supported situations, presenter, language, duration, audience, goal, demo scenario, control mode, and behavior settings; never show JSON. Add a PowerPoint asset card with filename, progress/status, cancel or retry action, safe error messaging, and replace action. Add a horizontal slide preview strip with three polished slide thumbnails and keyboard-accessible previous/next controls. Add a readiness card listing plain-language blockers with direct corrective actions. Provide Save draft, Preview, and Publish buttons, plus a compact historical versions section. Show an archive action with a where-used warning in a restrained danger zone.

Responsive intent: the hierarchy must translate cleanly to mobile by stacking library and detail, keeping actions full-width or wrapping without clipping, while preserving the same information architecture.

Visual style: polished production B2B SaaS, calm Scandinavian clarity, generous but efficient spacing, crisp typography, restrained borders, accessible contrast, status colors used semantically, minimal decorative illustration. The screen must immediately answer what is happening and what the user should do.

Text (verbatim): “Presentation presets”, “New preset”, “Search presentations”, “All”, “Draft”, “Processing”, “Needs review”, “Published”, “Failed”, “Archived”, “Quarterly business review”, “Draft v4”, “Published v3”, “Reusable defaults”, “PowerPoint source”, “Slide preview”, “Ready to publish”, “Save draft”, “Preview”, “Publish”.

Constraints: no mock browser chrome, no competing app shell, no settings/admin framing, no raw JSON, no storage IDs, no customer-specific facts in slide previews, no passive chart grid. Treat this as a visual design reference rather than a bitmap intended for product use.

Avoid: excessive gradients, glassmorphism, neon colors, oversized cards, illegible tiny text, decorative charts, duplicated navigation, generic stock illustrations, watermarks.
