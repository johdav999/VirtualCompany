# Company Today freshness

Product: Virtual Company Web/API on localhost:5062 / localhost:5301. Environment: existing development company VC, 43e6a825-d1b7-429a-8608-7e668087d005; configured development identity. Viewport 1985x837. Current API process 33676, LocalRun build on 2026-10-05, original localhost:1433 SQL database restored. Existing uncommitted work preserved.

## Root cause

`CompanyTodayWorkspaceQueryService.BuildSituationSummary` selected saved daily briefing copy up to 24 hours old. `Freshness` classifies sources older than six hours as stale. The Company Today header and workspace banner used that briefing freshness, while Refresh workspace only bypassed the Today read cache and reread sources. It did not regenerate the saved briefing. The exact user company/lens continued to show `This view needs a refresh / 8 hours ago` after Refresh workspace (before-browser.json).

## Fix

Use saved briefing copy only when its existing freshness classification is fresh/current. For stale, future-dated, or missing briefing times, use the existing deterministic summary of current authorized priorities. Preserve saved briefing/source timestamps and source-level freshness. Recent saved briefing copy remains dated honestly. No briefing generation, AI request, or provider synchronization was added to the read endpoint.

## Acceptance

- Real local Company Today entrypoint reproduced the warning before and after an actual Refresh workspace click.
- After rebuilding API with the original database configuration, initial load and Refresh workspace show `Summary as of / 1 minute ago`, stale banner count 0, and the summary describes five currently ranked priorities instead of old daily briefing copy.
- Reload stays free of the misleading dashboard banner.
- Original source-level stale warnings remain visible on old priority records.
- Browser page errors: 0.
- 27 focused API tests passed, 0 failures/skips. New coverage includes 15 minutes, exactly six hours, six hours plus one minute, eight hours, 24 hours, older than 24 hours, future and missing timestamps, and unchanged old source freshness.
- API LocalRun build succeeded. Initial changed-code compilation had 8 existing nullable warnings; final incremental build succeeded with 0 warnings/errors. Focused diff whitespace check passed.

Test command: `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --filter "FullyQualifiedName~TodayWorkspaceQueryServiceTests|FullyQualifiedName~TodayWorkspaceIntegrationTests" --logger "trx;LogFileName=today-freshness.trx" --results-directory docs/verification/vcscreens/today-freshness-2026-10-05/results -v minimal`.
Browser command: `node docs/verification/vcscreens/today-freshness-2026-10-05/verify-browser.mjs after`.
Evidence: before-browser.json, after-browser.json, before-company-today.png, after-company-today.png, results/today-freshness.trx.

## Runtime correction and scope

The first restart used server-local-sql.ps1, which selected a separate local SQL Express database rather than the existing Docker SQL connection. That process was stopped (only task-launched PID 46632). The original SQL connection from appsettings.Development.json was then passed in memory to run-api.ps1 without printing credentials. Final acceptance ran against the original company data. The separate local SQL Express startup applied migrations/initialization; it was not deleted or used as acceptance evidence. The user's original database was not replaced. No business command or customer/provider action was requested for this fix.

Browser evidence uses real local Web/API through headless Edge; the in-app browser automation kernel was unavailable in this session. This is scoped dashboard acceptance, not release/provider approval.
