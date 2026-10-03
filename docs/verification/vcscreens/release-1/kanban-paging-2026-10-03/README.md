# Agent team Kanban paging correction — 2026-10-03

Implemented and verified within the scope below. Preserve the same uncommitted P01–P10 checkout, department cards, Kanban layout and durable lifecycle/detail/return implementation. This packet supersedes the global board paging limitation in the earlier department-cards packet; historical evidence and human release approvals remain historical.

## Root cause and issue ledger

| ID | Severity / flow | Expected / observed | Root cause and acceptance | Result |
| --- | --- | --- | --- | --- |
| UAT-KANBAN-02 | P2 defect / opening Agent team | Active activities should appear immediately. User screenshot shows In progress total 8, page 0. | Query selected the newest 24 outcomes globally before grouping into lanes. New completed history displaced older active records. Each state must load its own bounded first page after authorization and filtering, and its page links must preserve filters and detail returns. | Verified in composed API/typed transport and real disposable browser |

The existing VC API on 5301 was inspected read-only using its configured Alice development identity and company `43e6a825-d1b7-429a-8608-7e668087d005`. `original-company-global-page.json` confirms 964 authorized outcomes: 8 In progress, 955 Completed, 1 Failed; only 24 items returned, zero In progress. The earlier Kanban restoration fixed grouping but retained this query defect. Its balanced, nine-outcome Finance replay did not guard the unfiltered initial load.

## Implementation and public query semantics

`AgentWorkQuery.PerState` is an optional flag, default false, mirrored in the Web view model and exposed as `perState=true` on the existing company-scoped GET agent-work endpoint. The typed client sends it only when enabled; AgentStaffOverview always enables it.

The Operations query resolves visibility and loads source families once, applies responsibility/agent/objective/state filters, sorts by retained UpdatedUtc descending then kind/ID, and takes Skip/Take separately for each canonical state. Each lane is limited to Take (default 24, maximum 100); at most seven such pages are returned. Counts/Total describe all authorized filtered outcomes in the existing source window. HasNext means at least one filtered state has another page. Requests omitting the flag preserve global list paging.

Column headers show their visible range and full filtered total. Previous/Next page links select that state while retaining responsibility, agent, objective, company and validated Overview context. View state resets Skip to zero. The all-state overview reports items shown across columns, avoiding a misleading global contiguous range; a state-filtered board retains its normal footer pager. Empty pages with positive totals offer first-page recovery and never claim that no work matches the filters.

No entity/schema/migration, permission, lifecycle, provider or command behavior changes. The existing 2,000-record source window per family and partial diagnostics remain explicit; known older identities retain direct detail access. This fix does not promise that records outside that source window will appear in lane counts/pages.

## Product profile and repeatable browser flows

Product: Virtual Company, Blazor Server / .NET 9, Windows PowerShell. Baseline HEAD `37834c7f` plus existing phased work. Browser adapter: `tests/VirtualCompany.Workspace.Uat`, composed API, disposable SQLite, workers disabled, synthetic P01 Owner / North Company. Test API 5319; test Web 5079 with environment-only API/identity settings; production config unchanged. Existing 5062/5301 snapshots stayed running and were inspected read-only.

Build/test output: `.codex-build/uat-cards-kanban`. Build commands use `dotnet build <project> --artifacts-path .codex-build/uat-cards-kanban --no-restore --disable-build-servers -p:BuildInParallel=false -p:UseAppHost=false`. Tests use the same artifacts path, project-owned filtered suites, TRX logger and this results directory. `web-build.txt` and `uat-build.txt` record zero errors; UAT build includes API and its dependency graph.

1. **F-KANBAN-INITIAL** — Open `/agents/staff?companyId=11111111-1111-1111-1111-111111111111` with all filters clear and a Company Today return. Fixture adds eight active Finance tasks dated two days earlier and 55 newer completed tasks to the retained P10 fixtures. Expected every state's first page immediately. Observed 68 cards across 107 outcomes, including all 13 active outcomes and all eight older regression tasks; Completed shows 24 of 56 independently. `fixture-per-state-source.json`, `initial-unfiltered.dom.txt`, `initial-layout.json`, `initial-unfiltered.jpg`.
2. **F-KANBAN-FILTER-PAGES** — Choose Finance, search objective Kanban, Apply. Expected 8 active and first 24 completed of 55. Observed exactly these counts and cards. Choose Completed's Next page: expected state Completed, Skip 24, retained Finance/objective/Overview. Observed 25–48 of 55, 24 cards. Footer Next gives 49–55 of 55, seven cards, disabled Next. `filtered-first-page.json`, `.dom.txt`, `.jpg`, `completed-second-page.json`, `.dom.txt`, `completed-last-page.json`.
3. **F-KANBAN-RETURN** — On Completed page two, Open work, then “← Back to filtered board”. Expected identical board URL and page. Observed exact return with Skip 24, Finance, Kanban objective, Completed and Company Today context intact. `work-detail.dom.txt`, `return-page.dom.txt`. No mutation/execution was performed.
4. **F-KANBAN-MOBILE** — Restore all states using Apply (Skip resets), retain Finance/Kanban filters, set 390×844 viewport. Expected stacked lanes, all eight older active cards and no horizontal document overflow. Observed document width 375, lanes 289px, vertical column flow and no nested height clipping. `mobile-layout.json`, `mobile.dom.txt`, `mobile.jpg`. Desktop 1633×1244 matches the user's regression viewport. Existing generated `docs/design/references/agent-team-kanban-reference.png` remains the visual target: labelled pale lanes, white cards, ownership/dependency/date structure; only compact page controls/range copy were added.

## Automated verification and boundaries

- 100 Web checks pass in `web-final.trx`: AgentWorkJourney, CompanyHealth, TodayWorkspaceComponent, WorkspaceNavigation, WorkspaceShell, PriorityEvidence and LocalizationQualityGate. The initial `web-focused.trx` has 16 overlapping checks and is not added again to coverage totals. Covers per-state transport opt-in/page usage, grouping, page/filter/return links, exhausted-page recovery, errors/cancellation and earlier department-card/navigation behavior.
- 26 API checks pass in `api-focused.trx`: AgentStaffOverviewIntegration and AgentWorkLifecycleQuery. Responsibility-scope and partial-window tests now cover both legacy global and per-state requests; hidden records/counts/agents/details and lifecycle invariants remain guarded.
- 2 typed Web/API checks pass in `wire-focused.trx`: composed API, eight older active plus 55 newer completed outcomes, deterministic ordering, bounded pages, disjoint next/final pages, state/area/agent/objective filtering, empty searches, cross-company denial and existing shared outcome/detail/Work identity.
- 128 distinct accepted checks, zero failures/skips; scoped evidence, not a full repository matrix. Web/API/UAT compilation passes; existing warnings and unavailable NuGet vulnerability metadata are recorded. The first wire command lacked an assets file; normal incremental restore resolved it, with no source workaround.
- Browser used a fresh background HTTP fixture tab. The original user tab is an internal `data:` error page; binding it was blocked by browser URL policy and was not retried or bypassed. The disposable browser is the strongest safe UI substitute, while the original-company query directly confirms the reported root cause.
- Normal fixture Web startup encountered Windows DPAPI/Event Log permissions. The first cleanup skipped its PID because JSON parsed StartedUtc as a DateTime and a string comparison failed; an intermediate desktop-account host therefore collided on 5079. Cleanup was corrected to compare DateTime values, the exact recorded sandbox PID was stopped, and the final desktop-account host served HTTP 200. Diagnostic logs/process records are retained; do not reuse historical PIDs.
- Viewport override reset; temporary tab closed and owned fixture hosts stopped. `cleanup.json` and `verification.json` record cleanup/results/hashes.

## Continuation

P11 must read this packet with `docs/vcscreens/implementation-status.md`, the historical P10 handoff, root prompt shared contract and applicable production/design/architecture guidance. Preserve per-state pages, typed durable kind/ID, scoped source counts and exact filtered-board returns. Normal startup must rebuild/restart **both API and Web** for the existing 5062/5301 snapshots to serve this cross-layer change; browser refresh alone cannot update those snapshot binaries. No live company data was reseeded or modified during verification.
