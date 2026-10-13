# Blazor feature component refactor — 2026-10-10

## Delivered scope

The two largest route components identified for this refactor were Agent
management and Marketing. Their feature surfaces now have explicit component
and state boundaries.

| Route component | Before | After | Extracted responsibilities |
| --- | ---: | ---: | --- |
| `src/VirtualCompany.Web/Pages/Agents.razor` | 3,307 lines | 1,819 lines | Roster, brief editor, department analysis, operating-profile editor |
| `src/VirtualCompany.Web/Pages/Marketing/MarketingDashboard.razor` | 1,267 lines | 196 lines | All 15 section workspaces; 473-line partial class retains page orchestration |

The new components live under `Components/Agents` and `Components/Marketing`.
Feature state and form models live under `Features/Agents` and
`Features/Marketing`.

## Ownership and behavior

- Marketing's route owns company resolution, query context, typed clients,
  loading, command execution, and reloads. Child workspaces receive feature
  state and typed callbacks; they do not inject API clients.
- Marketing plan eligibility and audience choices are derived from the current
  form and loaded strategy/objective/segment evidence in `MarketingPlansState`.
  Editing a child form immediately updates the same state used by commands.
- Agent brief state owns category text, document filtering, draft/dirty flags,
  and editor feedback. The route retains upload polling, cancellation,
  persistence, and navigation protection.
- Agent operating-profile state owns the form and validation feedback. Field
  visibility and request construction continue to exclude restricted governance
  fields.
- Agent department-analysis state owns request/results and payment selection,
  including clearing review confirmation when selection changes. It uses the
  existing typed clients supplied by the route and retains recommendation-run
  and snapshot context for payment proposal commands.
- All state instances belong to their route instance; none are registered as
  shared singleton services. Existing routes, approval callbacks, and contextual
  links remain in place.
- Existing CSS declarations were preserved. Marketing's isolated selectors now
  use the route's `.marketing-page` scope with `::deep` to reach child markup.

Agent management still owns its access/provider configuration, hiring, legacy
directory controls, and page lifecycle. This change does not claim that every
large page in the solution has been decomposed.

## Verification

- Web and Web test project compilation: passed; existing unrelated compiler and
  test-analyzer warnings remain.
- Focused Marketing, Agent navigation, guided-work, mailbox surface, and feature
  component checks: **116 passed, 0 failed**.
- Full `VirtualCompany.Web.Tests` suite: **1,152 passed, 0 failed, 0 skipped**.
- Added **37** component cases: brief edits/category retention, private/shared
  document visibility, restricted profile fields, payment review reset,
  objective callbacks, plan eligibility, content deep links, all 15 empty feature
  workspaces, and all 15 Marketing route compositions with company context.
- Existing Marketing source-contract checks now inspect the complete feature
  source tree rather than assuming that all behavior is declared in the route
  file.
- Static CSS comparison confirmed unchanged declarations; generated isolated
  CSS confirmed that child selectors remain anchored to the route scope.
- `git diff --check`: passed.

Tests use isolated presentation state and HTTP fixtures. They do not establish
live provider behavior or browser visual acceptance. No local host or user-owned
process was restarted for this structural refactor.

Logs and before snapshots are retained locally in
`artifacts/test-matrix/blazor-feature-refactor/` (ignored build artifacts).
