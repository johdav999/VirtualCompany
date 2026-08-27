# Swedish accounting Release 1 evidence and decision

Evidence date: 2026-08-25 (Europe/Stockholm)

Revision baseline: `fd4d268` plus the uncommitted Release 1 implementation.

## Release decision

**Blocked — do not enable or market Swedish statutory compliance.**

The latest registered Swedish pack is `sweden-statutory-candidate` version `1.3.0`, definition SHA-256 `24def3648fda889ddb35f05893034b7480c6ddb5a5bb1fb4e295bc986ae41d5a`, and `IsStatutoryComplianceValidated = false`. No signed or attributable evidence from a qualified Swedish accounting professional was supplied. `AccountingPolicyPackValidationEvidenceCatalog.All` is therefore intentionally empty. The application starts, but the company accounting-readiness response blocks a Swedish release with `policy_pack_validation = blocked`; user-facing surfaces must retain review-pending wording.

This document records engineering evidence only. It is not professional accounting advice, government approval, or statutory certification.

## Delivered release controls

- Bounded reviewer-evidence metadata covers exact pack key/version/hash, reviewer identity/reference, scope/date, evidence document reference/hash, approved fixture IDs, limitations, expiry, and revalidation triggers.
- Startup refuses a pack that declares statutory validation without current exact-hash evidence. A changed definition evaluates as `definition_hash_mismatch`; expired evidence evaluates as `evidence_expired`.
- Evidence cannot be enabled through runtime environment configuration. A reviewed release requires a new immutable pack version and a checked-in catalog record.
- Global health reports the gate as operational without making an expected candidate state an infrastructure outage. Bounded health data reports registered and validated Swedish pack counts and exact pack states.
- Company accounting readiness now reports exact policy validation, statutory-profile completeness, stale VAT returns, failed or expired statutory exports, and explicitly unsupported configured capabilities in addition to the existing accounting signals.
- Fixture-driven tests execute all eight checked-in VAT golden cases and all 18 documented unsupported boundaries. Unsupported cases return no rule identity, accounts, boxes, taxable basis, tax, or gross fallback.

## Golden inputs

- Specification: `sweden-domestic-vat-launch-2026.1`
- VAT runtime definition (pack `1.1.0`) SHA-256: `f81f0a5d7480d84b54c92541aaa23133006c568fc525634dfd7ebb94ce2b4fc2`
- Golden-fixture file SHA-256: `9306be95d459db5e11478cf7a416173a0fc72fd36f28272f63ed9f1b6f8e30a7`
- Unsupported-scenario file SHA-256: `a04ff836bbd3c77d485a8816e773e32ae3fb341240f7cad2ab50c8580f938ff9`
- SIE sample SHA-256: `fc66a49c9e913c818c6e564c59cd639fe4e0e608f1ccff6a00fe90db675e39c3`

The executable scenario ownership is split by the narrowest test boundary: `SwedishAccountingReleaseGoldenScenarioTests` runs the complete source fixture sets; the existing statutory-profile, document-numbering, customer/supplier posting, VAT return, SIE/archive, recovery, worker, authorization, tenant-isolation, API, and Web suites cover their production owners.

## Commands and results

| Command | Result |
| --- | --- |
| `dotnet test tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj --no-restore --filter "FullyQualifiedName~AccountingStatutoryReadinessTests\|FullyQualifiedName~AccountingPolicyPackValidationTests\|FullyQualifiedName~SwedishAccountingReleaseGoldenScenarioTests\|FullyQualifiedName~AccountingPolicyPackTests"` | Final run passed 21, failed 0. |
| `dotnet test tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj --no-restore --no-build` | Final run passed 242, skipped 7 environment-gated SQL/migration tests, failed 0. |
| `dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore` | Passed, 0 errors; existing warnings remain. |
| `dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore` | Passed, 0 errors; existing warnings remain. |
| `dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore` | Passed 386, failed 0. |
| `dotnet test tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj --no-restore` | Passed 16, failed 0. |
| `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore --no-build` | Final rerun passed 2,032, skipped 2 opt-in SQL Server integrity/performance tests, failed 0. The preceding diagnostic run exposed three health failures from initially mapping missing review evidence to HTTP 503; after correcting that boundary, the four affected health/rate-limit tests passed 4/4 before this full green rerun. |
| `dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj --startup-project src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-build` | Passed: `No changes have been made to the model since the last migration.` Existing EF model warnings remain. |
| `git diff --check` | Passed; Git reported only line-ending conversion warnings. |

Sandboxed `--no-restore` runs also reported NU1900 because NuGet vulnerability metadata was unreachable. Compilation and tests used the existing restored packages.

## Browser evidence

Prompt 6 authenticated evidence is retained in [accounting-r1-prompt6-uat-evidence.md](accounting-r1-prompt6-uat-evidence.md), with desktop and narrow screenshots in `docs/finance/uat-evidence/`. It covers the English VAT workspace, responsive behavior, Swedish localized component rendering, role/action states, review/finalization/correction/export variants through API and component tests, and the limitation that the live company remained country-neutral. The browser transport could not complete Blazor negotiation, so live click transitions and a real Swedish configured tenant were not claimed.

## Migration and recovery status

Release 1 migrations are additive and ordered as follows:

1. `20260824083718_AddSwedishStatutoryProfileFoundation`
2. `20260824100709_RetainDeterministicTaxFacts`
3. `20260824133301_AddSwedishStatutoryDocumentControls`
4. `20260824143033_AddSwedishVatReturnWorkflow`
5. `20260825092127_AddSwedishStatutoryAccountingArchive`

The model has no pending change. The recovery verifier and statutory-archive tests retain journal, tax, return-package, archive metadata, object hashes, approvals, audits, and deterministic checksums. A new coordinated local and Docker SQL Server restore rehearsal was not executed in this Prompt 7 run, so no new restore proof is claimed. Under the release criteria, missing current local/Docker restore proof is an independent release stop even after professional review is later supplied.

## Residual release stops

- Qualified Swedish accounting review evidence is absent.
- No new reviewed pack version exists; every Swedish pack remains explicitly unvalidated.
- Current coordinated local and Docker restore proof has not been produced for the complete Release 1 migration/object set.
- Authenticated browser evidence does not cover a real Swedish-configured company through every setup, VAT finalization/correction, export, and recovery transition in both languages.
- Environment-gated SQL Server migration, concurrency, integrity, recovery, and capacity tests were skipped in the general Finance/API runs and must pass in the release environment.
- Customer-facing compliance wording must remain limited to an unvalidated engineering candidate with explicit unsupported cases and no authority-submission claim.

Application rollback must keep the additive schema and all evidence, disable new capability selection, and forward-fix. Do not drop statutory tables, mutate historical pack definitions or tax facts, edit finalized returns or exports, delete review records, or renumber accounting documents.
