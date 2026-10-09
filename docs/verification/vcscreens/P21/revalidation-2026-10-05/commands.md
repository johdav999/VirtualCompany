# Commands and environment

PowerShell in `C:\Users\Johan\source\repos\Virtual Company`. Normal shell startup failed before commands because Windows could not apply sandbox deny-read ACLs; elevated execution was used for that verified startup limitation. Browser CUA startup independently failed with the same ACL error.

The initial regression run used:

```powershell
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore -m:1 -v:quiet --filter 'FullyQualifiedName~Unrepresentable_capacity|FullyQualifiedName~Structurally_invalid' --logger 'trx;LogFileName=regression-baseline.trx' --results-directory docs/verification/vcscreens/P21/revalidation-2026-10-05
```

Final checks use `DOTNET_PROCESSOR_COUNT=1`, `--no-restore -m:1 -p:UseSharedCompilation=false -v:quiet`, and logs/results in this directory. The documented local SQLEXPRESS service was verified running. SQL API tests use `VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION=Server=localhost\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Encrypt=False`; the existing test factory creates and disposes only GUID-owned test databases.

API filter:

```text
FullyQualifiedName~SalesManagement|FullyQualifiedName~SalesOperationsApiIntegrationTests|FullyQualifiedName~SalesOperationalReportTests|FullyQualifiedName~RevenueForecastServiceTests|FullyQualifiedName~ConversionAnalyticsServiceTests|FullyQualifiedName~SalesAnalyticsDashboardEndpointTests|FullyQualifiedName~MonthlyReview|FullyQualifiedName~MonthlyWorkspaceIntegrationTests|FullyQualifiedName~MonthlyWorkspacePeriodTests|FullyQualifiedName~WeeklyWorkspaceIntegrationTests|FullyQualifiedName~DependencyInjectionArchitectureTests
```

Web checks use the common flags plus `-p:BuildProjectReferences=false` against the unchanged, already-built Web dependencies, so independent rendered tests do not rebuild shared outputs while API tests run. Web filter:

```text
FullyQualifiedName~SalesManagementJourneyTests|FullyQualifiedName~SalesOperationalJourneyTests|FullyQualifiedName~SalesJourneyRoutesTests|FullyQualifiedName~MonthlyReviewJourneyTests|FullyQualifiedName~MonthlyWorkspaceComponentTests|FullyQualifiedName~WeeklyWorkspaceJourneyTests|FullyQualifiedName~WorkspaceNavigationTests
```

Wire: full `tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj` suite after API/Web dependencies are built, with `-p:BuildProjectReferences=false`.

Builds: `src/VirtualCompany.Api/VirtualCompany.Api.csproj` and `src/VirtualCompany.Web/VirtualCompany.Web.csproj` with the common flags. Model check: `dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations --startup-project src/VirtualCompany.Api --no-build`. Hygiene: `git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check` and a focused source hash/UTF-8 integrity check. An initial diagnostic check disabled `core.autocrlf` and consequently treated Windows CRLF endings as whitespace; the final check explicitly recognizes CRLF. This task did not edit EF model or migration files.
