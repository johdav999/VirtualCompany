# Finance controller split verification

The former `InternalFinanceController` had 63 constructor dependencies and 36
capability partial files. Its endpoints now belong to 35 independently activated
`InternalFinance<Capability>Controller` classes. Invoice review commands remain
with invoice queries and actions. Constructors take 2–11 dependencies, including
the common initialization-problem handler and their own logger.

Shared HTTP execution, actor/correlation context, and stable problem mappings
live in the abstract `InternalFinanceControllerBase` partials. The base has no
public actions and no persistence or capability-service dependencies. The scoped
`FinanceInitializationProblemHandler` preserves the existing missing-dataset
fallback, cancellation, initialization response, and durable audit behavior.
Request/response types retain their namespaces and names; 178 transport types
are assigned to 33 capability contract files. Shared customer-invoice request
mapping has one implementation.

## Contract and behavior evidence

All 395 internal Finance routes match the pre-refactor compiled baseline for
route templates, HTTP methods, action names, parameter names/types/defaults and
binding attributes, return types, controller context/auth attributes, and action
authorization attributes. This inventory includes the existing separate internal
approval and cash-posting-backfill controllers. Compiler-generated async state
machine and nullability encoding attributes are excluded from the HTTP metadata
comparison. They change when code moves between types.

The baseline was captured before moving the endpoints. Its raw and normalized
forms, the final manifest, dependency inventory, comparison, and reusable probe
are under `artifacts/finance-controller-split`. The normalized baseline is also
an executable regression fixture under API tests. New boundary tests assert
independent DI activation, MVC discovery without duplicate routes, unchanged
FinanceView/context protection, and the shared base's narrow dependencies.
Existing endpoint-policy tests now inspect their owning controller; their
authorization assertions are preserved.

Final validation:

- API: 162 passed, 0 failed, 0 skipped. Covers controller boundaries, accounting
  endpoint policies, initialization commands, sandbox controls/transparency,
  payments/allocations, period reporting, and insight refresh authorization.
- Web/API contracts: 70 passed, 0 failed, 0 skipped, with external SQL Server and
  accounting performance categories excluded. Covers invoice/bill/transaction
  responses, invoice review, tenant isolation, and initialization responses with
  fallback enabled and disabled, including audit persistence.
- Web source surface: 4 passed, 0 failed, 0 skipped.
- API and Web compile. API's three existing allocation-return nullability
  warnings remain; Web builds without warnings.
- EF reports no model changes since the last migration. No migration, snapshot,
  database mutation, provider call, host restart, commit, or deployment was made.
- `git diff --check` passes. Prior finance-query, EF-configuration, and test-project
  refactor work remains in the checkout.

The complete API suite and external-provider/SQL Server lanes were not run.
Earlier exploratory contract checks included generated async type names and
failed on that compiler metadata; the final baseline comparison and test runs
above pass.

## Reproduction

```powershell
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter "FullyQualifiedName~FinanceControllerBoundaryTests|FullyQualifiedName~ApiSurfaceTests|FullyQualifiedName~AuditPackageAuthorizationTests|FullyQualifiedName~CurrencyRevaluationAuthorizationTests|FullyQualifiedName~FinanceEntryInitializationIntegrationTests|FullyQualifiedName~FinanceSandboxAdminEndpointIntegrationTests|FullyQualifiedName~FinancePaymentsIntegrationTests|FullyQualifiedName~FinancePaymentAllocationsIntegrationTests|FullyQualifiedName~FinancePeriodReportingIntegrationTests|FullyQualifiedName~FinanceInsightsIntegrationTests"
dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter "Category!=SqlServer&Category!=AccountingPerformance"
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter "FullyQualifiedName~AccountingAuthoritySurfaceTests"
dotnet run --project artifacts/finance-controller-split/Probe/Probe.csproj -- "C:\Users\Johan\source\repos\Virtual Company" routes-current.json
dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj --startup-project src/VirtualCompany.Api/VirtualCompany.Api.csproj --context VirtualCompanyDbContext --no-build
```
