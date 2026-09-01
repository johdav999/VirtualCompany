# Finance agent operational proposal tools

## Boundary

Laura can prepare nine reviewable operational proposal types: close-task assignment, close or compliance evidence request, compliance evidence checklist, audit-package definition, accounting schedule, currency revaluation, fixed-asset addition, fixed-asset disposal, and fixed-asset depreciation.

Every response carries the target type, current target version, canonical proposal SHA-256, source evidence, proposed changes, deterministic blockers, required approvals, expected downstream effects, and allowed next actions. Tool execution and its complete response are retained by the shared Finance conversation execution contract. Schedule drafts, revaluation previews, tasks/handoffs, and audit-package requests additionally retain their own owning workflow history and idempotency records.

The model may propose descriptions, owners, and coordination details. The owning services remain authoritative for close eligibility and materiality, compliance requirements, audit scope snapshots, schedule calculations and posting previews, exchange rates and revaluation reconciliation, fixed-asset class rules, disposal posting previews, and depreciation calculations.

## Guarded actions

Four execute tools are registered, each requiring Accounting Administrator authority, explicit review, current proposal evidence, P0 confirmation/approval policy, and a fresh target/hash check:

- submit a current schedule or FX proposal to its existing independent approval workflow
- assign an eligible current close task through the owning close service
- create an idempotent typed evidence task and optional agent handoff
- request a frozen audit-package definition, which remains pending independent approval before background generation

No operational proposal tool can post accounting, run asset depreciation posting, dispose or register an asset, activate schedule occurrences, close/lock/reopen a period, perform year-end rollover, file or sign a statutory obligation, approve its own work, change provider credentials, authorize downloads, or deliver externally.

## Audit package generation and recovery

Audit preview computes the exact scope hash and snapshot-version JSON through `IAuditPackageService` and returns `ArtifactGenerated = false`. The guarded request is idempotent and creates a `pending_approval` package only. A different reviewer must approve the retained scope before the existing background worker can generate it.

The owning audit-package workflow writes the archive to protected object storage, records manifest and package checksums, enforces retention and one-time download authorization, exposes attempts and safe failures, retries classified transient/object-storage failures, and never marks a failed, incomplete, corrupt, inaccessible, or missing package as downloadable final success.

## Staleness, segregation, and evidence

Execute tools recompute the current proposal from the owning record. Any target-version, source, calculation, checksum, approval, or policy change yields `operational_proposal_stale` and requires a fresh review. Evidence requests create work but never satisfy a requirement. Close assignment never completes or signs off a task. Compliance checklists explicitly retain that evidence is not proof of filing, receipt, approval, or statutory compliance.
