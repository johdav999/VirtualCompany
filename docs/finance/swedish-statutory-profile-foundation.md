# Swedish statutory profile foundation operations

The `AddSwedishStatutoryProfileFoundation` migration is additive. It creates the company-scoped `company_statutory_profiles` table, tenant-scoped identifier indexes, relational verification/source metadata, and an optimistic concurrency version. Existing country-neutral companies and accounting records are not backfilled or changed.

Both local SQL Server and Docker SQL Server use the same `VirtualCompany.Persistence.Migrations` history. After either restore path, apply pending migrations with `VirtualCompany.Persistence.Migrations` as the migrations project and `VirtualCompany.Api` as the startup project. No provider-specific DDL or separate Docker step is required. On application rollback, preserve the additive table and its audit evidence and forward-fix; do not down-migrate after statutory profiles have been saved.

The `sweden-statutory-candidate` pack version `1.0.0` is intentionally not statutorily validated. Its deterministic definition hash identifies an immutable candidate, but it enables no Swedish VAT, statutory reporting, statutory export, or native statutory invoice capability. Operators must not describe format validation, user attestation, or stored external-source metadata as government verification or qualified reviewer approval.

Use the established migration commands:

```powershell
dotnet ef database update --project src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj --startup-project src/VirtualCompany.Api/VirtualCompany.Api.csproj --context VirtualCompanyDbContext
dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj --startup-project src/VirtualCompany.Api/VirtualCompany.Api.csproj --context VirtualCompanyDbContext
```
