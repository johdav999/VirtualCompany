# Microsoft 365 document repository production verification

Date: 2026-09-20  
Revision: current working tree (Prompts 1–8 are uncommitted together)  
Environment: local Windows development; deterministic Microsoft Graph adapter; no staging Microsoft tenant or production-compatible embedding credential supplied

## Product and UAT profile

```yaml
product: Virtual Company
type: web and background integration
roles:
  - name: Company administrator
    access: authenticated API integration fixture and deterministic Blazor component fixture
  - name: Agent
    access: company-scoped repository evidence tools under explicit grant
evidence:
  reference: docs/design/references/document-repository-operator-recovery-reference.png
  reference_prompt: docs/design/references/document-repository-operator-recovery-reference-prompt.md
  backend: tests/VirtualCompany.Api.Tests/CompanyDocumentRepositoryIntegrationTests.cs
  retention: tests/VirtualCompany.Api.Tests/CompanyDocumentPublicationRequestTests.cs
  web: tests/VirtualCompany.Web.Tests/DocumentRepositoryApiClientTests.cs and DocumentRepositorySettingsSurfaceTests.cs
flows:
  - id: OPS-001
    name: Pause and resume synchronization
  - id: OPS-002
    name: Deny all retrieval while retrieval is paused
  - id: OPS-003
    name: Retry failed items without duplicate jobs/documents
  - id: OPS-004
    name: Reconcile an uncertain approved upload
  - id: OPS-005
    name: Retain pending artifacts and clean eligible local data
  - id: OPS-006
    name: Disable and roll back the feature without affecting uploaded knowledge
```

## Evidence packets

### OPS-001 — Scoped pause/resume

Expected: an administrator changes one scope using optimistic concurrency; synchronization pause preserves work and blocks new imports/sync while retrieval and writes retain their independent state.

Observed: API integration coverage verifies pause, stale-version conflict, resume, and recovery command behavior. Worker code defers queued/running synchronization when the persisted pause is present. Result: **pass in deterministic integration tests**.

### OPS-002 — Fail-closed retrieval

Expected: a paused/disconnected/disabled/revoked source is absent from document access, agent tools, Support grounding, and cached evidence reuse.

Observed: the shared remote availability gate checks feature enablement, lifecycle, persisted retrieval pause, local grant, live remote access, and version before every retrieval path. The integration test imports evidence, pauses retrieval, and receives not-found from direct document access. Existing Prompt 3–4 tests cover revocation/tool/Support paths. Result: **pass in deterministic tests**.

### OPS-003 — Failed-item recovery

Expected: one durable full reconciliation is returned for repeated idempotency keys and same-version documents with failed ingestion/indexing are reprocessed.

Observed: the recovery endpoint delegates to the synchronization service with full-reconciliation mode. Duplicate calls return the same job; the same-version fast path now requires processed/indexed state before skipping. Result: **pass**.

### OPS-004 — Uncertain publication reconciliation

Expected: the request handler queues durable reconciliation, approval is rechecked in the worker, exact bytes can become delivered, and mismatching/later bytes cause no unconditional write.

Observed: the admin endpoint transitions only uncertain requests to reconciliation-required and enqueues the existing publication outbox topic with a recovery idempotency identity. Existing Prompt 6–7 adapter/domain tests cover exact-match, mismatch, conflict, and no-PUT reconciliation. Result: **pass in deterministic tests; live provider check blocked**.

### OPS-005 — Retention

Expected: cleanup removes only eligible local artifacts/inactive superseded chunks and preserves pending approval/reconciliation plus all remote content and audit evidence.

Observed: bounded retention queries exclude queued, sending, reconciliation-required, unresolved, and current chunk sets. Domain tests prove abandoned staged artifacts expire and reconciliation artifacts reject purge. No Microsoft delete API is present. Result: **pass in implementation/domain verification**.

### OPS-006 — Feature disable and rollback

Expected: disabling remote repositories stops new retrieval/jobs/writes without changing ordinary uploads or deleting repository state.

Observed: `DocumentRepositoryOperations:Enabled=false` is checked by the availability gate, provider validation/browse, imports, synchronization, publication preparation/delivery, and reconciliation. It does not modify uploaded-document paths. The runbook defines additive-schema rollback and forbids destructive reset. Result: **pass by contract and implementation review**.

## UI reference comparison and issue ledger

The implemented settings card follows the generated reference: an Operator recovery section contains separate retrieval/synchronization/write rows, plain-English pause consequences, queue/sync/lease/dependency/throttling health, and bounded recovery actions. The existing two-column card/attention layout remains intact; recovery panels stack at 980 px and become single-column 44 px touch targets at 680 px.

The strongest safe local substitute was used because no staging identity/test account or Microsoft resource grant was supplied. Component, surface, and typed-client tests validate success, permission, recovery, and responsive structure. A real browser screenshot and keyboard/touch replay against an authenticated running application remain a release-environment check.

| ID | Severity | Flow | Summary | Acceptance | Status |
|---|---|---|---|---|---|
| UAT-OPS-001 | P1 verification gate | OPS-001–006 | Authenticated browser UAT was not possible without a local/staging identity and seeded repository state. | Capture desktop and 390×844 views, operate every pause/recovery control by keyboard, and compare spacing/hierarchy with the stored reference. | blocked by environment |
| UAT-OPS-002 | P1 verification gate | full scenario | No staging tenant/library, Selected permission assignment, secret-store credential, scanner, or production embedding credential was supplied. | Connect → import/cite → edit/sync → revoke/deny → approved create → two-writer update conflict → reconciliation, recording only non-sensitive identities. | blocked by external access |

No deterministic product defect remains open from the Prompt 8 recovery flows.

## Commands and results

- `dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore`: **passed**, existing warnings only.
- `dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore`: **passed**, existing warnings only.
- Focused API recovery/security/retention tests: **19 passed, 0 failed**.
- Focused Web client/component/surface tests: **12 passed, 0 failed**.
- `dotnet ef migrations add AddDocumentRepositoryOperations ...`: generated SQL Server migration `20260920083235_AddDocumentRepositoryOperations` with only three pause timestamps and one local-artifact-purge timestamp.

- EF `has-pending-model-changes`: **passed**; no model changes exist beyond the generated migration.
- Idempotent SQL Server script from Prompt 7 to Prompt 8: **generated successfully** and contains only `retrieval_paused_at`, `synchronization_paused_at`, `writes_paused_at`, and `local_artifacts_purged_at` additions plus migration history.
- Agent/tool/retrieval/access-policy/Graph regressions: **40 passed, 0 failed**.
- Support grounding safety regression: **5 passed, 0 failed** using the existing built output after NuGet signature lookup was network-blocked during the first no-restore build attempt.
- `git diff --check`: **passed**; only repository line-ending conversion notices were emitted.
- Static recovery logging review found no document bodies, credentials, tokens, refresh tokens, provider payloads, or remote URLs in new Prompt 8 logs. Recovery/retention metrics use no per-company, connection, or file labels.
- Broader `dotnet build VirtualCompany.sln --no-restore`: **blocked outside Prompt 8** by the same seven existing `CS1705` .NET 9/10 dependency-version conflicts in Infrastructure Platform, Mailbox, and Finance test projects. API/Web and all affected Prompt 8 projects build successfully.

Live-provider results remain blocked until performed in the designated staging Microsoft tenant. SQL Server script/model validation passed; applying the migration and exercising lease concurrency against a running local/Docker SQL Server instance remains an environment check because no database endpoint was supplied.
