# Production source guidance

These instructions apply to production code under `/src`.

## Authoritative architecture and design sources

- For backend, data, workflow, agent, integration, approval, AI orchestration,
  authorization, audit, or cross-module changes, MUST read and follow
  `/docs/architecture-rules.md`.
- Use `/docs/architecture-overview.md` and `/.design-addin/architecture.md` only
  as background planning context. They must not override
  `/docs/architecture-rules.md` or the current repository implementation.
- Existing repository behavior and project ownership win when older planning
  material conflicts with the implemented system.
- For frontend UI, layout, styling, navigation, components, or user-facing text,
  MUST read and follow `/docs/design.md`. Treat it as the single design-system
  authority rather than copying its rules into this file.
- Database and EF Core changes use the database rules and completion checks in
  `/docs/architecture-rules.md`; do not maintain a second database rule set here.

## Polish and UAT workflow

For application polishing, UI/UX review, user-flow validation, user-acceptance
testing, screenshot-led iteration, or implementation of findings from hands-on
product review:

- MUST invoke and follow the installed `$polish-uat-loop` skill.
- Treat the repository architecture, design, security, approval, and
  external-side-effect rules as authoritative throughout the loop.
- Exercise the real user-facing flow when it can be run safely, capture evidence
  and a prioritized issue ledger, and re-run the original flow after each
  implemented fix.
- Do not mark a finding verified unless its acceptance criteria pass in the real
  surface or in the strongest explicitly documented safe substitute.
