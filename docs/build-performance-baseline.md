# Build Performance Baseline

Generated: 2026-07-22 18:06:55 UTC

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
| Clean-Api | 242031 | 3 | Domain, Application, Infrastructure, Api |
| Clean-Application | 39998 | 3 | Domain, Application |
| Clean-Domain | 10864 | 3 | Domain |
| Clean-Infrastructure | 93406 | 3 | Domain, Application, Infrastructure |
| Clean-Web | 82157 | 3 | Web |
| Edit-Finance | 83366 | 3 | Infrastructure |
| Edit-Migration | 74419 | 3 | Infrastructure |
| Edit-PersistenceConfiguration | 73153 | 3 | Infrastructure |
| Edit-Sales | 69360 | 3 | Infrastructure |
| Edit-Support | 79511 | 3 | Infrastructure |
| Warm-Api | 7336 | 3 | None reported |
| Warm-Application | 8388 | 3 | None reported |
| Warm-Domain | 4605 | 3 | None reported |
| Warm-Infrastructure | 5987 | 3 | None reported |
| Warm-Web | 7856 | 3 | None reported |

## Source Counts

| Project | C# files |
| --- | ---: |
| VirtualCompany.Api | 150 |
| VirtualCompany.Application | 177 |
| VirtualCompany.Domain | 216 |
| VirtualCompany.Infrastructure | 635 |
| VirtualCompany.Mobile | 26 |
| VirtualCompany.Shared | 73 |
| VirtualCompany.Web | 161 |

## Project Dependency Graph

- **Domain**: None
- **Application**: VirtualCompany.Domain, VirtualCompany.Shared
- **Infrastructure**: VirtualCompany.Application, VirtualCompany.Domain
- **Api**: VirtualCompany.Application, VirtualCompany.Infrastructure, VirtualCompany.Shared
- **Web**: VirtualCompany.Shared

Raw run data and logs: `.codex-build\build-performance\20260722171746454` (ignored build artifact).
