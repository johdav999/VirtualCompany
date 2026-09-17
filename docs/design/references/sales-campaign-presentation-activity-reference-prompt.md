# Sales campaign presentation activity reference prompt

Use case: ui-mockup

Asset type: high-fidelity desktop SaaS product reference screenshot for the Virtual Company Blazor web app

Primary request: Design the production Sales > Campaigns planning workspace after adding presentation presets as first-class campaign activities. A campaign planner must select an exact published preset version, choose per-contact, per-account, or campaign-event execution, resolve a presenter, configure preparation timing and task/meeting/handoff behavior, see projected run counts and audience grouping, understand authoritative readiness blockers, and monitor prepared runs without triggering a live customer presentation.

Existing product context: Virtual Company is an executive control center where AI agents operate the company and users supervise decisions. Preserve the established shell: fixed 240 px left navigation with Overview, Agent team, Finance, Sales, Support, and Work; Sales is active. The Sales header has tabs for Overview, Prospects, Pipeline, Campaigns, and Presentation presets. The existing campaign page has a campaign list, selected campaign detail, readiness summary, and tabs including Overview, Audience, Activity plan, Performance, and Agent. Use Inter, a #F7F9FC background, white cards, #2563EB primary actions, subtle shadows, restrained borders, and 12 px radii.

Layout structure: 16:10 desktop application screenshot focused on a selected campaign’s “Activity plan” tab. Keep a compact campaign header with lifecycle status, audience count, activity count, and authoritative campaign readiness. In the main wide column, show an activity timeline/table with Email and Presentation rows. Expand the selected Presentation activity into a structured editor card. Include a searchable published-preset selector with “Quarterly business review — Published v3”, a small three-slide preview strip, execution-scope segmented controls for “Per contact”, “Per account”, and “Campaign event”, presenter strategy and optional presenter, preparation lead time, dependency, and task/meeting/handoff strategy. Clearly label the preset version as pinned and immutable.

Planning evidence: beside or below the editor, show a “Run projection” card with 18 eligible contacts grouped into 7 accounts and the projected result “7 presentation runs”. Include a concise explanation that duplicate delivery will not duplicate runs. Add an authoritative “Readiness” card with one actionable blocker such as a missing presenter for two accounts, evidence text, a corrective action, and review/approval requirements. Use a calm warning state, not an alarming error wall.

Execution status: below the plan, show compact aggregate progress for a scheduled presentation activity: 5 prepared, 1 needs attention, 1 pending; include individual prepared-run rows with subject/account, presenter, preparation state, and a “View run” link. Visually distinguish campaign activity lifecycle from individual presentation-run state. Make “Save presentation activity” the primary action and “Remove activity” a restrained destructive action. Include “Retry failed subjects” only where failure state is visible. Do not include controls to start presenting, admit participants, send invitations, or send email.

Right rail: provide a narrow Sales Manager insight stating that account scope reduces 18 contacts to 7 governed preparation runs, and recommend resolving the remaining presenter blocker before requesting campaign approval.

Responsive intent: at mobile width, campaign navigation remains usable, the activity table becomes stacked cards, the editor fields and segmented scope controls wrap without clipping, run projection precedes readiness, and primary actions become full-width.

Visual style: polished production B2B SaaS with calm Scandinavian clarity, dense but readable operational information, strong hierarchy, accessible contrast, semantic status colors, visible focus treatment, and minimal decoration.

Text (verbatim): “Activity plan”, “Presentation”, “Quarterly business review — Published v3”, “Pinned version”, “Per contact”, “Per account”, “Campaign event”, “Run projection”, “18 eligible contacts”, “7 accounts”, “7 presentation runs”, “Readiness”, “Presenter missing for 2 accounts”, “Save presentation activity”, “Retry failed subjects”, “View run”.

Constraints: no mock browser chrome, no competing shell, no settings/admin framing, no raw JSON, no storage IDs, no provider internals, no client-only policy decisions, no automatic live presentation, no invitation or email-send action, no decorative chart grid. Treat this as a visual design reference rather than a bitmap intended for product use.

Avoid: gradients, glassmorphism, neon colors, oversized cards, tiny illegible text, decorative analytics, duplicated navigation, generic stock illustrations, logos, trademarks, and watermarks.
