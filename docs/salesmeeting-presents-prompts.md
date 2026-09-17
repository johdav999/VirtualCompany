# Sales Meeting Presentation Presets — Implementation Prompt Pack

## How to use this prompt pack

Execute these prompts in order. Each prompt delivers a bounded production outcome and identifies the earlier prompts it depends on. When the complete pack is assigned as one implementation sequence, continue through every prompt without stopping at intermediate build, test, analysis, or reporting checkpoints unless the work is genuinely blocked under the repository rules.

Every prompt must be implemented against the current repository state. Before changing files, inspect the relevant implementation and read all applicable `AGENTS.md` files. Follow:

- `/production-implementation.md`
- `/docs/architecture-rules.md`
- `/docs/design.md` for every UI change
- `/src/AGENTS.md` and any nearer source instructions
- `/tests/AGENTS.md` for test changes

Preserve unrelated working-tree changes. Existing production behavior wins when older planning material conflicts with the implementation. Do not deliver scaffolding, mock production data, silent failures, unhandled intermediate states, or deferred in-scope TODOs.

The target architecture and terminology are described in `/docs/salesmeeting-presets.md`.

---

## Prompt 1 — Deliver the versioned presentation preset library backend

### 1. Title and outcome

Implement a production-ready, company-scoped library of reusable sales presentation presets with immutable published versions and reusable presentation assets.

The outcome is that authorized users and API consumers can create a preset, edit its draft, upload and process a PowerPoint source, evaluate readiness, publish an immutable version, list versions, and archive the preset without creating a sales meeting. Existing meeting-owned presentations must continue working unchanged.

### 2. Current context

The current presentation implementation is meeting-owned:

- `SalesPresentationDeck` contains `SessionId`, `AgentId`, processing state, activation state, source-file storage, and brief version.
- `SalesPresentationSlide` contains rendered slide data, objectives, timing, and transitions.
- `SalesMeetingArtifact` requires a meeting session and deck.
- `SalesPresentationDeckService` imports, lists, activates, retries, and retrieves decks by meeting session.
- `SalesPresentationDeckProcessor` scans, extracts, renders, creates slide plans, and generates meeting-specific briefs in one processing flow.
- `SalesPresentationDecksController` exposes session-scoped routes.
- SQL Server migrations and the model snapshot are owned by `VirtualCompany.Persistence.Migrations`.

Relevant files include:

- `/src/VirtualCompany.Domain/Entities/SalesPresentationDeck.cs`
- `/src/VirtualCompany.Domain/Entities/SalesPresentationSlide.cs`
- `/src/VirtualCompany.Domain/Entities/SalesMeetingArtifact.cs`
- `/src/VirtualCompany.Application/Sales/SalesPresentationDeckContracts.cs`
- `/src/VirtualCompany.Infrastructure.Sales/Sales/SalesPresentationDeckService.cs`
- `/src/VirtualCompany.Infrastructure.Sales/Sales/SalesPresentationDeckProcessor.cs`
- `/src/VirtualCompany.Persistence/Persistence/Configurations/SalesPresentationDeckConfiguration.cs`
- `/src/VirtualCompany.Persistence/Persistence/Configurations/SalesPresentationSlideConfiguration.cs`
- `/src/VirtualCompany.Api/Controllers/SalesPresentationDecksController.cs`
- `/tests/VirtualCompany.Api.Tests/SalesPresentationDeckDomainTests.cs`
- `/tests/VirtualCompany.Api.Tests/SalesPresentationDeckServiceTests.cs`
- `/tests/VirtualCompany.Api.Tests/SalesPresentationDeckProcessorTests.cs`
- `/tests/VirtualCompany.Api.Tests/SalesPresentationDeckMigrationTests.cs`

The shared workflow subsystem already models immutable definition versions. Follow its versioning principles where useful, but keep presentation presets owned by the Sales capability.

### 3. Dependencies

None.

### 4. Implementation requirements

#### Domain and persistence

Add narrowly scoped Sales domain entities and configurations for:

- `SalesPresentationPreset`: stable identity, `CompanyId`, name, description, owner, lifecycle status, current published version reference, timestamps, and concurrency version.
- `SalesPresentationPresetVersion`: preset reference, monotonically increasing version number, draft/published/archived availability state, default presenter configuration, presentation behavior defaults, goal/audience/duration/demo defaults, language, allowed context types, publication metadata, and concurrency/version safeguards.
- `SalesPresentationPresetAsset`: company and preset-version ownership, source file metadata, content hash, object-storage reference, processing status, renderer evidence, bounded failure information, retry state, attempt state, and timestamps.
- `SalesPresentationPresetSlide`: reusable processed slide output including extracted text, speaker notes, rendered image storage reference, dimensions, content hash, generic objective, expected duration, transition, ordering, and processing version.

Use relational columns for core queryable state. Use JSON only for genuinely flexible bounded metadata such as optional presentation behavior settings. Do not store all preset configuration in one JSON document.

Define typed enums or stable storage-value mappings for preset lifecycle, version lifecycle, asset processing state, and allowed context types. Do not add new raw status strings throughout services and UI contracts.

Enforce these invariants in domain/application code:

- A preset is company-owned.
- A published version is immutable.
- Editing a published version creates the next draft version.
- Only one current published version exists per preset.
- Version numbers are unique within a preset.
- Only a successfully processed, readiness-approved version can be published.
- Archiving prevents new applications but does not invalidate historical references.
- A referenced published version cannot be destructively deleted.
- Default presenter IDs must identify active, company-owned Sales agents when supplied.
- Higher agent autonomy is never granted by preset configuration.

Add DbSets, global-query-filter participation, indexes, relationships, delete restrictions, object-storage lifecycle safeguards, an EF Core migration, and an updated SQL Server model snapshot.

#### Application contracts and services

Add Sales-owned commands, queries, DTOs, stable problem codes, validation exceptions, conflict exceptions, and services for:

- list and search presets
- retrieve one preset with current draft, current published version, historical versions, asset state, and where-used count placeholders backed by real queries
- create a preset and its first draft
- update a draft with expected concurrency version
- import or replace the draft asset
- retry retryable asset processing
- evaluate publication readiness
- publish a draft version
- create the next draft from a published version
- archive a preset
- retrieve reusable slide assets for authorized internal/runtime use

The readiness response must contain an authoritative state, stable blocker reason codes, explanations, allowed corrective actions, and review requirements. At minimum, block publication for missing or failed assets, zero processed slides, invalid presenter, invalid duration, unsupported context configuration, or unavailable required knowledge scope.

#### Reusable processing

Create a preset-asset processor that reuses the established validation, extraction, rendering, safe-failure, retry, storage, and observability behavior without requiring a `SalesMeetingSession`.

Generate only reusable content in this processor:

- rendered slides
- extracted text and notes
- generic objectives
- baseline talking points
- generic timing and transitions
- source and processing evidence

Do not generate lead-, deal-, contact-, campaign-, or customer-specific briefing content during preset processing.

Use the existing background-worker pattern for durable processing. Claims, retries, duplicate delivery, stale processing recovery, and object-storage writes must be idempotent and safe under concurrency. Derive stable storage paths and idempotency keys from company, preset version, asset, and processing version.

#### API, authorization, and audit

Add company-scoped REST endpoints under a coherent Sales presentation-preset route. Controllers must resolve authorized company/user context, validate transport input, call application services, and map typed failures to consistent problem responses.

Add authorization checks for list, read, create, edit, upload, retry, publish, and archive operations. Do not rely on route company IDs or UI visibility as proof of access.

Record audit evidence for preset creation, draft changes, asset import, processing success/failure, publication, new-version creation, and archive. Include preset/version/asset identifiers, actor, outcome, rationale, and correlation reference without logging sensitive payloads.

#### Compatibility

Do not change the existing session-scoped presentation API or runtime behavior in this prompt. Do not move or delete existing `SalesPresentationDeck`, `SalesPresentationSlide`, or `SalesMeetingArtifact` data. The new preset processor may extract shared internal helpers where doing so preserves existing behavior and tests.

### 5. Constraints and preservation rules

- Follow `/production-implementation.md` and `/docs/architecture-rules.md`.
- Keep the capability in `VirtualCompany.Infrastructure.Sales`; do not add Sales implementations to the root Infrastructure facade.
- Domain and Application projects must retain inward dependency direction.
- Use EF Core migrations as the only schema authority.
- Preserve existing migration IDs, storage references, meeting deck routes, runtime contracts, and session presentation behavior.
- All entities, reads, writes, background scans, object-storage paths, and audits must be company-scoped.
- `IgnoreQueryFilters` is permitted only for bounded system/background operations that explicitly reapply company scope.
- Do not call an LLM provider directly from Sales. Use the shared AI orchestration interfaces and current grounding rules.
- Default to conservative agent behavior and recheck permissions outside the preset.
- Do not delete or overwrite user-owned files or unrelated worktree changes.

### 6. Acceptance criteria

- Given an authorized company member, when they create a preset, then a company-owned preset and editable version 1 draft are persisted and audited.
- Given a draft preset, when a valid `.pptx` is uploaded, then the upload is durably accepted, processed in the background, and exposes safe processing progress and terminal state.
- Given successful asset processing, when reusable slides are retrieved, then they contain rendered/extracted presentation content but no lead, deal, contact, meeting, or customer briefing data.
- Given a draft with unresolved blockers, when publication is requested, then publication is rejected with stable blocker codes, explanations, and allowed corrective actions.
- Given a ready draft, when publication succeeds, then the version becomes immutable and is the preset's current published version.
- Given a published preset is edited, when editing begins, then a new draft version is created and the published version remains unchanged.
- Given an archived preset, when a new application is attempted through the preset service, then it is rejected while historical reads remain available.
- Given concurrent updates with a stale expected version, when a mutation is attempted, then a typed conflict is returned without losing data.
- Given a user from another company, when any preset/version/asset operation is attempted, then it is rejected without revealing the target.
- Given an existing meeting presentation, when the migration and new services are deployed, then its import, processing, activation, brief, and runtime behavior remain unchanged.

### 7. Verification

- Add domain tests for lifecycle transitions, immutable publication, version numbering, archive behavior, and invalid presenter/default configuration.
- Add service and API tests for commands, queries, validation, conflict mapping, authorization, and audit events.
- Add cross-company read and write tests for every new aggregate and endpoint family.
- Add processor tests for valid files, invalid files, slide limits, safe failures, retries, stale claims, duplicate attempts, and storage paths.
- Add SQL Server migration tests covering tables, columns, indexes, foreign keys, conversions, query filters, and snapshot consistency.
- Run the existing focused presentation deck tests to prove compatibility.
- Run a focused affected-area test pass, then one full solution build or broader repository validation as appropriate.

### 8. Definition of done

The preset library backend, reusable processing, persistence, migration, APIs, authorization, audit evidence, and tests are production-ready. Published versions are immutable, reusable content contains no customer-specific data, current meeting-owned presentations are preserved, and there are no in-scope TODOs or placeholder implementations.

---

## Prompt 2 — Deliver the Presentation Presets library user experience

### 1. Title and outcome

Implement the production user experience for creating, processing, previewing, publishing, versioning, searching, duplicating, and archiving sales presentation presets.

The outcome is a usable Sales presentation library backed entirely by the production APIs from Prompt 1. Users can manage reusable presentations before they have a specific meeting or opportunity.

### 2. Current context

Prompt 1 provides the preset, version, reusable asset, processing, readiness, API, authorization, and audit backend.

The current meeting presentation UI is embedded in:

- `/src/VirtualCompany.Web/Pages/Sales/SalesMeetingPreparation.razor`
- `/src/VirtualCompany.Web/Components/Sales/SalesPresentationDeckWorkflow.razor`
- `/src/VirtualCompany.Web/Services/SalesPresentationDeckApiClient.cs`
- `/src/VirtualCompany.Web/Services/SalesPresentationPreparationApiClient.cs`

The canonical Sales information architecture is defined in `/docs/design.md`. Presentation presets are operational Sales content, not general system administration. They should be reachable from relevant Sales surfaces without turning Settings into the daily workflow.

### 3. Dependencies

- Prompt 1 completed, including stable preset/version/asset contracts and endpoints.

### 4. Implementation requirements

#### Mandatory design workflow

This is significant new UI work. Complete the mandatory reference-screenshot workflow in `/docs/design.md` before implementing the pages or major components. Create the required explicit image-generation prompt, generate and store the reference under `/docs/design/references/`, implement against it, and compare/refine the running UI against the reference.

#### Navigation and page structure

Add a Presentation presets entry point in the Sales area using the established app shell and route conventions. Do not introduce a competing shell or restore retired navigation patterns.

Create a library page that supports:

- search by name, description, owner, and tags when supported by the backend
- filters for draft, processing, needs review, published, failed, and archived states
- concise preset cards or table rows with owner, current version, processing/readiness state, slide count, updated time, and where-used count
- clear primary actions based on authoritative allowed-action values
- loading, empty, error, authorization, processing, and retry states
- responsive behavior and accessible keyboard/focus operation

Create a preset editor/detail experience that supports:

- name, description, owner, supported situations, default presenter or presenter capability, language, duration, audience, goal, demo scenario, control mode, and presentation-behavior settings
- PowerPoint selection, upload validation, progress, cancellation, safe processing failure, retry, and replacement
- slide preview using processed assets
- readiness blocker display with direct corrective actions
- publish confirmation
- immutable published-version display
- creation of a new draft version from the current published version
- historical version list and preview
- duplication into a new preset draft
- archive confirmation and where-used warning

Make preset defaults visually distinct from status and operational history. Do not expose raw JSON or internal storage identifiers.

#### Web clients and presenters

Add typed Web API clients, exceptions, view models/presenters, localized user-facing strings, and telemetry following existing Sales Web patterns. Preserve centralized HTTP behavior in the established transport abstractions.

The UI must consume backend readiness and allowed-action decisions. Do not duplicate publication, presenter-eligibility, archive, or authorization policies in Razor components.

Use shared components for preset identity, status, preview, and picker-ready summaries where that will support later meeting and campaign prompts without creating a catch-all component.

#### Accessibility and resilience

- Associate every form control with a label and validation message.
- Announce asynchronous processing and terminal results appropriately.
- Preserve user-entered draft state across recoverable API failures.
- Prevent duplicate submissions while requests are in flight.
- Display concurrency conflicts with a safe refresh/reload path.
- Keep actions usable at supported mobile widths.
- Use localized plain-language messages for blocker codes and failures.

### 5. Constraints and preservation rules

- Follow `/production-implementation.md`, `/docs/architecture-rules.md`, and `/docs/design.md`.
- The mandatory design workflow applies in full.
- Use the existing app shell, Sales visual language, localization patterns, typed clients, and reusable form/action components.
- Administrative settings must not be mixed into daily preset operations.
- Do not expose an action the backend did not authorize.
- Do not add mock presets or client-only persistence.
- Preserve the existing meeting preparation experience in this prompt.
- Preserve unrelated working-tree changes.

### 6. Acceptance criteria

- Given an authorized user opens the Presentation presets entry point, when data loads, then the user can search, filter, and identify preset lifecycle/readiness state without entering a meeting.
- Given no presets exist, when the library loads, then a clear empty state explains the value and offers the authorized create action.
- Given a user creates a preset and uploads a PowerPoint, when processing runs, then progress, cancellation, retry, success, and safe failure states are visible and backed by real APIs.
- Given asset processing succeeds, when the user previews the preset, then the processed slides and reusable defaults are displayed without customer-specific data.
- Given readiness blockers exist, when the editor is viewed, then each blocker has a stable localized explanation and an applicable corrective action.
- Given a ready draft, when publication is confirmed, then the version becomes visibly immutable and the library reflects the new published version.
- Given a published preset, when the user chooses to edit it, then a new draft version is created rather than mutating the published version.
- Given a concurrent update conflict, when the user saves, then the UI explains that the preset changed and offers a safe reload without silently overwriting data.
- Given a narrow viewport or keyboard-only navigation, when the core flows are exercised, then all controls remain usable, visible, and correctly focused.

### 7. Verification

- Add Web client tests for routes, payloads, problem mapping, cancellation, and conflicts.
- Add component/page tests for library states, filtering, editor validation, processing, readiness, publication, new-version creation, duplication, archive confirmation, and authorization-dependent actions.
- Run the real UI in a browser and verify create, upload, process, preview, publish, version, duplicate, and archive flows.
- Compare the implemented UI with the generated reference image and refine layout, hierarchy, spacing, typography, colors, empty states, and responsive behavior.
- Verify keyboard navigation, focus behavior, labels, live-region announcements, and mobile-width layouts.
- Run focused Web tests and the appropriate broader build/validation once.

### 8. Definition of done

Authorized users can manage the complete preset lifecycle through a polished, accessible, responsive, production-backed Sales UI. The reference-screenshot workflow is complete, backend decisions remain authoritative, and there are no mock flows or deferred in-scope UI states.

---

## Prompt 3 — Apply immutable presets to individual sales meetings

### 1. Title and outcome

Implement production-ready presentation runs and allow an immutable published preset version to be applied to an individual sales meeting while generating independent customer-specific preparation.

The outcome is that two meetings can reuse the same published presentation definition and rendered content while retaining separate presenter resolution, customer briefing, overrides, readiness, runtime state, and audit evidence.

### 2. Current context

Prompts 1 and 2 provide a versioned preset library and reusable source processing.

Current meeting preparation and runtime behavior includes:

- `SalesMeetingSession` binds an invitation, lead, optional deal/contact, customer company, presenter, meeting goal, audience, duration, demo scenario, consent, retention, and presentation control state.
- `SalesPresentationDeck` is session-owned and has one active deck per session.
- `SalesMeetingArtifact` is session- and deck-owned.
- `SalesPresentationDeckProcessor` currently combines reusable slide work and contextual brief generation.
- Runtime, stage, narration, browser-room, Teams, question answering, and conductor services expect exact session, deck/version, slide, presenter, sequence, and presentation-version bindings.
- `SalesMeetingPreparation.razor` selects an agent and directly uploads a meeting deck.

Relevant areas include:

- `/src/VirtualCompany.Domain/Entities/SalesMeetingSession.cs`
- `/src/VirtualCompany.Domain/Entities/SalesPresentationDeck.cs`
- `/src/VirtualCompany.Domain/Entities/SalesMeetingArtifact.cs`
- `/src/VirtualCompany.Application/Sales/SalesMeetingPresentationPreparationContracts.cs`
- `/src/VirtualCompany.Application/Sales/SalesPresentationRuntimeContracts.cs`
- `/src/VirtualCompany.Infrastructure.Sales/Sales/SalesMeetingPresentationPreparationQuery.cs`
- `/src/VirtualCompany.Infrastructure.Sales/Sales/SalesPresentationRuntimeService.cs`
- `/src/VirtualCompany.Infrastructure.Sales/Sales/SalesPresentationDeckProcessor.cs`
- `/src/VirtualCompany.Infrastructure.Sales/Sales/SalesPresentationConductor.cs`
- `/src/VirtualCompany.Web/Pages/Sales/SalesMeetingPreparation.razor`

### 3. Dependencies

- Prompt 1 completed.
- Prompt 2 completed so the shared preset summary/preview components and Web client are available.

### 4. Implementation requirements

#### Presentation run aggregate

Add a company-owned `SalesPresentationRun` aggregate and persistence for one contextual use of a preset version. Include:

- immutable preset-version reference
- context type and context reference
- optional typed meeting-session binding where supported by relational integrity
- resolved presenter agent
- resolved goal, audience, duration, demo scenario, language, and control mode
- explicit override flags or provenance sufficient to distinguish defaults from overrides
- preparation/readiness state and stable blocker details
- preparation version, attempts, retry/failure state, timestamps, actor references, and concurrency version
- compatibility/runtime references needed to preserve the current exact session/deck/version contract

Add run-specific artifacts for confirmed facts, needs, risks, questions, recommendations, positioning, desired next steps, missing evidence, and run-specific slide guidance. Reuse or migrate `SalesMeetingArtifact` carefully where appropriate; do not place contextual artifacts back into a preset version.

Enforce one active meeting presentation run per session unless the existing replacement behavior explicitly requires version history. Preserve historical runs and exact preset-version provenance.

Add EF configurations, indexes, query filters, delete restrictions, migration, and model snapshot updates.

#### Apply and prepare services

Add application commands and services to:

- apply an exact published preset version to a meeting session
- apply the current published version when the user did not explicitly pin another version
- resolve preset defaults and permitted meeting overrides
- resolve and validate the presenter
- prepare or regenerate run-specific briefing artifacts
- calculate meeting presentation readiness
- replace the active run with an explicit newer-version application
- update an existing run to a newer published preset version with expected concurrency versions
- retrieve a comparison of changed defaults and incompatible overrides before updating
- retry retryable preparation failures
- expose the exact runtime binding

Use context-specific preparation to load the current meeting, lead, optional deal/contact, customer company, accessible knowledge, and approved Sales intelligence. Preserve source references, classifications, AI run references, safe degradation, and needs-review behavior.

An existing run remains pinned when a new preset version is published. Updating is always explicit and audited.

#### Runtime compatibility

Preserve the established live presentation semantics for:

- active deck/version selection
- stage rendering and acknowledgements
- presentation sequence and concurrency
- manual, assisted, and autonomous control modes
- narrator preemption and exact resume markers
- presenter authorization
- browser-room and Teams experiences
- slide search, question answering, capture, and audit evidence

Choose and implement one explicit compatibility strategy:

1. Materialize a session-owned compatibility deck from the reusable preset asset while referencing the run and avoiding duplicated binary storage; or
2. Refactor runtime reads to resolve preset assets through the run while preserving the existing external contracts during migration.

Document the chosen strategy in code and tests. Do not leave both strategies partially active or introduce ambiguous active-deck resolution.

#### Existing setup to preset

Add a **Save meeting setup as preset** application command that copies only reusable configuration and deck content into a new preset draft. Explicitly exclude:

- customer, lead, deal, or contact facts
- meeting brief artifacts
- transcripts, observations, questions, action items, or minutes
- invitation/provider identifiers
- participant data
- meeting runtime state

#### Meeting preparation UI

This is a significant change to an existing UI workflow. Follow the mandatory reference-screenshot workflow in `/docs/design.md` and update or create the appropriate reference before implementing.

Update meeting preparation to:

- choose and preview a published preset
- default to the current published version while permitting an authorized historical version when allowed
- show preset defaults separately from meeting overrides
- resolve or override the presenter within backend policy
- display customer-specific preparation progress and readiness blockers
- show when a newer preset version is available
- preview differences before an explicit update
- save an eligible current setup as a new preset draft
- retain the direct-upload workflow during compatibility migration
- open the presenter only from an authoritative ready run/runtime binding

Do not silently replace a direct-upload deck or an existing preset-backed run.

### 5. Constraints and preservation rules

- Follow `/production-implementation.md`, `/docs/architecture-rules.md`, and `/docs/design.md`.
- The mandatory design workflow applies to the meeting preparation redesign.
- Preset versions are immutable and contain no customer-specific content.
- Every run and context lookup is company-scoped and authorized.
- Agent permissions, tool access, autonomy, and approval requirements are rechecked during preparation and execution.
- Existing invitation, session, consent, retention, runtime, browser-room, Teams, narration, and capture behaviors must remain intact.
- Reliable background preparation uses bounded retries, stable idempotency keys, safe failures, and durable state.
- Do not delete legacy meeting decks or storage objects during this prompt.
- Preserve unrelated worktree changes, especially active Sales room and narration work.

### 6. Acceptance criteria

- Given one published preset version, when it is applied to two unrelated meetings, then both runs reference the same immutable preset version and reusable asset while keeping independent customer artifacts, presenter resolution, runtime state, and audits.
- Given two runs use different leads or deals, when preparation completes, then each brief is grounded only in its own authorized context and no contextual artifact is stored in the preset version.
- Given a new preset version is published, when an existing meeting is opened, then the run remains pinned and the UI only offers an explicit reviewed update.
- Given an update preview shows changed defaults, when the user confirms the update, then the new run/version binding is persisted with compatible overrides preserved and conflicts identified.
- Given a selected presenter is inactive or lacks required capability, when preparation or execution is attempted, then readiness is blocked with an actionable stable reason.
- Given the run is ready, when the presenter opens, then all existing stage, narration, control, interruption, question, and slide behaviors operate using an exact authoritative binding.
- Given a user saves an existing meeting setup as a preset, when the new draft is inspected, then reusable deck/configuration is present and no customer-specific or meeting-runtime data was copied.
- Given a legacy direct-upload meeting, when it is opened and presented, then its existing behavior remains supported.
- Given a cross-company preset, run, session, or agent identifier, when application is attempted, then the request is rejected without information disclosure.

### 7. Verification

- Add domain and persistence tests for run lifecycle, context binding, version pinning, active-run rules, override provenance, and delete restrictions.
- Add API/service tests for apply, prepare, retry, readiness, compare/update version, save-as-preset, conflicts, authorization, and audit.
- Add cross-company permutations involving presets, versions, sessions, leads/deals, agents, runs, and artifacts.
- Add grounding tests proving that customer-specific artifacts are isolated per run and excluded from preset data.
- Run and extend presentation deck, preparation, runtime, stage, narration, browser-room, Teams, and conductor tests.
- Add Web client/component/page tests for preset selection, preview, overrides, progress, blockers, version availability, update comparison, save-as-preset, and legacy flow.
- Exercise the complete meeting flow in the real browser and compare it with the design reference at desktop and mobile widths.
- Run focused affected tests followed by one full build or broader validation.

### 8. Definition of done

Published presets can be applied safely to individual meetings, each use creates an isolated and fully prepared presentation run, existing runtime behavior is preserved, users can explicitly manage versions and overrides, legacy meetings continue to work, and the implementation is production-ready with migrations, authorization, audit evidence, UI verification, and no in-scope TODOs.

---

## Prompt 4 — Include presentation presets as campaign activities

### 1. Title and outcome

Implement reusable presentation presets as first-class campaign activities with per-contact, per-account, and campaign-event execution scopes.

The outcome is that campaign planners can include a published presentation preset in a campaign plan, validate readiness before launch, and have due campaign activities create the correct presentation runs and human preparation tasks or meeting handoffs without misusing the email sequence executor or automatically presenting to customers.

### 2. Current context

Prompts 1–3 provide published preset versions, reusable assets, contextual presentation runs, meeting application, readiness, and runtime compatibility.

The current campaign implementation has two related but distinct models:

- `SalesSequence` and `SalesSequenceStep` drive outbound email sequences. Their contracts and execution service are shaped around subjects, bodies, email personalization, delivery, reply, bounce, and stop conditions.
- `SalesCampaignActivity` models broader campaign work with `ActivityType`, `Channel`, `ExecutionMode`, dependencies, owner user/agent, optional sequence-step linkage, required tool capability, lifecycle state, claims, retry, approval, and results.

Relevant files include:

- `/src/VirtualCompany.Domain/Entities/SalesCampaign.cs`
- `/src/VirtualCompany.Domain/Entities/SalesSequence.cs`
- `/src/VirtualCompany.Domain/Entities/SalesSequenceStep.cs`
- `/src/VirtualCompany.Domain/Entities/SalesCampaignInitiativeEntities.cs`
- `/src/VirtualCompany.Application/Sales/OutboundCampaignContracts.cs`
- `/src/VirtualCompany.Application/Sales/CampaignPlanningContracts.cs`
- `/src/VirtualCompany.Infrastructure.Sales/Sales/OutboundCampaignService.cs`
- `/src/VirtualCompany.Infrastructure.Sales/Sales/CampaignPlanningService.cs`
- `/src/VirtualCompany.Infrastructure.Sales/Sales/CampaignSchedulingCoordinator.cs`
- `/src/VirtualCompany.Api/Controllers/SalesCampaignsController.cs`
- `/src/VirtualCompany.Web/Pages/Sales/SalesCampaigns.razor`

### 3. Dependencies

- Prompt 1 completed.
- Prompt 2 completed for reusable preset picker/preview UI.
- Prompt 3 completed for presentation-run creation, preparation, readiness, and runtime binding.

### 4. Implementation requirements

#### Campaign presentation configuration

Add a typed company-owned campaign presentation activity configuration linked one-to-one with a `SalesCampaignActivity`. Persist:

- exact published preset-version ID
- execution scope: `per_contact`, `per_account`, or `campaign_event`
- presenter resolution strategy and optional explicit presenter
- meeting/task/handoff strategy
- allowed override policy
- any required event/session reference
- preparation lead time
- version/concurrency data and timestamps

Use a relational foreign key to the preset version. Do not encode the whole configuration into `RequiredToolCapability`, `ActivityType`, or an unstructured payload.

Add stable activity type/channel values for presentation work without changing existing stored values. Retain `SalesCampaignActivity` as the campaign lifecycle owner.

#### Planning and readiness

Extend campaign planning contracts and services to create, update, retrieve, and remove presentation activity configuration with expected-version conflict handling.

Campaign readiness must authoritatively validate:

- the preset and exact version belong to the campaign company
- the version is published and available for new use
- reusable asset processing succeeded and slides exist
- the selected execution scope is compatible with the campaign audience
- a valid presenter can be resolved for every required execution or a safe handoff exists
- required knowledge and tool capabilities are available
- dependencies, timing, owner, meeting/task strategy, and event information are complete
- projected run cardinality is bounded and visible to the user

Return stable blocker codes, explanations, evidence, corrective actions, and review/approval requirements. Include these blockers in existing campaign readiness and launch rules.

#### Scheduling and execution

Extend the campaign scheduling coordinator or add a narrowly owned Sales handler that processes due presentation activities through durable workflow/background execution.

For each scope:

- `per_contact`: create one idempotent presentation run per eligible campaign contact.
- `per_account`: group eligible contacts by company/account and create one idempotent run per distinct authorized account.
- `campaign_event`: create one idempotent run bound to the configured event/session context.

Derive stable idempotency keys from company, campaign, campaign activity, preset version, execution scope, and resolved subject. Duplicate worker delivery or concurrent claims must not create duplicate runs or tasks.

A due presentation activity must create the configured preparation task, handoff, or meeting binding. It must not automatically start a presentation, admit participants, send an invitation, or perform another external side effect unless that separate action is explicitly configured, authorized, approved, and routed through its established durable boundary.

Record progress, aggregate counts, safe failures, retry state, created run references, and result summary back through the campaign activity lifecycle. Define partial-success behavior explicitly and make failed subjects actionable without duplicating successful runs on retry.

Do not route presentation activity execution through `IOutboundEmailSender` or make `SalesSequenceStep` a generic union of unrelated channel payloads.

#### API and UI

Add transport-only endpoints for campaign presentation activity configuration, readiness evidence, run projections, and retry/remediation actions. Preserve current campaign routes and outbound email behavior.

This is significant campaign UI work. Follow the mandatory reference-screenshot workflow in `/docs/design.md` before implementing.

Extend campaign planning to:

- add a Presentation activity type
- select and preview a published preset/version
- choose execution scope
- configure presenter resolution, timing, dependencies, and task/meeting/handoff behavior
- show projected run count and affected audience/account grouping
- display authoritative readiness blockers
- show created run progress and failures after scheduling
- link to individual prepared runs where authorized

Reuse the preset picker and preview components from Prompt 2. Clearly distinguish the campaign activity lifecycle from individual presentation-run state.

#### Audit and observability

Audit configuration changes, readiness decisions, run creation, partial failures, retries, cancellation, and completion. Add structured logs and metrics for activities scanned, subjects resolved, runs created, duplicate creations prevented, tasks/handoffs created, retries, failures, and latency.

### 5. Constraints and preservation rules

- Follow `/production-implementation.md`, `/docs/architecture-rules.md`, and `/docs/design.md`.
- The mandatory design workflow applies to campaign UI changes.
- Keep outbound email sequences and provider delivery behavior unchanged.
- Use `SalesCampaignActivity` for broad campaign work and a typed configuration entity for presentation-specific data.
- All campaign, audience, account, preset, agent, run, task, and meeting operations must be explicitly company-scoped.
- Enforce authorization, approval, and tool capability in backend policy/application code.
- Reliable work must be durable, idempotent, concurrency-safe, retry-bounded, and operator-visible.
- Do not automatically execute a live customer presentation.
- Preserve campaign lifecycle, stop conditions, email delivery, reply, bounce, and deal-created behavior.
- Preserve unrelated working-tree changes.

### 6. Acceptance criteria

- Given a campaign planner adds a presentation activity, when configuration is saved, then an exact published preset version, scope, presenter strategy, timing, and execution behavior are persisted and audited.
- Given an unpublished, archived, failed, cross-company, or otherwise unavailable preset version, when campaign readiness is evaluated, then readiness is blocked with a stable reason and corrective action.
- Given `per_contact` scope and five eligible contacts, when the activity becomes due, then five idempotent presentation runs and the configured preparation work are created.
- Given `per_account` scope and five contacts across two customer accounts, when the activity becomes due, then two idempotent account-context runs are created with authorized account membership represented correctly.
- Given `campaign_event` scope, when a valid event/session is configured and the activity becomes due, then one event-context presentation run is created.
- Given duplicate worker delivery or concurrent claims, when execution repeats, then no duplicate runs or tasks are created.
- Given one subject fails after others succeed, when the activity is retried, then successful subjects are not duplicated and the failed subject remains actionable.
- Given a due activity, when it executes, then no live presentation, participant admission, invitation, or outbound email occurs unless separately configured through an authorized durable workflow.
- Given created runs progress or fail, when the campaign is viewed, then aggregate activity state and per-run details are visible and consistent.
- Given existing outbound email campaigns, when the feature is deployed, then scheduling, sending, stop conditions, delivery, replies, bounces, and reporting remain unchanged.

### 7. Verification

- Add domain/persistence tests for typed configuration, execution-scope invariants, version pinning, relationships, and delete restrictions.
- Add campaign planning/API tests for create/update/readiness/conflicts/authorization and problem mapping.
- Add cross-company tests covering campaigns, contacts/accounts, presets, agents, sessions, runs, and tasks.
- Add scheduler tests for all scopes, stable idempotency, duplicate delivery, concurrent claims, partial success, retry, cancellation, unavailable context, and approval/handoff behavior.
- Run the existing outbound campaign and campaign initiative test suites as regressions.
- Add Web tests for activity configuration, preset selection, projected cardinality, blockers, status, failures, and run links.
- Exercise the real campaign planning and due-activity flow in a browser and compare the UI against the design reference at desktop and mobile widths.
- Run focused affected tests followed by one full build or broader validation.

### 8. Definition of done

Campaigns can include fully configured and readiness-validated presentation activities that create the correct idempotent presentation runs and preparation work for all supported scopes. Existing outbound email behavior is preserved, external actions remain behind established approval/workflow boundaries, the UI is verified, and no in-scope TODOs remain.

---

## Prompt 5 — Deliver ad-hoc preset use and consolidate legacy presentation ownership

### 1. Title and outcome

Implement ad-hoc use of published presentation presets and complete the compatibility migration from opportunity/session-owned presentation setup to preset-backed presentation runs.

The outcome is that authorized Sales users can start a prepared presentation outside a campaign or existing opportunity, legacy presentations remain traceable and supported, redundant processing/storage ownership is removed safely, and the product has one coherent preset/run model for future sales contexts.

### 2. Current context

Prompts 1–4 provide:

- versioned preset library and reusable assets
- preset-management UI
- contextual presentation runs and individual meeting use
- campaign presentation activities
- compatibility with the existing live runtime

Legacy data and behavior still include session-owned `SalesPresentationDeck` rows, session-owned slide processing, direct uploads, and meeting artifact relationships. Existing browser-room, Teams, narration, stage, capture, question, and meeting history flows may still read these records directly.

The architecture in `/docs/salesmeeting-presets.md` requires reusable source processing to live at the preset-version boundary and situation-specific preparation to live at the presentation-run boundary.

### 3. Dependencies

- Prompt 1 completed.
- Prompt 2 completed.
- Prompt 3 completed and stable under existing runtime regression tests.
- Prompt 4 completed.

### 4. Implementation requirements

#### Ad-hoc presentation flow

Add a production Sales entry point and application flow that creates an `ad_hoc` presentation run from a published preset without requiring a lead, deal, campaign, or existing meeting invitation.

Support optional authorized context selection:

- customer company/account
- contact
- lead
- deal

Require or resolve:

- presenter
- goal
- intended audience
- duration
- language
- control mode
- meeting/runtime strategy

When optional business context is supplied, generate run-specific preparation from only that authorized context. When it is absent, clearly mark unavailable customer evidence and require review rather than inventing facts.

If starting the live presenter requires a meeting session, create a clearly identified ad-hoc session through an application/workflow boundary with appropriate organizer, consent, retention, authorization, and lifecycle semantics. Do not fabricate provider meeting identifiers or bypass meeting policies.

#### Ad-hoc UI

This is a new user-facing flow. Follow the mandatory reference-screenshot workflow in `/docs/design.md`.

Add a **Use preset** action from the preset library and appropriate Sales contexts. The flow should:

- select or confirm a published preset/version
- optionally select authorized customer/deal context
- resolve or select the presenter
- show defaults and overrides
- prepare the run
- expose blockers and review requirements
- open the presenter only when authoritative readiness allows it
- preserve the run for history and follow-up

Reuse the preset picker, preview, override, readiness, and preparation components established in earlier prompts.

#### Legacy inventory and migration

Perform a bounded repository and data-model inventory of all reads/writes involving:

- `SalesPresentationDeck`
- `SalesPresentationSlide`
- `SalesMeetingArtifact`
- active-deck resolution
- object-storage presentation keys
- meeting-session preparation
- runtime, stage, narration, browser-room, Teams, question, search, capture, and audit paths

Implement a production migration strategy that preserves every existing presentation and its provenance. Choose the correct treatment per legacy record:

- link to a generated legacy preset/version and run when the content is safely reusable
- create a legacy run/compatibility marker when contextual content cannot become a reusable preset
- leave an explicitly supported compatibility projection when migration would alter historical meaning

Do not infer that customer-specific artifacts are reusable. Do not merge decks solely because filenames or titles match. Content hashes can support deduplication only when company scope, processing version, provenance, and semantic ownership are compatible.

Backfill through an idempotent, resumable, observable process appropriate to production data volume. Persist progress and safe failure information. Do not use startup ad-hoc DDL or silently discard records.

#### Consolidate processing and storage

After compatibility is proven:

- make preset assets the single owner of reusable source files, slide extraction, and rendered slide content for preset-backed runs
- make presentation runs the single owner of situation-specific preparation, overrides, and runtime provenance
- eliminate duplicate processing paths for preset-backed content
- retain legacy read support only where required by unmigrated/historical records
- add reference-aware retention rules for source files, rendered assets, runs, meeting history, and archived presets
- prevent deletion while any published version, run, meeting, campaign activity, history, audit, or artifact reference remains

Do not destructively remove legacy columns, tables, routes, or storage until migration verification proves they are no longer required. Any eventual schema cleanup requires its own safe EF Core migration and upgrade-path validation.

#### Future context extension point

Formalize a bounded Sales application abstraction for creating and resolving presentation-run contexts. It must support the implemented meeting, campaign activity, and ad-hoc contexts without exposing provider schemas or moving business decisions into controllers.

Document how a future Sales context adds:

- a stable context type
- company-scoped resolution
- authorization
- supported defaults/overrides
- preparation evidence
- readiness rules
- runtime/task/handoff behavior

Do not build a generic policy engine or untyped catch-all manager.

#### Observability and audit

Add metrics, structured logging, and audit evidence for ad-hoc run creation, preparation, presenter resolution, migration scanning, backfill outcomes, skipped/ambiguous records, deduplication, compatibility reads, and storage retention decisions.

Provide operator-visible failure and retry paths for backfill or reconciliation. Ambiguous historical ownership must not be silently resolved.

### 5. Constraints and preservation rules

- Follow `/production-implementation.md`, `/docs/architecture-rules.md`, and `/docs/design.md`.
- The mandatory design workflow applies to the ad-hoc UI.
- Preserve all existing sales meeting history, customer evidence, audit provenance, storage references, and runtime behavior.
- Never move customer-specific data into a reusable preset version.
- Backfills must be idempotent, resumable, company-scoped, bounded, and observable.
- Use EF Core migrations as the only schema authority.
- Do not drop/recreate production data or use `EnsureCreated` as a migration substitute.
- Do not remove compatibility code until repository searches and tests prove all required consumers are migrated.
- Agent, context, consent, retention, authorization, approval, and tool policies remain authoritative at execution time.
- Preserve unrelated working-tree changes.

### 6. Acceptance criteria

- Given an authorized Sales user with no existing opportunity or campaign, when they choose a published preset and complete required context, then an `ad_hoc` presentation run is created, prepared, audited, and made available to the presenter when ready.
- Given no customer context, when an ad-hoc run is prepared, then unsupported customer facts are marked unavailable or needing review and no invented customer evidence is produced.
- Given optional lead, deal, contact, or account context, when preparation runs, then only authorized records from the same company are used.
- Given legacy session-owned presentations, when the backfill runs repeatedly, then it is idempotent, preserves historical meaning, and reports migrated, compatibility-only, skipped, ambiguous, and failed records.
- Given two legacy decks with matching names but different company, content, context, or provenance, when migration runs, then they are not incorrectly deduplicated.
- Given a preset-backed run, when slides and source content are used, then reusable processing comes from the preset asset and contextual artifacts come from the run.
- Given an existing historical presentation, when users open its meeting history or runtime-compatible view, then slides, artifacts, presenter, state, and audit provenance remain available.
- Given a referenced source or rendered asset, when retention or deletion is evaluated, then deletion is blocked while any active or historical reference remains.
- Given all supported consumers have migrated, when compatibility cleanup is applied, then no runtime, meeting, campaign, browser-room, Teams, narration, question, capture, or audit regression occurs.
- Given a future context integration, when it uses the documented context abstraction, then company scope, authorization, readiness, preparation, and runtime/task behavior remain explicit.

### 7. Verification

- Add application/API tests for ad-hoc creation, optional context resolution, overrides, preparation, readiness, authorization, and audit.
- Add cross-company tests for every optional ad-hoc context and presenter/preset/run combination.
- Add UI tests and real browser verification for **Use preset**, context selection, preparation, blockers, review, and presenter launch.
- Compare the ad-hoc UI to its reference image at desktop and mobile widths.
- Add migration/backfill tests covering idempotent reruns, partial failure, resume, ambiguous records, content-hash safeguards, compatibility-only records, and production-scale batching.
- Add storage retention and delete-restriction tests.
- Search for every legacy presentation read/write path and either migrate it or document/test the required compatibility path.
- Run all existing presentation, meeting preparation, runtime, browser-room, Teams, narration, stage, question, capture, campaign, authorization, audit, and migration tests.
- Run the full solution build and the repository's broader final validation once after the last implementation change.

### 8. Definition of done

Ad-hoc preset use is production-ready, every supported presentation context uses the same coherent preset/run architecture, legacy data is migrated or explicitly supported without loss, reusable and contextual processing ownership is unambiguous, storage retention is safe, all affected runtime and campaign flows pass regression validation, and no in-scope compatibility or cleanup TODOs remain.

---

## Final sequence-level completion check

After all five prompts are complete, verify the full outcome rather than treating individual prompt completion as sufficient:

- Presets can be created, processed, previewed, published, versioned, duplicated, and archived.
- One immutable preset version can be reused across unrelated meetings, campaign activities, and ad-hoc runs.
- Reusable assets contain no customer-specific content.
- Every run has isolated context, briefing, overrides, readiness, runtime state, and audit provenance.
- Campaign execution scopes are correct and idempotent.
- Current agent permissions and policies are enforced at preparation and execution time.
- Cross-company access is rejected across every new route and background path.
- Existing meeting presentations and live presenter behavior remain functional throughout and after migration.
- The required design-reference and browser-verification workflow is complete for each affected UI.
- EF Core migrations, SQL Server model validation, focused tests, full build, and broader final verification pass.
- Documentation reflects the implemented architecture and operational behavior.
