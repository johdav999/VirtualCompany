# Goal
Implement backlog task **TASK-32.4.4** for **US-32.4 Fortnox integration UI, approval-gated writes, and production readiness** by adding comprehensive automated coverage and operational diagnostics for Fortnox sync and token refresh flows.

Deliver:
- **Unit tests** for Fortnox OAuth URL generation, token refresh, encryption, DTO mapping, duplicate prevention, sync cursor updates, and approval creation.
- **UI tests** for the Finance Settings → Integrations Fortnox card states and actions.
- **Opt-in real API integration tests** that only execute against the real Fortnox API when explicit environment variables are present.
- **Operational metrics and diagnostics** for sync and token refresh flows, including structured logs, counters/timers where the project’s observability stack supports them, and safe redaction of secrets.
- Any required **UI refinements** to ensure the Fortnox integration card and approval-gated write UX satisfy acceptance criteria.
- If UI is newly introduced or materially refactored, generate and store a reference screenshot at `/docs/design/references/fortnox-integration-settings.png` using the OpenAI `image.2` API, and align implementation with `/docs/style.md` and `/docs/design.md`.

Do not weaken approval gating, tenant isolation, or secret handling.

# Scope
In scope:
- Fortnox integration test coverage across application, infrastructure, API, and web UI layers.
- Diagnostics for:
  - sync start/success/failure
  - token refresh start/success/failure
  - duplicate prevention decisions
  - sync cursor progression
  - approval creation before write execution
- UI verification for Fortnox card states:
  - Not connected
  - Connecting
  - Connected
  - Syncing
  - Needs reconnect
  - Error
- UI verification for actions:
  - Connect Fortnox
  - Sync now
  - Reconnect
  - Disconnect
  - View sync history
- Approval-gated Fortnox write behavior:
  - approval item created before any external write call
  - target company and payload summary shown
  - execution only after explicit approval
- Audit event verification for approved writes:
  - approver
  - entity type
  - direction
  - summary
  - payload hash
  - no tokens or sensitive Fortnox secrets
- Opt-in real API integration tests guarded by explicit environment variables and skipped otherwise.

Out of scope unless required to make tests pass:
- Broad redesign of unrelated integrations UI.
- New production infrastructure outside app-level metrics/logging hooks already used by the solution.
- Non-Fortnox integrations.
- Mobile app changes unless shared UI/test contracts require them.

# Files to touch
Inspect the solution first and update the exact files that already own these concerns. Expect to touch files in these areas:

- `src/VirtualCompany.Domain/**`
  - Fortnox-related entities/value objects/status enums
  - approval/audit models if Fortnox write metadata needs completion
- `src/VirtualCompany.Application/**`
  - Fortnox commands/queries/handlers
  - approval creation flow for Fortnox writes
  - sync orchestration and cursor update logic
  - DTO mapping services
  - diagnostics/metrics abstractions if application-owned
- `src/VirtualCompany.Infrastructure/**`
  - Fortnox API client
  - OAuth/token refresh implementation
  - encryption/token storage
  - sync persistence/repositories
  - structured logging/metrics emission
  - real integration test support helpers if infrastructure-owned
- `src/VirtualCompany.Api/**`
  - endpoints/controllers for Fortnox connect/sync/reconnect/disconnect/history
  - DI registration for metrics/diagnostics
  - health/observability wiring if needed
- `src/VirtualCompany.Web/**`
  - Finance Settings → Integrations Fortnox card UI
  - approval UX for Fortnox writes
  - sourced-from-Fortnox indicators and overwrite prevention messaging
  - component tests if colocated
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests
  - opt-in real API tests with environment guards
- Add or update additional test projects if the repo already has or needs:
  - `tests/VirtualCompany.Application.Tests/**`
  - `tests/VirtualCompany.Infrastructure.Tests/**`
  - `tests/VirtualCompany.Web.Tests/**`
- `docs/design/references/fortnox-integration-settings.png`
  - only if UI is new/refactored enough to require the reference image
- Potentially:
  - `README.md`
  - test README/docs for required Fortnox env vars
  - `.runsettings` or test helper docs if the repo uses them

Do not invent parallel patterns if the repository already has established testing, logging, metrics, or UI conventions.

# Implementation plan
1. **Discover existing Fortnox implementation and test seams**
   - Locate all Fortnox-related code paths across Application, Infrastructure, API, and Web.
   - Identify:
     - OAuth URL generation
     - token refresh logic
     - token encryption/storage
     - sync job/service
     - duplicate prevention logic
     - sync cursor persistence
     - approval creation for writes
     - audit event creation
     - Finance Settings → Integrations UI
   - Reuse existing abstractions and naming conventions.

2. **Map acceptance criteria to concrete test cases**
   - Create a checklist and ensure each criterion is covered by at least one automated test.
   - Prefer:
     - unit tests for pure/domain/application logic
     - component/UI tests for Blazor rendering and action availability
     - API/integration tests for endpoint behavior and approval gating
     - opt-in real API tests for live Fortnox connectivity

3. **Add/complete unit tests**
   - Cover OAuth URL generation:
     - correct base URL
     - required query parameters
     - redirect URI handling
     - state/correlation handling
     - tenant/company context where applicable
   - Cover token refresh:
     - refresh request composition
     - success path updates stored tokens/expiry
     - failure path marks reconnect/error state appropriately
     - no secret leakage in logs/exceptions
   - Cover encryption:
     - round-trip encrypt/decrypt
     - ciphertext differs from plaintext
     - deterministic behavior only if intentionally designed; otherwise verify safe non-plaintext persistence
   - Cover DTO mapping:
     - Fortnox payload → internal normalized DTO/entity
     - null/optional field handling
     - culture/decimal/date correctness
   - Cover duplicate prevention:
     - same external Fortnox record not imported twice
     - idempotent sync behavior
   - Cover sync cursor updates:
     - cursor advances on success
     - cursor does not advance incorrectly on failure
     - partial sync semantics if implemented
   - Cover approval creation:
     - write request creates approval before external API call
     - approval contains target company and payload summary
     - external write is blocked until approval is explicit

4. **Add/complete UI tests for Finance Settings → Integrations**
   - Use the project’s existing Blazor component/UI test approach.
   - Verify Fortnox card renders all required states.
   - Verify action visibility/enabled state per status.
   - Verify sync history action exists.
   - Verify sourced-from-Fortnox indicators are shown where relevant.
   - Verify simulation/overwrite prevention messaging for Fortnox-linked records.
   - If approval UI is in web scope, verify payload summary and target company are displayed before approval.

5. **Implement or refine UI only where tests reveal gaps**
   - Ensure the Fortnox card supports the required states and actions exactly as specified.
   - Keep styling aligned with `/docs/style.md` and `/docs/design.md`.
   - If the UI is new or materially refactored:
     - generate `/docs/design/references/fortnox-integration-settings.png` via OpenAI `image.2`
     - keep the final implementation visually consistent with the reference
   - Do not introduce unnecessary visual churn.

6. **Enforce and verify approval-gated write flow**
   - Confirm every MVP Fortnox write path creates an approval item before any external API write call.
   - Ensure approval payload includes:
     - target company
     - payload summary
   - Ensure execution only proceeds after explicit approval.
   - Add tests that assert no external client write method is invoked before approval.
   - If needed, refactor write orchestration to make this sequencing explicit and testable.

7. **Enforce and verify audit event safety**
   - Ensure approved writes create audit events with:
     - approver
     - entity type
     - direction
     - summary
     - payload hash
   - Ensure audit payload excludes:
     - access tokens
     - refresh tokens
     - client secrets
     - sensitive Fortnox secrets
   - Add tests for redaction/exclusion.

8. **Add operational metrics and diagnostics**
   - Follow existing observability patterns in the repo.
   - Add structured logs with correlation ID and tenant/company context where applicable.
   - Emit metrics for:
     - sync attempts
     - sync successes
     - sync failures
     - token refresh attempts
     - token refresh successes
     - token refresh failures
     - duplicate-skipped records
     - approvals created for Fortnox writes
   - If supported, add duration metrics/timers for:
     - sync execution
     - token refresh latency
   - Ensure diagnostics never log secrets or raw sensitive payloads.
   - Prefer centralized helper methods or instrumentation wrappers over scattered ad hoc logging.

9. **Add opt-in real Fortnox API integration tests**
   - Implement tests so they run only when explicit env vars are present.
   - Use a clear guard pattern, e.g. skip unless all required vars exist.
   - Likely env vars:
     - `FORTNOX_INTEGRATION_TESTS_ENABLED=true`
     - `FORTNOX_CLIENT_ID`
     - `FORTNOX_CLIENT_SECRET`
     - `FORTNOX_ACCESS_TOKEN` and/or refresh-token/test-account values as required by the existing auth flow
     - any base URL / test company identifiers if needed
   - Keep these tests isolated, non-destructive, and safe by default.
   - Prefer read-only or explicitly sandboxed calls unless a write test is absolutely necessary and approval-gated.
   - Document required env vars and execution steps.

10. **Keep tests deterministic**
   - Use fakes/mocks for unit tests.
   - Avoid wall-clock and random flakiness; inject clocks/IDs where needed.
   - For UI tests, assert semantic states and actions rather than brittle markup details.
   - For integration tests, isolate external dependency usage behind explicit opt-in.

11. **Update docs if needed**
   - Add a short section describing:
     - how to run Fortnox tests
     - which tests are opt-in/live
     - required environment variables
     - any screenshot generation note if applicable

12. **Final quality pass**
   - Ensure no secrets are committed.
   - Ensure all new tests pass locally.
   - Ensure skipped live tests report clearly when env vars are absent.
   - Ensure code follows existing solution structure and .NET conventions.

# Validation steps
Run and report the results of the relevant commands you actually executed.

Minimum:
1. `dotnet build`
2. `dotnet test`

Also run targeted tests if separate projects/suites exist, for example:
- application/unit tests
- infrastructure tests
- web/component tests
- API tests

Validation checklist:
- Unit tests exist and pass for:
  - OAuth URL generation
  - token refresh
  - encryption
  - DTO mapping
  - duplicate prevention
  - sync cursor updates
  - approval creation
- UI tests exist and pass for Fortnox card states/actions.
- Approval-gated write tests prove no external write occurs before approval.
- Audit event tests prove required fields are present and secrets are excluded.
- Metrics/diagnostics code is covered by tests where practical, or at minimum verified through focused tests/assertions on emitted events/log calls.
- Opt-in real API tests:
  - skip cleanly when env vars are absent
  - run only when explicitly enabled
- If UI was added/refactored:
  - `/docs/design/references/fortnox-integration-settings.png` exists
  - implementation aligns with docs styling guidance

Include in your final summary:
- files changed
- tests added
- commands run
- any skipped live tests and why

# Risks and follow-ups
- **Repo structure uncertainty:** Fortnox code may not yet be fully present or may be split across unexpected modules. Discover before changing architecture.
- **UI test framework uncertainty:** If no existing Blazor UI/component test setup exists, add the smallest viable test harness consistent with the repo.
- **Metrics stack uncertainty:** The solution may use `ILogger`, `System.Diagnostics.Metrics`, OpenTelemetry, or custom abstractions. Reuse the existing pattern rather than introducing a competing one.
- **Live API fragility:** Real Fortnox tests can be flaky due to credentials, rate limits, or tenant state. Keep them opt-in, minimal, and clearly skipped by default.
- **Approval flow gaps:** If some Fortnox write paths bypass approval today, fix the orchestration rather than only adding tests.
- **Audit safety:** Be especially careful that payload summaries and hashes do not accidentally include secrets.
- **Screenshot generation dependency:** Only generate the reference image if the UI is new/refactored enough to require it; do not block delivery on image generation if no UI change is needed.
- **Potential follow-up work:** dashboard surfacing of Fortnox operational metrics, richer sync history UX, and broader integration observability once the base diagnostics are in place.