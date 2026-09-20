# Microsoft 365 document repositories

Virtual Company can register a company-owned Microsoft 365 document repository, browse an explicitly approved root, queue a bounded initial import, and keep imported evidence synchronized through Microsoft Graph. Connections are read-only by default. An administrator can separately enable approval-bound creation and whole-file replacement in one designated output folder. Imported files pass through the existing storage, malware scan, extraction, chunking, embedding, and indexing pipeline. A connection is a company publication boundary: application access granted in Microsoft 365 does not mean that every employee or agent may use the content.

## Access model

- Authentication is application-only. Consumer Microsoft accounts and delegated employee tokens are not supported.
- Every connection belongs to one Virtual Company company, has the explicit `company` audience, and starts with no agent grants unless agent IDs are deliberately supplied.
- Company owners and administrators manage connections. All reads remain server-scoped to the active company.
- The application never provisions Microsoft permissions, widens consent, changes sharing, deletes remote content, or deletes a shared application credential when a connection is disconnected.
- A disconnected connection is immediately unusable. Later import/synchronization workers must recheck this lifecycle state before doing work.

A dedicated SharePoint site and document library is the recommended company-owned source. OneDrive for Business folders are supported for organizations that deliberately assign an application to that folder.

## Application identity and secrets

Create a single-tenant Entra application for the intended Microsoft 365 tenant. Record its directory tenant ID and application (client) ID. Store its client secret in the configured `IPlatformSecretStore`; the API receives only that secret name as `credentialReference` and SQL stores only the reference.

Production should use Azure Key Vault:

```text
PlatformSecrets__Provider=azure_key_vault
PlatformSecrets__KeyVaultUri=https://<vault-name>.vault.azure.net/
```

Grant the API managed identity read access to the secret. Rotate by adding a new secret version under the same name. Token acquisition checks the secret version and partitions cached app-only tokens by directory tenant, application ID, and credential reference; a new version therefore replaces the cached credential without changing the connection. Keep the prior version enabled until validation succeeds, then retire it according to the organization's rollback policy.

Client secrets are the supported credential method for this endpoint. They must never be put in request bodies, SQL, appsettings, logs, or audit metadata. Workload-identity federation is not silently substituted and requires a future explicit implementation.

## Microsoft permission and endpoint matrix

Microsoft Selected permissions require both Entra administrator consent and a separate assignment on the target resource. Consent alone grants no resource access. Virtual Company reports `missing_resource_grant` on a 403 and never falls back to tenant-wide read permission. See Microsoft's [Selected permissions overview](https://learn.microsoft.com/en-us/graph/permissions-selected-overview), [drive metadata API](https://learn.microsoft.com/en-us/graph/api/drive-get), [drive item metadata API](https://learn.microsoft.com/en-us/graph/api/driveitem-get), and [list children API](https://learn.microsoft.com/en-us/graph/api/driveitem-list-children).

| Source | Recommended application consent and assignment | Runtime endpoints |
|---|---|---|
| SharePoint document library | `Sites.Selected` with `read` assigned to a dedicated site. Use a separately reviewed `write` assignment only when approved agent output is enabled. `Lists.SelectedOperations.Selected` assigned to the exact library is an acceptable narrower alternative when the tenant has verified drive endpoint compatibility. | Read endpoints plus create `PUT /drives/{drive-id}/items/{folder-id}:/{filename}:/content` and conditional replacement `PUT /drives/{drive-id}/items/{item-id}/content` |
| OneDrive for Business folder | `Files.SelectedOperations.Selected` with `read` assigned to the approved root. Assign `write` only to the designated output folder when approved agent output is enabled. The administrator supplies the already granted drive and folder item IDs. | Read endpoints plus the same create and conditional replacement content endpoints |

Do not grant `Files.Read.All`, `Sites.Read.All`, or a write scope merely to make discovery work. Virtual Company has no broad discovery dependency: administrators provide the pre-granted drive ID and root item ID. A write assignment is justified only for the explicit output folder and is rechecked immediately before delivery. Microsoft permission provisioning must be performed independently by an authorized Microsoft 365 administrator.

Microsoft's current v1.0 `driveItem: delta` permission table lists `Files.Read.All` as the least-privileged application permission and does not list Selected application permissions. Virtual Company therefore uses delta only when that endpoint succeeds under the connection's already approved permission profile. A 403 from delta does not trigger broader consent: the worker performs a bounded full enumeration under the existing Selected grant and commits removals only after every page succeeds. An expired delta cursor also stages a complete reconciliation before unseen files are made unavailable. See Microsoft's [drive delta documentation](https://learn.microsoft.com/en-us/graph/api/driveitem-delta?view=graph-rest-1.0).

## Runtime configuration

The global Microsoft Graph cloud is the default:

```json
{
  "MicrosoftGraphDocumentRepositories": {
    "BaseUrl": "https://graph.microsoft.com/v1.0/",
    "RequestTimeoutSeconds": 30,
    "MaxProviderPagesPerBrowse": 20,
    "MaxAncestryDepth": 256,
    "MaxImportItems": 500,
    "AllowedDownloadHostSuffixes": [ ".sharepoint.com", ".onedrive.com" ]
  },
  "CompanyDocumentVirusScanner": {
    "Enabled": true,
    "Host": "clamav",
    "Port": 3310,
    "TimeoutSeconds": 30
  },
  "DocumentRepositoryImports": {
    "Enabled": true,
    "PollIntervalSeconds": 10
  },
  "DocumentRepositorySynchronization": {
    "Enabled": true,
    "PollIntervalSeconds": 30,
    "SynchronizationIntervalMinutes": 15,
    "LeaseSeconds": 120,
    "MaxAttempts": 8,
    "InitialRetrySeconds": 30
  },
  "DocumentRepositoryOperations": {
    "Enabled": true,
    "RetentionEnabled": true,
    "CleanupIntervalMinutes": 360,
    "AbandonedStagedArtifactDays": 30,
    "TerminalArtifactDays": 90,
    "SupersededChunkDays": 30,
    "CleanupBatchSize": 100
  }
}
```

Run a maintained ClamAV daemon reachable from the API and configure signature updates and health monitoring. Repository imports fail closed when scanning is unavailable, times out, or detects malware; they never become searchable through the placeholder scanner. Configure `KnowledgeEmbeddings:Provider` to `openai`, supply its credential through deployment secrets, and leave `AllowDeterministic` false. Deterministic embeddings are test-only.

Downloads are bounded by `CompanyDocuments:MaxUploadBytes`. Unsupported extension/MIME combinations and unsafe or missing source links become explicit per-item failures. Provider redirects are accepted only over HTTPS to configured Microsoft download-host suffixes. Stable Microsoft web links are persisted for citations; bearer and pre-authenticated download URLs are not persisted.

Supported Graph cloud hosts are `graph.microsoft.com`, `graph.microsoft.us`, `dod-graph.microsoft.us`, and `microsoftgraph.chinacloudapi.cn`. HTTPS is mandatory. Continuation URLs are accepted only from the configured Graph authority and within the configured drive; redirects or paging links to other hosts are rejected.

## API lifecycle

All routes require the company-admin policy and resolved company context:

- `POST /api/companies/{companyId}/document-repositories` registers a pending connection. It remains read-only unless `enableWrites` and a designated `writableFolderItemId` are both supplied.
- `PUT /api/companies/{companyId}/document-repositories/{connectionId}` reconfigures it using `expectedConcurrencyVersion` and resets validation.
- `GET` collection and item routes return sanitized state and never return the credential reference.
- `POST .../{connectionId}/validate` performs real token acquisition and validates the drive and root folder.
- `GET .../{connectionId}/browse?parentItemId=...&maxItems=...` enumerates at most 200 children after resolving item ancestry by opaque item IDs. Remote-item shortcuts and drive changes fail closed.
- `POST .../{connectionId}/disconnect` requires the current concurrency version and does not delete remote content or the shared secret.
- `POST .../{connectionId}/imports` accepts an administrator-supplied idempotency key and queues durable work.
- `GET .../{connectionId}/imports/{jobId}` returns job totals and per-item success, unchanged, retryable failure, or permanent failure details.
- `POST .../{connectionId}/synchronizations` queues an idempotent resync; `forceFullReconciliation` bypasses a saved delta cursor without widening Microsoft permissions.
- `GET .../{connectionId}/synchronizations/{jobId}` returns durable mode, attempts, queue/retry state, observed/changed/removed/failed totals, and safe failure details.
- `POST .../{connectionId}/synchronizations/{jobId}/cancel` cancels queued or resumable synchronization work.
- `POST .../{connectionId}/pause` changes exactly one scope—`retrieval`, `synchronization`, or `writes`—using the current connection concurrency version.
- `POST .../{connectionId}/recovery/retry-failed-items` queues an idempotent complete reconciliation after scanner, parser, or embedding recovery. It also reprocesses a same-version document whose prior ingestion/indexing did not complete.
- `POST .../publications/{publicationRequestId}/reconcile` queues outbox-backed provider reconciliation for an uncertain create/update. It never performs an unconditional write in the request handler.
- GET /api/companies/{companyId}/document-repositories/publications/{publicationRequestId} returns sanitized durable publication state. Its /content route provides the immutable staged artifact to authorized administrators for approval review.

Validation states distinguish invalid credentials, missing resource grants, missing resources, throttling, provider availability, and boundary violations. Provider response bodies, access tokens, download URLs, and paging URLs are never returned or persisted.

## Administrator and user experience

Open **Settings → Document repositories** to manage the publication boundary. The page uses only live API state; it does not simulate a connected provider when the API, scanner, embedding service, or Microsoft grant is unavailable.

1. Choose SharePoint document library or OneDrive for Business and enter the pre-granted Microsoft tenant, application, drive/library, and starting folder identifiers.
2. Enter the name of the credential already stored in the platform secret store. Never paste a client secret into the form.
3. Select the explicit company audience and the agents allowed to search and cite the source. No agents are selected by default.
4. Save and validate the resource. A missing Microsoft resource assignment is shown separately from invalid credentials or provider availability; fix the grant in Microsoft 365 and then validate again.
5. Browse within the validated boundary, select the published root, and start the initial import. The page reports scanning/indexing work, partial failures, indexed totals, validation freshness, synchronization freshness, and the next safe action.
6. Use **Sync now** for ordinary refresh or the full-resynchronization recovery action after repairing a dependency. Disconnect immediately removes the source from new agent retrieval without deleting Microsoft files or shared credentials.

Repository access can also be reached from **Settings → Agents → Document repository access**. Agent answers and Support knowledge retain safe source provenance; an unavailable source remains unavailable rather than silently degrading into an uncited answer. Read-only members see an access explanation and cannot invoke management endpoints because the API independently enforces the company-admin policy.

## Approval-bound agent publication

Agent publication is a two-tool workflow. `documents.prepare_create` is a recommendation-classified operation that accepts bounded base64 content, computes a SHA-256 hash, writes an immutable staged artifact, and returns a durable publication request. It accepts no URL or provider identity. `documents.create` is a sensitive execute-classified operation that can reference only that staged request and must repeat its repository, folder, filename, size, and hash exactly. The shared policy and approval pipeline always pauses this tool for a current human decision.

The approval review displays the exact filename, repository, designated folder item ID, byte size, SHA-256 hash, content type, and a staged preview/download link. Approval authorizes one create attempt for those immutable values; it does not authorize overwrite, rename, relocation, arbitrary URL retrieval, broader repository writes, or later content changes.

Delivery is asynchronous through the transactional company outbox. Immediately before Microsoft Graph is called, the worker rechecks tenant context, active connection state, designated output folder, current agent grant, the snapshotted policy decision and exact approval version, and the staged byte length and content hash. It sends `If-None-Match: *`, so an existing name is a permanent collision rather than an overwrite. The initial contract caps staged output at 4 MiB, deliberately keeping it in the bounded simple-upload path; larger artifacts are rejected until a resumable upload-session implementation receives separate operational review.

Publication status moves through `staged`, `queued`, `sending`, `reconciliation_required`, `failed`, and `delivered`. Provider item ID, version, web URL, attempt count, approval/execution identity, and safe failure details are persisted. A timeout or connection loss after upload is treated as ambiguous: the retry first searches the approved folder for the exact filename and verifies size and SHA-256 content before recording success or attempting another create. A mismatching existing item fails closed. Stable idempotency keys and provider reconciliation prevent duplicate files.

After delivery, an idempotent normal repository synchronization is queued so the new file enters the existing scan, extraction, indexing, provenance, and availability pipeline. Synchronization does not call publication tools, preventing write/read feedback loops. Preparation, queueing, delivery, reconciliation outcomes, and policy decisions are audited without document bodies, credentials, tokens, or provider response bodies.

## Approval-bound whole-file replacement and conflicts

`documents.prepare_update` binds a proposal to the connection, exact drive item, filename, designated writable folder, current remote ETag, original evidence version, and immutable SHA-256 hashes of both the captured original and proposed replacement. The service captures the original bytes with the expected version, stores original and proposed artifacts separately, and rejects a proposal if the target moved, left the writable boundary, or changed before capture. `documents.update` is a sensitive execute action and must repeat every bound identifier, version, size, and replacement hash exactly.

The work-approval detail provides downloads for the captured original and proposed artifact. Supported text files also receive a bounded line diff. Binary files explicitly state that approval replaces the whole file; approval is never described as a merge. The approval authorizes only the reviewed replacement of the reviewed remote version.

The durable worker rechecks the active connection, agent grant, writable-folder boundary, exact approval version, staged hash and size, and current remote version. It then replaces content by item ID with `If-Match` set to the approved ETag. Microsoft documents the [small-file replacement endpoint](https://learn.microsoft.com/en-us/graph/api/driveitem-put-content?view=graph-rest-1.0), case-sensitive ETag use in [`If-Match`](https://learn.microsoft.com/en-us/onedrive/developer/rest-api/concepts/case-sensitivity?view=odsp-graph-online), and `412 Precondition Failed` / `entityTagDoesNotMatch` for a failed conditional request in the [OneDrive error contract](https://learn.microsoft.com/en-us/onedrive/developer/rest-api/concepts/errors?view=odsp-graph-online). A 409 or 412 is terminal for that proposal: the worker records `conflict`, marks the approval stale, and never retries without the condition or overwrites the human version.

To resolve a conflict, fetch the current permitted item version and call `documents.prepare_update` again with the stale publication request ID. This creates a separate proposal with a new original capture, replacement hash, execution, policy decision, and approval. The prior proposal and its safe audit evidence remain unchanged; neither its approval nor its replacement bytes are reused implicitly.

A timeout or transport loss after the conditional update begins produces `reconciliation_required`. Reconciliation reads the same item and compares current size and SHA-256 bytes. An exact replacement is recorded as delivered. A missing item, mismatching bytes, or any later version becomes `unresolved`, makes the approval stale, and requires human review; the system never follows ambiguity with an unconditional write. Repository settings show the latest `conflict` or `unresolved` replacement and link back to work approvals.

After verified replacement, every tracked remote source for the replaced item and its active chunks is made unavailable before a normal synchronization is queued. The new Microsoft version must pass the ordinary download, malware scan, extraction, embedding, and atomic publication pipeline before agents can cite it. Audit entries contain only company, connection, item, versions, hashes, request/approval/execution identities, status and safe failure codes—not original or replacement content.
## Synchronization and stale-access behavior

- Delta next links and delta links are stored as opaque checkpoints only after the corresponding page changes are durably applied. They are accepted only from the configured Graph authority and the configured drive/root delta path.
- Full reconciliation marks each observed item with a new generation. Unseen documents are not removed when enumeration or any item processing fails; availability changes are committed only after a complete successful pass.
- A newly observed remote version immediately makes the previous chunk set unavailable. The replacement is downloaded, scanned, parsed, embedded and atomically published; an older or failed attempt cannot restore stale evidence.
- Folder deletion or movement outside the approved root invalidates tracked descendants. Disconnect, local agent-grant removal, remote denial, deletion, provider validation failure, or a version mismatch all fail closed.
- The same asynchronous availability gate protects document list/detail reads, semantic retrieval, Support grounding, and cached grounding sections. It performs Graph validation outside the synchronous local policy evaluator. Previously delivered responses cannot be retroactively withdrawn, but old snippets are not reused for a new response.
- Retryable failures use bounded exponential backoff and honor a longer Microsoft `Retry-After` value. Jobs use expiring leases and resume from the last committed page after interruption.

Metrics are emitted for synchronization completion, queue age, item failures, and authorization failures. Audit entries contain the company/connection/job identity, mode, attempts, counts and safe outcome; they never contain document bodies, credentials, bearer URLs or raw Graph responses.

## Agent evidence tools

An active company-audience connection grants no agent access by default. Administrators must add each agent ID to the connection's established grant list. A grant makes the read-classified `knowledge.search`, `documents.list`, and `documents.read` tools available through normal agent authority and policy evaluation. Removing the grant, disconnecting the connection, or losing the remote resource grant takes effect on the next invocation.

- `knowledge.search` keeps its existing uploaded-document behavior and also searches permitted repository chunks. It combines semantic similarity with filename/title and exact-term keyword matches, then ranks only evidence that passed local access and live remote validation.
- `documents.list` returns at most 50 permitted documents per page. Its opaque cursor and internal repository/document handles are bounded and cannot be replaced by a provider URL or credential reference.
- `documents.read` accepts only an internal document handle and returns at most 8,000 characters per request with an opaque continuation cursor. Results include the stable source link, content version, and chunk citations.

Tool payloads never select a company, user, agent, arbitrary URL, or credential; those identities come from the trusted execution context. Responses distinguish `no_match`, `source_unavailable`, and `not_yet_indexed` for sources the agent is allowed to know about. Missing and ungranted handles use the same `not_found` outcome so restricted titles and snippets are not disclosed.

Repository text is classified as `untrusted_evidence` and every response sets `mayAuthorizeActions` to false. Instructions contained in a document may be quoted as evidence but do not change tool permissions, invoke execute-classified tools, or satisfy an approval requirement. Audit metadata records correlation, outcome, internal source identifiers, and repository handles without recording document bodies, secrets, access tokens, or download URLs.

Support continues to use the shared grounding contract. If permitted evidence is unavailable or insufficient, it must retain the existing review/knowledge-gap outcome rather than present an ungrounded answer as authoritative.

## Operational verification

1. Confirm the Entra application has only the selected read permission needed for the source.
2. Confirm a Microsoft 365 administrator assigned `read` to the exact site, library, or folder.
3. Register the connection with explicit drive and root item IDs and an empty agent list initially.
4. Validate and confirm the returned repository and root names.
5. Browse the root, a nested folder, and attempt an unrelated item ID; the unrelated item must be rejected.
6. Disconnect and confirm validation and browse are denied.
7. Review the business audit entries; they contain identifiers and safe outcomes but no secret or raw provider response.
8. Queue synchronization, edit one file, move one file outside the root, and revoke the resource grant. Confirm the edit replaces the active chunk set and both the moved and revoked sources disappear from document reads, semantic search, Support grounding and cached agent grounding.
9. In a live two-writer test, prepare and approve a replacement at version A, make a human edit to version B, then dispatch. Confirm Microsoft returns a conditional conflict, version B remains intact, settings and work approvals show the stale conflict, and resolving it creates a new approval bound to version B.
10. Simulate a lost response after a successful conditional replacement and confirm reconciliation records delivery only when the current bytes match the approved SHA-256. Make a later human edit before reconciliation and confirm the request becomes unresolved without another write.

Live smoke verification is separate from deterministic adapter and API tests and requires an Entra application, a Microsoft 365 tenant, an explicit resource assignment, and a secret present in the platform secret store.

## Operator recovery semantics

The **Operator recovery** section on each repository card reports queue age, last successful synchronization, stale leases, scanner/embedding health, throttling, retryable item count, and unresolved publication count. Metrics use fixed instrument names and do not use company, connection, file, or provider item IDs as labels.

- Pausing **Agent retrieval** immediately denies list, detail, semantic search, agent tools, Support grounding, and cached-grounding reuse for remote sources. Synchronization and approved writes may continue.
- Pausing **Synchronization** preserves queued imports and sync jobs but prevents workers from claiming them and prevents new manual sync/import requests. Retrieval continues from evidence that still passes live provider validation; approved writes remain separate.
- Pausing **Approved writes** preserves staged/queued artifacts. Dispatch is deferred, and the original approval/version, agent grant, destination, bytes, hash, and connection are rechecked when work resumes. Retrieval and synchronization continue.
- **Disconnect** is stronger than every pause: it immediately denies retrieval and makes queued remote work ineligible. It does not delete Microsoft files or revoke the shared Entra credential.
- **Feature disable** (`DocumentRepositoryOperations:Enabled=false`) fail-closes remote retrieval, validation/browse, imports, synchronization, publication preparation/delivery, and reconciliation while leaving ordinary uploaded knowledge and unrelated agent tools available.

Recovery commands are company-admin-only and optimistic-concurrency protected where they change connection state. Repeated failed-item retry/full-resync requests use stable caller idempotency keys and return the already active/durable job. Reconciliation returns an uncertain publication to `reconciliation_required` and enqueues a new outbox attempt; delivery still rechecks current approval and never bypasses provider concurrency.

## Retention and cleanup

The retention worker runs in bounded batches. It deletes only local data:

- staged artifacts that were abandoned without queueing/approval after `AbandonedStagedArtifactDays` (the durable request becomes `expired`)
- local proposed/original artifacts for delivered or permanently failed requests after `TerminalArtifactDays`
- inactive chunk rows older than `SupersededChunkDays` when their document has a newer current chunk-set version

Artifacts for `queued`, `sending`, `reconciliation_required`, or `unresolved` publications are never eligible. Remote Microsoft documents are never deleted. Repository downloads are streamed through bounded memory and do not create persistent temporary download files. Business audit rows, provider identities, hashes, statuses, and safe failure evidence remain after local artifact cleanup.

Disconnect removes the local authorization path but cannot revoke a shared application secret or Microsoft resource grant. Separately, an Entra/secret-store administrator must remove the selected resource assignment, disable or delete the client secret version, and validate that token acquisition no longer succeeds when credential revocation is required.

## Alerts and recovery playbook

Alert on sustained growth in `document_repository.sync.queue_age_seconds`, repeated `document_repository.sync.item_failures`, `document_repository.sync.authorization_failures`, stale running leases, degraded scanner/embedding status, repeated throttling, and any unresolved publication. Do not include file names, content, URLs, credentials, or provider payloads in alert labels.

1. For scanner/embedding failures, restore the dependency and choose **Retry failed items**. Verify the new full reconciliation completes before declaring recovery.
2. For an expired cursor, interrupted scan, or suspected missed change, choose **Run full resync**. Unseen sources are removed only after the complete enumeration succeeds.
3. For throttling, leave the job in bounded retry and honor Microsoft `Retry-After`; do not widen permissions or start repeated manual jobs.
4. For an uncertain upload, choose **Reconcile uncertain upload**. Exact bytes become delivered; mismatching/later content stays unresolved and requires a new proposal/approval.
5. For a stale lease, allow the expiring lease to be reclaimed once. Repeated stale leases require worker/database health investigation, not duplicate command submission.
6. For suspected unauthorized access, disconnect first, then revoke the Microsoft resource grant and credential separately.

## Database deployment and rollback

EF migrations are applied from `VirtualCompany.Persistence.Migrations` with `VirtualCompany.Api` as startup project. For a local SQL Server:

```powershell
dotnet ef database update --project src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj --startup-project src/VirtualCompany.Api/VirtualCompany.Api.csproj --context VirtualCompanyDbContext
```

For Docker SQL Server, start the supported SQL Server container, point the API connection string at that container, and run the same migration command from the host/container deployment job. Both paths use the same SQL Server migration assembly and history; do not use `EnsureCreated` or ad-hoc DDL.

For operational rollback, first set `DocumentRepositoryOperations:Enabled=false`, stop repository import/synchronization workers, and leave the additive repository tables/columns and object storage intact. Roll back application binaries only after confirming older binaries tolerate the already-applied schema. Do not migrate down or delete repository/upload data as a release workaround. Re-enable by deploying compatible binaries, applying any pending migration, restoring scanner/embedding/secret dependencies, validating one bounded source, and then resuming synchronization/writes explicitly.
