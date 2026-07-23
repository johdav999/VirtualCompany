# Build Performance Measurement

Run the reproducible Infrastructure build benchmark from the repository root:

```powershell
.\scripts\measure-build-performance.ps1 -Iterations 3
```

The command uses the installed `dotnet` SDK, the `LocalRun` configuration, isolated output/intermediate directories, disabled build servers, disabled shared compilation, and a single MSBuild worker. It records clean builds, warm no-change builds, and timestamp-only edit scenarios for Finance, Sales, Support, an EF configuration, and a migration. Touched source timestamps are restored in `finally` blocks; source content is never changed.

Use `-ReportPath docs/build-performance-baseline.md` for a pre-refactor baseline and `-ReportPath docs/build-performance-after.md` for a post-refactor comparison. Raw logs and JSON are written below `.codex-build/build-performance/` and remain ignored build artifacts. Timing values are comparative evidence only. Compare results only when the machine, SDK, source state, package cache, configuration, and process count are equivalent.

The extracted graph supports narrow builds during backend development:

```powershell
dotnet build src\VirtualCompany.Infrastructure.Finance\VirtualCompany.Infrastructure.Finance.csproj --no-restore
dotnet build src\VirtualCompany.Infrastructure.Sales\VirtualCompany.Infrastructure.Sales.csproj --no-restore
dotnet build src\VirtualCompany.Infrastructure.Support\VirtualCompany.Infrastructure.Support.csproj --no-restore
dotnet build src\VirtualCompany.Infrastructure.Mailbox\VirtualCompany.Infrastructure.Mailbox.csproj --no-restore
dotnet build src\VirtualCompany.Infrastructure.Operations\VirtualCompany.Infrastructure.Operations.csproj --no-restore
```

Build `VirtualCompany.Api` when host composition, migrations, or multiple capabilities changed. The local launch scripts still build the complete startup project because they must produce a runnable application; they use `--no-restore`, isolated output directories, and a shared lock to avoid locked assembly failures.

Useful narrower runs:

```powershell
.\scripts\measure-build-performance.ps1 -ScenarioSet Warm -Iterations 3 -NoRestore
.\scripts\measure-build-performance.ps1 -ScenarioSet Edits -Iterations 3 -NoRestore
```

## Refactor Result

The post-refactor edit benchmark is checked in as `docs/build-performance-after.md`. On the same machine and SDK, median timestamp-only edit builds changed as follows:

| Ownership area | Baseline | Extracted project | Reduction |
| --- | ---: | ---: | ---: |
| Finance | 83.4 s | 39.3 s | 52.8% |
| Sales | 69.4 s | 29.9 s | 56.9% |
| Support | 79.5 s | 24.4 s | 69.3% |
| Persistence configuration | 73.2 s | 27.4 s | 62.5% |
| Migration | 74.4 s | 41.2 s | 44.6% |

The root `VirtualCompany.Infrastructure` facade now contains one C# source file. A focused Finance build compiles the inward dependency chain plus Finance, without compiling Mailbox, Sales, Support, Operations, API, or Web. Migration edits are measured against `VirtualCompany.Persistence.Migrations`; they do not trigger capability compilation. The one-run migration validation improved by 44.6%, below the 50% target because the migrations project still compiles the complete 187-file immutable migration history; extracting it nevertheless removes that cost from every capability edit and from the runtime facade.
