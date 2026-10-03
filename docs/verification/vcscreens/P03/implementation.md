# P03 implementation and verification

P03 is complete within its recorded scope. Baseline `37834c7f`, 2026-10-01; uncommitted P01/P02 changes remain preserved. No deployment, commit, migration or release approval. P04 is ready to continue in this chat.

Read the shared implementation contract, P03, relevant current-code entries, root/src/Web/tests/docs instructions, authoritative architecture/design/UI rules and P02 prerequisite evidence. Applied the reference-first and real-browser evidence workflow. Reused reference 01 for Company Today and the P02 evidence detail; generated and inspected `docs/design/references/company-health-report-reference-prompt.md` and its PNG before the new report.

## Delivered behavior

- Company Today presents cash/runway availability, the owning Sales stage-weighted open pipeline, authorized department attention counts, plan coverage, accountable department owners, ranked risks, actual decisions and compact recorded agent work. Card layout responds to available content width. Other role views and existing Monthly are preserved.
- `/dashboard/company-health` refreshes the same authorized Company Today projection. It includes company/as-of/timezone, department coverage and observation times, department filtering, up to 20 deduplicated attention records, source/owner/deadline drilldowns, plan comparison, decisions and recorded agent states. Filtering applies to coverage/risk rows; the company summary stays global and says so. Projection observations do not claim provider freshness. Source records retain their own timestamps. Missing, restricted, partial, stale, loading, error and empty states remain distinct.
- Company risks remain available through `/dashboard/priorities` beyond the top five. Validated `healthReturnUrl` retains the report filter through evidence → Work → evidence → report, alongside the exact validated Overview return. Late/departing reads cannot overwrite a changed company or view.
- “Record my follow-up” revalidates fresh scoped risk evidence and creates an independent user-owned task through the existing typed task command. Existing input payload stores source key/type/ID/UTC time/company/lens; no new evidence database. Work displays the durable evidence link. Today/report show matched active personal follow-ups separately, even when department risks fill the top five. A source risk and its linked personal action do not rank twice. Existing visible active tasks are reused after fresh validation; no new atomic uniqueness/idempotency guarantee. Uncertain POST results disable blind retry and direct the user to existing Work history.
- Existing approval review remains the owning decision workflow. Opening it is read-only. The browser recorded an explicit internal rejection, reloaded the stored result and saw the pending decision disappear from Today/report. This is no payment approval, provider execution or business-result proof.
- Finance uses `IFinanceReadService.GetBudgetsAsync`/`GetVarianceAsync` and existing cash/runway values. No new ledger or variance calculation. No budget means `no_baseline`; one version gives `recorded_unapproved`; multiple versions give `selection_required`; failure gives `unavailable` while preserving known cash. Budget records have no approval state: all cases retain “No approved baseline.” Recorded comparison rows use their own currency/account/cost-center and actual owning values. The period is the owning Finance UTC month, explicitly labeled.
- Failed projection logs contain company/lens and exception type, not exception objects/raw provider text. Failed contributor fallback sections remain unavailable. Snapshot generation follows completed department reads. Legacy evidence timestamp strings without a Z are parsed as UTC, and new payloads write explicit UTC. Approval request dates use the company formatter as well.
- English/Swedish resources cover the new UI. Company lens and labels do not grant Finance access. Risk follow-ups are actor/company scoped and only matched against current authorized risks; inaccessible keys reveal no retained source detail.

## Measures and preserved reports

Company summary/report display `SalesOperationsService` stage-weighted open pipeline: the fixture's SEK 12,000 Qualified deal × owning 0.45 stage weight = SEK 5,400. Sales overview's `RevenueForecastService` 30/60/90-day risk-adjusted windows show SEK 4,050. The UI now explains these distinct measures; neither is ledger revenue or cash. P04 owns window/deal inclusion and export reconciliation, not a replacement calculation in Company Today.

Existing `/finance/cash-position`, `/finance`, `/finance/reports`, `/finance/ledger`, `/finance/report-definitions`, `/app/sales`, `/app/sales/pipeline`, Sales deal/preset/meeting/room pages, `/marketing`, `/support`, Work and Monthly remain. P03 adapts connections, not those department journeys. Company-health is a read-only operational report with no supported report export; existing owning report exports remain intact. No speculative export control or approval-state capability is introduced.

## Verification

Final results: **109 Web, 51 API, 5 Finance projection and 3 typed-wire tests passed**, zero failures/skips. API/Web builds passed; fixture adapter build passed. TRX and build logs are in this folder. Earlier test fixture initialization/property mistakes were repaired and superseded by the final passing runs. Existing nullability/analyzer and offline NuGet vulnerability-audit warnings occurred during recompilation; no new production warning or unresolved error remains. Final incremental API/Web builds report zero warnings/errors.

Coverage includes safe fallback/logging, assembled timestamp, cross-module composition/deduplication, fresh/missing/revoked evidence, scope/departure guards, plan version/gap/failure states, row currency and wire shape, company-scoped typed task persistence, legacy UTC payloads, explicit approval read/decision behavior, personal follow-up reuse/uncertain-write recovery and shared navigation/localization. Finance projection tests use controlled owning-service responses; they do not certify ledger correctness or provider balances.

Representative commands (all normal sandbox):

```powershell
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore --verbosity quiet -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CompanyHealthTests|FullyQualifiedName~PriorityEvidenceTests|FullyQualifiedName~PriorityWorkActionTests|FullyQualifiedName~TodayWorkspaceComponentTests|FullyQualifiedName~WorkspaceNavigationTests|FullyQualifiedName~WorkspaceShellTests|FullyQualifiedName~SharedLocalizationTests' --logger 'trx;LogFileName=p03-web.trx' --results-directory docs/verification/vcscreens/P03
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore --verbosity quiet -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~TodayWorkspaceQueryServiceTests|FullyQualifiedName~TodayWorkspaceIntegrationTests|FullyQualifiedName~TodayWorkspacePriorityOrderingTests|FullyQualifiedName~TaskLifecycleIntegrationTests|FullyQualifiedName~ApprovalDecisionApiIntegrationTests|FullyQualifiedName~FocusEngineTests' --logger 'trx;LogFileName=p03-api.trx' --results-directory docs/verification/vcscreens/P03
dotnet test tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj --no-restore --verbosity quiet -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~FinanceTodayPlanTests --logger 'trx;LogFileName=p03-finance.trx' --results-directory docs/verification/vcscreens/P03
dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj --no-restore --verbosity quiet -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~TodayPriorityWireContractTests --logger 'trx;LogFileName=p03-contract.trx' --results-directory docs/verification/vcscreens/P03
dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore --verbosity quiet -m:1 -p:UseSharedCompilation=false
dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore --verbosity quiet -m:1 -p:UseSharedCompilation=false
git diff --check
```

Actual browser/API checks and screenshot comparison are in `uat.md`/`browser-checks.json`; defects and acceptance limits are in `issue-ledger.md`. The disposed SQLite fixture proves real command persistence across browser reload, not database persistence across fixture restarts. All recorded hosts stopped, tabs closed and temporary viewport reset. No external mailbox, calendar, customer, payment or publishing side effects.

## P04 handoff

Read `docs/vcscreens/implementation-status.md` and the relevant P02/P03 packets; retain the current working tree. Reuse the fresh Today/typed Work contracts, existing Sales services and exact return helpers. P04 still owns Sales Today and the opportunity/contact/meeting/proposal/subscreen/report journey. Finance provider/SQL Server/deployed-tenant acceptance and human release/statutory approval remain unverified independent gates.
