# OneDrive and SharePoint agent document integration

Implementation prompt pack, grounded in repository inspection on 2026-09-17.

## Scope and execution

Implement Microsoft 365 business document repositories for Virtual Company agents. Support OneDrive for Business and SharePoint document libraries through Microsoft Graph. A shared SharePoint library is the recommended company-owned source; connecting a OneDrive for Business folder remains supported. Consumer Microsoft accounts, Git repositories, document coauthoring, arbitrary sharing links, deletion of remote files, and changes to Microsoft permissions by agents are outside this pack.

Execute prompts in order. Prompts 1–5 deliver the read-only release; prompts 6–8 complete controlled creation, updates and recovery. Prompts 9–12 replace the normal manual-ID setup with a guided Microsoft 365 administrator connection while retaining bring-your-own-app configuration as an advanced fallback. This ordering is not permission to stop at an intermediate prompt when asked to implement the whole pack. Each prompt delivers working behavior and its tests, not just contracts or a plan. Do not implement this pack merely because asked to read or edit it.

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

## Guided Microsoft 365 connection follow-on

The following prompts are grounded in repository inspection on 2026-09-20, after prompts 1–8 were implemented. The production repository now includes company-scoped connection, validation, folder browsing, import, synchronization, agent retrieval, approved publication/update, recovery and retention behavior. `DocumentRepositoriesSettings.razor` still exposes directory tenant ID, application client ID, credential reference, drive ID and root item ID in the primary connection form. `CompanyDocumentRepositoryConnection`, `DocumentRepositoryContracts`, `CompanyDocumentRepositoriesController`, `CompanyDocumentRepositoryService` and `MicrosoftGraphDocumentRepositoryAdapter` assume a customer-managed application identity and client-secret reference.

Prompts 9–12 introduce a platform-managed connection path with Microsoft administrator authorization, source discovery, reviewed selected-permission provisioning and a guided UI. They do not replace the app-only runtime access model with per-user access. The administrator's delegated authorization exists only to complete setup; autonomous imports, synchronization, retrieval and approved writes continue under the narrowly granted application identity. Existing customer-managed connections, imported documents, approvals and jobs must remain compatible.

When implementing these prompts, verify the current Microsoft identity and Graph behavior against official documentation. In particular, distinguish tenant-wide admin consent from the separate assignment on a selected OneDrive or SharePoint resource. Determine and document the least-privileged delegated authorization needed temporarily for discovery and permission provisioning, and the selected application permission used by the runtime. Do not request `Files.Read.All`, `Files.ReadWrite.All`, `Sites.Read.All` or `Sites.ReadWrite.All` as a persistent runtime fallback merely to simplify discovery.

## Prompt 9 — Add secure Microsoft administrator authorization

### Title and outcome

Implement the platform-managed authorization foundation behind **Connect Microsoft 365** so a company administrator can sign in with a Microsoft 365 administrator account, grant the reviewed consent and return to the same company setup flow without entering tenant, application or secret identifiers.

### Current context

The existing document repository connection uses a directory tenant ID, application client ID and `IPlatformSecretStore` credential reference supplied through the UI. `MicrosoftGraphDocumentRepositoryAdapter` acquires application-only tokens from that tuple. `DocumentRepositoriesSettings.razor` and `DocumentRepositoryApiClient` expose the manual configuration. The repository already contains protected OAuth/state and replay patterns for mailbox, calendar, Finance and Marketing integrations, plus a Teams administrator-consent surface; inspect and reuse the appropriate platform conventions without importing capability implementation projects into Operations. `IPlatformSecretStore` has Get/Set but no delete, so it is not automatically suitable for disposable authorization-session material.

### Dependencies

Prompts 1–8. Deployment registration of a production multi-tenant Microsoft Entra application, reviewed redirect URIs and a platform-owned certificate, workload identity or other supported non-user credential are required for live authorization. Local deterministic tests must not require real Microsoft credentials.

### Implementation requirements

1. Add an explicit document-repository credential mode that distinguishes existing customer-managed application connections from platform-managed Microsoft 365 connections. Preserve all existing records and API behavior through a compatible migration and backfill. Platform-managed connections resolve the deployment-owned application identity server-side; they do not copy its credential reference into browser contracts or require a company-specific client secret.
2. Add a company-owned, expiring onboarding session with initiating user, company, return destination, correlation, status, provider tenant, replay/concurrency state and safe failure information. Persist only queryable workflow state. Protect state, nonce and PKCE material using established security boundaries. Any temporary delegated token material must be encrypted at rest in an appropriate short-lived credential/session store, excluded from logs and responses, and actually removed or made cryptographically unusable on completion, cancellation and expiry. Do not assume `IPlatformSecretStore` can delete it.
3. Add company-admin endpoints to begin authorization, receive or complete the callback, read status and cancel an onboarding session. The browser receives only an authorization URL or opaque session handle. Validate the authenticated initiating user and company again after callback; enforce single use, expiry, same-company ownership, safe return-URL allowlisting and replay denial.
4. Use authorization code with PKCE and current Microsoft identity guidance. Require an organizational Microsoft account in the intended tenant and an administrator consent result sufficient for the platform application's selected application permission. Validate issuer, tenant, audience, state, nonce and callback errors. Do not trust a tenant ID, role claim, redirect destination or completion flag supplied by the browser.
5. Separate setup authority from runtime authority. The delegated administrator credential may be used only by the bounded onboarding workflow introduced in prompts 9–11 and must never become the repository's continuing identity, an agent credential or a general Graph token. The runtime remains application-only and receives no access until a selected-resource assignment is completed.
6. Add stable safe failure codes for consent denied, non-organizational account, wrong tenant, insufficient administrator authority, expired/replayed state, invalid callback, unavailable configuration and provider throttling. Audit start, successful tenant association, denial, cancellation and expiry without names, email addresses, codes or tokens. Add low-cardinality observability for authorization outcome and age.
7. Add validated configuration for authority host, platform client ID, callback URI, credential mode/reference and onboarding lifetimes. Fail the connection action clearly when configuration is absent while allowing unrelated application startup and existing customer-managed connections to continue. Document registration, credential rotation, redirect URI and sovereign-cloud limitations in `docs/onedrive-agent-integration.md`.

### Constraints and preservation rules

Follow `/production-implementation.md`, `/docs/architecture-rules.md`, the shared instructions in this prompt pack and all scoped `AGENTS.md` files. Operations owns the document onboarding use case; cross-cutting token protection may live in Platform. Do not persist authorization codes, raw claims, access/refresh tokens or client credentials in ordinary business columns, audit events, URLs, browser storage or logs. Do not silently convert existing customer-managed connections. There is no folder picker or final resource grant in this prompt.

### Acceptance criteria

- Given an authorized Virtual Company administrator and valid Microsoft administrator consent, the callback resumes the same company-owned onboarding session and records the verified Microsoft tenant without exposing a credential.
- Given a callback with changed state, nonce, tenant, user/company context, redirect target or an already consumed code, the flow fails closed and cannot be replayed.
- Given cancellation or expiry, temporary authorization material can no longer be used and status explains that setup must restart.
- Given missing platform Microsoft configuration, existing customer-managed repositories remain operational and the new action reports a specific unavailable state.

### Verification

Add unit tests for state/nonce/PKCE, issuer/tenant validation, replay, expiry, cancellation, protected material cleanup and configuration. Add API authorization and tenant-isolation tests for begin/status/callback completion, including forged company and open-redirect attempts. Verify the SQL Server migration and pending-model check, then run affected API, Operations and security tests. Categorize a real Entra consent smoke test separately when credentials are available.

### Definition of done

The real authorization start/callback lifecycle is secure, company-scoped, observable and documented. It returns a verified tenant-bound setup session, not a mock success or a long-lived delegated repository credential, and has no unfinished production token-handling path.

## Prompt 10 — Discover Microsoft 365 sources and browse folders

### Title and outcome

Let the authorized administrator choose **OneDrive for Business** or **SharePoint**, discover an eligible drive or document library and browse folders without copying Graph IDs or URLs.

### Current context

Prompt 9 supplies an authenticated, tenant-bound onboarding session and temporary setup authority. The current browse API operates only after a `CompanyDocumentRepositoryConnection` has been saved and validated with a pre-granted root. `MicrosoftGraphDocumentRepositoryAdapter` already contains bounded drive-item operations for an active connection, but no setup-time source discovery. The normal UI still asks for drive and root IDs.

### Dependencies

Prompt 9 and its configured Microsoft application. A live tenant must contain at least one eligible business OneDrive or SharePoint library for external verification.

### Implementation requirements

1. Add setup-time queries for source kinds, eligible OneDrive for Business drives, eligible SharePoint sites/libraries and folder children. Keep Microsoft response models inside the provider adapter and return normalized, minimal source/folder views. Verify the current least-privileged delegated permissions for each discovery endpoint and document the matrix; request no scope unrelated to the implemented flow.
2. Make source selection explicit. OneDrive for Business discovery must identify the organizational drive and owner context safely; SharePoint discovery must identify the selected site and document library. Exclude consumer OneDrive, unsupported drives, shortcuts or remote items that cannot be safely bounded, and explain unavailable sources without leaking inaccessible tenant resources.
3. Implement bounded, paged folder-only browsing with breadcrumb navigation, loading, empty, throttled and access-lost results. Enforce configured page/depth limits and cancellation. Accept only opaque server-issued selection handles tied to the onboarding session; do not let the browser substitute arbitrary tenant, site, drive, item, path, paging URL or Graph URL values.
4. Resolve and retain the canonical provider identities needed for finalization—provider kind, tenant, site where applicable, drive, selected root item, stable display/web-link metadata and selection version—inside the protected onboarding state. Re-resolve ancestry and folder type server-side before accepting a selection. Do not yet create the production connection or represent the source as granted.
5. Provide an availability/preflight check for the selected root and determine whether selected application permission can be assigned there using the intended runtime permission. Consent without resource assignment is still not connected. Never fall back to tenant-wide application read access when selected discovery or assignment is unsupported.
6. Audit safe discovery outcomes and selected source type/opaque identifiers without folder names, paths, URLs, user principal names or provider payloads. Apply throttling and abuse limits to searches and browse calls.
7. Extend typed API clients and contracts for the setup flow without breaking the existing active-connection browse contract used for root maintenance. Update the Microsoft endpoint/permission documentation and list any tenant policies that can prevent discovery.

### Constraints and preservation rules

Follow shared architecture, tenant and provider-adapter rules. Setup reads use the temporary delegated administrator authority only within its company-bound session. Runtime jobs continue to use application-only access. This prompt performs no persistent Microsoft permission assignment, import or write grant, and must not create a partially active repository connection.

### Acceptance criteria

- Given a valid onboarding session, the administrator can select OneDrive for Business or SharePoint and browse eligible folders without seeing or entering a Graph ID.
- Given a forged selection handle, paging link, drive from another tenant/session or item outside the browsed hierarchy, the API returns no metadata and does not alter setup state.
- Given a consumer drive, unsupported shortcut or inaccessible library, it cannot be selected as a valid company source.
- Given throttling or lost delegated access, the session remains recoverable and returns an actionable bounded failure rather than broadening permissions.

### Verification

Add Graph adapter contract tests for both source kinds, paging, empty libraries, special characters, shortcuts, ancestry, cross-drive references, throttling and expired delegated authority. Add API tests for forged handles, cross-company/session access, bounds and cancellation. Run focused Operations/API/Web-client tests and builds. Perform a live discovery/browse smoke test for both source kinds when authorized and record any endpoint-specific permission differences.

### Definition of done

Real Microsoft source and folder discovery works through server-controlled opaque selections with bounded navigation and no manual IDs. Unsupported or unauthorized states are explicit, and no production connection or resource permission is prematurely claimed.

## Prompt 11 — Review and provision least-privilege repository access

### Title and outcome

Complete the administrator's choices for read-only versus an optional writable output folder and agent access, show an exact review, then idempotently provision the selected Microsoft resource permission and activate the repository connection.

### Current context

Prompt 10 leaves a verified source/root selection in the onboarding session. Existing connection entities already model audience, explicit agent grants, read-only mode and a writable folder item ID. Existing validation, import, synchronization, publication and update workflows assume the Microsoft resource grant already exists. The current create endpoint writes a pending connection before validation; it does not provision Microsoft permissions. Creating a selected-resource permission is a new external side effect and must follow the repository's workflow/outbox and reconciliation rules.

### Dependencies

Prompts 9–10. The platform Entra application must have administrator consent for the reviewed selected application permission, and the onboarding administrator must retain the temporary authority required by current Microsoft Graph documentation to assign the selected resource.

### Implementation requirements

1. Extend the onboarding draft with an access mode: read-only by default, or approved agent output enabled with one explicitly selected writable folder. Reuse the secure folder browser to select the output folder and prove it is the same drive and a descendant of the approved root. Do not permit the root, output folder or access mode to be replaced by browser-supplied IDs.
2. Add explicit company-agent selection using the current roster and backend company validation. Default to no agent grants. Preserve the `company` publication audience explanation: Microsoft administrator access and a resource grant do not automatically authorize every Virtual Company agent or employee.
3. Produce a server-derived review projection containing Microsoft tenant/source type, source and root names, read/write behavior, output folder when enabled, selected agents, import behavior and the exact permission changes Virtual Company will request. The browser submits the draft concurrency/version and a confirmation, not a reconstructed connection payload.
4. Implement finalization as a durable, idempotent provisioning workflow. At execution time, revalidate session ownership/expiry, administrator authority, source/root identity and ancestry; assign the platform application `read` on the approved root and, only when enabled, the documented write role on the designated output folder. Verify current Graph role semantics and endpoint compatibility for OneDrive and SharePoint before coding. Never widen to an entire tenant, site or drive as a convenience.
5. Persist provider permission identifiers, resource identities, provisioning attempts and ownership provenance needed to distinguish wizard-managed grants from pre-existing/customer-managed grants. Reconcile timeouts and duplicate delivery by reading the exact resource permission before retrying; do not create duplicate grants or claim success from an ambiguous response. Partial read/write provisioning remains an operator-visible incomplete state and never enables writes.
6. Create or activate the `CompanyDocumentRepositoryConnection` only from the finalized server-side selection, using platform-managed credential mode. Reuse existing validation and agent-grant boundaries. Queue the initial import only after the read grant is verified and connection validation succeeds. A failed import does not roll back an otherwise valid connection, but its failure must be visible.
7. Consume and clean the temporary delegated authorization after finalization. Runtime Graph operations must prove they use the platform application token and selected resource grant rather than the administrator token. Recheck read/write grant availability before existing retrieval and publication behavior as already required.
8. Define safe cancellation and disconnect semantics for wizard-managed grants. A cancellation before permission mutation has no Microsoft side effect. If a managed grant was created and setup cannot complete, expose a reviewed retry or cleanup action with durable reconciliation. Existing customer-managed grants must never be revoked. Any future disconnect option to revoke wizard-managed grants requires explicit administrator confirmation, targets only persisted grant IDs created by this workflow and never deletes remote content.
9. Audit draft confirmation, provisioning, validation, activation, cleanup/reconciliation and failure using safe identifiers and correlation. Add low-cardinality metrics for provisioning age/outcome. Update the operations documentation with consent-versus-resource-grant behavior, retry, cleanup and credential rotation.

### Constraints and preservation rules

Follow the Database and EF Core, Workflow and Approval, and External Side Effects and Outbox sections of `/docs/architecture-rules.md`. Microsoft permission mutation is authorized only by the administrator's explicit review confirmation; agents cannot invoke it. Preserve existing manual/customer-managed connections and their non-revocation behavior. Never store delegated tokens or expose platform credentials in the resulting connection DTO.

### Acceptance criteria

- Given a confirmed read-only draft, finalization creates or verifies exactly one root read assignment, validates the application-only connection and queues initial import without a client secret or Graph ID being entered in the UI.
- Given approved writes, only the selected descendant output folder receives the reviewed write assignment; a folder outside the root or another drive is denied.
- Given no selected agents, imported content is inaccessible to agents until an administrator later grants access; given selected agents, only valid agents in that company are persisted.
- Given duplicate execution, a timeout or process restart, reconciliation produces at most the intended grants and one connection, with no false active state.
- Given expired authority, changed source, revoked consent or incomplete write provisioning, the workflow fails safely with a recoverable state and does not run a write.

### Verification

Test draft tampering, cross-company agents, output-folder escape, consent/grant races, duplicate claims, partial success, ambiguous Graph responses, restart recovery and platform-token runtime use. Add SQL Server migration, uniqueness, concurrency and pending-model checks. Run existing connection/import/publication/update regressions for customer-managed and platform-managed modes. Perform separately recorded live read-only and writable-folder provisioning checks when authorized; do not simulate them as live success.

### Definition of done

Review and connect is a real, durable permission-provisioning workflow. The activated connection is least-privileged, application-only, compatible with existing repository operations and recoverable from ambiguity, with no broad fallback or unmanaged partial side effect.

## Prompt 12 — Deliver the guided Connect Microsoft 365 experience

### Title and outcome

Replace the normal technical-ID form with a polished guided workflow: connect Microsoft 365, sign in as an administrator, approve access, choose OneDrive or SharePoint, select the root, choose optional writes, select agents, review and connect.

### Current context

Prompts 9–11 provide authorization, discovery, draft/review and durable finalization APIs. `DocumentRepositoriesSettings.razor` already displays connection health, imports, synchronization, recovery and disconnect controls, but its primary editor exposes tenant ID, application ID, credential reference, drive ID and root item ID. The page has focused component, surface and API-client tests. The current settings reference image predates the guided flow.

### Dependencies

Prompts 9–11. The mandatory reference-image workflow in `/docs/design.md` applies. Read `/src/VirtualCompany.Web/AGENTS.md`, use `$polish-uat-loop` for hands-on browser UAT and keep a real configured Microsoft tenant separate from deterministic UI fixtures.

### Implementation requirements

1. Before UI implementation, write a design reference prompt and generate/store a new guided-connection reference under `docs/design/references/`. It must use the canonical Settings shell and show the desktop and responsive intent for the eight requested milestones. Compare the implemented UI to the reference and refine it; `/docs/design.md` wins over the image.
2. Make **Connect Microsoft 365** the primary action. Present a short explanation of administrator consent, folder-bounded application access and Virtual Company agent access. Starting the flow calls the typed API client and navigates to Microsoft; never ask the user to find or paste a tenant ID, application ID, client secret, drive ID or folder ID.
3. Resume the correct wizard after the Microsoft callback using only the opaque onboarding handle. Show an eight-step progress treatment matching the requested journey: connect, administrator sign-in, approval, source type, source/folder selection, read/write choice, agents, and review/connect. Steps may share a screen when that improves usability, but progress, back behavior and completed choices must remain clear.
4. Build accessible OneDrive/SharePoint selection and folder browsing with breadcrumb navigation, search only if the backend safely supports it, pagination/load-more, keyboard operation and clear selected-folder summary. Distinguish OneDrive for Business from SharePoint in plain language and recommend SharePoint for durable company-owned knowledge without blocking deliberate OneDrive selection.
5. Make read-only the recommended default. When writes are enabled, require a separate output-folder selection and explain that agents can only create or replace approved whole files there. Reuse the existing approval/conflict explanations; do not imply coauthoring, deletion or arbitrary sharing.
6. Present agent access as explicit checkboxes/cards from the current company roster, initially empty. The review page must repeat source, approved root, access mode, output folder, agent audience and Microsoft permission changes. Disable final confirmation until server review is current and require a deliberate **Connect repository** action.
7. Represent finalization as pending work rather than a synchronous fiction. Show connecting, grant verification, validation, import queued, connected and recoverable failure states. Handle consent denied, wrong account/tenant, missing admin authority, expired session, no eligible sources, permission policy block, throttling, partial provisioning, validation failure and cancellation with one safe next action. Refresh/retry must not duplicate connections or grants.
8. Move the existing customer-managed form behind an administrator-only **Advanced: use your own Entra application** disclosure or separate route. Clearly label its operational burden and keep its existing validation behavior. Editing an existing customer-managed connection continues to work; platform-managed connections never reveal or request the platform credential reference.
9. Preserve the existing repository list, health, import/sync/recovery, agent access editing, publication and disconnect experiences after connection. Add a safe reauthorization/reconnect entry point for platform-managed connections when consent is revoked. Do not add a new primary navigation destination.
10. Update `DocumentRepositoryApiClient`, component tests, surface tests, route inventory and `docs/onedrive-agent-integration.md`. Provide administrator-facing setup copy and operator troubleshooting without exposing Graph IDs in the normal workflow. Localize or centralize new user-facing text according to established conventions.

### Constraints and preservation rules

Follow `/docs/design.md`, `/ui-instructions.md`, shared security rules and backend-authoritative decisions. Blazor state cannot confer authorization or manufacture completed steps. Avoid popup-only behavior that breaks callback/resume; use full-page navigation unless an established, accessible popup flow is proven. Do not store tokens or sensitive callback parameters in browser storage, query history, telemetry or error displays.

### Acceptance criteria

- Given an authorized company administrator, the visible primary journey completes all eight milestones without entering or seeing tenant, application, credential, drive or folder identifiers.
- Given a non-administrator or direct API attempt, setup and provisioning are denied server-side even if the UI is manipulated.
- Given consent denial, session expiry, Microsoft throttling or partial provisioning, the page explains the state and offers only a safe restart, retry, cleanup or support action; retry does not duplicate side effects.
- Given no selected agents, the review says no agents will receive access; given selected agents, the final connected card reflects exactly those grants.
- Given a narrow viewport or keyboard-only use, every step, folder choice, error and confirmation remains reachable and understandable.
- Given an existing customer-managed connection, it remains editable and operational through the advanced path with no migration to platform-managed credentials.

### Verification

Run focused component, typed-client, API contract, authorization and tenant-isolation tests. Exercise success, consent denial, expired session, no sources, read-only, writable output, no-agent, partial-provisioning and customer-managed fallback states. Use `$polish-uat-loop` for browser UAT, capture comparison evidence against the generated reference and fix in-scope findings. Build API and Web after focused checks. Record live Microsoft success separately from fixture-driven UI coverage.

### Definition of done

The primary user experience is the requested eight-step guided connection, visually verified and production-backed. Normal users never handle Microsoft implementation identifiers or secrets, all intermediate/failure states are implemented, the advanced legacy path remains compatible and no button or screen is backed by mock behavior.
