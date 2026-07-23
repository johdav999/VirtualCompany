# Build Performance Report

Generated: 2026-07-22 20:54:13 UTC

This report is comparative evidence, not a fixed performance gate. Re-run on the same machine, SDK, configuration, package cache, process count, and source state after each assembly-boundary change.

## Environment

- SDK: 9.0.315
- Configuration: LocalRun
- OS: Microsoft Windows 10.0.26200 
- Processor: Intel64 Family 6 Model 140 Stepping 1, GenuineIntel
- Memory: Unavailable GB
- Iterations per measured scenario: 3
- Process isolation: build servers disabled, shared compilation disabled, parallel build disabled

## Median Timings

| Scenario | Median (ms) | Runs | Projects reported as compiled |
| --- | ---: | ---: | --- |
| Edit-Finance | 39305 | 3 | Infrastructure.Finance |
| Edit-Migration | 41218 | 1 | Persistence.Migrations |
| Edit-PersistenceConfiguration | 27396 | 3 | Persistence |
| Edit-Sales | 29884 | 3 | Infrastructure.Sales |
| Edit-Support | 24374 | 3 | Infrastructure.Support |

## Source Counts

| Project | C# files |
| --- | ---: |
| VirtualCompany.Api | 80 |
| VirtualCompany.Application | 128 |
| VirtualCompany.Domain | 162 |
| VirtualCompany.Infrastructure | 1 |
| VirtualCompany.Infrastructure.Finance | 102 |
| VirtualCompany.Infrastructure.Mailbox | 17 |
| VirtualCompany.Infrastructure.Operations | 81 |
| VirtualCompany.Infrastructure.Platform | 31 |
| VirtualCompany.Infrastructure.Sales | 19 |
| VirtualCompany.Infrastructure.Support | 28 |
| VirtualCompany.Mobile | 14 |
| VirtualCompany.Persistence | 126 |
| VirtualCompany.Persistence.Migrations | 188 |
| VirtualCompany.Shared | 10 |
| VirtualCompany.Web | 103 |

## Project Dependency Graph

- **Domain**: None
- **Application**: VirtualCompany.Domain, VirtualCompany.Shared
- **Persistence**: VirtualCompany.Application, VirtualCompany.Domain
- **Persistence.Migrations**: VirtualCompany.Persistence
- **Infrastructure.Platform**: VirtualCompany.Application, VirtualCompany.Domain, VirtualCompany.Persistence
- **Infrastructure.Mailbox**: VirtualCompany.Application, VirtualCompany.Domain, VirtualCompany.Infrastructure.Platform, VirtualCompany.Persistence
- **Infrastructure.Finance**: VirtualCompany.Application, VirtualCompany.Domain, VirtualCompany.Infrastructure.Platform, VirtualCompany.Persistence
- **Infrastructure.Sales**: VirtualCompany.Application, VirtualCompany.Domain, VirtualCompany.Infrastructure.Platform, VirtualCompany.Persistence
- **Infrastructure.Support**: VirtualCompany.Application, VirtualCompany.Domain, VirtualCompany.Infrastructure.Platform, VirtualCompany.Persistence
- **Infrastructure.Operations**: VirtualCompany.Application, VirtualCompany.Domain, VirtualCompany.Infrastructure.Platform, VirtualCompany.Persistence
- **Infrastructure**: VirtualCompany.Infrastructure.Platform, VirtualCompany.Infrastructure.Mailbox, VirtualCompany.Infrastructure.Finance, VirtualCompany.Infrastructure.Sales, VirtualCompany.Infrastructure.Support, VirtualCompany.Infrastructure.Operations
- **Api**: VirtualCompany.Application, VirtualCompany.Infrastructure, VirtualCompany.Persistence.Migrations, VirtualCompany.Shared
- **Web**: VirtualCompany.Shared

Raw run data and logs: `.codex-build\build-performance\20260722204702952` (ignored build artifact).

The migration row is a one-run harness validation recorded after correcting the target from the compatibility facade to `VirtualCompany.Persistence.Migrations`; its raw data is under `.codex-build\build-performance\20260722205558910`. The capability and Persistence configuration rows are three-run medians.
