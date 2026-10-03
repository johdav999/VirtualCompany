# P01 implementation and verification

P01 extends the existing role/period workspace and its navigation. The actual backend lens resolver, typed Today/Monthly clients, membership policies and company-selection persistence are retained. There is no new authorization role, business/report engine, schema, migration, view preference or provider dependency.

Production changes:

- NavMenu: active-membership company select, exact Overview return links, cleared company-sensitive state on switch, guarded asynchronous navigation/agent refresh, and company-scoped fallback identity.
- Dashboard: cancel/version guards, clear old workspace/access/entry before loading, reject inactive/foreign membership before requesting workspace records, server-default lens canonicalization, explicit invalid Monthly period feedback, and no canonicalization after leaving the Dashboard route.
- Today/Monthly: Support label for customers lens, visible single-lens availability explanation, and scoped return links for records, operating-area cards, decisions and agent destinations.
- DashboardRoutes/ReturnUrlNavigation: canonical business prefixes and boundaries, forced current company for workspace links, validated local Overview origin, replaced return parameter and retained fragments. OverviewNavigationContext stores only circuit-local return context; URL retains it after reload.
- English/Swedish resources and small shell/responsibility styles reuse the established design system. Route inventory and full release screen/report register are updated.

## Prerequisite evidence

P01 has no prompt prerequisite or external credential. Inspection confirmed existing Today/Monthly contract, `CompanyTodayWorkspaceLensResolver`, membership selection, scoped queries, cache revision and retained routes. Shared implementation contract, P01, relevant C1/C2/C5–C8 navigation entries and all applicable repository/design/test instructions were read. Existing references 01/02/05, shell and Today/Monthly were reused. Separate missing Marketing Today and Finance Today references are registered for P05/P06 before those redesigns.

## Final checks

| Check | Command / scope | Result / evidence |
| --- | --- | --- |
| Web behavioral regressions | `dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkspaceShellTests\|FullyQualifiedName~WorkspaceNavigationTests\|FullyQualifiedName~TodayWorkspaceComponentTests\|FullyQualifiedName~MonthlyWorkspaceComponentTests\|FullyQualifiedName~DashboardRoutesTests\|FullyQualifiedName~NavigationRaceRegressionTests\|FullyQualifiedName~DashboardPageTests\|FullyQualifiedName~WorkspaceApiClientTests\|FullyQualifiedName~SharedLocalizationTests" --logger "trx;LogFileName=p01-web.trx" --results-directory docs/verification/vcscreens/P01 --verbosity quiet` | 106 passed, 0 failed, 0 skipped; `p01-web.trx` |
| Composed authenticated API | `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~TodayWorkspaceIntegrationTests\|FullyQualifiedName~MonthlyWorkspaceIntegrationTests\|FullyQualifiedName~CompanyResponsibilityIntegrationTests" --logger "trx;LogFileName=p01-api.trx" --results-directory docs/verification/vcscreens/P01 --verbosity quiet` | 28 passed, 0 failed, 0 skipped; `p01-api.trx` |
| Web build | `dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore --verbosity quiet` | Succeeded, final 0 errors / 0 warnings; `web-build.log` |
| API build | `dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore --verbosity quiet` | Succeeded, 0 errors; cached restore warnings NU1900 because vulnerability feed inaccessible; `api-build.log` |
| Isolated browser adapter | `dotnet build tests/VirtualCompany.Workspace.Uat/VirtualCompany.Workspace.Uat.csproj --no-restore --verbosity quiet` | Succeeded, 0 errors; cached restore NU1900 warnings; `uat-host-build.log` |
| Browser/keyboard/responsive | Real Web + composed test API | Passed scoped flows F01–06; `uat.md`, screenshots, `browser-checks.json` |
| Diff hygiene | `git diff --check` | Passed |

The pipes in the table filter strings are Markdown-escaped; use literal `|` inside the quoted PowerShell filter argument when replaying. Logs are local run artifacts and may be ignored by Git; TRX, screenshots and Markdown carry durable results. Existing nonfatal compiler warnings appeared on rebuilt dependencies; no new compiler error remains. No migration/SQL Server test is required for this presentation/navigation change because persistence contracts and schema are unchanged.

New regression coverage exercises exact Today/Monthly return context, company replacement/URL safety, company reset, inactive links, two assigned views with restricted requested lens, late response isolation, leaving Overview during query updates, invalid month, role-section return propagation and current-company identity. A stale dashboard layout test was updated to the current action-first Today surface through the real typed client rather than obsolete briefing selectors.

API tests additionally prove dual-responsibility switching cannot reuse another company's cached lens; existing suites retain unauthorized/revoked membership, safe no-assignment fallback, responsibility revision invalidation, Monthly period scope and isolation coverage.

Release approval and deployed/provider verification are separate and remain pending. P02 may continue from the status file; no P02 implementation was started.
