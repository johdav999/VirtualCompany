# Infrastructure Refactor Validation

Validated on 2026-07-22 with .NET SDK 9.0.315 and the `LocalRun` configuration.

## Passing checks

- Architecture and dependency-injection tests: 21 passed.
- Finance focused tests: 11 passed.
- Sales source focused tests: 6 passed.
- Support grounding focused tests: 5 passed.
- Clean `VirtualCompany.Api` build: succeeded in 3m13s with 32 existing compiler warnings and no errors.
- Clean `VirtualCompany.Web` build: succeeded in 1m04s with 9 existing compiler/Razor warnings and no errors.
- EF Core pending-model check: no model changes since the latest migration.
- Migration discovery: first migration, latest migration, snapshot, and dedicated migrations assembly were discovered.
- Local SQL Server smoke test: API started against `localhost\\SQLEXPRESS`; `/health/ready` returned HTTP 200 and reported the database healthy.
- Repository diff check: no whitespace errors; Git reported only line-ending conversion notices.
- Dependency-injection audit: mandatory services resolve and hosted services are registered once.

## Build performance

The three-run median comparison is recorded in `docs/build-performance-baseline.md` and `docs/build-performance-after.md`.

| Edit scenario | Before | After | Improvement |
| --- | ---: | ---: | ---: |
| Finance | 83,366 ms | 39,305 ms | 52.8% |
| Sales | 69,360 ms | 29,884 ms | 56.9% |
| Support | 79,511 ms | 24,374 ms | 69.3% |
| Persistence configuration | 73,153 ms | 27,396 ms | 62.5% |

A migration-only harness run completed in 41,218 ms, 44.6% faster than the prior migration edit scenario. It now compiles only `VirtualCompany.Persistence.Migrations`; the remaining cost is the immutable 187-migration history in that assembly.

## Environment and existing-suite limitations

- A full solution restore is blocked by the optional `VirtualCompany.Mobile` project because the local SDK does not have the `maui-android` workload installed (`NETSDK1147`). API and Web paths do not require that workload.
- Docker SQL Server runtime validation is unavailable on this PC because hardware virtualization is unavailable. Docker SQL configuration and restore paths remain unchanged, and local SQL Server uses the same EF model and migration history.
- The broad API suite exposes existing shared-database/global-filter assumptions. SQLite migration compatibility tests now discover the dedicated SQL Server migrations assembly but cannot execute SQL Server-specific migration SQL with SQLite.
- The broad Web component suite reports existing bUnit registrations missing `IMoneyFormatter`; the failures cascade across components and are unrelated to the infrastructure project graph.
- The Web contract suite produced no compiler or test output for several minutes and was stopped as inconclusive. This is not counted as a passing check.

These limitations must not be interpreted as skipped production validation: the affected API/module builds, focused architecture and capability tests, EF model check, and local SQL Server runtime startup were validated directly.
