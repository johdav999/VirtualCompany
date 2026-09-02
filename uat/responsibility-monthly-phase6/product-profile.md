# Product profile: responsibility-aware Monthly workspace

## Product and user

- Product: Virtual Company Overview with a canonical Monthly operating-review period.
- Primary users: company owners reviewing the whole authorized company and managers reviewing an assigned responsibility lens.
- Primary job: understand monthly results, compare them with the prior month, choose next-month priorities, inspect feature-owned review sections, and follow decisions or agent outcomes into the existing Work and Approval surfaces.
- Critical boundary: the Monthly workspace is a separate read model from Today. Responsibility narrows relevance; API authorization, approval policy, and feature-owned data remain independent enforcement boundaries.

## Happy path

1. Open `/dashboard?companyId={companyId}&period=month&lens={lens}`.
2. Switch between Today and Monthly without losing company or lens context.
3. Move to the previous or next month and review timezone-aware current and comparison periods.
4. Read the deterministic management summary and the four highest-signal result cards.
5. Review ranked next-month priorities and feature-owned Finance, Sales, Marketing, Support, or Company-operation sections available to the selected lens.
6. Follow an authorized decision or agent outcome into the existing Approval or Work surface.

## Acceptance targets

- Owners see only authorized Company and feature contributors; managers see only their assigned responsibility lens.
- Current and comparison month boundaries use the company timezone and remain correct across daylight-saving and year transitions.
- Monthly and Today contracts and cache identities remain distinct.
- Unavailable source metrics are explicit and never replaced with inferred or invented values.
- Loading, empty, partial, unavailable/setup, unauthorized, and retry states are visible.
- Desktop presents result cards, priorities, feature review, decisions, and agent outcomes in a readable hierarchy; narrow layouts collapse cleanly with at least 44 px interactive targets.
- English and Swedish localization keys remain paired.

## Visual baseline

- Reference: `docs/design/references/responsibility-driven-monthly-workspace-reference.png`.
- Prompt: `docs/design/references/responsibility-driven-monthly-workspace-reference-prompt.md`.
- Generated before the production UI using the built-in image generation workflow.

## Verification environment

- Automated rendered-component and transport/API integration checks ran locally on 2026-09-02.
- Live authenticated browser inspection was unavailable because neither the configured Web port (`localhost:5062`) nor local SQL Server (`localhost:1433`) had a listener.

