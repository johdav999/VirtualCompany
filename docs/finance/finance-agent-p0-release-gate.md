# Finance agent P0 release gate

This document defines the repeatable release proof for the Finance agent authorization boundary. It is an engineering gate, not a statutory opinion or qualified-accountant approval.

## Mandatory checkpoints

| Checkpoint | Command owner | Passing condition | Failure effect |
| --- | --- | --- | --- |
| Authority matrix and adversarial authorization | `VirtualCompany.Api.Tests` Finance P0 filter | All registered Finance actions have complete actor/grant/risk metadata; all forged contexts deny without provider mutation | `no_go` |
| Approval, fault, replay, and continuation proof | `VirtualCompany.Api.Tests` Finance P0 filter | Authorization, approval, audit, transaction/outbox persistence, duplicate delivery, stale binding, and ambiguous-result checks pass | `no_go` |
| Hermetic solution matrix | API, Finance, Web, Web contract test projects | Every mandatory project completes with zero failed or skipped P0 tests | `no_go` |
| Release build | `dotnet build VirtualCompany.sln -c Release` | Exit code zero | `no_go` |
| Localization | Web localization and authority surface tests | English and Swedish resources resolve and required keys match | `no_go` |
| SQL and migrations | EF pending-model check plus SQL Server P0 tests | No pending model changes; migration/concurrency/rollback tests pass on a disposable SQL Server | `no_go` |
| Swedish accounting evidence verifier | `verify_virtual_company_evidence.py` | Technical evidence layout verifies; human review status remains truthful | `no_go` for the governed Swedish launch scope |

The gate cannot average a failed security checkpoint against passing tests. If the SQL connection is absent, a required test is skipped, the evidence verifier reports a gap, or qualified review is still pending for a scope that requires it, the generated decision remains `no_go`.

## Evidence handling

Run `scripts/verify-finance-agent-p0.ps1` from the repository root. It creates machine-readable JSON and a Markdown summary beneath `artifacts/finance-agent-p0/`. The evidence records the current Git revision, dirty-state flag, a SHA-256 checksum over tracked changes and untracked file contents, every command and exit code, TRX counts, skipped and failed tests, and a checksum over the final manifest.

Never edit a generated green result. Re-run the script after any source, policy, migration, fixture, test, or evidence change. Preserve the JSON manifest alongside the build artifact being evaluated.

## Release classification

The strongest engineering conclusion before attributable human accounting review is `technically_verified_for_human_review` with `human_accountant_review_pending`. That classification does not assert statutory compliance, filing correctness, or a signed professional opinion. The generated P0 decision may be green for the software authorization boundary while the separate Swedish launch decision remains held for human review.
