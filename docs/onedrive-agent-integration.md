# Microsoft 365 document repositories

Virtual Company can register a company-owned Microsoft 365 document repository, browse an explicitly approved root, queue a bounded initial import, and keep imported evidence synchronized through Microsoft Graph. Connections are read-only by default. An administrator can separately enable approval-bound creation and whole-file replacement in one designated output folder. Imported files pass through the existing storage, malware scan, extraction, chunking, embedding, and indexing pipeline. A connection is a company publication boundary: application access granted in Microsoft 365 does not mean that every employee or agent may use the content.

## Access model

- Repository runtime authentication is application-only. The guided platform-managed setup uses a short-lived delegated Microsoft administrator credential only inside its expiring onboarding session; it never becomes an agent or repository runtime credential. Consumer Microsoft accounts are not supported.
- Every connection belongs to one Virtual Company company, has the explicit `company` audience, and starts with no agent grants unless agent IDs are deliberately supplied.
- Company owners and administrators manage connections. All reads remain server-scoped to the active company.
- Prompt 10 discovery is read-only and never provisions Microsoft permissions, changes sharing, deletes remote content, or creates a repository connection. Resource assignment remains a separately reviewed finalization step.
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

### Platform-managed administrator authorization

The guided **Connect Microsoft 365** foundation uses one production multi-tenant Entra application owned by the Virtual Company deployment. Register the exact callback URI configured in `Microsoft365DocumentOnboarding:CallbackUri`; wildcard, HTTP (except loopback development), fragment, and browser-supplied redirect URIs are rejected. The authorization request uses the organizational endpoint, authorization code with PKCE, nonce and the v2-supported `prompt=consent`. Permissions that require administrator approval still require an eligible Microsoft administrator; the prompt value itself is not `admin_consent`. The callback validates the signing key, issuer, audience, tenant, nonce, initiating Virtual Company user and continuing company-admin membership.

The setup scopes include delegated `Files.ReadWrite` for bounded OneDrive/folder discovery and the later reviewed folder-permission assignment, plus `Sites.Read.All` for SharePoint site and library discovery. Prompt 10 uses this authority only for bounded reads; prompt 11 uses it only after explicit review confirmation to assign the selected application. It is temporary setup authority, not the repository runtime permission. The deployment application separately carries administrator consent for `Files.SelectedOperations.Selected`. Consent alone grants no folder access; the runtime receives access only after the reviewed assignment succeeds.

Onboarding state is stored as queryable company/user/status metadata plus Data Protection ciphertext. State handles are SHA-256 hashed; nonce, PKCE verifier and delegated tokens are never stored in ordinary columns or returned to the browser. Cancellation, expiry and callback failure clear the ciphertext. Protect the shared Data Protection key ring as production credential material and retain prior keys long enough for the maximum onboarding lifetime during rotation.

Store the platform client secret under `Microsoft365DocumentOnboarding:CredentialReference` in `IPlatformSecretStore`. Rotate it by adding a new secret-store version under the same reference, verify a new authorization start and app-only token acquisition, then retire the prior version. Changing the client ID, credential mode, redirect URI or authority is a reviewed deployment change. The current implementation supports `client_secret_reference`; certificate, managed identity and workload-identity modes must not be configured until their explicit token exchange implementation exists.

For local Development only, keep the application secret in ASP.NET user secrets as `Microsoft365DocumentOnboarding:DevelopmentClientSecret`. Startup copies it into the encrypted local `IPlatformSecretStore` entry named by `CredentialReference`; it is never written to repository configuration or logs. Configure `PlatformClientId`, the exact loopback `CallbackUri`, and `CredentialReference` in user secrets as well. Non-Development environments ignore `DevelopmentClientSecret` and must use the configured production secret-store provider.

The authority host is configurable for future sovereign-cloud support, but the Graph base URL, authority, app registration, redirect URI and permission availability must all belong to the same cloud. The default scope values and role/issuer expectations are verified for the global Microsoft cloud only. Treat Azure Government, DoD and China deployments as unsupported until their endpoints and selected-permission behavior have been live-validated and documented; changing only `AuthorityHost` is insufficient.

## Microsoft permission and endpoint matrix

Microsoft Selected permissions require both Entra administrator consent and a separate assignment on the target resource. Consent alone grants no resource access. Virtual Company reports `missing_resource_grant` on a 403 and never falls back to tenant-wide read permission. See Microsoft's [Selected permissions overview](https://learn.microsoft.com/en-us/graph/permissions-selected-overview), [drive metadata API](https://learn.microsoft.com/en-us/graph/api/drive-get), [drive item metadata API](https://learn.microsoft.com/en-us/graph/api/driveitem-get), and [list children API](https://learn.microsoft.com/en-us/graph/api/driveitem-list-children).

| Source | Recommended application consent and assignment | Runtime endpoints |
|---|---|---|
| SharePoint document library folder | `Files.SelectedOperations.Selected` with `read` assigned to the approved root drive item. Use a separately reviewed `write` assignment only on the designated descendant output folder. | Read endpoints plus create `PUT /drives/{drive-id}/items/{folder-id}:/{filename}:/content` and conditional replacement `PUT /drives/{drive-id}/items/{item-id}/content` |
| OneDrive for Business folder | `Files.SelectedOperations.Selected` with `read` assigned to the approved root. Assign `write` only to the designated output folder when approved agent output is enabled. The administrator supplies the already granted drive and folder item IDs. | Read endpoints plus the same create and conditional replacement content endpoints |

Do not grant tenant-wide **application** `Files.Read.All`, `Sites.Read.All`, or a write scope merely to make discovery work. The guided setup uses short-lived delegated `Files.ReadWrite` and `Sites.Read.All` during the administrator session and returns only Data Protection-protected selection handles to the browser. Runtime access still requires `Files.SelectedOperations.Selected` application consent and a separate drive-item assignment. The advanced customer-managed path may still accept pre-granted IDs.

Microsoft's current v1.0 `driveItem: delta` permission table lists `Files.Read.All` as the least-privileged application permission and does not list Selected application permissions. Virtual Company therefore uses delta only when that endpoint succeeds under the connection's already approved permission profile. A 403 from delta does not trigger broader consent: the worker performs a bounded full enumeration under the existing Selected grant and commits removals only after every page succeeds. An expired delta cursor also stages a complete reconciliation before unseen files are made unavailable. See Microsoft's [drive delta documentation](https://learn.microsoft.com/en-us/graph/api/driveitem-delta?view=graph-rest-1.0).

### Guided discovery endpoint matrix

| Setup operation | Microsoft Graph endpoint | Delegated permission | Notes |
|---|---|---|---|
| OneDrive for Business discovery | `GET /me/drive` | `Files.ReadWrite` | Accepts only `driveType=business`; consumer drives are excluded. The write-capable delegated scope is retained only for the reviewed permission-assignment call. |
| SharePoint site search | `GET /sites?search={query}` | `Sites.Read.All` | Personal Microsoft accounts are unsupported. Search is bounded and rate-limited. |
| Site libraries | `GET /sites/{site-id}/drives` and `GET /drives/{drive-id}/root` | `Sites.Read.All` | Consumer drives, remote items and unsupported roots are excluded. |
| Folder metadata and children | `GET /drives/{drive-id}/items/{item-id}` and `/children` | `Files.ReadWrite` or `Sites.Read.All` | Folder-only results; ancestry, drive identity, paging host and depth are validated server-side. |
| Selection preflight | folder metadata plus ancestry revalidation | same as browse | Determines the intended Selected application permission but performs no assignment. |
| Reviewed root assignment | `GET/POST /drives/{drive-id}/items/{root-id}/permissions` | delegated `Files.ReadWrite`; application consent `Files.SelectedOperations.Selected` | Reuses an exact existing application grant or creates one `read` role. No site/drive-wide fallback. |
| Reviewed output assignment | `GET/POST /drives/{drive-id}/items/{output-id}/permissions` | same | Created only when output is enabled and server-verified as a descendant; role is `write`. |
| Managed partial cleanup | `DELETE /drives/{drive-id}/items/{item-id}/permissions/{permission-id}` | delegated `Files.ReadWrite` | Targets only persisted permission IDs created by this wizard after explicit confirmation. |

The browser receives only opaque, Data Protection-protected handles bound to the onboarding session. Handles carry no authority on their own and cannot be reused across sessions. The canonical tenant, site, drive, root, display metadata and selection version are retained inside encrypted onboarding state. Folder names, paths, URLs, user principal names, provider payloads and continuation URLs are excluded from audit metadata.

Tenant Conditional Access, consent policies, SharePoint access policies, disabled site search, an unlicensed or unprovisioned OneDrive, administrator role restrictions, and Graph throttling can prevent discovery. The API reports bounded `access_lost`, `discovery_access_denied`, `source_unavailable`, `provider_throttled`, or `provider_unavailable` states and never compensates by requesting tenant-wide application access.
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
  "Microsoft365DocumentOnboarding": {
    "AuthorityHost": "https://login.microsoftonline.com",
    "PlatformClientId": "<multi-tenant-application-id>",
    "CallbackUri": "https://api.example.com/api/document-repositories/microsoft/callback",
    "CredentialMode": "client_secret_reference",
    "CredentialReference": "platform/microsoft365/client-secret",
    "WebOrigin": "https://your-web-host.example",
    "DelegatedSetupScopes": [ "openid", "profile", "offline_access", "https://graph.microsoft.com/Files.ReadWrite", "https://graph.microsoft.com/Sites.Read.All" ],
    "SelectedApplicationPermission": "Files.SelectedOperations.Selected",
    "OneDriveSelectedApplicationPermission": "Files.SelectedOperations.Selected",
    "SharePointSelectedApplicationPermission": "Files.SelectedOperations.Selected",
    "SessionLifetimeMinutes": 20,
    "CleanupIntervalMinutes": 5,
    "ProvisioningPollIntervalSeconds": 5,
    "AllowedReturnPathPrefixes": [ "/settings/document-repositories" ]
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

- `GET .../microsoft/onboarding/{sessionHandle}/source-kinds` reports the configured OneDrive for Business and SharePoint discovery capabilities.
- `GET .../sources/onedrive` discovers the signed-in administrator's organizational OneDrive without exposing its Graph ID.
- `GET .../sources/sharepoint/sites` and `/libraries` return bounded pages represented by opaque site/source handles.
- `GET .../folders` returns folder-only children, breadcrumb handles and an opaque continuation handle.
- `POST .../selection` revalidates the folder and ancestry, persists the canonical encrypted selection and returns selected-permission preflight; it does not create a connection or permission grant.
- `POST .../access` persists the server-validated read/write choice, optional descendant output folder and company-agent selections; no Microsoft mutation occurs.
- `GET .../review` returns the server-derived tenant/source/root, output, agents, import behavior and exact `read`/`write` changes.
- `POST .../finalize` requires the current draft version and explicit confirmation, then idempotently queues durable provisioning. Repeated requests return the same operation.
- `GET .../provisioning` reports queued, provisioning, reconciliation-required, failed, connected or cleaned state without exposing provider IDs or credentials.
- `POST .../provisioning/retry` reconciles the exact application permission before any retry. `POST .../provisioning/cleanup` requires confirmation and durably queues removal of only wizard-managed partial grants; its worker treats timeouts as reconciliation-required instead of claiming synchronous success.

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

## Provisioning, retry, cleanup, and rotation

Finalization is a durable background workflow. It rechecks the initiating company administrator, tenant, root ancestry, output-folder ancestry and current delegated authority; records every exact resource, role, permission ID, attempt and ownership decision; and verifies the finished connection with a platform application token. The connection becomes active and its idempotent initial import is queued only after the root `read` grant is observable. Write behavior remains disabled unless the separate output `write` grant is also verified.

An existing customer-managed application permission is recorded as pre-existing and is never treated as wizard-managed. Timeout, throttling and provider 5xx responses enter reconciliation: the worker reads the exact drive-item permission and does not issue another POST when the intended application/role already exists. Partial setup remains operator-visible and never enables writes. Retry requires a still-authorized Virtual Company administrator and retained temporary Microsoft authority. If that authority expires, restart authorization before reconciling.

Cancellation before finalization has no Microsoft side effect. After provisioning begins, ordinary cancellation is rejected; use retry or the explicitly confirmed cleanup action. Cleanup deletes only the persisted permission IDs that this workflow created, never a pre-existing/customer-managed grant and never remote content. Disconnect continues to preserve all Microsoft permissions; any future revoke-on-disconnect option must remain separately confirmed and ownership-bound.

For platform credential rotation, add the new secret-store version under the same reference, allow app-only token caches to refresh, validate a platform-managed connection, and then retire the old secret. Rotation does not change persisted resource assignments because those target the stable application ID. Changing the application ID requires a reviewed migration of resource grants; do not silently repoint existing connections.

## Administrator and user experience

Open **Settings → Document repositories** to manage the publication boundary. The page uses only live API state; it does not simulate a connected provider when the API, scanner, embedding service, or Microsoft grant is unavailable.

The primary path is **Connect Microsoft 365**. It is an eight-milestone guided workflow: connect, administrator sign-in, approval, source type, source/folder selection, access, agents, and review/connect. The administrator never copies or sees tenant, application, credential, drive, folder, or Graph identifiers in this path.

1. Start the guided connection and continue to Microsoft with full-page navigation. The callback returns to the settings page with only the opaque, company/user-bound setup handle.
2. Choose OneDrive for Business or SharePoint. SharePoint is recommended for durable company-owned knowledge. Search is offered only for the backend-supported SharePoint site discovery endpoint.
3. Browse eligible sources and folders using server-issued opaque handles. Breadcrumbs, pagination, empty results, throttling and access loss remain inside the protected setup session.
4. Keep the recommended read-only mode or explicitly enable approval-bound output and choose a separate descendant output folder. Coauthoring, deletion and arbitrary sharing are not supported.
5. Select agents from the current company roster. The initial selection is empty, and Microsoft administrator approval never grants Virtual Company agent access by itself.
6. Review the server-derived tenant/source names, approved root, access mode, output folder, agent audience, import behavior and exact permission changes. **Connect repository** remains disabled until the administrator deliberately confirms the review.
7. The page then reports durable `queued`, `provisioning`, `reconciliation_required`, `failed`, `cleaned` and `connected` outcomes. Retry reads the exact managed permission before another attempt; cleanup targets only a persisted wizard-managed grant. Refreshing does not manufacture success or duplicate a connection.
8. After connection, use the existing health, import, synchronization, agent-access, publication, recovery and disconnect controls. Platform-managed connections that lose consent expose a guided reconnect action.

The legacy customer-managed form remains under **Advanced: use your own Entra application**. It is intended only for administrators who operate their own registration, credential rotation, resource grants and technical identifiers. Existing customer-managed connections remain editable through that path and are never silently converted to platform-managed credentials.

Troubleshooting is state-specific: consent denial restarts safely; wrong tenant or account requires the company administrator account; insufficient authority requires an authorized Microsoft 365 administrator; expired sessions restart without a permission mutation; no eligible sources and tenant policy blocks require Microsoft-side policy/resource review; throttling offers bounded retry; partial or ambiguous provisioning offers only reviewed retry or managed-grant cleanup; validation failure never enables writes. Live Microsoft verification requires the configured tenant, administrator authority and selected-resource consent described above.

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
