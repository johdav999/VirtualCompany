# OneDrive and SharePoint agent document integration

Implementation prompt pack, grounded in repository inspection on 2026-09-17.

## Scope and execution

Implement Microsoft 365 business document repositories for Virtual Company agents. Support OneDrive for Business and SharePoint document libraries through Microsoft Graph. A shared SharePoint library is the recommended company-owned source; connecting a OneDrive for Business folder remains supported. Consumer Microsoft accounts, Git repositories, document coauthoring, arbitrary sharing links, deletion of remote files, and changes to Microsoft permissions by agents are outside this pack.

Execute prompts in order. Prompts 1–5 deliver the read-only release; prompts 6–8 complete controlled creation, updates and recovery. This ordering is not permission to stop at prompt 5 when asked to implement the whole pack. Each prompt delivers working behavior and its tests, not just contracts or a plan. Do not implement this pack merely because asked to read or edit it.

Default to application authentication for autonomous company agents, with explicitly granted Microsoft resources and an explicit company audience configured by an administrator. The connection is a company knowledge publication boundary: application access does not imply that every company member or every agent may read everything. Do not imply that this mode mirrors each employee's Microsoft permissions. Delegated, per-user repository access is a separate future feature; do not silently use an employee's refresh token as the company service identity.

All prompts inherit the following requirements from this document:

- Read and follow `/production-implementation.md`, `/AGENTS.md`, `/src/AGENTS.md`, `/tests/AGENTS.md`, and `/docs/architecture-rules.md`, plus nearer instructions for changed files. Reinspect relevant code when executing; the baseline below is evidence, not a frozen design.
- Follow `/docs/architecture-rules.md` by reference, especially Database and EF Core, Multi-Tenancy and Authorization, Agent and AI Orchestration, Workflow and Approval, External Side Effects and Outbox, and Test Architecture. Every schema change includes the SQL Server migration and snapshot in `VirtualCompany.Persistence.Migrations`, upgrade verification and a pending-model-change check. Do not introduce PostgreSQL behavior because an old comment mentions it.
- Implement document capability adapters and workers in `VirtualCompany.Infrastructure.Operations`, contracts in Application, domain state in Domain, and configurations in Persistence. Register through `Companies/OperationsModuleRegistration.cs`. Cross-cutting security remains in Platform. Do not reference Mailbox or Sales implementation projects from Operations, create a second orchestration engine, or put implementations in the root Infrastructure facade.
- For UI work, follow `/docs/design.md`, `/src/VirtualCompany.Web/AGENTS.md`, and `/ui-instructions.md` as the subordinate companion. The mandatory reference-image workflow applies to new or significantly changed surfaces. Generate the reference only when implementing UI, not when writing this prompt pack. Apply the required `$polish-uat-loop` skill to hands-on UAT and fixes.
- Keep credentials, bearer URLs, raw provider responses and document bodies out of logs, audit metadata and agent tool schemas. Use bounded result sizes and cancellation. Treat imported document content as untrusted evidence, never as instructions granting new capabilities.
- Tests may use deterministic provider fixtures; production must use real configured integrations. Missing Microsoft grants, scanner infrastructure or credentials must produce explicit unavailable states. Finish independent implementation and verification, record external checks as blocked with exact prerequisites, and never claim live provider verification without evidence.
- For each completed prompt report behavior delivered, migrations/configuration, focused checks and results, and any actual external blocker. No production scaffolding, mock data, silent fallback, unhandled intermediate state, or deferred in-scope TODO is acceptable.

## Verified repository baseline

Read these files before changing their responsibilities:

| Area | Existing implementation and implications |
|---|---|
| Document contracts | `src/VirtualCompany.Application/Documents/DocumentContracts.cs` contains upload/list/get, storage, virus scanning and ingestion contracts. `KnowledgeContracts.cs` contains extraction, chunking, embeddings, indexing, search, access context and citation contracts. Extend these boundaries rather than inventing another knowledge subsystem. |
| Persistence | `src/VirtualCompany.Domain/Entities/KnowledgeDocumentEntities.cs` already contains documents, chunk-set versions and indexing state. Its document constructor initializes the source as Upload. `CompanyKnowledgeDocumentAccessScope.cs` models the company audience. The new source must not masquerade as an upload. |
| Processing | `src/VirtualCompany.Infrastructure.Operations/Documents/CompanyDocumentService.cs`, `InlineCompanyDocumentIngestionOrchestrator.cs`, and `CompanyKnowledgeServices.cs` implement storage, scan transitions, extraction, chunks, embedding generation, indexing workers and semantic retrieval. Upload currently invokes scanning inline; remote batch ingestion must use bounded background work. |
| Production gap | `Companies/OperationsModuleRegistration.cs` currently registers `NoOpCompanyDocumentVirusScanner`. The ingestion file explicitly identifies its clean result as a placeholder. The embedding implementation also has a deterministic path. Neither constitutes evidence of real scanning or production semantic search. |
| Authorization | `src/VirtualCompany.Infrastructure.Platform/Security/KnowledgeAccessPolicyEvaluator.cs` checks company, role, data scopes and agent restrictions. Human knowledge management access is deliberately distinguished from retrieval on behalf of an agent. Preserve that distinction. |
| HTTP | `src/VirtualCompany.Api/Controllers/CompanyDocumentsController.cs` owns `api/companies/{companyId}/documents`, upload/list/get and `semantic-search`. Preserve its public contracts. |
| Agent runtime | `Companies/StaticCompanyToolRegistry.cs` already registers `knowledge.search`; `Companies/InternalCompanyToolContract.cs` executes it. `CompanyAgentToolExecutionService.cs` is the shared execution boundary. `AgentCapabilityCatalog.cs` includes knowledge search. Preserve existing tool identity and permissions. |
| Support | `src/VirtualCompany.Infrastructure.Support/Support/SupportKnowledgeContextProvider.cs` uses the shared knowledge boundary. Imported sources must not bypass Support grounding and safety. |
| Secrets and execution | `src/VirtualCompany.Application/Security/PlatformSecretStoreContracts.cs` exposes `IPlatformSecretStore`; its current interface has Get/Set but no delete. `Companies/CompanyOutboxInfrastructure.cs` provides company outbox infrastructure. Reuse applicable contracts without assuming unsupported secret deletion. |
| UI | `src/VirtualCompany.Web/Pages/SettingsHub.razor`, `AgentSettingsHub.razor` and `Pages/Support/SupportKnowledgeGaps.razor` are relevant entry points. The latter serves `/support/knowledge` as well as the compatibility route. Source administration belongs in Settings. |
| Tests | Existing API test coverage includes `CompanyDocumentIngestionIntegrationTests`, `CompanyKnowledgeDocumentTests`, `KnowledgeRetrievalIntegrationTests`, `KnowledgeAccessPolicyEvaluatorTests`, and `InternalCompanyToolRegistryTests`. Use focused Web and Support projects for their respective behavior. |

No OneDrive/SharePoint document connector was identified in the inspected Operations, Application and Web sources. Existing Microsoft Graph meeting adapters in Sales are reference context only, not a dependency to import into Operations.

## Microsoft references

Verify endpoint and permission compatibility against current official documentation during implementation, particularly when using selected permissions. Consent and resource assignment are separate; discovery, delta and write operations must be proven for the chosen resource scope. Do not silently broaden permissions to make an endpoint work.

- [Graph file API overview](https://learn.microsoft.com/en-us/graph/onedrive-concept-overview)
- [Selected permissions](https://learn.microsoft.com/en-us/graph/permissions-selected-overview)
- [Authentication modes](https://learn.microsoft.com/en-us/graph/auth/auth-concepts)
- [Drive delta tracking](https://learn.microsoft.com/en-us/graph/api/driveitem-delta?view=graph-rest-1.0)
- [Upload sessions and conditional requests](https://learn.microsoft.com/en-us/graph/api/driveitem-createuploadsession?view=graph-rest-1.0)

## Prompt 1 — Connect and browse an explicitly authorized repository

### Title and outcome

Implement a working company-scoped Microsoft 365 document connection API. An authorized administrator can register a repository, validate access, browse a selected root and disconnect it. No content is searchable yet.

### Current context

Use the baseline document contracts, Operations registration and `IPlatformSecretStore`. Existing upload APIs and knowledge entities are not repository connection management. Inspect established company authorization and integration lifecycle conventions before adding endpoints.

### Dependencies

No earlier prompt. Live validation needs an Entra application identity, a Microsoft 365 tenant and a pre-granted OneDrive for Business or SharePoint resource. Provisioning Microsoft resource permissions is an administrator deployment task, not an agent capability.

### Implementation requirements

1. Add company-owned connection persistence with provider kind, directory tenant ID, explicit drive/root identity, display name, credential reference, read-only mode, lifecycle state, audience and concurrency version. Use normalized relational fields for queryable state and tenant-aware uniqueness/relationships. Add migration and snapshot.
2. Implement real application token acquisition and a narrowly scoped Graph adapter for root validation, metadata and paged child enumeration. Use platform secret storage or the established workload identity mechanism; select a supported production credential method and document rotation. Partition token caching by directory tenant and application identity.
3. Implement authenticated company endpoints for create/configure, list, get, validate, bounded browse and disconnect. Bind all provider IDs to the server-resolved connection. Resolve root ancestry by item identities, not a path prefix. Fail closed on shortcuts/remote items crossing the approved boundary.
4. Document and verify an endpoint-to-permission matrix for both supported source types. Prefer a dedicated explicitly granted site/library. Permit administrators to supply pre-granted resource identifiers when broad discovery is unavailable. Never request tenant-wide read permissions as a fallback.
5. Require explicit company audience and agent grants; default new connections to no agent access. Audit lifecycle changes, expose sanitized failures and correlate requests. Disconnect immediately stops use and queued work without deleting remote content or shared application credentials.
6. Write the deployment/configuration section of `docs/onedrive-agent-integration.md`, including the company-publication access model and how Microsoft resource grants are administered.

### Constraints and preservation rules

Follow the shared instructions. There is no new UI in this prompt. Preserve uploaded documents and mailbox credentials. Keep Graph schemas inside the adapter; opaque external IDs may be persisted in the integration mapping. Treat provider URLs, redirects and paging links as untrusted: enforce HTTPS and approved Graph/cloud boundaries, avoid arbitrary URL fetches, and never forward Graph bearer tokens to download hosts.

### Acceptance criteria

- Given a valid explicit grant, validation returns the actual repository name and browsing returns only items under the approved root.
- Given another company's connection ID or an item outside the root, no metadata is returned and no unauthorized mutation occurs.
- Given consent without a resource grant, validation reports the missing-access state without broadening permissions.
- Given a disconnected connection, subsequent browse and queued operations are denied.

### Verification

Run adapter contract tests for pagination, token expiry, 401/403/404, throttling and boundary escape; API authorization/cross-company tests; SQL Server migration upgrade checks and focused builds. Perform a separately categorized live read-only smoke check when credentials are available.

### Definition of done

The API can connect and browse a real permitted repository, persistence and authorization are complete, configuration is documented, and the shared production completion requirements hold. Live checks lacking credentials are explicitly unverified.

## Prompt 2 — Ingest repository documents through the existing knowledge pipeline

### Title and outcome

Implement a bounded initial import that produces scanned, processed, indexed company knowledge with durable Microsoft source references.

### Current context

Extend the existing document/chunk entities, extractor, indexing processor and storage contracts. `CompanyDocumentService` currently assumes an interactive authorized upload; a worker must not impersonate an owner to call it. `NoOpCompanyDocumentVirusScanner` is currently registered.

### Dependencies

Prompt 1. A real malware-scanning deployment and production embedding configuration are needed for production indexing; inspect existing deployment integrations first. If no scanner exists, implement a concrete configurable ClamAV streaming adapter behind the existing scanner interface and document its operational dependency.

### Implementation requirements

1. Add a company/connection/drive/item mapping to the canonical knowledge document with remote version, safe source web link, content hash, import state and last-seen metadata. Add source-type support without changing persisted upload values. Use unique keys to prevent duplicate imports; include migration/snapshot.
2. Implement an authorized import command that queues durable background work and exposes progress. Download files as bounded streams with timeouts, MIME/extension/size checks, temporary storage cleanup and credential-safe redirect handling. Reject unsupported/encrypted/unreadable documents with actionable status.
3. Refactor only the necessary shared ingestion boundary so uploads and trusted, explicitly company-scoped jobs share validation and state transitions. A background worker must carry a verifiable connection/job identity rather than fabricated user membership.
4. Reuse existing parsers, chunker, embeddings and indexing. Implement real scan handling and block remote imports from reaching clean/indexed state through the no-op scanner. Production semantic indexing must not silently select deterministic embeddings. Make unavailable dependencies visible without preventing unrelated application startup when the feature is disabled.
5. Persist versioned citations identifying connection, canonical document, remote item, source web link and chunk. Do not use expiring download URLs as citations. Only publish a chunk set after its complete ingestion succeeds.
6. Expose per-item failures, bounded progress, safe retries and audit/correlation evidence. Imported content must never supply trusted `extracted_text` metadata or override access scope.

### Constraints and preservation rules

Follow the shared database/background rules. Keep current upload contracts and access behavior. This prompt does not add another vector database or embedding orchestration path. Reuse configured file formats; unsupported formats are explicit rather than silently omitted.

### Acceptance criteria

- Given a supported clean document, import produces one canonical document with searchable chunks and a stable source citation.
- Given duplicate job delivery, the same remote version is not duplicated or repeatedly embedded.
- Given a scanner timeout, malware detection or missing production scanner, the document never becomes searchable.
- Given parser failure or an oversized file, other valid files finish and the failed item has a recoverable or permanent reason.

### Verification

Extend document ingestion/retrieval tests; test malicious metadata, MIME mismatch, stream limits, scanner outcomes, partial failures, duplicate delivery and cross-company mappings. Verify SQL Server uniqueness and migrations, then build affected API/Operations projects. Confirm a live clean-file import when dependencies exist.

### Definition of done

Initial import works through the real existing pipeline with production scanning and embeddings, secure source mapping, meaningful status and tests. Missing external services are not disguised as successful indexing.

## Prompt 3 — Keep imported knowledge synchronized and revoke stale access

### Title and outcome

Implement resumable background synchronization so edits, moves, deletions and access loss cannot leave misleading or unauthorized knowledge available.

### Current context

Prompts 1–2 provide connection and imported-document state. Existing indexing uses chunk-set versions and leases. Search, document list/get, cached evidence and Support grounding all need consistent source-availability checks.

### Dependencies

Prompts 1–2. Verify delta support under the selected permission scope; implement bounded enumeration/reconciliation if delta is unsupported for that exact connection profile. Do not broaden grants.

### Implementation requirements

1. Persist synchronization jobs, checkpoints, leases, attempt state, next retry time and safe failure reasons. Schedule through established background infrastructure. Bound work per company and support concurrent workers without duplicate imports.
2. For compatible drives, implement initial delta enumeration and subsequent delta pages. Persist a page checkpoint only after its item changes are durably recorded for processing. Treat next/delta links as opaque validated provider values; distinguish item ingestion failure from enumeration failure. Handle repeated items, token expiration and interrupted full reconciliation.
3. Detect content edits, metadata-only changes, renames, parent moves, folder deletions and moves across the selected root. Invalidate descendants when a containing folder leaves scope. Never treat an incomplete or failed listing as proof of deletion. On invalid delta tokens, stage a complete rescan before marking unseen items removed.
4. Once a changed remote version is observed, exclude its old chunks until the replacement is ready. Use atomic chunk-set publication, avoid re-embedding unchanged content, and prevent an older in-flight job from publishing over newer state.
5. Define a single asynchronous remote-source availability gate used by retrieval and document content access. Revalidate selected remote items/root membership under the configured service identity before their text or metadata is exposed; do not rely on delta to report all permission changes. Local audience revocation and disconnect apply immediately. Provider denial, deletion or an inability to validate must fail closed for remote content. Never perform network I/O inside the synchronous `IKnowledgeAccessPolicyEvaluator`.
6. Apply the gate to cached results and previously collected grounding evidence before a new agent response uses them. Preserve historical audit references without exposing old snippets through normal retrieval. Document that already delivered responses cannot be retroactively withdrawn.
7. Provide authenticated resync/retry commands, cancellation, bounded backoff respecting Retry-After, and metrics for lag, queue age, item failures and authorization failures. Record schema effects and migrations.

### Constraints and preservation rules

Follow shared instructions. Disconnect is not remote deletion. No webhook infrastructure is required for this release; polling/delta is the authoritative synchronization path. An authorized live validation may confirm unchanged source content even if a scheduled poll is delayed, but stale or unverifiable content must not be silently represented as current.

### Acceptance criteria

- Given an edit followed by an interrupted worker, retry eventually publishes only the latest valid version without duplicate chunks.
- Given a move outside the root, revoked grant, disconnect or deleted folder, affected sources are excluded from all retrieval paths.
- Given a failed enumeration page, previously unseen documents are not falsely deleted.
- Given a permission change without a content delta, runtime validation still denies the source.

### Verification

Test multi-page restarts, expired cursors, duplicate events, out-of-order versions, descendants, authorization revocation, provider outage and cross-company job claims. Validate SQL Server leases/concurrency and migrations. Run relevant Support retrieval regression tests.

### Definition of done

Synchronization and access invalidation are working production behavior, with recoverable jobs and no stale-source authorization bypass through alternate read paths.

## Prompt 4 — Let configured agents discover and read repository evidence

### Title and outcome

Enable permitted agents to search, list and read imported repository documents with bounded results and verifiable citations.

### Current context

Preserve `knowledge.search` in `StaticCompanyToolRegistry`, `InternalCompanyToolContract` and `AgentCapabilityCatalog`. Extend `ICompanyKnowledgeSearchService` and existing access context rather than creating an independent `documents.search` engine. Support already uses the shared grounding contract.

### Dependencies

Prompts 1–3. At least one imported source and an explicit agent grant are needed for live demonstration.

### Implementation requirements

1. Extend existing knowledge search to include authorized remote documents. Add typed `documents.list` and `documents.read` tools through shared orchestration with read classification, strict schemas, bounded pagination/content windows and cancellation. Tool arguments use internal repository/document handles, never arbitrary URLs or credential references.
2. Resolve company, user and agent identity from trusted execution context; payloads cannot select a more privileged actor. Apply local audience, agent grants and Prompt 3 remote validation before returning even titles, excerpts or citations. Preserve the human-management versus agent-access distinction.
3. Implement keyword retrieval alongside existing semantic retrieval where needed for filenames and exact terms. Merge/rank through the same search contract with permission checks before final top-N selection. Do not introduce broad catch-all services or a parallel search store.
4. Return source links, content version and chunk references. Distinguish no match, unavailable source and not-yet-indexed content without leaking inaccessible source existence. Maintain existing tool compatibility and Support review/knowledge-gap behavior for insufficient grounding.
5. Audit tool execution with source identifiers, outcome and correlation, excluding bodies and secrets. Scope prompt construction to returned evidence; malicious document instructions cannot trigger execute tools or expand access.
6. Persist grants or capability settings using established configuration boundaries with migrations where required. New agents receive no repository permission automatically.

### Constraints and preservation rules

Shared orchestration, policy and tenant rules apply. Do not expose provider-wide search directly to agents. No remote writes are enabled. Existing uploaded knowledge remains searchable with unchanged access semantics.

### Acceptance criteria

- Given an authorized sales agent, a product query returns approved repository evidence with working citations.
- Given the same query from an ungranted agent, restricted titles and snippets never appear.
- Given a malicious instruction inside a document, it is returned only as evidence and cannot bypass tool authorization.
- Given unavailable grounding, Support creates the established review/gap outcome rather than an authoritative answer.

### Verification

Extend tool registry/execution, knowledge access and retrieval integration tests. Add mixed-upload/remote ranking, forged-context, pagination, prompt-injection authorization and revocation tests. Run narrow Support grounding checks and API builds.

### Definition of done

Agents actually invoke the tools through shared execution and receive permission-filtered, cited evidence. Capability discovery, execution, audit and failure paths are all implemented.

## Prompt 5 — Deliver the repository settings and knowledge experience

### Title and outcome

Give administrators a complete read-only connection workflow and let users understand repository availability, agent access and document provenance.

### Current context

Extend `SettingsHub.razor`, agent settings and the existing `/support/knowledge` experience where relevant. Use typed clients and `ICompanyApiTransport`. Prompts 1–4 already provide working APIs and agent retrieval.

### Dependencies

Prompts 1–4. The mandatory `/docs/design.md` reference-image workflow applies. Read `/src/VirtualCompany.Web/AGENTS.md`; use `$polish-uat-loop` for browser UAT.

### Implementation requirements

1. Write the design-image prompt and generate/store a repository settings reference under `docs/design/references/` before UI implementation. Use existing components, canonical navigation and design tokens.
2. Add a Document repositories entry under Settings and a typed capability API client. Build a real flow to configure an approved Microsoft resource, validate connection, select a permitted root and explicit company/agent audience, and start import. Keep raw secret management in established secure configuration surfaces.
3. Explain in plain language that the selected source is published to the chosen Virtual Company audience, not automatically filtered by every viewer's Microsoft account. Display source name/link, mode, connection health, synchronization freshness, indexed/failed counts and next actionable step.
4. Implement loading, empty, no-permission, missing-grant, pending-scan, importing, partially failed, reconnect/credential-repair and disconnected states. Add authorized retry/resync/disconnect actions backed by server decisions. No simulated connected state.
5. Surface repository provenance and safe citations in knowledge results and agent responses without changing Support's meaning of a knowledge gap. Add grant controls to the appropriate agent settings surface. Do not add another primary navigation destination.
6. Update `docs/ui-route-inventory.md` and deployment/user documentation. Audit configuration changes; expose no credentials or provider internals in normal user copy.

### Constraints and preservation rules

Follow shared UI instructions and mandatory image/reference comparison. The backend remains authoritative for every action. Preserve current uploads, routes, company selection and accessibility/localization conventions. No schema changes are expected beyond gaps discovered in prior contracts; any needed changes require migrations.

### Acceptance criteria

- Given an authorized administrator and real configured identity, the complete connect → root/audience selection → import → agent query flow is usable.
- Given missing grants or failed scanning, the page explains the problem and appropriate action without reporting readiness.
- Given a read-only member, management actions are unavailable and direct API attempts are denied.
- Given narrow/mobile layouts, connection status and required actions remain reachable by keyboard and touch.

### Verification

Run focused Web component/typed-client and Web/API contract tests. Perform browser UAT with screenshot evidence, compare against the reference and fix findings. Exercise at least success, partial failure and disconnect. Build API and Web; report live-provider checks separately from fixture-based UI checks.

### Definition of done

The read-only release is usable end to end, visually verified and documented. No mock status, unconnected buttons or unfinished failure states remain.

## Prompt 6 — Save approved agent outputs to a designated folder

### Title and outcome

Let an agent propose a new file, obtain policy-required approval and reliably create it in an explicitly writable output folder.

### Current context

Read tools and repository configuration exist from prompts 1–5. Reuse the shared agent execution, approval and company outbox boundaries; Graph file creation is an external side effect.

### Dependencies

Prompts 1–5. An administrator must explicitly enable writes and configure a writable target resource/grant. Read-only connections remain the default. Verify upload endpoint compatibility with the selected grant.

### Implementation requirements

1. Implement durable document publication requests containing company, connection, target folder, proposed filename, immutable staged artifact reference/hash, requesting actor/agent, policy decision, approval/version, idempotency identity and delivery state. Add relational configuration, migration and snapshot.
2. Add a recommendation tool for preparing a publication and an execute-classified `documents.create` action routed through the established policy/approval system. Stage actual bytes from an existing authorized artifact or supported bounded content; never fetch an arbitrary model-provided URL. Initially support the existing artifact formats without adding a new Office authoring engine.
3. Show the exact filename, folder, file preview/download, size and version/hash in an approval review surface. Bind approval to those immutable details. Changing bytes or destination invalidates approval. Follow the mandatory design workflow for significantly new UI.
4. Enqueue delivery transactionally. A worker rechecks connection, root ancestry, agent authority, write policy and current approval immediately before upload. Use deterministic create targeting with conflict behavior that cannot overwrite an existing human file. Use upload sessions where appropriate and supported.
5. Persist provider item/version and attempt state. After an ambiguous timeout, reconcile against recorded target identity and verified content before deciding success or retry; do not assume Graph provides a generic idempotency header. Prevent duplicate files under concurrent delivery.
6. Feed successful files into normal synchronization, avoiding duplicate import or self-triggered publication loops. Audit proposal, approval and external result. Surface queued, approved, sending, failed and reconciliation states with actionable recovery.

### Constraints and preservation rules

Apply Workflow and Approval and External Side Effects and Outbox rules from the architecture document. No direct uploads in request handlers or generic tool dispatch. Do not grant sharing permissions, delete files or enable tenant-wide writes. Limit output folder access explicitly.

### Acceptance criteria

- Given an approved immutable artifact, delivery creates exactly one expected file and returns its stable source reference.
- Given duplicate delivery or a lost upload response, retries/reconciliation do not create additional files or overwrite unrelated ones.
- Given changed content, expired approval, revoked write access or disconnect, no upload occurs.
- Given a read-only agent/connection, the execute action is denied server-side.

### Verification

Test approval expiry/rejection/cancellation, payload tampering, destination escape, duplicate claims, ambiguous outcome, name collision and tenant isolation. Validate SQL Server request/outbox concurrency and migrations. Run relevant Web approval tests and a live approved upload to a designated test folder when authorized.

### Definition of done

Approved file creation is real, durable, traceable and recoverable, with an implemented review flow and no bypass around policy or outbox execution.

## Prompt 7 — Update files without overwriting concurrent human edits

### Title and outcome

Enable approved whole-file replacement with optimistic concurrency and a clear conflict review flow.

### Current context

Prompt 6 provides immutable publication requests, approvals and durable delivery. Remote source mappings already retain item versions. Updating a file adds a distinct external mutation; this is not in-document coauthoring.

### Dependencies

Prompts 1–6. A writable target file and an endpoint proven to enforce the expected remote version are required. Verify conditional upload/session semantics in current Microsoft documentation and live tests; a metadata preflight alone is not concurrency protection.

### Implementation requirements

1. Add a typed update proposal and execute-classified `documents.update` action. Bind it to connection/item identity, expected remote ETag/version, original evidence version, immutable replacement hash and destination. Include request schema/migration changes where needed.
2. Provide review of the original and proposed artifact, with a text diff when supported and a clear whole-file replacement explanation for binary documents. Tie approval to the original version as well as the replacement bytes. Follow UI reference workflow where applicable.
3. Execute through the durable worker with a provider-enforced conditional mutation. A changed remote version becomes a conflict, never an unconditional retry/overwrite. Recheck current access and approval as in prompt 6.
4. Implement conflict resolution by fetching the current permitted version and preparing a new reviewed proposal. Never silently merge or reuse approval for changed content. Retain the rejected/stale proposal's safe audit trail.
5. Reconcile uncertain success using remote identity/version and content verification. If a later human edit prevents conclusive reconciliation, retain an explicit operator-visible unresolved state without overwriting that edit.
6. After verified success, invalidate old indexed evidence and schedule normal re-ingestion. Expose delivery/conflict status through API and existing settings/work approval surfaces. Record audit evidence without raw content.

### Constraints and preservation rules

Follow shared database, workflow and outbox rules. No delete/recreate workaround, force-overwrite option, new sharing links or automatic conflict approval. Existing Microsoft version history must not be intentionally purged.

### Acceptance criteria

- Given an unchanged target and valid approval, exactly the reviewed replacement is uploaded and a new source version is recorded.
- Given a human edit after proposal or after preflight, the conditional upload rejects the stale update and preserves the human edit.
- Given a resolved conflict with new bytes/version, a new approval is required.
- Given an ambiguous result followed by another human edit, reconciliation never retries an unconditional overwrite.

### Verification

Test 412/conflict handling, time-of-check/time-of-use races, expired approvals, version/hash tampering, duplicate dispatch and ambiguous success. Validate SQL Server concurrency/migrations, UI review states and a categorized live two-writer concurrency scenario.

### Definition of done

The supported update path has demonstrated provider-enforced conflict protection, complete review/recovery behavior and no path that silently overwrites a newer version.

## Prompt 8 — Deliver operator recovery and complete the production rollout

### Title and outcome

Implement operational controls that let administrators diagnose, pause and recover repository work, then verify the complete feature for deployment.

### Current context

Prompts 1–7 supply real connections, ingestion, synchronization, agent retrieval and approved writes. Existing background execution and outbox infrastructure already expose attempts/correlation; extend those capabilities instead of creating a separate operations dashboard.

### Dependencies

Prompts 1–7. A staging Microsoft tenant/library, real scanner and production-compatible embeddings are required for external acceptance. Missing credentials block live acceptance, not completion of independent code and tests.

### Implementation requirements

1. Implement authorized status/recovery commands for pause/resume, failed-item retry, full resync and unresolved publication reconciliation. Pause must have explicit semantics for retrieval versus synchronization versus queued writes; disconnect remains immediate denial. Recheck approval on resumed writes, and prevent concurrent recovery commands from duplicating effects.
2. Integrate queue age, last successful sync, stale leases, scanner/embedding failures, throttling and ambiguous writes with existing operator visibility. Add safe audit events and metrics without unbounded per-file metric labels.
3. Implement bounded retention/cleanup for temporary downloads, abandoned staged artifacts and superseded local chunk sets. Do not delete remote documents. Preserve required audit evidence and never remove staged content needed by a pending approval or reconciliation. Respect the actual secret-store capabilities on disconnect and document separate administrator credential revocation.
4. Finish `docs/onedrive-agent-integration.md`: supported authentication/access model, permission matrix, Entra/resource-grant steps, scanner setup, configuration, initial import, agents, output approvals, conflict handling, retention, alerts, recovery, feature disable and rollback. Cover equivalent local/Docker SQL Server migration paths and protect existing connection/upload data during rollback.
5. Execute end-to-end acceptance through the real application and fix in-scope defects. Use `$polish-uat-loop` for the UI validation. Record commands/results and evidence in `docs/onedrive-agent-integration-verification.md`, distinguishing unit/contract, browser and live-provider results.

### Constraints and preservation rules

Follow shared rules. This prompt must deliver working recovery/cleanup behavior, not only an audit report. No broad permission escalation or destructive production reset is a release workaround. UI changes follow the required reference workflow; schema changes include migrations.

### Acceptance criteria

- An administrator can recover a failed scan/sync without duplicate documents and can reconcile an uncertain upload without duplicate files.
- A disconnected or unauthorized source is unavailable through agent tools, direct document access, Support grounding and cached evidence reuse.
- A full scenario connects a source, imports and cites it, reflects an edit, blocks a revoked source, creates an approved output, and rejects a concurrent update.
- Feature disable stops remote jobs and writes while preserving ordinary uploaded knowledge and unrelated agent workflows.
- Retention removes only eligible local artifacts and leaves pending work and remote content intact.

### Verification

Run focused recovery/retention/security tests, relevant document/agent/Support/Web contract regressions and SQL Server migration/concurrency checks. Run one broader solution build after focused checks pass; repeat only for new changes or failures. Validate logs for secret/body leakage. Perform staging smoke scenarios with recorded identities/resources and no sensitive payloads in the evidence report.

### Definition of done

Recovery controls are implemented, the full requested integration is complete, documentation is usable and acceptance evidence is honest. Clearly list any external verification still blocked; do not describe an unverified deployment as production-validated.
