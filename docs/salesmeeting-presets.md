# Reusable Sales Meeting Presentation Presets

## Purpose

Sales presentation preparation is currently created inside an individual sales meeting. The uploaded deck, selected presenter, meeting goal, audience, timing, generated slide plan, and customer-specific briefing are therefore coupled to one meeting session.

The target design is a reusable presentation preset that can be selected in multiple sales situations, including:

- an individual sales meeting
- a campaign activity
- an ad-hoc presentation
- future sales workflows that require a prepared presentation

The preset must define the reusable presentation approach without carrying customer, lead, deal, or meeting data from one use into another.

## Current implementation

The current implementation combines reusable content and meeting-specific state:

- `SalesPresentationDeck` belongs directly to a `SalesMeetingSession` and an `Agent`.
- Deck import, processing, listing, activation, and briefing are scoped by meeting session.
- The meeting preparation page starts from a meeting invitation and configures the presenter, goal, audience, duration, demo scenario, and deck for that invitation.
- Slide plans use the meeting goal and company knowledge.
- Pre-meeting briefs use the current lead or deal, sales intelligence, risks, needs, and recommendations.
- The live runtime expects an exact meeting session, active deck version, presenter, and presentation state.

This means that the source deck and baseline presentation behavior could be reusable, but the current processed result cannot safely be reused in full because it contains situation-specific and customer-specific material.

Relevant implementation areas include:

- `src/VirtualCompany.Domain/Entities/SalesPresentationDeck.cs`
- `src/VirtualCompany.Domain/Entities/SalesPresentationSlide.cs`
- `src/VirtualCompany.Domain/Entities/SalesMeetingArtifact.cs`
- `src/VirtualCompany.Domain/Entities/SalesMeetingSession.cs`
- `src/VirtualCompany.Infrastructure.Sales/Sales/SalesPresentationDeckService.cs`
- `src/VirtualCompany.Infrastructure.Sales/Sales/SalesPresentationDeckProcessor.cs`
- `src/VirtualCompany.Web/Pages/Sales/SalesMeetingPreparation.razor`
- `src/VirtualCompany.Web/Components/Sales/SalesPresentationDeckWorkflow.razor`

## Proposed model

Use three distinct concepts:

```text
Presentation preset -> Published preset version -> Presentation run
      reusable               immutable              situation-specific
                                                       |- sales meeting
                                                       |- campaign activity
                                                       `- ad-hoc use
```

### Presentation preset

`SalesPresentationPreset` is the stable, company-owned library entry. It contains:

- name and description
- owner
- tags and supported use cases
- lifecycle status: draft, published, or archived
- current published version
- created and updated timestamps
- concurrency version

The preset provides the identity users select from meeting preparation, campaign planning, and other sales workflows.

### Presentation preset version

`SalesPresentationPresetVersion` is an immutable published definition. It contains:

- preset ID and version number
- source presentation asset version
- default presenter agent or required presenter capability
- presentation-specific agent behavior
- default meeting goal template
- default intended audience
- default duration
- optional demo scenario
- default presentation control mode
- language and localization settings
- required knowledge collections or scopes
- permitted situation types
- publication metadata

Presentation-specific agent behavior can include narration style, tone, question handling, interruption behavior, and control-mode defaults. It must not duplicate or override the agent's global permissions, tool authority, or approval policy.

Publishing creates an immutable version. Editing a published preset creates a new draft version. Existing presentation runs remain pinned to the version from which they were created.

### Reusable presentation asset

The uploaded PowerPoint and content that does not depend on a customer or meeting should be processed once and stored as a reusable versioned asset. Reusable data includes:

- the original uploaded file
- extracted slide text and speaker notes
- rendered slide images
- slide ordering
- generic objectives and transitions
- baseline talking points
- processing status, renderer information, hashes, and safe failure details

The reusable asset must not contain a customer briefing, deal analysis, lead facts, or other engagement-specific content.

### Presentation run

`SalesPresentationRun` represents one use of one published preset version. It contains:

- company ID
- preset version ID
- context type and context reference
- resolved presenter agent ID
- resolved goal, audience, duration, language, and control mode
- explicit situation-level overrides
- preparation and readiness status
- current presentation state
- source and correlation references
- created, updated, completed, and failure timestamps
- concurrency version

Initial context types should be:

- `sales_meeting`
- `campaign_activity`
- `ad_hoc`

Each context type must have an application-level resolver that validates the referenced record, company ownership, user authorization, and available customer context before a run is created.

## Reusable and contextual processing

Processing should be divided into two stages.

### Preset-version processing

Performed when a preset version is prepared for publication:

- validate and scan the source PowerPoint
- extract slides and notes
- render slide images
- create generic slide objectives, transitions, and baseline talking points
- record processing evidence and failures
- verify that the version is ready for publication

### Run preparation

Performed whenever a preset version is applied to a sales situation:

- resolve the presenter and current permissions
- load the lead, deal, contact, campaign, meeting, and customer context that is relevant to the situation
- retrieve accessible company knowledge
- generate customer-specific facts, needs, risks, questions, recommendations, positioning, and desired next steps
- classify claims and preserve source references
- calculate readiness and actionable blockers
- create the runtime binding required by the live presenter

This boundary prevents context from one opportunity or campaign member from leaking into another presentation.

## Agent behavior and authority

The preset should reference an existing named agent or specify a required Sales presenter capability. It should not create a separate agent-orchestration stack or clone an agent configuration.

When a run is prepared or executed, the system must:

1. Resolve the addressed agent.
2. Verify that the agent is active and belongs to the company.
3. Re-evaluate its current scopes, permissions, tools, autonomy, and approval requirements.
4. Apply the preset's presentation-specific behavior as a bounded overlay.
5. Block execution with a stable reason code when the presenter is no longer eligible.

Changing or deactivating an agent must never be bypassed by an older preset snapshot. Higher autonomy must not be inferred from the preset.

## Individual sales meeting flow

The meeting preparation flow should become:

1. Choose a published presentation preset.
2. Preview its deck, presenter, behavior, defaults, and version.
3. Apply the preset to the meeting.
4. Review the resolved customer context and permitted overrides.
5. Generate or regenerate the meeting-specific brief.
6. Resolve any readiness blockers.
7. Open the presenter using the run's exact session, deck, version, and agent binding.

The flow should also offer **Save as preset** for an existing meeting setup. This creates a draft preset from reusable fields only; it must exclude customer facts, deal analysis, transcripts, meeting observations, and other context-specific information.

When a newer preset version exists, the meeting should display that fact without updating automatically. An explicit **Update to latest version** action should preview changes and preserve or identify incompatible overrides.

The existing direct-upload flow should remain available during migration so current presentations continue to work.

## Campaign integration

Presentation presets should be integrated through campaign activities rather than through the existing outbound email sequence executor.

`SalesCampaignActivity` already models activity type, channel, execution mode, dependencies, owner agent, and required tool capability. The outbound `SalesSequenceStep` and its execution service are currently email-specific and should not be expanded into a generic presentation runtime.

A presentation campaign activity should contain or reference:

- the exact published preset version
- execution scope
- owner or presenter resolution rule
- planned start and due time
- dependency on earlier activities
- required meeting, task, or handoff behavior
- allowed situation-level overrides

Supported execution scopes should initially be:

- `per_contact`: create one prepared run for each eligible campaign contact
- `per_account`: create one prepared run for each distinct customer account
- `campaign_event`: create one run for a shared event or webinar

When the activity becomes due, it should create a presentation run and the associated preparation task or meeting binding. It must not automatically start presenting to a customer.

Campaign readiness should block scheduling or launch when:

- the preset is not published
- the selected version is archived or unavailable for new use
- the source presentation has not processed successfully
- no eligible presenter can be resolved
- required knowledge or tool capabilities are unavailable
- the preset belongs to another company
- the activity lacks the meeting, owner, or audience information required by its execution scope

Presentation activity progress and outcome should flow back into the existing campaign activity status and reporting model.

## Ad-hoc and future contexts

An ad-hoc entry point can create a presentation run without requiring an opportunity or campaign. The user selects a preset, optional contact/account context, and presenter, then prepares and starts the run.

Future sales contexts should integrate by adding a bounded context resolver and run creation adapter. New contexts should not add presentation logic directly to their controllers or user interfaces.

## Lifecycle and versioning rules

- Draft preset versions can be edited.
- Only successfully processed and valid versions can be published.
- Published versions are immutable.
- Editing a published preset creates the next draft version.
- Existing runs always retain their original preset version.
- A run may update to a newer version only through an explicit command.
- Archived presets cannot create new runs.
- Existing runs based on an archived preset remain readable and executable when policy allows.
- Deleting a preset version that has been used by a run is not allowed.
- Preset publication, application, override, presenter replacement, version update, archive, and failure events are audited.

## Readiness and policy

Readiness must be an authoritative backend decision. It should return:

- whether the preset or run is ready
- a stable readiness state
- blocker reason codes
- plain-language explanations
- allowed corrective actions
- whether human review or approval is required

Suggested run states are:

- `draft`
- `preparing`
- `needs_review`
- `ready`
- `presenting`
- `completed`
- `failed`
- `cancelled`

Suggested blocker codes include:

- `preset_not_published`
- `preset_version_unavailable`
- `presentation_asset_processing`
- `presentation_asset_failed`
- `presenter_missing`
- `presenter_ineligible`
- `context_missing`
- `knowledge_scope_unavailable`
- `customer_brief_needs_review`
- `meeting_binding_missing`

## API and application boundaries

Introduce Sales-owned application contracts for:

- listing and retrieving presets
- creating and editing draft presets
- importing or replacing a preset presentation asset
- publishing and archiving preset versions
- evaluating preset readiness
- applying a preset to a context
- retrieving and preparing a presentation run
- updating a run to a newer preset version
- recording explicit run overrides

Controllers must remain transport-only. Company and user context must be resolved server-side, and every command and query must be company-scoped.

The live presentation runtime should consume a prepared presentation run or a compatibility projection that still supplies its required session, deck/version, presenter, and presentation-state identifiers.

## Persistence and migration

The implementation requires an EF Core migration and updated model snapshot. Core queryable state must remain in relational columns. JSON may be used only for bounded flexible metadata such as optional behavior settings or resolved context snapshots.

Use a compatibility-first migration:

1. Add preset, preset-version, reusable-asset, and run persistence without changing existing meeting behavior.
2. Allow an existing meeting presentation to be saved as a draft preset.
3. Allow a preset version to materialize or bind into the existing session runtime.
4. Move reusable slide processing to the preset asset boundary.
5. Move customer-specific artifacts to the run boundary.
6. Backfill legacy presentations with a legacy run or compatibility marker when required.
7. Remove obsolete session-owned duplication only after all runtime and migration tests pass.

Existing migration IDs and production upgrade paths must be preserved. Storage keys, slide assets, and source files must not be deleted while referenced by a preset version or presentation run.

## Authorization, isolation, and audit

- Every new entity must be company-owned and company-filtered.
- Every preset, version, asset, and run operation must verify company scope explicitly.
- Cross-company reads, writes, publication, application, and context binding must be rejected.
- UI visibility must not be treated as authorization.
- Agent permissions and allowed tools must be rechecked at run preparation and execution.
- Customer-specific content must never be written back into a reusable preset version.
- Audit events must include preset, version, run, context, actor, outcome, rationale, and correlation references where applicable.
- Logs and audit metadata must not contain credentials, access tokens, or unnecessarily sensitive customer payloads.

## User experience

Add a Presentation presets library in the Sales area. Presets are operational sales content rather than general system administration, so they should be discoverable from meeting preparation and campaign planning without requiring a settings workflow.

The library should support:

- search and filtering
- draft, published, processing, failed, and archived states
- preset preview
- current and historical versions
- create, duplicate, publish, and archive actions
- where-used information
- processing and readiness blockers

Meeting and campaign screens should use the same preset picker and preview model. Situation-specific overrides must be visibly distinguished from preset defaults.

Any implementation or significant redesign of these user interfaces must follow the mandatory reference-screenshot workflow in `docs/design.md`.

## Delivery sequence

### Phase 1: Preset foundation

Deliver preset and version entities, persistence, migrations, processing readiness, application services, APIs, authorization, auditing, and tenant-isolation tests. Existing meeting presentation behavior remains unchanged.

### Phase 2: Meeting adoption

Deliver the preset library, reusable picker, meeting application flow, **Save as preset**, explicit version upgrades, compatibility binding to the existing presenter runtime, and UI/browser verification.

### Phase 3: Reusable processing boundary

Process source decks once per preset version, separate reusable slide data from customer-specific run artifacts, and verify storage lifecycle, processing retries, and compatibility with live narration and presentation controls.

### Phase 4: Campaign activities

Deliver presentation activity configuration, campaign readiness checks, execution-scope behavior, presentation-run creation, preparation task or meeting handoff, status reporting, authorization, and scheduling tests.

### Phase 5: Ad-hoc use and legacy consolidation

Deliver the ad-hoc entry point, legacy backfill or compatibility projections, telemetry, cleanup of obsolete duplication, and full regression verification.

## Acceptance criteria

- Given one published preset, when it is applied to two unrelated meetings, then both runs use the same immutable preset version and have independent customer-specific briefs and runtime state.
- Given a preset is updated, when a new version is published, then existing runs remain unchanged and new runs use the new version by default.
- Given an existing meeting setup, when a user saves it as a preset, then reusable configuration is copied and customer-specific information is excluded.
- Given a campaign presentation activity, when it becomes due, then the configured execution scope creates the correct presentation run or runs and the campaign activity records their progress.
- Given a campaign references an invalid or unready preset version, when readiness is evaluated, then launch or scheduling is blocked with stable reason codes and corrective actions.
- Given a preset is archived, when a user creates a new run, then the preset cannot be selected; existing pinned runs remain intact.
- Given an agent becomes inactive or loses required permissions, when a run is prepared or executed, then the operation is blocked until an eligible presenter is selected.
- Given a user from another company, when that user attempts to read, modify, publish, apply, or execute the preset, then the request is rejected without exposing preset data.
- Given the live presentation runtime, when a prepared meeting run starts, then it receives an exact session, deck/version, presenter, and presentation-state binding.
- Given existing opportunity presentations, when the new persistence and APIs are deployed, then the existing flows continue to work throughout the migration.

## Verification

Implementation should include, in proportion to each phase:

- domain tests for preset lifecycle, immutability, publication, archive, and run pinning
- application and API tests for commands, queries, conflicts, and problem mappings
- cross-company read and write tests
- migration and SQL Server model tests
- reusable processing, retry, and storage-lifecycle tests
- meeting preparation and presentation-runtime regression tests
- campaign readiness, scheduling, execution-scope, and reporting tests
- authorization and agent-eligibility tests
- audit-event verification
- Blazor component tests and real browser checks for the preset library, meeting picker, campaign configuration, responsive layouts, and error states
- a focused affected-area test run followed by the full build or broader validation required by the repository instructions

## Definition of done

The feature is complete when reusable presentation presets can be created, processed, published, versioned, selected in meetings, included in campaign activities, and used for ad-hoc sales presentations with production persistence, authorization, tenant isolation, audit evidence, customer-specific run preparation, and compatibility with the existing live presentation runtime.

The implementation must follow `production-implementation.md`, `docs/architecture-rules.md`, and `docs/design.md`. It must contain no production mock data, silent failures, unhandled intermediate states, or deferred in-scope TODOs.
