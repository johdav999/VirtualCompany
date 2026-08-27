# Native Receivables Release 2 evidence and decision

Evidence date: 2026-08-27 (Europe/Stockholm)

Revision baseline: `fd4d268` plus the uncommitted Release 1 and Release 2 implementation.

## Release decision

**NO-GO — do not enable Native Receivables for production companies.**

The application controls, deterministic suites, company-scoped readiness API, operator UI, runbooks, supported-volume declarations, migration chain, and normal build gates are implemented and green. Production release proof is incomplete because the current Release 2 schema/object set has not passed SQL Server fresh-install and representative-upgrade lanes, small/medium receivables performance runs, coordinated local and Docker restore rehearsals, authenticated English/Swedish browser E2E, or real mailbox/e-invoice checks. Docker Desktop is not running on this host, the in-app browser runtime cannot initialize, and no current coordinated SQL/object backup was supplied.

Swedish production claims remain independently blocked by the Release 1 decision in `accounting-r1-release-evidence.md`: no attributable qualified Swedish accounting review exists. No recipient delivery, bank-refund completion, e-invoice delivery, performance result, restore result, or statutory validity is inferred from deterministic acceptance or provider acknowledgement.

## Delivered production controls

- `GET /api/companies/{companyId}/finance/receivables/readiness` requires `AccountingView`, enforces company context, and returns ten bounded checks with at most 25 evidence identifiers per signal.
- The checks cover stale/failed approval, statutory number gaps, render failures/staleness, ambiguous email/e-invoice/reminder delivery, recurring blockers, rejected e-invoices, unreconciled/stale refunds, AR-control differences, overdue collection follow-ups, and failed statutory archive exports.
- Finance → Receivables → Operations presents blocking/attention/healthy totals, friendly localized check names, measured values, remediation, evaluation time, and recovery links. It explicitly states that provider acceptance is not recipient delivery and does not offer an unsafe retry for ambiguous outcomes.
- The small and medium capacity profiles now measure native drafts, rendered artifacts, delivery attempts, recurring occurrences, customer statements, collection cases, and worker backlog in addition to accounting truth.
- The operations runbook covers reconciliation, numbering gaps, re-render/resend policy, refund recovery, recurring blockers, collection holds, e-invoice operations, retention, deployment order, feature controls, coordinated recovery, and additive forward-fix rollback.
- The initially incomplete hosted-service architecture manifest was repaired to include the collection, refund-execution, and recurring-schedule workers. The complete API suite passed after the repair; no test was weakened or removed.

## Deterministic scenario coverage

| Customer-to-cash area | Executable owner/evidence | Result and limit |
| --- | --- | --- |
| Customer creation, billing profile, merge/source conflict | `CustomerBillingProfileServiceTests`, `CustomerBillingApiIntegrationTests` | Hermetic suite passed; no live master-data integration is claimed. |
| Draft calculation, approval, edit invalidation, evidence | `CustomerInvoiceDraftTests`, `CustomerInvoiceDraftApiSurfaceTests` | Hermetic suite passed. |
| Issue, number, immutable snapshot, posting, idempotency | native draft/issue tests plus `CustomerInvoiceAccountingApiIntegrationTests` and `AccountingPostingServiceTests` | Hermetic suite passed; SQL Server concurrency/rollback lane not run. |
| PDF, email fallback, ambiguity, provider acknowledgement | `CustomerInvoiceDeliveryFallbackPolicyTests`, `CustomerInvoiceDeliveryApiSurfaceTests`, `B2BRouterPeppolDeliveryTests` | Deterministic provider contracts passed; real recipient/provider proof not run. |
| Payment, allocation, bank reconciliation, AR control | `AccountingIntegrityScenarioTests`, `BankTransactionsIntegrationTests`, customer-invoice accounting suites | SQLite/hermetic coverage passed; SQL Server integrity fact skipped. |
| Credit, write-off, bad-debt recovery, refund ambiguity | `CustomerInvoiceCorrectionPolicyTests`, `CustomerInvoiceCorrectionApiSurfaceTests` | Deterministic policy/API coverage passed; no real bank refund completion is claimed. |
| Recurring generation, leases, restart/idempotency | `CustomerInvoiceScheduleDomainTests`, `CustomerInvoiceScheduleApiSurfaceTests`, hosted-service architecture tests | Hermetic coverage passed; process-death/SQL Server lane not run. |
| Statements, reminders, disputes, promises, collection holds/tasks | `CustomerCollectionsTests`, `CustomerCollectionsApiSurfaceTests` | Deterministic coverage passed. |
| Close, reports, export, recovery verification | `ReportingPeriodCloseIntegrationTests`, `AccountingIntegrityScenarioTests`, statutory/export/recovery suites | Hermetic coverage passed; current local/Docker coordinated restore proof not run. |
| Authorization, cross-company IDs, bounded operations evidence | `NativeReceivablesReadinessApiIntegrationTests`, API authorization/tenant suites | Passed; the readiness test proves foreign-company gaps are excluded. |

These owners provide deterministic component and integration coverage across the lifecycle. They do not substitute for the missing production-shaped SQL Server, restore, browser, and real-provider lanes; those omissions are release stops.

## Supported volumes and objectives

| Profile | Users / worker concurrency | Native receivables maxima |
| --- | --- | --- |
| Small | 25 / 10 | 100,000 invoices; 100,000 drafts; 100,000 rendered artifacts; 250,000 delivery attempts; 250,000 recurring occurrences; 100,000 statements; 100,000 collection cases; 25,000 queued work items. |
| Medium | 100 / 30 | 1,000,000 invoices; 1,000,000 drafts; 1,000,000 rendered artifacts; 2,500,000 delivery attempts; 2,500,000 recurring occurrences; 1,000,000 statements; 1,000,000 collection cases; 250,000 queued work items. |

Declared p95 objectives are: invoice list 500 ms (1,000 ms breach), 100-line draft preview 750/1,500 ms, issue 1,500/3,000 ms, 25-page PDF render 3,000/6,000 ms, aging 1,500/3,000 ms, 5,000-item statement 3,000/6,000 ms, first 250 collection items 750/1,500 ms, and ten-check readiness 1,000/2,000 ms. These are operational objectives, not measured production results. The opt-in supported-volume SQL Server lane was not configured in this run and therefore remains a release stop.

## Release 2 migrations

The additive migration order is:

1. `20260825172345_AddCustomerBillingMasterR2`
2. `20260825181053_AddNativeCustomerInvoiceDraftsR2`
3. `20260825195833_AddRecurringCustomerInvoiceSchedulesR2Generated`
4. `20260825210650_CompleteRecurringCustomerInvoiceSchedulesR2`
5. `20260825213000_AddAtomicNativeCustomerInvoiceIssueR2`
6. `20260825233000_AddCustomerInvoicePdfDeliveryR2`
7. `20260826052524_AddCustomerInvoiceCorrectionsR2`
8. `20260826064427_AddCustomerCollectionsR2`
9. `20260826070720_AddCustomerCollectionPolicyExceptionsR2`
10. `20260826121656_AddCustomerInvoiceEmailFallbackR2`
11. `20260826142558_AddB2BRouterPeppolDeliveryR2`

The model has no pending changes. An already-current development database is not accepted as upgrade or fresh-install proof; both SQL Server migration paths remain not run for this decision.

## Commands and results

| Command | Result |
| --- | --- |
| `dotnet test tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj --no-restore --no-build` | Passed 296, skipped 7 environment-gated SQL/migration tests, failed 0. |
| `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore --no-build` | Final run passed 2,084, skipped 2 opt-in SQL Server integrity/performance tests, failed 0. |
| `dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore` | Passed 401, failed 0. |
| `dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj --no-restore` | Passed 16, failed 0. |
| `dotnet test tests/VirtualCompany.Infrastructure.Platform.Tests/VirtualCompany.Infrastructure.Platform.Tests.csproj --no-restore` | Passed 2, failed 0. |
| `dotnet test tests/VirtualCompany.Infrastructure.Mailbox.Tests/VirtualCompany.Infrastructure.Mailbox.Tests.csproj --no-restore` | Passed 5, failed 0. |
| `dotnet test tests/VirtualCompany.SalesSource.Tests/VirtualCompany.SalesSource.Tests.csproj --no-restore --no-build` | Passed 6, failed 0. |
| `dotnet test tests/VirtualCompany.SupportGrounding.Tests/VirtualCompany.SupportGrounding.Tests.csproj --no-restore --no-build` | Passed 5, failed 0. |
| `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~DependencyInjectionArchitectureTests\|FullyQualifiedName~NativeReceivablesReadinessApiIntegrationTests"` | Passed 6, failed 0 after repairing the hosted-service manifest. |
| `dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj -c Release --no-restore` | Passed, 0 errors and 30 existing warnings. |
| `dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj -c Release --no-restore` | Passed, 0 errors and 9 existing warnings. |
| `dotnet build VirtualCompany.sln -c Release --no-restore` | Passed, 0 errors and 91 existing compiler/analyzer warnings. |
| `dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj --startup-project src/VirtualCompany.Api/VirtualCompany.Api.csproj --configuration Release --no-build` | Passed: `No changes have been made to the model since the last migration.` Existing EF model warnings remain. |
| `git diff --check` | Passed; Git reported line-ending conversion warnings only. |
| changed/untracked text-file secret scan | Scanned 292 files; 0 suspicious files. |
| `docker version --format ...` | Not run-ready: Docker client 29.3.1 is installed, but the Docker Desktop Linux engine pipe is absent. |

## Browser and visual evidence

The screenshot-first reference is `docs/design/references/native-receivables-operations-reference.png`; its complete generation prompt is retained in `docs/design/references/native-receivables-reference-prompts.md` and it is inventoried in `docs/design.md`. The implemented Razor/CSS surface and English/Swedish resources were checked by Web/client/surface tests.

The UAT packet is `prompt10-native-receivables-uat-evidence.md`. The in-app browser failed before navigation because required kernel assets were unavailable. Consequently, no authenticated desktop/narrow runtime screenshot, keyboard pass, console-error pass, or English/Swedish end-to-end browser transition is claimed.

## Artifact and license review

Prompt 10 adds one generated PNG reference (1,380,314 bytes), its human-readable prompt, source code, tests, and Markdown evidence/runbook changes. It adds no runtime package dependency, bundled executable, font, third-party logo, or copied provider asset. The image is design evidence only and is not shipped as a production UI asset. The wider dirty working tree contains artifacts and dependencies from Prompts 1–9; their release review is not replaced by this Prompt 10 review.

## Recovery, integration, and residual release stops

- A current coordinated SQL/object backup and matching manifest were not available. Local SQL Server Express is running, but there was no representative Release 2 backup to restore. No new local restore result is claimed.
- Docker Desktop is unavailable, so Docker fresh-install, representative-upgrade, backup/restore, `DBCC CHECKDB`, object-manifest comparison, checksum verification, and safe-continuation proof were not run.
- The SQL Server migration/concurrency/rollback/integrity facts and supported-volume receivables measurements were not configured; hermetic skips do not count as passes.
- Failure behavior is covered deterministically by the owning suites, but the production-shaped matrix for SQL rollback, concurrent issue, duplicate outbox delivery, process death, expired leases, mailbox/provider timeout, ambiguous success, object persistence failure, webhook replay, and stale/cross-company input has not been rerun as one SQL Server release lane against the full Release 2 migration set.
- Real mailbox and B2Brouter/Peppol credentials were not supplied. Provider contract tests passed, but recipient delivery and provider-network delivery remain unproved.
- Authenticated browser E2E in English and Swedish is blocked by the browser runtime failure.
- Qualified Swedish accounting review is absent, so Swedish statutory-compliance claims remain blocked independently of receivables engineering health.
- Existing solution warnings and EF relationship/default warnings are not caused by Prompt 10, but remain technical-debt evidence for the release owner.

Application rollback must preserve the additive schema, issued documents, number gaps, journals, rendered artifacts, delivery/acknowledgement attempts, external references, refunds, statements, reminders, approvals, audits, and hashes. Disable affected features/workers, reconcile ambiguous outcomes, and forward-fix. Never delete or renumber issued records to make a rollback appear clean.
