# Alex Sales Meeting Sidekick — implementation prompt pack

## How to use this pack

Run Prompts 1–8 in order to deliver the first production milestone. Stop there unless the realtime voice pilot or controlled product-demo work is explicitly authorized. Prompts 9–10 are post-milestone extensions and must not become dependencies of the production milestone.

Each prompt is intended to be given to a coding agent as a standalone task. The agent must inspect the current repository before changing it because the paths and implementation details below describe the baseline as of 3 September 2026, not a substitute for current code.

For every prompt:

- Read and follow `/AGENTS.md`, `/production-implementation.md`, `/src/AGENTS.md`, `/tests/AGENTS.md`, `/docs/architecture-rules.md`, and any nearer `AGENTS.md` files for touched paths.
- Follow `/docs/shared-agent-ai.md` for shared-agent work. Alex is an existing configured Sales agent; do not create a separate AI stack or a second agent runtime.
- For UI work, read and follow `/docs/design.md`. Its mandatory reference-screenshot workflow applies to all new Blazor meeting surfaces.
- Preserve the modular-monolith project boundaries. Put domain invariants in `VirtualCompany.Domain`, use-case contracts and policies in `VirtualCompany.Application`, Sales implementations in `VirtualCompany.Infrastructure.Sales`, shared AI implementations in the existing shared orchestration owner, EF configuration in `VirtualCompany.Persistence`, migrations in `VirtualCompany.Persistence.Migrations`, transport in `VirtualCompany.Api`, and UI/API clients in `VirtualCompany.Web`.
- Treat all meeting data as company-owned. Enforce company scope and server-side authorization on every read, write, hub connection, provider callback, background job, and AI retrieval.
- Use UTC internally, explicit stable status values, optimistic concurrency where concurrent meeting clients can write, safe error summaries, correlation IDs, and business audit evidence.
- Do not put important queryable business facts solely in JSON. JSON is allowed only for bounded raw provider metadata or similarly flexible snapshots.
- Implement production behavior rather than scaffolding, placeholder endpoints, mock production data, or deferred in-scope TODOs.
- Run focused tests first, followed by the appropriate solution build or broader validation. Do not weaken existing tests.

---

## Prompt 1 — Add the company-scoped sales meeting session foundation

### 1. Title and outcome

Implement the persistent `SalesMeetingSession` aggregate and its application/API lifecycle so a scheduled invitation can become a durable, resumable sales meeting owned by the existing Alex sales workflow. This creates the safe system-of-record boundary required by every later prompt.

### 2. Current context

The repository already has `SalesMeetingInvitation` and `SalesMeetingChangeRequest` entities, EF configurations, SQL Server migrations, `ISalesMeetingSchedulingService`, `SalesMeetingSchedulingService`, approval-backed scheduling/change delivery, and routes in `VirtualCompany.Api/Controllers/SalesController.cs`. Invitations already reference company, lead, optional deal/contact, calendar connection, provider IDs, and online meeting URL. The lead detail Blazor page schedules, reschedules, and cancels meetings. `VirtualCompanyDbContext` exposes the existing sales DbSets. There is no meeting-session aggregate or runtime lifecycle yet.

### 3. Dependencies

None.

### 4. Implementation requirements

- Add a `SalesMeetingSession` company-owned aggregate with explicit invariants and stable enums/value mappings. It must reference one `SalesMeetingInvitation` and the resolved Lead, optional Deal, optional Contact, and customer/company account using the repository’s existing customer model rather than duplicating customer data.
- Persist meeting goal, intended audience, planned duration, optional demo scenario, provider meeting ID, lifecycle status, current slide index, current talking-point/resume marker, consent state, retention policy/configuration, created/updated timestamps, and an optimistic concurrency version.
- Model lifecycle transitions needed by later work: `Ready`, `Presenting`, `Interrupted`, `Answering`, `Resuming`, `Discussion`, `Closing`, `Completed`, and an explicit cancelled/failed terminal state if current repository conventions require them. Reject invalid transitions in domain/application code.
- Add CQRS-lite commands/queries and response contracts to create or update a session from an authorized existing invitation, get a session, and transition lifecycle state. Repeated create requests for the same invitation must be idempotent and return the existing session.
- Validate that all linked records belong to the same company and are consistent with the invitation. Do not trust IDs supplied by the UI.
- Add transport-only API endpoints under a focused sales-meeting controller or focused controller partial rather than expanding a catch-all controller. Apply the established company context and authorization policies.
- Add typed Web client methods using the established `ICompanyApiTransport` path and safe problem mapping, without building the meeting UI yet.
- Add EF configurations, DbSets, a SQL Server migration, and an updated model snapshot. Use relational columns and tenant-safe composite foreign keys/indexes. Follow the `Database and EF Core` section of `/docs/architecture-rules.md`.
- Record audit evidence for creation and lifecycle transitions, including actor, previous/new status, source invitation, correlation ID, and timestamps.

### 5. Constraints and preservation rules

- Follow `/docs/architecture-rules.md`, especially `Multi-Tenancy and Authorization`, `Database and EF Core`, `Commands, Queries, and Policies`, and `Audit and Observability`.
- Do not alter existing invitation scheduling, approval, confirmation-email, reschedule, or cancellation behavior.
- A session is not permission to mutate Deal, Lead, Contact, pricing, terms, or external provider data.
- The meeting workflow must remain usable without Teams raw audio, OpenAI Realtime, or any voice provider.

### 6. Acceptance criteria

- Given an authorized company member and an invitation in that company, when a session is created twice, then exactly one session exists and both calls identify it.
- Given an invitation or related entity from another company, when session creation or retrieval is attempted, then no cross-company data is returned or mutated.
- Given a `Ready` session, when valid transitions occur, then status and resume state persist with audit evidence.
- Given an invalid lifecycle transition or stale concurrency version, when a command executes, then it fails with a stable, actionable conflict response and leaves persisted state unchanged.
- Given migrations are applied to SQL Server, then the schema, indexes, foreign keys, constraints, and model snapshot match the implemented model with no pending model changes.

### 7. Verification

Add focused domain tests for invariants/transitions; API/service tests for idempotency, authorization, tenant isolation, invalid relationships, and concurrency; migration metadata/SQL Server validation for the new schema; typed Web client contract tests; then build the affected projects or solution.

### 8. Definition of done

The persistent session lifecycle is production-ready end to end, with real authenticated endpoints, migration, audit evidence, explicit failures, and tests. There is no UI placeholder, fake session data, silent transition failure, or deferred in-scope TODO.

---

## Prompt 2 — Import presentation decks and generate Alex’s pre-meeting brief

### 1. Title and outcome

Implement secure PowerPoint ingestion, deterministic slide assets, evidence-aware slide plans, and an Alex pre-meeting brief so the salesperson can prepare from approved company material before joining the meeting.

### 2. Current context

Prompt 1 provides `SalesMeetingSession`. The repository already has company document/knowledge ingestion and `ICompanyKnowledgeSearchService`, which returns company-scoped processed knowledge; shared AI contracts expose `IAgentReasoningGateway`, sourced claims, confidence, and structured results; Sales exposes `ISalesAgentDecisionService` with intelligence-brief, deal-strategy, and proposal-advice capabilities. `DocumentFormat.OpenXml` is currently referenced by Operations and API tests, but there is no Sales-owned presentation deck processor, slide renderer, slide plan, or session-linked pre-meeting brief.

### 3. Dependencies

Prompt 1 and its migration.

### 4. Implementation requirements

- Add company-owned `SalesPresentationDeck`, `SalesPresentationSlide`, and `SalesMeetingArtifact` records. Link the active deck to a meeting session while preserving version/history rather than overwriting processed evidence.
- Store deck identity, original safe file reference, content hash, processing/version state, failure reason, slide count, uploader, timestamps, and active version. Store per-slide number, title, extracted text, speaker notes, rendered image reference and exact pixel dimensions, processing state, and content hash. Store bounded provider/render metadata only where relational columns are not appropriate.
- Reuse the repository’s existing document/blob storage and access-control abstractions. Do not store large binaries in arbitrary JSON or create an ungoverned local filesystem path.
- Implement a Sales-owned deck-processing application boundary and Infrastructure.Sales implementation. Extract PowerPoint slide text and speaker notes. Render every slide to a deterministic, exact-size image through a replaceable renderer abstraction suitable for the deployed environment. Flatten/ignore complex animations for v1 and expose that limitation in metadata/UI state.
- Validate file type by content, enforce configured size/slide limits, scan or route through the repository’s established safe upload path, and fail safely on malformed, encrypted, or unsupported decks.
- Generate and persist a per-slide plan containing objective, ordered talking points, expected timing, transition, source IDs, and claims needing source or human confirmation. Use the shared agent reasoning gateway and approved company-scoped knowledge only; do not call an LLM provider directly from Sales.
- Generate a structured pre-meeting brief by composing existing Sales intelligence services for customer context, likely needs, deal risks, recommended questions, product positioning, desired next step, missing evidence, and source IDs. Preserve deterministic confirmed facts separately from generated recommendations.
- Add authorized commands/queries/endpoints and typed Web client methods for upload/import, processing status, deck/slide retrieval, activation, retry after a retryable processing failure, and pre-meeting brief retrieval/regeneration.
- Run processing as durable background work when it can outlive a request. Make retries idempotent by company/session/deck content hash and processing version. Persist safe failure state and audit evidence.
- Add EF configuration, DbSets, a SQL Server migration, and model snapshot updates under the repository database rules.

### 5. Constraints and preservation rules

- Follow the AI, knowledge, integration, tenant, database, audit, and background-work boundaries in `/docs/architecture-rules.md` and `/docs/shared-agent-ai.md`.
- Only accessible, processed, approved company sources may support product, pricing, policy, or customer claims. Mark unsupported claims for confirmation; never convert them into authoritative facts.
- Do not couple core presentation state to a proprietary renderer. If production rendering requires an external executable/service, keep it behind an application contract with configuration, health reporting, timeouts, and safe failures.
- Do not make PowerPoint animation fidelity a v1 requirement.

### 6. Acceptance criteria

- Given a valid PPTX and authorized user, when it is imported, then all slides have ordered extracted text/notes and exact-dimension rendered images, and processing can be resumed safely after interruption.
- Given the same deck content is submitted repeatedly to the same session, then processing is idempotent and does not duplicate deck versions, slides, artifacts, or AI runs.
- Given a slide claim lacks approved evidence, when the slide plan is generated, then it is explicitly flagged for confirmation and is not presented as verified.
- Given a deck or source belongs to another company or is not accessible/processed/approved, then it cannot be attached, retrieved, or used for grounding.
- Given malformed, oversized, encrypted, or unsupported input, then processing stops with a safe actionable status and no partial deck is activated.
- Given a processed deck, then Alex’s pre-meeting brief includes source-linked confirmed facts, recommendations, gaps, and review status without changing canonical sales data.

### 7. Verification

Add unit tests for parsing/validation, ordering, dimensions, claim classification, and idempotency; integration tests for background processing, authorization and tenant isolation; migration validation; AI gateway contract tests with deterministic test doubles; representative PPTX fixtures covering notes, blank slides, unsupported content, and malformed files; then run affected builds.

### 8. Definition of done

Deck ingestion through pre-meeting brief retrieval works with real storage, durable processing, source evidence, migration, APIs, typed client support, audit/failure visibility, and no fake slide assets or production mocks.

---

## Prompt 3 — Implement the synchronized presentation runtime and meeting hub

### 1. Title and outcome

Implement the authoritative presentation state machine, deterministic presentation commands, and a dedicated SignalR hub so multiple authorized meeting clients stay synchronized and Alex can resume exactly after an interruption.

### 2. Current context

Prompts 1–2 provide persistent sessions, decks, slides, slide plans, current slide, and resume metadata. The API already registers SignalR and maps `ActivityFeedHub`; the Web project already references the SignalR client package. There is no sales meeting hub or presentation command surface. Existing company context and authorization patterns must be reused.

### 3. Dependencies

Prompts 1–2, including their migrations and an active processed deck.

### 4. Implementation requirements

- Define typed application contracts for `presentation.get_current_slide`, `presentation.next`, `presentation.previous`, `presentation.goto`, `presentation.search_slides`, `presentation.pause`, and `presentation.resume`. Treat these as explicit, company-scoped tools/commands, not arbitrary client messages.
- Implement an authoritative backend state machine. Persist the exact current slide and talking-point/resume marker before changing from `Presenting` to `Interrupted`/`Answering`, then restore through `Resuming` without losing position.
- Define command sequence/version semantics so duplicate, delayed, or out-of-order client commands do not corrupt meeting state. Use optimistic concurrency and return the accepted authoritative snapshot after every command.
- Add a dedicated Sales meeting SignalR hub with a stable route, typed event payloads, session groups, connection/reconnection snapshot support, and server-side membership checks. Derive company/user context from authenticated server context; never accept a client-asserted company as authorization.
- Separate stage-safe events from private side-panel events. Customer-visible stage clients must never receive private notes, internal intelligence, confidence, approvals, or salesperson-only suggestions.
- Add HTTP query/command fallback endpoints so state can be recovered when SignalR is unavailable. SignalR broadcasts only after successful persisted state changes.
- Register the hub and services in their owning modules. Add audit/telemetry for transitions, commands, rejected commands, reconnects, latency, and concurrency conflicts without logging sensitive content.
- Add typed Web client/hub client abstractions that manage reconnect, recover the authoritative snapshot, and make offline/degraded status explicit.

### 5. Constraints and preservation rules

- Follow `/docs/architecture-rules.md` for authorization, command/query separation, audit, and UI transport boundaries.
- The server is authoritative. Do not let Blazor components or SignalR clients implement lifecycle policy.
- Do not broadcast company data across session groups or leak private side-panel content to the shared stage.
- Core presentation commands must work without Teams SDK, raw media, or realtime voice.

### 6. Acceptance criteria

- Given two authorized clients in one session, when a valid presentation command succeeds, then both receive the same versioned authoritative state.
- Given an unauthorized user, wrong-company user, or wrong-session group request, then the hub and HTTP fallback disclose no session state and perform no mutation.
- Given Alex is interrupted on slide N at talking point M, when answering completes and resume is requested, then the persisted and broadcast state returns to slide N/talking point M before continuing.
- Given duplicate or out-of-order commands, then state changes at most once, remains valid, and the caller receives a conflict or current snapshot.
- Given a dropped/reconnected client, when it rejoins, then it receives the current server snapshot and does not replay stale commands.
- Given a stage connection, then its payload contains only customer-visible slide/presentation data.

### 7. Verification

Add domain/state-machine tests, API authorization and tenant-isolation tests, multi-client SignalR integration tests, reconnect/order/idempotency tests, stage/private contract-leak tests, and Web hub-client tests; then run affected builds and an end-to-end local synchronization check.

### 8. Definition of done

The persisted state machine, tool commands, HTTP fallback, hub, registrations, clients, security, audit, and recovery behavior are production-ready. No in-memory-only meeting state or placeholder broadcasts remain.

---

## Prompt 4 — Deliver the Microsoft Teams stage and private Alex cockpit

### 1. Title and outcome

Create and approve reference visuals, then implement the two production Blazor meeting surfaces: a customer-visible `meetingStage` presentation and a salesperson-private `meetingSidePanel` cockpit, packaged as a Microsoft Teams meeting app.

### 2. Current context

Prompts 1–3 provide session/deck APIs, typed presentation commands, and synchronized public/private streams. The existing Web app is Blazor, uses typed sales clients, and has established sales pages/components. `/docs/design.md` requires reference screenshots before implementation. No Teams meeting app manifest, Teams-aware stage/side-panel route, or dedicated meeting UI currently exists.

### 3. Dependencies

Prompts 1–3. A user-approved reference image for both desktop surfaces is a mandatory implementation prerequisite. Microsoft Teams tenant/app registration values and hosting URLs are required only for live Teams installation; local browser-hosted verification must still be completed without them.

### 4. Implementation requirements

- First execute the mandatory reference-screenshot workflow in `/docs/design.md`: inspect relevant current sales/agent surfaces, create high-fidelity reference images for both stage and side panel, save them under the repository’s design-reference convention, and obtain explicit user approval. If approval requires a later turn, stop only at that genuine decision boundary; after approval, implement against the selected references.
- Build separate Blazor routes/components for the shared meeting stage and private side panel. Reuse design tokens/components and localize user-facing text according to repository conventions.
- The stage must show the exact rendered slide at the correct aspect ratio, current presentation state, intentional paused/answering/closing states, and a safe degraded/reconnect experience. It must contain no private notes or internal sales intelligence.
- The side panel must show the current slide, next talking point, plan timing, detected/entered questions, sources and confidence, autosave status, meeting lifecycle state, and accessible controls for next/previous/goto/search/pause/resume. Leave later Q&A/capture sections in truthful unavailable states until Prompt 5 rather than mocking them.
- Integrate the typed HTTP/SignalR clients from Prompt 3 with cancellation, reconnect, authoritative resync, duplicate-click protection, loading/empty/error/conflict states, and accessible keyboard/focus behavior.
- Add a production Teams app manifest/configuration for `meetingStage` and `meetingSidePanel`, content URLs, allowed domains, identity/context bootstrap, and share-to-stage behavior. Keep environment-specific IDs/URLs in validated configuration; do not commit secrets.
- Validate current Teams meeting-app APIs against official Microsoft documentation during implementation. Keep Teams SDK/provider details at the Web/integration boundary and normalize them before application commands.
- Add navigation/deep links from the existing meeting invitation/lead/deal experience only where authorized and only when a session is ready.
- Document local setup, Teams app registration/configuration, packaging, deployment prerequisites, permission scope, and limitations. Do not claim raw meeting audio access.

### 5. Constraints and preservation rules

- The mandatory UI workflow in `/docs/design.md` applies in full; `/docs/design.md` wins over companion UI guidance.
- Keep public and private data contracts separate end to end. Hiding an element with CSS is not a security boundary.
- Do not embed desktop PowerPoint. Use the custom viewer and rendered slide assets.
- Do not make the feature depend on raw-media bots, microphone access, or OpenAI Realtime.
- Preserve existing sales scheduling UI and responsive navigation.

### 6. Acceptance criteria

- Given an approved reference set, when the surfaces are rendered at required target sizes, then visual comparison shows faithful structure, hierarchy, spacing, states, and responsive behavior under `/docs/design.md`.
- Given an authorized Teams meeting participant opens the stage and salesperson opens the side panel, then both synchronize to the same public presentation state while only the salesperson receives private data.
- Given a stage request manipulated with another company/session ID, then no slide or meeting data is disclosed.
- Given SignalR disconnects, then each surface shows degraded status, reconnects, and restores the authoritative server snapshot without duplicate commands.
- Given missing or invalid Teams context/configuration, then the app fails safely with actionable setup guidance and remains testable in its authorized browser-hosted mode.
- Given keyboard-only and screen-reader use, then primary presentation controls and state changes are operable and announced.

### 7. Verification

Add Blazor component and route tests, public/private contract tests, auth/context bootstrap tests, localization checks, keyboard/accessibility checks, manifest/configuration validation, and browser UAT at the design target sizes. Capture final comparison screenshots and run the affected Web/API builds.

### 8. Definition of done

Approved reference images and the production Teams package, stage, cockpit, deep links, configuration documentation, tests, and verified browser flow are complete. No mock meeting content, secret values, or unhandled loading/reconnect states remain.

---

## Prompt 5 — Add grounded meeting Q&A and continuous structured capture

### 1. Title and outcome

Enable Alex to answer typed or host-mediated questions from approved evidence and continuously autosave the meeting discussion as structured, reviewable records without changing canonical sales data.

### 2. Current context

Prompts 1–4 provide sessions, processed slides/notes, slide-plan sources, pre-meeting intelligence, presentation state, stage, and private cockpit. The repository already has shared `IAgentReasoningGateway`, sourced claims/confidence, company knowledge search, Sales agent decision services, audit infrastructure, and explicit agent tool governance. There is no meeting-specific question, transcript, observation, or action-item persistence and no meeting Q&A orchestrator.

### 3. Dependencies

Prompts 1–4 and at least one active processed deck for slide-aware grounding. The workflow must also support discussion-only meetings without a deck by using approved sources.

### 4. Implementation requirements

- Add relational company-owned entities and stable status/type values for `SalesMeetingTranscriptSegment`, `SalesMeetingQuestion`, `SalesMeetingObservation`, and `SalesMeetingActionItem`. Include session linkage, speaker/source attribution, timestamps/order, content, confidence where relevant, source IDs through relational associations or the established evidence model, review state, provenance, created/updated timestamps, and concurrency/idempotency metadata.
- Model observation categories for customer needs/pain points, product interests, objections, competitive references, buying signals, commitments, open questions, and internal sales intelligence. Meeting observations remain meeting-owned evidence; they do not directly update Deal, Lead, Contact, pricing, promises, or terms.
- Implement append/upsert commands and batch autosave endpoints with stable client idempotency keys and optimistic concurrency. Preserve ordering, tolerate retries/reconnect, reject cross-session/company links, and return authoritative save/version state.
- Add a meeting Q&A orchestration service in the Sales capability that uses the shared agent runtime. Ground each answer only in the visible slide and approved notes, accessible approved product/pricing/policy documents, company-scoped knowledge search, and authorized customer/Deal/Contact records.
- Store the question, answer, source IDs, per-claim confidence, visible slide/version, asker/speaker attribution, timestamps, and whether follow-up/review is required. If evidence is absent or insufficient, answer clearly that the detail is not verified and create an open follow-up question; do not improvise.
- Expose narrowly scoped read/recommend tools to Alex under the existing agent capability/tool registry and guardrails. No execute tool may update canonical sales data in this prompt.
- Connect question submission, answer streaming/status, source inspection, structured observations, action items, and autosave indicators to the private side panel. Only an explicitly customer-visible, approved answer may be mirrored to the stage.
- Make AI timeouts, cancellation, partial response, provider failure, and moderation/safety outcomes explicit and recoverable. Persist audit evidence without logging unnecessary sensitive transcript content.
- Add EF configurations, DbSets, SQL Server migration, and snapshot updates.

### 5. Constraints and preservation rules

- Follow `Agent and AI Orchestration`, knowledge, security, database, and audit rules in `/docs/architecture-rules.md` plus `/docs/shared-agent-ai.md`.
- Sales must not call an LLM provider directly. The shared gateway remains the provider boundary.
- Continuous capture may autosave meeting-owned notes and observations. Suggested changes to canonical sales records are out of scope until Prompt 8.
- Do not depend on raw Teams audio. MVP inputs are typed, host-mediated, or a later normalized transcript adapter.
- Never expose internal observations or private sources to the shared stage by default.

### 6. Acceptance criteria

- Given a question answerable from the visible slide or approved accessible sources, when Alex answers, then the stored response cites valid source IDs and confidence and can be inspected from the cockpit.
- Given insufficient evidence, when a question is asked, then Alex states it is unverified and a deduplicated open follow-up is persisted.
- Given autosave retries or reconnects, when identical transcript/observation/action batches are submitted, then records are not duplicated and ordering remains stable.
- Given a request references another company’s slide, customer, deal, contact, or knowledge source, then it is rejected or excluded without disclosure.
- Given an observation suggests a deal-stage, probability, value, next-step, discount, price, promise, or term change, then no canonical field or external system changes.
- Given provider failure or cancellation, then the question has a truthful recoverable state rather than a fabricated or silently lost answer.

### 7. Verification

Add domain/persistence tests for types and ordering; API tests for idempotency, concurrency, authorization, and tenant isolation; grounded-answer tests for source allowlists, missing evidence, confidence, and data leakage; AI failure/cancellation tests; Blazor autosave/reconnect/source-display tests; migration validation; then run affected builds and a browser flow.

### 8. Definition of done

Grounded Q&A and continuous meeting capture operate end to end with real persistence, source evidence, robust autosave, safe failures, UI integration, migration, and tests. No AI-generated answer can masquerade as verified without evidence, and no canonical sales mutation path exists here.

---

## Prompt 6 — Produce closing summaries and separated meeting artifacts

### 1. Title and outcome

Implement Alex’s closing workflow and generate two strictly separated artifacts: a customer-facing Minutes of Meeting and internal sales intelligence, both reviewable and traceable to meeting evidence.

### 2. Current context

Prompts 1–5 provide session lifecycle, approved presentation sources, grounded Q&A, transcript fragments, observations, commitments, follow-ups, and action items. `SalesMeetingArtifact` exists from Prompt 2. There is no `SalesMeetingMinutes` aggregate, closing-summary command, artifact separation policy, or MoM review projection.

### 3. Dependencies

Prompts 1–5 and their migrations.

### 4. Implementation requirements

- Add a company-owned, versioned `SalesMeetingMinutes` aggregate linked to the session and artifact records. Persist draft/review/approved/superseded status, evidence cutoff/version, generator metadata, source IDs, created/updated/reviewed timestamps, reviewer, and concurrency version.
- Generate a customer-facing MoM containing only decisions, agreed actions/owners/dates, outstanding questions, proposed next meeting, and approved product statements. Exclude internal objections analysis, buying-signal scoring, competitive strategy, private notes, confidence commentary, and Deal recommendations.
- Generate a separately stored and authorized internal sales-intelligence artifact containing objections, buying signals, competitive information, risks, recommendations, and source links. Make accidental cross-rendering structurally difficult by using separate contracts/projections, not a shared blob with a visibility flag.
- Add a closing-summary query/command that assembles what was agreed, outstanding questions, actions/owners/dates, proposed next meeting, and proposed Deal changes for visual presentation. It must not mark the meeting completed until required capture is flushed and the closing snapshot is persisted.
- Use the shared reasoning gateway for generated wording while deriving deterministic facts/actions from persisted meeting records. Every factual statement must retain source/evidence linkage or be marked for review.
- Support regenerate-as-new-version, edit draft, submit for review, and approve content. Never overwrite the approved historical version in place.
- Add authorized endpoints, typed Web client methods, and side-panel/closing UI. Customer-visible preview must use only the customer-facing projection.
- Add EF configuration, DbSets, SQL Server migration, model snapshot, audit events, and retention behavior consistent with session consent/retention configuration.

### 5. Constraints and preservation rules

- Follow tenant, AI, database, authorization, audit, and data-retention rules in `/docs/architecture-rules.md`.
- Artifact generation is not permission to send email, create a calendar event, or update sales records.
- Keep customer and internal artifacts separate in persistence, contracts, authorization, rendering, export, and tests.
- Existing meeting evidence must remain immutable/auditable enough to explain each generated version.

### 6. Acceptance criteria

- Given persisted meeting evidence, when closing is prepared, then the summary lists agreements, open questions, actions with owners/dates, proposed next meeting, and proposed Deal changes without executing them.
- Given internal-only observations, when the customer MoM is generated or rendered, then none appear in its persistence projection, API payload, UI, or export.
- Given an unsupported product statement, when a MoM is generated, then it is omitted or visibly requires review and cannot be approved as an approved product statement without evidence.
- Given a draft is edited or regenerated after approval, then a new version is created and the approved history remains intact.
- Given a session with unsaved capture, when completion is attempted, then completion is blocked or flushes the capture transactionally before persisting the closing snapshot.
- Given a cross-company request, then neither customer nor internal artifact content is disclosed.

### 7. Verification

Add separation/leakage tests at domain, query, API, and rendered-component levels; generation/source tests; version/concurrency tests; authorization and tenant-isolation tests; lifecycle completion tests; migration validation; accessible UI/browser checks against approved design references; then run affected builds.

### 8. Definition of done

Closing and both artifact types are production-ready, versioned, sourced, independently authorized, reviewable, retained according to policy, rendered in the UI, and covered against internal-data leakage. Nothing is sent externally or applied to canonical sales records yet.

---

## Prompt 7 — Reconcile Microsoft Teams transcripts through Microsoft Graph

### 1. Title and outcome

Implement post-meeting transcript ingestion and reconciliation so Microsoft Graph can correct speaker attribution and fill live-capture gaps without duplicating or silently replacing reviewed meeting evidence.

### 2. Current context

Prompts 1–6 provide provider meeting IDs, consent/retention settings, live transcript segments, Q&A, observations, action items, and versioned artifacts. The repository already has Microsoft 365 calendar-provider integration patterns and durable background/outbox infrastructure, but no Graph meeting-transcript subscription/webhook adapter or reconciliation workflow. The core meeting workflow is intentionally independent of raw Teams audio.

### 3. Dependencies

Prompts 1–6. A configured Microsoft Entra/Graph application with the approved transcript permissions, webhook URL, and tenant administrator consent is required for live-provider verification, but local contract/integration tests must use deterministic provider test doubles.

### 4. Implementation requirements

- Define provider-neutral application contracts for meeting transcript notification, fetch, normalized segment/page, subscription lifecycle, and reconciliation result. Implement the Microsoft Graph adapter in the appropriate integration-owning module without leaking Graph DTOs into Domain/Application.
- Verify the current official Microsoft Graph transcript/change-notification API, permissions, validation-token flow, subscription lifecycle, expiration/renewal, resource identifiers, pagination, and retry semantics during implementation. Document the selected route and permission rationale.
- Add transport endpoints for Graph webhook validation/notifications with provider authenticity validation, replay protection, safe acknowledgement, and no trust in a payload-supplied company ID. Resolve the company/session from persisted provider identifiers.
- Enqueue durable, idempotent background ingestion keyed by company, provider meeting/transcript ID, and provider version/change token. Handle duplicate and out-of-order notifications.
- Persist bounded raw provider metadata and normalized transcript source/version state. Do not log raw tokens or sensitive provider payloads.
- Implement deterministic reconciliation that aligns provider transcript segments with live segments, corrects speaker attribution, fills gaps, and preserves provenance. Never duplicate equivalent records, erase user edits, or silently rewrite reviewed/approved evidence. Conflicts must enter explicit review state with before/after evidence.
- Mark downstream MoM/internal-artifact versions stale when reconciled evidence materially changes them; do not silently regenerate or resend. Expose reconciliation status, gaps, conflicts, and safe failures in the private review UI.
- Respect consent and retention configuration before subscription, ingestion, storage, retrieval, and deletion. Make permission/reconnect/permanent failures operator-visible and do not retry them indefinitely.
- Add any required relational entities/configurations, DbSets, SQL Server migration, snapshot, audit, telemetry, health checks, and operational documentation.

### 5. Constraints and preservation rules

- Follow `Integration Boundaries`, `External Side Effects and Outbox`, tenant, database, audit, and background-execution rules in `/docs/architecture-rules.md`.
- Microsoft Graph is an integration, not the system of record. Normalize its data before domain/application processing.
- Do not build or depend on a Teams raw-media bot.
- Do not bypass consent/retention policy, overwrite reviewed evidence, or auto-send regenerated artifacts.

### 6. Acceptance criteria

- Given a valid duplicate Graph notification, when it is received repeatedly, then it is acknowledged safely and produces one logical ingestion/reconciliation result.
- Given transcript segments overlap live capture, when reconciled, then equivalent content is merged without duplication and provenance for both sources is retained.
- Given corrected speaker attribution or missing provider content, then reconciled data updates the appropriate evidence and reports the change.
- Given a conflict with user-reviewed evidence, then the conflict is preserved for review and no reviewed/approved record is silently overwritten.
- Given a materially changed evidence set, then affected artifact versions are marked stale but are neither silently regenerated nor sent.
- Given invalid webhook authenticity, missing consent, expired permissions, or cross-company provider IDs, then no meeting content is disclosed or ingested and the failure is safely observable.

### 7. Verification

Add adapter contract tests, webhook authentication/validation tests, duplicate/out-of-order notification tests, pagination and retry classification tests, reconciliation golden-case/property tests, user-edit conflict tests, consent/retention and tenant-isolation tests, migration checks, and background-worker recovery tests. Run live Graph verification only when credentials are explicitly available, then run affected builds.

### 8. Definition of done

The Graph subscription-to-reconciliation path is production-ready with real provider configuration, secure webhooks, durable processing, provenance, conflict review, retention enforcement, operations documentation, and tests. Lack of live credentials is documented as an external verification gap, not filled with a fake production adapter.

---

## Prompt 8 — Review, approve, and execute sales changes and follow-up delivery

### 1. Title and outcome

Deliver the production review workspace where a salesperson can edit and approve individual proposals, approve eligible safe proposals in bulk, reject proposals, apply approved canonical sales changes through application commands, and send approved MoM/follow-up communication through the durable outbox.

### 2. Current context

Prompts 1–7 provide the complete meeting record, customer MoM, internal intelligence, reconciled transcript, closing summary, and suggested changes that have not modified canonical data. The repository already has `ApprovalRequest`/approval steps/tasks, `CompanyApprovalRequestService`, Sales action/meeting approvals, Deal/Lead/Contact application operations, approval-backed calendar scheduling/change delivery, and `CompanyOutboxMessage` dispatch infrastructure. Existing deal detail and approval UI patterns can inform the review surface. There is no `SalesMeetingChangeProposal`, field-diff policy, meeting review workspace, or MoM delivery topic.

### 3. Dependencies

Prompts 1–7 and their migrations. Prompt 7 may remain provider-unverified when external Graph credentials are unavailable, but review must clearly show transcript reconciliation state and allow an authorized reviewer to proceed according to policy.

### 4. Implementation requirements

- Add a relational company-owned `SalesMeetingChangeProposal` aggregate linked to session, evidence, target entity, field/action, typed proposed value, captured before value/version, confidence, rationale, source IDs, risk class, status, approval requirement/request, reviewer/decision timestamps, execution state, idempotency key, conflict/failure details, and concurrency version.
- Implement a backend `SalesMeetingChangePolicy` that classifies proposals. Meeting notes, transcript, Q&A, MoM drafts, needs, objections, and product interests remain meeting-owned autosaved evidence. Canonical Deal stage/probability/value/next step and any Lead/Contact change require explicit confirmation or approval based on existing authority. Discounts, prices, promises, and contract terms are always approval-gated. Customer communication and calendar actions always require approval plus durable delivery.
- Generate typed proposals from meeting evidence without writing target records. Validate target/field allowlists, value types/ranges, evidence, target version, actor authority, and current business rules. Do not accept arbitrary property paths or command names from the model/client.
- Create query projections showing human-readable before/after diffs such as stage, probability, primary need, and next action, together with rationale, evidence, confidence, policy result, approval/execution state, and conflicts.
- Support edit, approve, reject, and bulk approve-all-safe. Bulk approval must reevaluate every proposal independently on the server and must not include sensitive/always-gated proposals merely because the client labelled them safe.
- Bind approval to the exact proposal target, typed value, evidence/version hash, and policy version. Immediately before execution, recheck approval, permissions, proposal/target versions, and policy. Changed targets enter conflict review rather than overwriting newer data.
- Execute approved sales changes only through existing or focused new Application commands owned by Sales. Make execution idempotent and transactional per proposal; persist before/after evidence and audit records. Never let the AI or controller update EF entities directly.
- Implement the review Blazor screen with customer MoM preview, clearly separated internal intelligence, field diffs, individual/bulk review controls, edit/reject paths, actions/owners/dates, transcript reconciliation/staleness warnings, approval status, execution outcome, and retry/reconciliation states. Follow the approved-reference workflow in `/docs/design.md` for this new surface.
- Implement approved customer-MoM delivery and approved next-meeting/calendar actions through durable outbox/background dispatch. Reuse existing mailbox/calendar adapters and approval flows where suitable. Stable idempotency keys must derive from company, session, artifact/action, recipient/target, and approved version. Handle retryable, permanent, authentication-required, and ambiguous outcomes explicitly.
- Add transport endpoints, typed Web client methods, authorization policies as needed, outbox topics/dispatchers, audit/telemetry, EF configurations/DbSets, a SQL Server migration, model snapshot, and operator/user documentation.

### 5. Constraints and preservation rules

- Follow `Workflow and Approval`, `External Side Effects and Outbox`, `Commands, Queries, and Policies`, tenant, audit, and database sections of `/docs/architecture-rules.md`.
- The AI proposes; authoritative backend policy and authorized humans decide. UI labels and prompts are not enforcement boundaries.
- No email, Teams message, calendar/provider write, price/discount promise, contract term, or canonical record change may bypass its command/policy/approval/outbox boundary.
- Do not reuse the invitation-change entity for unrelated sales-field proposals; keep meeting proposals focused and typed.
- Approval of one artifact/proposal version does not authorize a later edited version.

### 6. Acceptance criteria

- Given meeting evidence suggests a sales-field change, when proposals are generated, then the canonical Deal/Lead/Contact remains unchanged and a sourced before/after proposal is available for review.
- Given mixed safe and sensitive proposals, when `approve all safe` is invoked, then the server reevaluates all items, approves only currently eligible ones, and leaves approval-gated/conflicted items untouched.
- Given an approved proposal whose target changed after capture, when execution is attempted, then it enters an actionable conflict state and does not overwrite current data.
- Given a valid approved proposal, when executed or retried, then the owning Application command changes the target exactly once and persists audit before/after evidence.
- Given an edited proposal or MoM, then any prior approval binding is invalid and fresh approval is required.
- Given MoM/follow-up delivery receives duplicate work or an ambiguous provider result, then it never blindly sends twice; it retries/reconciles according to failure classification and exposes the outcome.
- Given unauthorized or cross-company requests at any review/approval/execute/delivery endpoint, then no data is disclosed, changed, approved, or enqueued.
- Given the full review flow, then customer MoM and internal intelligence remain visibly and contractually separate.

### 7. Verification

Add policy matrix tests for every information/action class; proposal type/value/evidence tests; approval binding/version and stale-target conflict tests; authorization and tenant-isolation tests; command idempotency/transaction tests; outbox duplicate/retry/permanent/ambiguous outcome tests; API contract tests; Blazor component, accessibility, and browser UAT against approved references; SQL Server migration checks; then run affected builds and the full first-milestone happy/failure paths.

### 8. Definition of done

Prompts 1–8 form a production-ready first milestone: Alex can prepare and present, answer with evidence, continuously capture, reconcile, produce separated artifacts, propose diffs, obtain enforceable approval, execute typed sales commands, and deliver approved follow-up durably. No important sales or external side effect is controlled only by AI output or UI state, and no in-scope placeholder or TODO remains.

---

## Prompt 9 — Add a replaceable realtime voice pilot with graceful fallback

### 1. Title and outcome

Add an explicitly optional voice pilot that gives Alex low-latency speech, interruption handling, and function calling while preserving every typed workflow and falling back cleanly whenever media or provider capability is unavailable.

### 2. Current context

Prompts 1–8 deliver the complete production milestone without raw meeting audio. The shared agent subsystem uses `IAgentReasoningGateway`; Sales has no direct provider dependency. Meeting presentation/capture already exposes typed commands, interruption state, transcript append, Q&A, consent, and evidence boundaries. There is no `IRealtimeAgentSessionGateway` or media adapter. Microsoft advises against making raw-media bots the primary architecture for this scenario.

### 3. Dependencies

Prompts 1–8. Explicit pilot authorization, approved consent/legal/retention policy, an approved Teams voice route or certified media provider, and realtime-provider credentials are required before live voice use.

### 4. Implementation requirements

- Define provider-neutral `IRealtimeAgentSessionGateway` contracts in the shared agent Application boundary for session creation, ephemeral client credentials where applicable, audio/text events, interruption, tool invocation, cancellation, health, usage, and termination.
- Implement the selected realtime provider in the existing shared AI infrastructure owner, not in Sales. Use current official provider documentation and keep credentials server-side. Do not expose long-lived provider secrets to the browser.
- Define a separate replaceable meeting-media adapter for the approved Teams voice route/provider. Normalize speaker/audio/transcript events before they enter application behavior. Keep it optional behind capability/configuration checks.
- Route realtime tool calls only through the existing explicit, company-scoped, permissioned meeting tools and guardrails. The realtime model may read/recommend but may not bypass proposal, approval, or outbox enforcement.
- On detected interruption, atomically persist the slide/talking-point resume marker, transition to `Interrupted`/`Answering`, answer through grounded Q&A, and transition through `Resuming`. Ensure barge-in/cancellation does not duplicate transcript, questions, answers, or commands.
- Enforce explicit consent before media starts; expose recording/voice state; stop capture promptly on revoke/end; apply retention policy; and audit starts/stops/provider failures without storing secrets or unnecessary raw audio.
- Add timeout/reconnect/usage limits, bounded retry, health/feature status, safe error translation, telemetry, and immediate fallback to typed/host-mediated questions. The meeting must remain fully operable when voice is disabled or fails.
- Integrate pilot controls/status into the approved side-panel design without changing the customer stage’s data boundary. Document pilot setup, permissions, costs/limits, risks, and rollback/disable procedure.

### 5. Constraints and preservation rules

- Follow `/docs/shared-agent-ai.md` and the AI, integration, approval, external-side-effect, tenant, audit, and security rules in `/docs/architecture-rules.md`.
- Do not implement an unapproved Teams raw-media bot or make core meeting completion depend on a media bot.
- Sales must never call the realtime provider directly.
- Voice output follows the same evidence rules as typed Q&A, and voice tool calls follow the same backend policy as UI commands.

### 6. Acceptance criteria

- Given voice is disabled, unavailable, disconnected, or out of quota, then the meeting, slides, capture, typed Q&A, closing, review, and approval flows continue with a clear degraded status.
- Given valid consent and provider configuration, when the pilot starts, then short-lived scoped realtime access is established without exposing server credentials.
- Given a participant interrupts Alex, then the exact slide/talking-point marker is persisted, the grounded answer is captured once, and presentation resumes from that marker.
- Given an unsupported or execute-class realtime tool request, then guardrails reject it and no sensitive action occurs.
- Given consent is revoked, then media handling stops, state is audited, and retention behavior is enforced.
- Given duplicate/reordered media/provider events, then transcript, Q&A, and tool actions remain idempotent.

### 7. Verification

Add gateway/adapter contract tests, ephemeral-credential security tests, consent and retention tests, interruption/resume integration tests, tool-guardrail tests, duplicate/reorder/cancellation tests, outage/quota fallback tests, tenant-isolation tests, and UI accessibility/status tests. Run live provider verification only with explicitly supplied credentials, then run affected builds and the original typed-flow regression.

### 8. Definition of done

Voice is a production-quality, feature-gated pilot behind shared interfaces with consent, grounding, guarded tools, observability, and graceful fallback. Disabling or removing its provider adapters leaves Prompts 1–8 fully functional.

---

## Prompt 10 — Add the resettable controlled product-demo workflow

### 1. Title and outcome

Implement a controlled, resettable demo tenant and typed product-demo commands so Alex can demonstrate Virtual Company safely without simulated mouse movement or risking real customer/provider data.

### 2. Current context

Prompts 1–8 provide the production meeting assistant; Prompt 9 is optional and irrelevant to demo correctness. The repository already has company setup, role agents, application commands, audit, tenant authorization, and sales/finance/support capabilities. There is no meeting-linked demo-scenario aggregate, isolated resettable demo tenant lifecycle, or allowlisted demo command surface.

### 3. Dependencies

Prompts 1–8. Prompt 9 is not required. Explicit authorization is required before creating/resetting any environment outside local/test infrastructure.

### 4. Implementation requirements

- Define a versioned demo-scenario specification containing seeded entities, deterministic starting state, allowed roles/tools, ordered demo actions, expected visible outcomes, reset rules, and validation checks. Keep it in a bounded, reviewable format; do not encode arbitrary executable scripts supplied by the model.
- Provision an unmistakably labelled, isolated demo company through existing company/application setup boundaries. Demo data must be synthetic and must not copy production personal/customer data, credentials, mailboxes, calendar connections, or provider identifiers.
- Implement reset as an authorized, audited application workflow scoped to the exact demo company and scenario version. Verify the target is marked as a demo tenant before any destructive reset. Prefer transactional/recoverable replacement of scenario-owned records and preserve required audit history according to repository policy.
- Expose only allowlisted typed application commands as demo tools. Each command must enforce company scope, permissions, preconditions, deterministic outputs, idempotency, and backend policy. Alex must not drive the demo through cursor coordinates, DOM guessing, simulated mouse movement, arbitrary HTTP, SQL, or arbitrary code execution.
- Add dry-run/preview and validation that reports the exact demo tenant, scenario version, affected record classes/counts, external integrations disabled, and expected post-reset invariants before reset executes.
- Prevent external side effects by default. Email, Teams, calendar, payment, accounting-provider, and other integrations must be disabled or route to explicit safe test adapters for demo tenants; UI must visibly identify simulated/test delivery.
- Link a meeting session to an approved demo scenario and present the salesperson with start/reset/step/status controls in the private side panel. Customer-visible output should show the real application state produced by typed commands, not fabricated screenshots.
- Add APIs, typed Web client methods, authorization policy, persistence/migration if required, audit/telemetry, seed/version documentation, cleanup guidance, and operational safeguards.

### 5. Constraints and preservation rules

- Follow all architecture, tenant, authorization, command, audit, and external-side-effect rules in `/docs/architecture-rules.md`.
- Reset is destructive: resolve and verify the exact demo-company target before mutation. Never accept a broad path/database/company wildcard or use a production company as a reset target.
- Demo behavior may be deterministic, but it must exercise real application commands and persistence rather than mock UI data.
- Do not weaken normal authorization/policy because the company is a demo tenant.

### 6. Acceptance criteria

- Given a scenario reset preview, then it identifies one verified demo company, scenario version, affected scope, disabled integrations, and expected invariants without changing data.
- Given an authorized reset of a verified demo company, when executed repeatedly, then it produces the same deterministic starting state without affecting any other company.
- Given a reset request for a non-demo or wrong-company target, then it is rejected before destructive mutation.
- Given Alex performs a demo action, then an allowlisted typed Application command changes real demo state, produces the expected UI outcome, and records audit evidence.
- Given any demo flow attempts an external side effect, then production delivery/provider access is blocked and only an explicitly configured safe test path can run.
- Given two demo meetings run concurrently, then their session/scenario state and commands do not leak or interfere.

### 7. Verification

Add scenario parser/version tests, reset preview/target-guard tests, repeated-reset determinism tests, cross-company and concurrency tests, typed-tool allowlist/authorization tests, external-side-effect suppression tests, audit tests, UI component/browser UAT, and migration checks if applicable. Run the demo from reset through its expected actions twice, then run affected builds and tenant-isolation regression tests.

### 8. Definition of done

The controlled demo can be provisioned, previewed, reset, executed through real typed commands, observed in the actual UI, and repeated safely. It contains only synthetic data, cannot target non-demo tenants, cannot produce unintended external effects, and uses no simulated mouse movement or mock production UI state.
