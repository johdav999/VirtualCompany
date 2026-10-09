# Replay

Use PowerShell in the same checkout. Normal shell/CUA initialization fails at the Windows deny-read ACL helper; the verified shell and fresh Edge substitute are recorded. Set DOTNET_PROCESSOR_COUNT=1 and DOTNET_TieredCompilation=0. For disposable SQL tests set VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION to `Server=localhost\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Encrypt=False`.

Run the Api.Tests filter `AnnualPlanning|ApprovalDecisionApiIntegrationTests|QuarterlyPlanningIntegrationTests|MonthlyReviewSnapshotIntegrationTests` with `FullyQualifiedName~` alternatives. The final affected AnnualPlanning filter supersedes those results after the progress/approval wording fix. Run Web.Tests for AnnualPlanningJourneyTests, DecisionReviewJourneyTests and the native quarter/month/Overview/telemetry regressions; Web.Contract.Tests for AnnualPlanningWireTests and QuarterlyPlanningWireTests. Builds use `--no-restore -m:1 -p:UseSharedCompilation=false`. `BuildProjectReferences=false` reuses already-compiled native dependencies only where recorded.

Migration `20261006151544_AddAnnualPlanning` was generated through the migrations project and API startup project. Do not regenerate it. Run `dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations --startup-project src/VirtualCompany.Api --no-build` after the native API build.

Verify ports 5346/5106 free. Start native UAT and Web DLLs directly with hidden Start-Process -PassThru; record PIDs immediately, then poll readiness in a separate command limited to 30 seconds. Web uses native content root, Development, ApiBaseUrl=http://localhost:5346/, development subject p19-owner/email p19-owner@example.test/provider dev-header. Run `node docs/verification/vcscreens/P26/verify-browser.mjs`. Current profile is `/_uat/p26/profile`. Stop only exact verified owned PID/DLL/listener matches; historical PIDs are not cleanup instructions.

