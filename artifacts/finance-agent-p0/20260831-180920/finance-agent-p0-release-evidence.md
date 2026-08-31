# Finance agent P0 release evidence

- Generated UTC: 2026-08-31T16:24:17.7034300Z
- Repository revision: 7d977fcc8eb1df734cfdc5e2e3649626e3707904
- Dirty working tree: True
- Working-tree manifest checksum: e8a96574c0e47fc328ae5dadb7ff40664abd8140fbba07e40baaebfb7461844e
- Evidence checksum: 92c567fe64a6b6b078c0f722be380cd1bcb4701c4142339682445b3cbaa82c4a
- P0 decision: go
- Technical classification: technically_verified_for_human_review
- Human accounting review: human_accountant_review_pending

| Checkpoint | Outcome | Passed | Failed | Skipped | Command |
| --- | --- | ---: | ---: | ---: | --- |
| focused-p0 | passed | 148 | 0 | 0 | `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --configuration Release --nologo -p:NuGetAudit=false --filter FullyQualifiedName~FinanceAgentAuthorityMatrixTests|FullyQualifiedName~FinanceAgentAuthorizationServiceTests|FullyQualifiedName~FinanceToolRiskPolicyTests|FullyQualifiedName~FinanceToolExecutionFlowIntegrationTests|FullyQualifiedName~ApprovalDecisionChainTests|FullyQualifiedName~AgentEffectiveAuthorityResolverTests|FullyQualifiedName~FinanceAgentAuthorityMigrationTests --logger trx;LogFileName=finance-agent-p0-focused.trx --results-directory C:\Users\Johan\source\repos\Virtual Company\artifacts\finance-agent-p0\20260831-180920 --no-restore` |
| release-build | passed | 0 | 0 | 0 | `dotnet build VirtualCompany.sln --configuration Release --nologo -p:NuGetAudit=false --no-restore` |
| hermetic-matrix | passed | 3318 | 0 | 0 | `pwsh -NoProfile -ExecutionPolicy Bypass -File C:\Users\Johan\source\repos\Virtual Company\scripts\test-matrix.ps1 -Lane hermetic -Configuration Release -ResultsRoot C:\Users\Johan\source\repos\Virtual Company\artifacts\finance-agent-p0\20260831-180920\hermetic -NoBuild -NoRestore` |
| ef-pending-model | passed | 0 | 0 | 0 | `dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj --startup-project src/VirtualCompany.Api/VirtualCompany.Api.csproj --configuration Release --no-build` |
| sqlserver-migration-concurrency-rollback | passed | 1 | 0 | 0 | `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --configuration Release --nologo -p:NuGetAudit=false --no-build --filter Category=SqlServer&FullyQualifiedName~FinanceToolExecutionFlowIntegrationTests.Sql_server_approval_continuation_is_atomic_and_ambiguous_results_require_reconciliation --logger trx;LogFileName=finance-agent-p0-sqlserver.trx --results-directory C:\Users\Johan\source\repos\Virtual Company\artifacts\finance-agent-p0\20260831-180920 --no-restore` |
| swedish-accounting-evidence-verifier | passed | 0 | 0 | 0 | `python C:\Users\Johan\.codex\skills\swedish-accountant-expert\scripts\verify_virtual_company_evidence.py` |

This is engineering evidence only; it is not statutory approval or a signed professional opinion.
