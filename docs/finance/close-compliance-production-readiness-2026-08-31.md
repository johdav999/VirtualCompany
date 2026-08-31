# Close and compliance production readiness — 2026-08-31

## Decision

**NO-GO.** Prompt 10’s fail-closed decision path is implemented, but this workstation does not hold all release proof required to promote the combined close/compliance flow. Current-revision authenticated EN/SV browser evidence, coordinated SQL/object restore evidence, provider-boundary approval, and qualified Swedish accountant approval are not present. The automated small-profile accounting capacity result passed, but a production-shaped large-package/report operator record is still required by the final verifier. Prior Prompt 9 SQL migration proof is useful regression evidence but cannot be relabelled as current Prompt 10 release approval.

## Implemented release controls

- Company-scoped `AccountingAdmin` readiness endpoint with active company-context enforcement.
- Ten deterministic signals: overdue and blocked close tasks, unresolved reconciliations, stale reports, missing sign-offs and task/readiness evidence, incomplete packages, compliance ambiguity, accountant-access anomalies, and failed rollover.
- Explicit remediation, evidence time, and source drill-downs for every signal.
- Stable SHA-256 over company, period, ordered signals, evidence times, and ordered source links. Evaluation time is excluded, so an unchanged restore replays the same hash.
- Cross-company grant/engagement data becomes a non-disclosing anomaly and can never make a company ready.
- Readiness telemetry for evaluation duration/count and release-stop count.
- A schema-version-2 proof-matrix manifest with retained TRX paths, evidence checksum, explicit release stops, and exit code 2 for incomplete proof.
- A final verifier that accepts only company/period-bound operator records, compares restored hash/source links, validates the supported manual-provider boundary, and hashes all final evidence files.

## Deterministic combined scenario

`CloseComplianceReleaseReadinessPolicyTests.Deterministic_year_end_release_reopens_for_subsequent_event_then_preserves_corrected_restore_proof` composes the combined scenario after the matrix executes the underlying subledger and workflow suites. Its ordered stages are receivables, payables, bank, fixed assets, accruals/deferrals, currency revaluation, inventory adjustment, payroll adjustment, report suite, tax/compliance, close, audit package, accountant review, lock, and rollover. It then injects a subsequent event, proves stale reports/missing approval/incomplete package yield no-go, applies a new governed readiness snapshot and sign-off, and proves an unchanged restored state reproduces the corrected hash and source links.

The close-compliance matrix also runs the feature-level report, tax, close governance, package, year-end, accountant isolation, API authorization, and Web contract suites so the orchestration test is not treated as a substitute for the engines it summarizes.

## Verification map

| Proof | Automated entry | Current disposition |
| --- | --- | --- |
| Policy, deterministic scenario, missing evidence, date boundary | `CloseComplianceReleaseReadinessPolicyTests` | Implemented; focused Finance run passed. |
| Admin route, read-only surface, company context | `CloseComplianceReleaseReadinessApiSurfaceTests` | Implemented; focused API run passed 2/2. |
| Full month/year close domains | `close-compliance-hermetic` matrix result | Required for final manifest. |
| Role and tenant penetration | `close-compliance-api-isolation` plus authenticated accountant UAT | Automated suite and live proof both required. |
| Fresh/upgrade, SQL concurrency/rollback | `close-compliance-sqlserver` | Required; missing connection produces release stop. |
| Large reports/packages and SLO | `close-compliance-capacity` | Small profile required; medium is not a supported claim. |
| Worker restart, missing/corrupt object, coordinated restore | `close-compliance-recovery` operator record | Required and must preserve the API hash/source links. |
| EN/SV, finance/accountant, narrow, accessibility, timezone/date | `close-compliance-browser` operator record | Required on an owned authenticated host. |
| Submission provider boundary | `close-compliance-provider-scope` record | Must approve only `export_and_manual_evidence_only`; direct submission is unsupported. |
| Statutory/professional conclusions | `close-compliance-professional-review` record | Qualified human approval required. |

## Current-revision executed evidence

The retained current-revision manifest is `artifacts/close-compliance-proof/20260831-prompt10-release/matrix-manifest.json`, generated at `2026-08-31T05:24:24Z`, with evidence checksum `439c8d0120695508d599a76d7c72715b49a81e422b7a6f4d7bde1d1d541ef2eb`.

| Result | Tests | Outcome |
| --- | ---: | --- |
| Finance close/compliance hermetic | 53 | Passed |
| API authorization and tenant isolation | 31 | Passed |
| Web close/accountant/compliance/package/year-end contracts | 11 | Passed |
| API SQL fresh/upgrade/migration/integrity | 3 | Passed against isolated LocalDB |
| Finance SQL migration/concurrency/rollback | 11 | Passed against isolated LocalDB |
| Small-profile accounting capacity/SLO | 1 | Passed against isolated LocalDB |

The matrix decision is `no_go` with exactly four retained stops: coordinated recovery, authenticated browser UAT, provider-scope approval, and qualified professional review. The full solution build succeeded with 0 errors and 246 existing warnings.

## Swedish accounting evidence pre-audit

The deterministic Swedish evidence verifier classified the frozen BAS/VAT pack as `technically_verified_for_human_review`: 13 checkpoints verified, 0 failed, `humanDecision: pending`, and `statutoryApproval: false`. It confirmed workbook/source SHA-256 `a86b39937fab280d4e5db895c04c2af6e145695863d4845ff14eea5d0302328a`, catalogue SHA-256 `2ed4f76eca5655bb62d77be4b30dbc6f511afa67d6470656b24a75b441672efc`, manifest artifact hashes, supported domestic 25% rules/boxes, golden fixtures, unsupported boundaries, and retained `review_pending` gates.

This is engineering pre-audit evidence only. It does not satisfy `close-compliance-professional-review`, does not activate statutory validation, and is not a qualified accountant’s opinion. Any policy pack, catalogue, fixture, hash, source edition, limitation, or implementation change requires re-verification and fresh attributable human review.

## Commands

```powershell
dotnet restore VirtualCompany.sln -p:NuGetAudit=false
dotnet test tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~CloseComplianceReleaseReadinessPolicyTests
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~CloseComplianceReleaseReadinessApiSurfaceTests
./scripts/test-matrix.ps1 -Lane close-compliance-proof -NoRestore
dotnet build VirtualCompany.sln --configuration Release --no-restore
```

For a release candidate, configure dedicated SQL Server and the `small` performance profile before the matrix. After completing the five external records, run the verifier documented in [close-compliance-production-operations.md](../runbooks/close-compliance-production-operations.md). A failed command, a `not-run` required lane, a backend `no_go`, or an unsigned evidence record leaves this report no-go.
