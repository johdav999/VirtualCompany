# Prompt 9 advanced accounting workspace UAT evidence

## Scope

Prompt 9 adds the focused Currency/Rates, Dimensions, Schedules, Fixed Assets,
and Revaluation workspaces. The verification target is the generated desktop
reference in `docs/design/references/advanced-accounting-workspace-reference.png`
plus authenticated English and Swedish desktop and narrow browser flows.

## Environment

- Date: 2026-08-30
- API launcher: `server-local-sql.ps1`
- Web launcher: `client.ps1 -Port 5062`
- Desktop target: 1440 x 1000
- Narrow target: 390 x 844
- Reference prompt: `docs/design/references/advanced-accounting-workspace-reference-prompt.md`

## Automated evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Advanced workspace surface, routing, recovery, accessibility, cross-links, localization, and stored reference | Pass | Prompt 9 Web surface plus reporting/localization tests: 9 passed |
| Advanced accounting authorization and API surfaces | Pass | Dimensions, schedules, fixed assets, and revaluation tests: 23 passed |
| Advanced accounting domain and golden scenarios | Pass | Rates, dimensions, schedules, assets, revaluation, and end-to-end ledger drill-down tests: 31 passed |
| EF model/migration alignment | Pass | `has-pending-model-changes`: no changes; corrected dimensions migration reached and completed before subsequent migrations ran |
| Repository build | Pass | `dotnet build VirtualCompany.sln --no-restore`: 0 errors |

## Browser flow ledger

| Flow | Target | Result | Evidence or blocker |
| --- | --- | --- | --- |
| FLOW-P9-001 | Authenticated English desktop Currency/Rates investigation and evidence cross-links | Blocked | The in-app browser reached the desktop route and rendered the Finance access-recovery shell. Authenticated workspace data could not load because the API startup continued past all Prompt 9 migrations and then failed in the later `AddAccountingCloseGovernance` migration on a duplicate `finance_accounts.is_reportable` column. That later migration is outside Prompt 9. |
| FLOW-P9-002 | Authenticated English narrow keyboard-only review flow | Blocked | Depends on an authenticated API host. Static coverage confirms canonical links, keyboard-selectable rows including dimension members, retry actions, live regions, labels, and 1100/640 responsive breakpoints. |
| FLOW-P9-003 | Authenticated Swedish desktop and narrow state review | Blocked | Depends on an authenticated API host. English and Swedish resource parity is covered by localization quality gates and the Prompt 9 localization surface test. |

## Residual verification

The implementation is build-, focused-test-, and EF-model-clean. The browser can
reach and render the local Web host, and the Prompt 9 SQL Server migration now
applies successfully. Authenticated visual comparison, interaction, narrow-flow,
and real screen-reader behavior remain unverified because a later accounting-close
migration prevents the API from starting. Rerun these three browser flows after
that later migration is repaired before treating browser UAT as complete.
