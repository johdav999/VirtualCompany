# EF configuration consolidation

Implemented 9 October 2026 in the existing checkout, preserving the earlier
finance insight query/command refactor and all other local work.

## Result

Each of the 13 previously duplicated entities now has one assembly-discovered
`IEntityTypeConfiguration<T>` owner: FinanceAccount, FinanceCounterparty,
FinanceInvoice, FinanceBill, FinanceTransaction, FinanceBalance,
FinancePolicyConfiguration, FinanceAsset, Payment, PaymentAllocation,
CompanyBankAccount, BankTransaction, and CompanyKnowledgeDocument.

- Removed 12 Legacy/Duplicate files and the two supplemental document/simulation
  configuration files. Removed the supplemental source-tracking configuration
  class from the integration configuration file, retaining its integration owners.
- Merged document and simulation provenance mappings into the owning configurations.
  PaymentAllocation already contained all three simulation provenance relationships.
- Reused the identical provider/source rules through `FinanceSourceTrackingMapping`,
  explicitly invoked by each owner rather than independently discovered by EF.
- Preserved the account/balance composite relationship and transaction `bill_id`
  mapping previously supplied only by retired configurations.
- Preserved the historical `CK_Payments_source_type` and
  `CK_FinanceTransactions_source_type` constraint names explicitly. Their names
  previously depended on assembly configuration application order.
- Moved affected bank, asset, payment, and allocation configurations into
  `Persistence/Configurations`. Assembly registration and company query filters
  remain unchanged.

## Verification

The metadata-only probe under `artifacts/ef-config-consolidation/probe` captured
the complete SQL Server and SQLite design-time models and generated create scripts
before production edits and after the completed refactor. Each contains 658
entities. All four before/after files match byte for byte; SHA-256 values:

| Provider | Model metadata | Generated schema |
| --- | --- | --- |
| SQL Server | `39798E0C960488D86792803C86D319CFE6F124109580196DB02DDD127246521A` | `75DC8153490109E560D7B453807E0BA3D29A830558D34C361DF55C6E1047057E` |
| SQLite | `F807A2B22DECB88BE85FDDADD4D952C7AA7D7990E26F2367CB25845681E66F25` | `8A2D84D7385BF545FFCAA3210B39C005505130C930BA15547C182FC298FFDCAA` |

The probe constructs provider models and generates SQL without connecting to a
database or applying schema changes. Baseline and final captures are in
`artifacts/ef-config-consolidation/before` and `after`.

Focused Finance tests: **49 passed, 0 failed, 0 skipped**. The selection covers
PersistenceConfigurationOwnershipTests, PaymentBatchPersistenceModelTests,
AccountingConfigurationPersistenceTests, FinanceInsightReadBoundaryTests,
BankFeedSynchronizationTests, and BankStatementImportResumabilityTests.
The 25 new architecture/model cases guard unique configuration ownership,
source fields/defaults/check constraints/filtered indexes/query filters, and
company-scoped document/simulation/account relationships with restricted deletion.
Results: `artifacts/ef-config-consolidation/tests/ef-consolidation.trx`.

API and Web builds succeeded. API emitted 35 existing warnings; Web emitted none.
An initial parallel test build hit a Roslyn compiler crash in migration History2;
the final test/API builds succeeded with `-m:1 -p:UseSharedCompilation=false`.

`dotnet ef migrations has-pending-model-changes --project
src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj
--startup-project src/VirtualCompany.Api/VirtualCompany.Api.csproj --context
VirtualCompanyDbContext --no-build` succeeded and reported no changes since the
last migration. Existing EF model-validation warnings remain unchanged.

`git diff --check` passed. No migration or snapshot modification was needed.
No production/local database mutation, external provider operation, running-host
restart, commit, or deployment was performed.

## Reproduction

```powershell
dotnet run --project artifacts/ef-config-consolidation/probe/Probe.csproj -- artifacts/ef-config-consolidation/current
dotnet test tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter "FullyQualifiedName~PersistenceConfigurationOwnershipTests|FullyQualifiedName~PaymentBatchPersistenceModelTests|FullyQualifiedName~AccountingConfigurationPersistenceTests|FullyQualifiedName~FinanceInsightReadBoundaryTests|FullyQualifiedName~BankFeedSynchronizationTests|FullyQualifiedName~BankStatementImportResumabilityTests"
dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj --startup-project src/VirtualCompany.Api/VirtualCompany.Api.csproj --context VirtualCompanyDbContext --no-build
```
