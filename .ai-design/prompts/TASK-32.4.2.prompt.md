# Goal
Implement backlog task **TASK-32.4.2** for **US-32.4 Fortnox integration UI, approval-gated writes, and production readiness** in the existing .NET solution.

Deliver a production-ready Fortnox integration settings experience in **Finance Settings → Integrations** with:
- a Fortnox card supporting the required connection/sync/error states and actions
- SaaS styling aligned to `/docs/style.md` and `/docs/design.md`
- a generated reference screenshot at `/docs/design/references/fortnox-integration-settings.png` using **OpenAI image.2** if the UI is new or materially refactored
- approval-gated Fortnox write behavior before any external API call
- audit events for approved writes with sensitive-secret exclusion
- clear UI indicators for Fortnox-sourced records and prevention of simulation overwrite on Fortnox-linked records
- unit tests for the specified Fortnox integration behaviors
- opt-in real API integration tests guarded by explicit environment variables only

Work within the existing modular monolith architecture and preserve tenant isolation, CQRS-lite boundaries, approval-first execution, and auditability as domain features.

# Scope
In scope:
- Web UI changes in the Blazor app for Finance Settings → Integrations
- Fortnox integration state modeling and view models needed to render:
  - Not connected
  - Connecting
  - Connected
  - Syncing
  - Needs reconnect
  - Error
- UI actions:
  - Connect Fortnox
  - Sync now
  - Reconnect
  - Disconnect
  - View sync history
- Styling updates to align with `/docs/style.md` and `/docs/design.md`
- Reference screenshot generation and storage at:
  - `/docs/design/references/fortnox-integration-settings.png`
- Approval-first write flow for all MVP Fortnox write operations:
  - create approval item before any external API call
  - show target company and payload summary
  - execute only after explicit approval
- Audit event creation for approved writes including:
  - approver
  - entity type
  - direction
  - summary
  - payload hash
  - excluding tokens and sensitive Fortnox secrets
- UI affordances indicating Fortnox-linked/sourced records
- prevention of simulation actions overwriting Fortnox-linked records
- Unit tests for:
  - OAuth URL generation
  - token refresh
  - encryption
  - DTO mapping
  - duplicate prevention
  - sync cursor updates
  - approval creation
- Optional integration tests that call real Fortnox API only when explicit env vars are present

Out of scope unless required to satisfy acceptance criteria:
- broad redesign of unrelated settings pages
- mobile app changes
- non-Fortnox integrations
- introducing microservices or major architectural rewrites
- exposing secrets in logs, tests, screenshots, fixtures, or audit payloads

# Files to touch
Inspect the repo first and then update the most relevant files. Expected areas include:

Documentation:
- `docs/style.md`
- `docs/design.md`
- `docs/design/references/fortnox-integration-settings.png`
- any nearby design/readme notes if a screenshot generation workflow needs documenting

Web/UI:
- `src/VirtualCompany.Web/**`
  - Finance settings/integrations page(s)
  - Fortnox card component(s)
  - shared status badge/button/card styling
  - record detail/list components that need Fortnox-source indicators
  - simulation action UI guards/disabled states

Application layer:
- `src/VirtualCompany.Application/**`
  - Fortnox commands/queries/DTOs
  - approval creation orchestration for write operations
  - payload summary generation
  - duplicate prevention and sync cursor handling
  - audit event application services

Domain layer:
- `src/VirtualCompany.Domain/**`
  - entities/value objects/enums for integration state, approval metadata, audit metadata, source linkage flags, payload hashing abstractions if domain-owned

Infrastructure:
- `src/VirtualCompany.Infrastructure/**`
  - Fortnox OAuth/token client
  - token refresh logic
  - encryption/secret storage
  - Fortnox DTO mapping
  - external API execution moved behind approval gate if needed
  - audit persistence and secret redaction/sanitization
  - optional screenshot generation helper/script only if appropriate for repo conventions

API:
- `src/VirtualCompany.Api/**`
  - endpoints/controllers for integration status/actions/history/approval-triggered execution if API-backed
  - environment-gated integration test configuration

Tests:
- `tests/**`
  - unit tests for Fortnox integration logic
  - optional integration tests with explicit env var guards

Also inspect for existing Fortnox-related files before creating new ones. Prefer extending established patterns over inventing parallel structures.

# Implementation plan
1. **Discover existing Fortnox implementation and UI structure**
   - Search the solution for `Fortnox`, `Integration`, `Approval`, `Audit`, `Sync`, `OAuth`, `simulation`, and `Finance Settings`.
   - Identify current modules, commands, pages, DTOs, and tests.
   - Determine whether the Fortnox settings UI already exists and whether this task is a refinement or a new implementation.
   - Review `/docs/style.md` and `/docs/design.md` before making UI changes.

2. **Model the Fortnox integration states and actions**
   - Ensure there is a clear state model for:
     - Not connected
     - Connecting
     - Connected
     - Syncing
     - Needs reconnect
     - Error
   - Map backend/infrastructure status data into a UI-safe view model.
   - Ensure action availability is state-dependent, e.g.:
     - Not connected → Connect Fortnox
     - Connected → Sync now, Disconnect, View sync history
     - Needs reconnect/Error → Reconnect, View sync history
     - Syncing → View sync history, disable duplicate sync action as appropriate

3. **Implement/update Finance Settings → Integrations Fortnox card**
   - Add or refine the Fortnox card in the correct settings page.
   - Include:
     - provider name/logo treatment if already used in design system
     - current status badge
     - concise description
     - last sync / sync health / reconnect hint if available
     - required actions
   - Apply SaaS styling from `/docs/style.md` and `/docs/design.md`:
     - spacing, typography, card hierarchy, button variants, status colors, empty/error states
   - Reuse shared components/tokens where possible.

4. **Generate the reference screenshot if UI is new or materially refactored**
   - If the UI is newly introduced or significantly changed, generate a reference screenshot using **OpenAI image.2**.
   - Store the final artifact exactly at:
     - `/docs/design/references/fortnox-integration-settings.png`
   - The screenshot should depict the intended SaaS-styled Finance Settings → Integrations Fortnox card/page state and align with `/docs/style.md` and `/docs/design.md`.
   - If the repo has no existing automation for image generation, add the minimal documented workflow needed without overengineering.
   - Do not commit secrets or API keys; use environment variables if generation is scripted.
   - If generation cannot be automated in-code, still prepare the prompt/instructions and ensure the file path and documentation are in place.

5. **Enforce approval-first Fortnox write operations**
   - Identify all MVP Fortnox write operations.
   - Refactor so that no external Fortnox write API call occurs before an approval item is created and explicitly approved.
   - Approval item must include:
     - target company
     - payload summary
     - enough context for a human approver to make a decision
   - Execution flow should be:
     1. user/system requests write
     2. approval item created
     3. UI/API reflects pending approval state
     4. only after explicit approval does the external Fortnox call execute
   - Ensure rejected/expired/cancelled approvals do not execute writes.

6. **Add audit events for approved writes**
   - On approved Fortnox write execution, create business audit events containing:
     - approver
     - entity type
     - direction
     - summary
     - payload hash
   - Exclude:
     - access tokens
     - refresh tokens
     - client secrets
     - authorization codes
     - any sensitive Fortnox secrets
   - If audit payloads currently store raw request data, sanitize/redact before persistence.
   - Prefer deterministic payload hashing over storing sensitive payload bodies.

7. **Mark Fortnox-sourced records and block simulation overwrite**
   - Identify the record types surfaced in UI that can be sourced/linked from Fortnox.
   - Add clear visual indication for Fortnox-linked records.
   - Prevent simulation actions from overwriting those records:
     - disable action in UI with explanatory text where possible
     - enforce server-side guard as well, not UI-only
   - Ensure the guard is tenant-safe and based on source linkage metadata, not fragile UI assumptions.

8. **Strengthen Fortnox integration internals**
   - Verify or implement:
     - OAuth URL generation
     - token refresh flow
     - encryption for stored secrets/tokens
     - DTO mapping between Fortnox payloads and internal contracts
     - duplicate prevention/idempotency for sync/import
     - sync cursor updates
     - approval creation for writes
   - Keep external integration logic in infrastructure adapters and application orchestration in the application layer.
   - Preserve typed contracts rather than direct DB or ad hoc HTTP usage from UI.

9. **Add tests**
   - Add unit tests covering the acceptance criteria explicitly:
     - OAuth URL generation
     - token refresh
     - encryption
     - DTO mapping
     - duplicate prevention
     - sync cursor updates
     - approval creation
   - Add opt-in integration tests for real Fortnox API calls only when explicit environment variables are provided.
   - Skip or no-op safely when env vars are absent.
   - Never require real credentials for default local or CI test runs.

10. **Polish and align with architecture**
   - Keep tenant scoping enforced in all queries/commands.
   - Keep approval and audit behavior as domain/application concerns, not UI-only.
   - Ensure outbox/retry/idempotency patterns are respected where side effects already use them.
   - Avoid leaking secrets in logs, exceptions, screenshots, snapshots, or test output.

# Validation steps
Run these checks after implementation:

1. **Build and test**
   - `dotnet build`
   - `dotnet test`

2. **UI verification**
   - Navigate to Finance Settings → Integrations.
   - Confirm the Fortnox card renders and supports all required states:
     - Not connected
     - Connecting
     - Connected
     - Syncing
     - Needs reconnect
     - Error
   - Confirm required actions appear appropriately:
     - Connect Fortnox
     - Sync now
     - Reconnect
     - Disconnect
     - View sync history
   - Confirm styling is consistent with `/docs/style.md` and `/docs/design.md`.

3. **Reference screenshot verification**
   - Confirm the file exists at:
     - `/docs/design/references/fortnox-integration-settings.png`
   - Confirm it visually reflects the implemented UI and SaaS styling.

4. **Approval-gated write verification**
   - Trigger each MVP Fortnox write path.
   - Confirm:
     - an approval item is created before any external API call
     - approval shows target company and payload summary
     - no external write occurs until explicit approval
     - rejected/expired/cancelled approvals do not execute

5. **Audit verification**
   - Approve a Fortnox write operation and confirm an audit event is created with:
     - approver
     - entity type
     - direction
     - summary
     - payload hash
   - Confirm tokens and sensitive Fortnox secrets are excluded from persisted audit data.

6. **Fortnox-linked record protection**
   - Verify records sourced from Fortnox are clearly labeled in the UI.
   - Attempt simulation overwrite on a Fortnox-linked record.
   - Confirm the action is prevented in both UI and server-side behavior.

7. **Test coverage verification**
   - Confirm unit tests exist and pass for:
     - OAuth URL generation
     - token refresh
     - encryption
     - DTO mapping
     - duplicate prevention
     - sync cursor updates
     - approval creation
   - Confirm real Fortnox integration tests only run when explicit env vars are set.

8. **Security/logging sanity check**
   - Search changed files for accidental secret exposure.
   - Ensure no tokens/secrets are logged, snapshotted, or embedded in docs/assets.

# Risks and follow-ups
- **Unknown existing Fortnox implementation shape**: the repo may already have partial Fortnox support with patterns that must be preserved. Inspect before refactoring.
- **Screenshot generation practicality**: if OpenAI image.2 generation is not already wired into the repo, keep the implementation minimal and documented; do not block core product behavior on elaborate tooling.
- **Approval boundary gaps**: some write paths may be indirect or background-triggered. Verify all MVP Fortnox writes are covered, not just UI-triggered ones.
- **UI-only protection is insufficient**: simulation overwrite prevention must also be enforced server-side.
- **Audit overcollection risk**: be careful not to persist raw sensitive payloads while adding payload summaries and hashes.
- **Integration test fragility**: real Fortnox API tests must be strictly opt-in and resilient to missing credentials/environment.
- **State explosion in UI**: keep state rendering centralized to avoid inconsistent action availability across components.
- **Follow-up suggestion**: if not already present, consider a shared integration-status component pattern for future connectors so Fortnox does not become a one-off implementation.