# Immutable audit package technical-readiness report — 2026-08-30

Release classification: `technically_verified_for_human_review`  
Human decision: `pending`  
Statutory approval: **No**

This is engineering evidence for Prompt 6. It does not replace review or approval by a qualified Swedish accountant, auditor, tax specialist, security reviewer, or records-management owner.

## Frozen source identity

| Field | Frozen value |
|---|---|
| Evaluation date | `2026-08-30` |
| Repository base commit | `25d68a4a38e668d49b75740053da00c92505748b` |
| Working tree | Contains the ordered Prompt 1–6 implementation changes; evidence applies to that uncommitted tree, not the base commit alone |
| Package scope | `period_close` / `audit-package-v1` |
| Migration | `20260830220000_AddImmutableAuditPackages` |
| UI reference | `docs/design/references/audit-packages-workspace-reference.png` |
| Swedish VAT specification | `sweden-domestic-vat-launch-2026.1` |
| BAS source SHA-256 | `a86b247fe0d882c0de75fe508050906c49c1a6f28a14f0c3ae7201232392328a` |
| BAS catalogue SHA-256 | `2ed42830420e6afccf66ea548188107551714d8e1bd0646c3574812653772efc` |

The Swedish accounting evidence verifier classified its frozen pack as `technically_verified_for_human_review`, with 13 verified checks and 0 failed checks. Its human decision remains pending and its statutory-approval flag remains false.

## Blocking findings

No Prompt 6 technical blocker was found in the focused build, migration-model, deterministic-archive, recovery, authorization, and UI contract checks. Human accountant review, security review of production storage and access settings, retention-owner approval, and live environment recovery evidence remain required release decisions.

## Technical audit table

| Checkpoint | Evidence reviewed | Technical status | Human decision | Finding / action |
|---|---|---|---|---|
| Scope identity and idempotency | Scope hash, version snapshot, idempotency-key persistence and tests | `verified` | `pending` | Same company/period/scope/version/source snapshot resolves to the same logical package; changed evidence identity produces a different hash. |
| Independent approval | Domain guards, finance-approval policy, API authorization tests | `verified` | `pending` | Requester self-approval is rejected; generation begins only after approval. |
| Evidence coverage | Collector projections and manifest tests | `verified` | `pending` | Period ledger/reporting, VAT/tax, reconciliations, significant journals, approvals, close history, provider exceptions, policy identity, and accessible documents are represented. |
| Access preservation | Membership checks, document access-policy evaluation, redaction test | `verified` | `pending` | Packages do not broaden company/document access and do not include raw provider payloads or credentials. |
| Deterministic archive | Archive builder and repeated-build tests | `verified` | `pending` | Ordering, ZIP timestamps, paths, human index, machine manifest, item hashes, manifest hash, and package hash are deterministic. |
| Finality | Missing/inaccessible/corrupt manifest handling and tests | `verified` | `pending` | Any required finding produces `incomplete`; it is never labeled final. |
| Bounded background processing | Worker, batch/page/document/package limits, retry history | `verified` | `pending` | Claims and reads are bounded; retries have a fixed maximum and exponential delay. |
| Crash and cancellation recovery | Generation lease, expired-lease test, pre/post-storage cancellation checks | `verified` | `pending` | Expired work is reclaimable; cancellation before finalization deletes a just-written object when necessary. |
| Download authorization | One-time hashed token, expiry, actor/company checks, no-store response | `verified` | `pending` | Download requires a short-lived one-time authorization and returns both checksum headers. |
| Verification and restore | ZIP/package/manifest/item/DB cross-check logic and corrupt-item tests | `verified` | `pending` | Missing or corrupt bytes produce an invalid retained verification result; runbook requires hash verification after restore. |
| Tenant isolation | Company-scoped keys, query filters on package child entities, service membership checks | `verified` | `pending` | Reads and writes remain company scoped even where workers use `IgnoreQueryFilters`. |
| Audit and telemetry | Audit event writer and finance meter definitions | `verified` | `pending` | Request, approval, cancellation, generation, download, and verification are audited; request/generation/duration/verification metrics are emitted. |
| Migration | EF migration list and pending-model check | `verified` | `pending` | Prompt 6 migration is ordered after Prompt 5 and EF reports no pending model changes. Existing unrelated EF model warnings remain. |
| Accountant-facing surface | Generated design reference, route/navigation integration, Razor contract tests | `verified` using automated substitute | `pending` | Workspace exposes status, evidence findings, source versions, checksums, attempts, approvals, verification, and authorized download. Signed-in browser UAT remains an environment check. |

## Verification results

| Verification | Result |
|---|---|
| Audit-package finance tests | 12 passed, 0 failed |
| Audit-package API authorization tests | 5 passed, 0 failed |
| Audit-package Web surface tests | 2 passed, 0 failed |
| Finance infrastructure build | Succeeded with 0 errors; existing warnings only |
| EF model comparison | No pending model changes |
| EF migration ordering | Prompt 6 listed after `20260830211000_AddComplianceObligationDefinitions` |
| Swedish frozen-evidence verifier | 13 verified, 0 failed; human review pending |
| Full solution Debug/Release build | Toolchain blocked in `VirtualCompany.Persistence.Migrations` by a .NET 9.0.300 Roslyn `AccessViolationException`; focused Prompt 6 project builds, migration compilation, EF model comparison, and migration SQL generation succeeded |

## Limitations and revalidation triggers

- No live production SQL migration, object-store restore, legal-hold exercise, large-volume performance run, or signed-in browser UAT was performed in this repository verification.
- The full solution build currently encounters a Roslyn compiler process failure in the large migration assembly in both Debug and Release. This is not a C# diagnostic from the Prompt 6 code, but it remains a repository/toolchain issue to resolve before treating a solution-wide build as green.
- No package or software control proves that the accounting content is legally sufficient, complete for a particular audit engagement, filed, accepted by an authority, or professionally approved.
- Production operators must validate object-store durability, backup/restore, encryption, access logs, retention, legal hold, monitoring, and alert routing in the target environment.
- Revalidate after changes to package scope/version, source projections, report definitions, policy-pack identity/hash, archive format, checksum algorithm, worker lease/retry behavior, storage provider, authorization, download-token policy, retention, migration, or accountant-facing UI.

## Release conclusion

Prompt 6 is `technically_verified_for_human_review` for the implemented immutable `period_close` evidence-package boundary. It is not statutory approval or a signed professional opinion. Final release status remains `human_accountant_review_pending` until the named human reviews and production-environment controls are completed.
