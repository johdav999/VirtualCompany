# Company cards and Agent team Kanban UAT — 2026-10-03

Later correction: [Kanban paging evidence](../kanban-paging-2026-10-03/README.md) supersedes this packet's global board paging limitation. The original nine-row Finance replay verified grouping, but did not catch completed history hiding older active work on the unfiltered first page.

Implemented and verified within the local scope below. P01–P10 work remains in this same uncommitted checkout. This follow-up replaces the P03 coverage-only department cards and P10 flat outcome grid. Historical phase packets remain historical; these captures describe the current UI.

## Product profile

Virtual Company, Blazor Server / .NET 9, Windows PowerShell. Baseline HEAD `37834c7f75d4c1ddd532e1a48464887724a84978` plus uncommitted phased work. Original review: local VC company `43e6a825-d1b7-429a-8608-7e668087d005`, Alice Admin, Web 5062. `before.jpg` and `before.dom.txt` record the Company baseline; the user's Agent team screenshot supplied the flat-grid baseline.

Original Web 5062 and API 5301 were no longer listening during this run; they were not replaced with fixture data. Final browser verification uses the existing `tests/VirtualCompany.Workspace.Uat` composed API, disposable SQLite, workers disabled, synthetic P01 Owner / North Company. API 5319; Web 5079 with environment-only API URL and existing P01 development identity. No production configuration file was changed.

Build Web: `dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --artifacts-path .codex-build/uat-cards-kanban --no-restore --disable-build-servers -p:UseAppHost=false`. Adapter build uses the same artifacts directory. Browser: temporary background Codex in-app browser tab; default 1280×720, original review size 1633×1244, mobile 390×844. Actual captures and illustrative generated references are separate.

## Issue ledger

| ID | Severity / type | Flow / observed problem | Acceptance / regression | Result |
| --- | --- | --- | --- | --- |
| UAT-CARDS-01 | P2 enhancement | Company cards show observation without business indicators | Four authorized cards show source counts, currency-labelled pipeline, highest-ranked recorded department action, owner/date and availability. Null Finance counts remain unavailable. Restricted/unavailable cards hide retained indicators/actions. Review returns to Company Today. CompanyHealthTests guard these boundaries. | Verified |
| UAT-KANBAN-01 | P2 usability defect | Agent team mixes lifecycle states in a repeated grid | Seven labelled lanes; one card per retained outcome in its source state; Completed separate. Ownership, collaborators, dependency, update and Open work retained. Filters, pagination and filtered-board return preserved. Total/page counts distinguished. AgentWorkJourneyTests guard grouping and navigation. | Verified |
| UAT-RESPONSIVE-01 | P2 regression boundary | Revised summaries must remain readable on mobile / keyboard | Cards and lanes stack at 390×844 without document overflow; visible link focus; desktop board scrolls by keyboard and later lanes are reachable. | Verified |

## Repeatable flows and evidence

1. **F-CARDS** — Company Today, owner, all four authorized departments. Expected indicators match the owning source. Observed: Finance overdue invoices / supplier bills due / reconciliation exceptions `1/1/1`; Sales pipeline / attention / hot leads `SEK 12,000.00 / 1 / 0`; Marketing launch / content / spend / attribution `1/1/2/2`; Support open / breached / at risk / waiting / approval `5/3/1/1/0`. Weighted forecast stays separately `SEK 5,400.00`. Pipeline is the existing selected-currency snapshot; currency-separated Sales reports retain the full portfolio. No currency conversion or new health score. Artifacts: `cards-desktop.jpg`, `cards-desktop.dom.txt`, `today-source.json`.
2. **F-CARD-REVIEW** — Finance card Review → existing priority evidence → Back to Today. Expected correct company/source/action and origin. Observed P06 invoice evidence, remaining 800 SEK, dated/stale source warning and Company lens return. No approval/execution. Artifact: `cards-finance-review.dom.txt`. Null/missing/revoked/unavailable source cases use focused renderer tests as the strongest safe substitute; no live permission was changed.
3. **F-KANBAN** — Agent team → Responsibility Finance → Apply. Expected outcomes in retained states and matching totals. Observed nine outcomes: `1 planned / 2 in progress / 1 awaiting approval / 2 blocked / 1 failed / 1 paused / 1 completed`, matching `kanban-source.json`. Shared outcome appears once with both agents. Unfiltered page shows 23 Planned and one In progress row, distinguishing page rows from totals and identifying work on another page. Artifacts: `kanban-finance-desktop.dom.txt`, `kanban-desktop.jpg`, `kanban-finance-desktop.jpg`.
4. **F-KANBAN-RETURN** — View In progress → Open work → Back to filtered board. Expected state choice resets page while preserving company, responsibility, Overview origin. Observed exact Finance/In progress return with two outcomes and retained dependencies. Artifacts: `kanban-state-filter.dom.txt`, `kanban-detail.dom.txt`; focused tests also cover objective/agent preservation and empty state. Keyboard arrows scroll the desktop board 80px with solid focus outline; Tab reaches Completed. Artifact: `kanban-desktop-layout.json`.
5. **F-MOBILE** — Reload both surfaces at 390×844. Expected stacked readable cards / lanes and no document overflow. Observed document width 375, card widths 307px, lane widths 289px; vertical lane flow without nested clipping; focused Finance Review visible. Artifacts: `cards-mobile.jpg`, `cards-mobile-layout.json`, `kanban-mobile.jpg`, `kanban-mobile.dom.txt`, `kanban-mobile-layout.json`.

## Verification and runtime

- 33 focused tests pass, zero failures/skips: `cards-kanban-focused.trx`, CompanyHealthTests / AgentWorkJourneyTests, including six added cases.
- 66 related regression tests pass, zero failures/skips: `cards-kanban-regression.trx`, Today workspace, shell/navigation, priority evidence, localization quality. These are 99 distinct cases, not a full repository test run.
- Web / adapter builds pass with zero errors: `web-build.txt`, `uat-build.txt`. Existing compiler/analyzer warnings and unavailable NuGet vulnerability metadata are recorded; no completed vulnerability audit is claimed.
- Initial interrupted/default-output builds encountered invalid generated assembly/apphost metadata; isolated artifact output resolved this without source workarounds or recursive cleanup.
- Sandbox Web requests hit existing Windows DPAPI/Event Log permission failures. Its recorded PID was stopped in the sandbox. A failed intermediate desktop-account launch still found that port occupied; after the old PID was stopped, final desktop-account host served HTTP 200. Startup logs retain these diagnostics. Owned process files are historical after cleanup.
- Old error-page tab acquisition was blocked by its internal URL protocol; it was not retried. Final browser evidence uses a healthy HTTP origin on the disposable fixture.
- `verification.json` records results/hashes; `cleanup.json` records owned-host shutdown. Temporary viewport overrides are reset and the verification tab is closed.

## Design comparison and continuation

Provider: built-in OpenAI ImageGen, current approved image model. Written prompts and images saved before implementation: [department prompt](../../../../design/references/company-department-cards-reference-prompt.md), [department reference](../../../../design/references/company-department-cards-reference.png), [Kanban prompt](../../../../design/references/agent-team-kanban-reference-prompt.md), [Kanban reference](../../../../design/references/agent-team-kanban-reference.png).

Cards follow label/value rows, action divider and supporting metadata, adapting to four/two/one columns. Kanban follows clear lane headers, pale lane backgrounds, white cards, readable ownership/dependency structure, page-slice/empty states and horizontal desktop / vertical mobile flow. Existing shell and authorized data take precedence over illustrative navigation/counts/dates. Coverage is a disclosure below the board so it does not consume a lane.

P11 must read this packet alongside the P10 handoff and preserve the Kanban and typed durable lifecycle/detail/return behavior. Global pagination remains bounded at 24 outcomes; View state accesses work outside the current page. No drag-to-change lifecycle behavior, API/entity/schema/migration, permission or provider change was added.

To review the original VC company, start the normal repository server/client and refresh the browser. The stopped 5062/5301 hosts were not certified by fixture screenshots. Live company-data replay and existing human release/provider/deployed tenant/SQL Server/statutory gates remain separate.
