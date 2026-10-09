# Sales priority Review navigation

Product: Virtual Company Web; existing local Web/API on http://localhost:5062.
Role: configured development identity, VC company 43e6a825-d1b7-429a-8608-7e668087d005.
Build: LocalRun client rebuilt with `powershell -NoProfile -File ./client.ps1` on 2026-10-05; Web process 38368. Existing uncommitted work preserved.
Flow NAV-SALES-001: Overview > Today > Sales > opportunity Review > opportunity > originating view.

## Root cause and change

`TodayWorkspace.razor` hard-coded every priority Review href to `DashboardRoutes.BuildPriorityPath`. The Sales contributor already supplied the correct owning opportunity link. This was a presentation routing defect, rather than an API redirect. The exact supplied browser URL was reproduced before changing the running client.

Sales Review now resolves the supplied source link through `EnsureWorkspaceContext`, retaining company and Sales overview return context. A separately labeled Priority details link retains the evidence page. Company and other responsibility lenses keep their existing priority-evidence entry point. Review telemetry records the actual rendered destination.

## Acceptance evidence

1. Original Sales Review navigated to `/dashboard/priorities` with the supplied sales-deal key: `before-browser.json`.
2. Rebuilt Sales Review navigates to `/app/sales/deals/015351a5-f2ec-4bf7-8b26-c6718b9e1363` for the same company: `after-browser.json`, `after-review.png`.
3. Back to originating view returns exactly to `/dashboard?companyId=43e6a825-d1b7-429a-8608-7e668087d005&lens=sales`.
4. Priority details remains accessible and Back to Today retains that exact origin.
5. Sales section Open workspace reaches the Sales review heading at `/app/sales`.
6. 390x844 viewport has no horizontal page overflow: `after-overview-mobile.png`. Desktop `after-overview.png` visually inspected. Initial overview capture was transient hydration/loading; verification script now waits for settled interactive rendering before capture. The original before URL/result remains the primary before evidence.
7. No browser page errors in the completed checks.

Focused command: `dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --filter "FullyQualifiedName~TodayWorkspaceComponentTests|FullyQualifiedName~SalesOperationalJourneyTests|FullyQualifiedName~CompanyHealthTests" --logger "trx;LogFileName=sales-navigation.trx" --results-directory docs/verification/vcscreens/sales-review-navigation-2026-10-05/results -v minimal`.
Result: 54 passed, 0 failed, 0 skipped. LocalRun Web build succeeded with 6 existing nullable warnings. Focused `git diff --check` passed.

Browser verification uses real local Web/API with headless Edge. The in-app browser control kernel exited during initialization, so the user's existing tab was not operated. No business mutations, customer messages, or provider actions were performed. To run the navigation checks: `node docs/verification/vcscreens/sales-review-navigation-2026-10-05/verify-browser.mjs after`.
