# Document repositories Prompt 7 UAT

## Product profile

```yaml
product: Virtual Company
type: web and Microsoft Graph background integration
revision: working tree on 2026-09-20
roles:
  - name: Requesting agent
    access: typed recommend and execute tool contracts
  - name: Human approver
    access: work approval detail
  - name: Company administrator
    access: repository settings and publication status
evidence:
  reference: docs/design/references/document-update-approval-reference.png
  reference_prompt: docs/design/references/document-update-approval-reference-prompt.md
  automated_backend:
    - tests/VirtualCompany.Api.Tests/CompanyDocumentPublicationRequestTests.cs
    - tests/VirtualCompany.Api.Tests/MicrosoftGraphDocumentRepositoryAdapterTests.cs
    - tests/VirtualCompany.Api.Tests/InternalCompanyToolRegistryTests.cs
  automated_ui:
    - tests/VirtualCompany.Web.Tests/DocumentUpdateApprovalDetailTests.cs
    - tests/VirtualCompany.Web.Tests/DocumentRepositorySettingsComponentTests.cs
flows:
  - id: UPDATE-001
    name: Review a whole-file text replacement
  - id: UPDATE-002
    name: Stop a replacement after a concurrent human edit
  - id: UPDATE-003
    name: Reconcile an ambiguous provider outcome
  - id: UPDATE-004
    name: Resolve a conflict through a new proposal and approval
  - id: UPDATE-005
    name: Publish the replacement as fresh indexed evidence
```

## Evidence packets

### UPDATE-001 — Review a whole-file text replacement

Expected: the approval binds the exact repository, folder, item, expected remote version, original evidence version, replacement size and SHA-256. The approver can download both immutable artifacts and open a bounded text diff; binary content is explained as whole-file replacement.

Observed: the typed `documents.prepare_update` and sensitive `documents.update` schemas separate staging from execution. The approval component renders original and proposed downloads, exact version fields, replacement hash, text-review link, and the whole-file warning. Pending and stale component fixtures pass. Result: **pass**.

### UPDATE-002 — Stop a replacement after a concurrent human edit

Expected: an update is never sent unconditionally. A changed Microsoft ETag produces a terminal conflict, preserves the human file, stales the approval, and appears in settings and work approvals.

Observed: the Graph adapter sends `If-Match` on the item-ID content replacement and maps 409/412 to a conflict with the current remote version. The worker records `conflict`, marks the approval stale, and throws a permanent outbox outcome. Deterministic adapter tests prove one conditional PUT and no overwrite retry. The settings component exposes the safe conflict message and approval route. Result: **pass in deterministic tests; live tenant check tracked separately**.

### UPDATE-003 — Reconcile an ambiguous provider outcome

Expected: a lost response is resolved only by reading the same item and comparing size and SHA-256. Exact bytes are delivered; a later human edit is unresolved and causes no write.

Observed: reconciliation tests prove both the exact replacement and equal-size/different-hash later-edit branches. Neither branch sends a PUT. The worker marks the latter `unresolved`, stales the approval, and permanently stops automatic retry. Result: **pass**.

### UPDATE-004 — Resolve a conflict through a new proposal and approval

Expected: conflict resolution captures the current permitted version into a new proposal and never reuses stale approval or bytes implicitly.

Observed: `stalePublicationRequestId` is accepted only for a conflict/unresolved update against the same item. Preparation captures a new original, records a new replacement hash and request identity, while preserving the stale request. Normal tool execution creates a new execution and approval. Result: **pass by contract/domain verification**.

### UPDATE-005 — Publish the replacement as fresh indexed evidence

Expected: after verified delivery, old chunks become unavailable before normal re-ingestion; only the new Microsoft version can become active evidence.

Observed: update completion marks all matching remote sources unavailable, deactivates active chunks, then queues idempotent normal synchronization. Audit metadata contains versions, identities and hashes without content. Result: **pass by implementation review and compilation**.

## Provider documentation check

Microsoft Graph v1.0 documents item-ID whole-file replacement at `PUT /drives/{drive-id}/items/{item-id}/content`. Microsoft’s OneDrive contract defines case-sensitive ETags in `If-Match` and 412 / `entityTagDoesNotMatch` when a precondition does not match the current resource. The implementation keeps the 4 MiB bounded simple-upload limit, uses the exact ETag on the mutating request, and does not offer force overwrite. Selected-permission compatibility must still be demonstrated in the target tenant because consent and resource assignment are tenant configuration, not deterministic test behavior.

## Issue ledger

| ID | Severity | Flow | Type | Summary | Acceptance / regression | Status |
|---|---|---|---|---|---|---|
| UAT-UPDATE-001 | P1 verification gate | UPDATE-002 | environment | A live Microsoft 365 two-writer run was not possible without a tenant, selected resource assignment, and secret-store credential. | Prepare/approve at version A, human-edit to B, dispatch, verify provider 412 and unchanged B; then repropose from B and require a new approval. | blocked |
| UAT-UPDATE-002 | P1 verification gate | UPDATE-003 | environment | A real transport-loss-after-commit scenario was not injected against Microsoft Graph. | Drop the response after commit, reconcile exact bytes as delivered; repeat with a later human edit and verify unresolved/no PUT. | blocked |

No deterministic product defect remains open. The two blocked rows are release-environment checks, not permission to claim live provider validation.

## Automated verification

- Focused Prompt 7 backend tests: **27 passed, 0 failed**.
- Focused approval/settings UI tests: **7 passed, 0 failed**.
- API and Web project builds: passed with existing warnings.
- EF `has-pending-model-changes`: no changes since the last migration.
- Prompt 6-to-7 SQL Server migration script: generated successfully; nine update fields added and existing rows default to `operation_kind = create`.
- Full solution build: blocked by seven pre-existing `CS1705` .NET 9/10 dependency-version errors in Infrastructure Platform, Mailbox, and Finance test projects.