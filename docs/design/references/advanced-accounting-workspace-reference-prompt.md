# Advanced accounting workspace visual reference prompt

Use case: ui-mockup

Asset type: visual target for the Virtual Company Blazor advanced-accounting workspace introduced by `financial-app-p3-prompts.md` Prompt 9

Primary request: Create a polished accountant-grade SaaS workspace that makes multi-currency evidence, governed dimensions, schedules, fixed assets, period-end revaluation, approvals, reconciliation, and immutable journal drill-down discoverable from one coherent accounting destination. Follow `docs/design.md`; this is a visual reference only and must not be served as a product asset.

Scene/backdrop: The existing Virtual Company executive control-center shell with a 240px left navigation. Finance is selected. The main content sits on the light `#F7F9FC` application background with white cards and a compact Finance Manager insight card on the right edge.

Subject: Show the canonical page titled "Advanced accounting" with the description "Investigate controlled subledgers, rates, approvals and journal evidence from one workspace." Beneath the standard accounting navigation, show a horizontally scrollable secondary navigation with the exact destinations "Currency & rates", "Dimensions", "Schedules", "Fixed assets", and "Revaluation". Select "Currency & rates". Show four compact summary cards for functional currency, rate readiness, enabled currencies, and pending review. The main area is a two-column list/detail workspace: on the left, an authoritative exchange-rate source and currency list with status, priority, last refresh, next refresh, and a clear stale/error badge; on the right, a selected source detail with governance facts, safe next action, source timeline, lookup evidence, and a drill-down chain reading "Source document → Rate observation → Journal line → Report". Include a small period selector and direct cross-links to Revaluation, Reports & close, Journal, and Bank reconciliation. Include one amber stale-source recovery state with the backend-derived explanation "Approved rates are older than the allowed window" and the action "Request a controlled refresh". Show empty/loading/error-state affordances without exposing technical tokens.

Style/medium: High-fidelity modern enterprise SaaS UI mockup, calm and precise, information-dense but readable, matching the repository's existing accounting workspaces rather than a generic analytics dashboard.

Composition/framing: Wide 16:9 desktop screenshot at approximately 1440×1000. Fixed left sidebar, spacious central workspace, narrow right insight area. Use 12px rounded cards, 16px card padding, subtle borders and shadows, compact Inter-style typography, sticky readable tables, visible keyboard focus treatment, and grouping that can collapse to a single column on narrow viewports.

Lighting/mood: Bright light-mode interface, trustworthy, restrained, operational.

Color palette: Background `#F7F9FC`, white cards, primary `#2563EB`, success `#16A34A`, warning `#F59E0B`, danger `#DC2626`, dark slate text.

Text (verbatim): "Advanced accounting", "Currency & rates", "Dimensions", "Schedules", "Fixed assets", "Revaluation", "Rate readiness", "Pending review", "Approved rates are older than the allowed window", "Request a controlled refresh", "Source document → Rate observation → Journal line → Report", "Open revaluation", "View immutable journal".

Constraints: Preserve the existing Finance and Accounting information architecture. Keep daily investigation separate from provider administration. All rates, statuses, reasons, and allowed actions must look backend-derived. Include plain-English stale and recovery states, localized-ready labels, source/evidence timeline, approval status, reconciliation status, and journal/report cross-links. No raw GUIDs, enum tokens, provider payloads, database terminology, or UI-side calculations.

Avoid: decorative gradients, large charts, consumer-finance visuals, fake browser chrome, unlabeled icons, dark mode, watermarks, modal-heavy layout, technical admin tables, or controls that imply silent recalculation.

Generation mode: built-in image generation.
