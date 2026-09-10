# Teams preservation and future reactivation

Status: mandatory preservation requirements for browser sales rooms, 9 September 2026. This document does not disable, deploy or reactivate an environment.

## Required outcome

Browser meetings are an additional conferencing route. Keep the Teams implementation in the active source tree, supported build, configuration schema, migration history, tests, deployment assets and operating documentation. Future reactivation must not require recovering deleted code from Git history.

The current working tree contains modified and untracked implementation work. The preservation baseline must include that work, not just HEAD. Never reset, clean, overwrite, discard or stash it away as a browser migration step. Before editing, capture a sanitized path/status/content-hash inventory of relevant source, tests, templates, migrations, scripts and infrastructure. Do not include credentials, certificates, local secret files, raw meeting evidence or token-bearing artifacts. The inventory is evidence, not a substitute for retaining the files.

## Preservation map

This starting map was checked against the working tree. Prompt 1 in `/docs/browser-sales-room-implementation-prompts.md` must complete it from the actual tree before implementation.

| Area | Preserve |
| --- | --- |
| Contracts | `src/VirtualCompany.Application/Sales/Teams*Contracts.cs`, `ITeamsMediaHostUpgradeReadiness.cs`, and shared meeting/presentation/realtime contracts used by Teams. |
| Sales infrastructure | Teams implementations in `src/VirtualCompany.Infrastructure.Sales/Sales`, including media adapter/coordinator/bridge, readiness, rollout, organizer experience, call control, callbacks, `MicrosoftGraphTeamsCallControlAdapter`, `AzureTeamsAppOnlyTokenProvider`, `FirstTeamsUatService` and owning DI registrations. |
| Identity/API | `TeamsSsoAuthentication.cs`, Teams controllers, package command, callback validation, authorization policies and startup wiring. |
| Web | Teams layout/pages, presenter admin, picker, first-UAT controls, typed clients, context service and `wwwroot/js/teams-meeting.js`. |
| Persistence | `TeamsTenantRegistration`, `TeamsMeetingCall`, EF mappings, all existing migration IDs/snapshot content, meeting/call/deck associations and status values. |
| Package/deployment | `src/VirtualCompany.Infrastructure.Sales/Teams/Package/`, package builder, `scripts/Install-TeamsMediaHost.ps1`, `Deploy-TeamsMediaHost.ps1`, `Test-TeamsMediaSdkFreshness.ps1`, `build-teams-presenter-package.ps1`, and all discovered related infrastructure/templates. |
| Tests/evidence | Existing Teams API/Web/migration/package/deployment tests and sanitized UAT evidence. Keep known blockers visible instead of declaring readiness. |

Preserve existing runbooks: `/docs/teams-presenter-production-rollout.md`, `/docs/teams-presenter-identity-and-consent.md`, `/docs/teams-presenter-app-package.md`, `/docs/teams-call-control-runbook.md`, `/docs/teams-presenter-stage-runbook.md`, `/docs/teams-presenter-live-uat-matrix.md`, `/docs/teams-media-azure-runtime-decision.md` and `/docs/runbooks/teams-media-azure-runtime.md`.

## Coexistence rules

- Persist conferencing selection separately from calendar provider. Browser invitations use our link without requesting a Teams online meeting. Existing Teams invitations retain their links and meaning.
- Select readiness, adapter and lifecycle by explicit meeting route. Missing Teams credentials must not block browser readiness; missing browser credentials must not block Teams readiness. Disabled optional routes must not cause unrelated startup failures.
- Preserve `TeamsPresenter` configuration names and gate meanings, including production/pilot, consent, allowlists, route approval, evidence, budgets and emergency disable. Browser settings cannot enable Teams or bypass its checks.
- Keep existing `browser_webrtc` single-user semantics. Use a distinct value for multi-party rooms; never silently reinterpret existing settings or records.
- Shared presentation settings/contracts require tested compatibility mapping. New defaults must not silently override explicitly configured Teams settings.
- Keep supported Teams dependencies/builds. Isolate platform-specific loading within approved boundaries instead of deleting packages to make browser builds pass.
- Do not change live Teams flags, terminate calls, revoke permissions, uninstall packages, or delete/rotate secrets, certificates or Azure resources as part of browser implementation. Those actions require a separate explicit request.
- Retaining code does not require keeping billable infrastructure running indefinitely. A later power-down request follows its operating workflow and preserves restoration inputs.

## Reactivation procedure to maintain

1. Inspect preserved build/test evidence and unresolved blockers. Distinguish code availability, automated validation, environment readiness and live UAT.
2. Rebuild on the supported Windows/Azure media runtime. Check current SDK support/freshness and update compatibility if needed without altering browser routing.
3. Restore infrastructure through the preserved runbooks/templates if separately suspended. Resolve secret references from approved stores and validate certificate, callback, media networking and host identity.
4. Verify tenant registration, exact permissions, admin consent, policy attestation and installed package version through existing readiness/admin tools.
5. Verify migrations and existing data. Never reset the database or replace migration history.
6. Run automated regressions and the existing isolated first-live-test/UAT flow under its original gates and explicit live-test authorization. Old evidence is not automatically valid after runtime changes.
7. With deployment authorization, enable the scoped pilot/allowlists. Production release retains its existing requirements. Browser settings remain independent.

Browser implementation acceptance requires Teams source/assets retained and applicable regressions verified, with explicit pre-existing/environment blockers. It does not require switching Teams on. Maintain this document with exact changed paths/configuration mappings during implementation.

## Prompt 1 preservation evidence — 9 September 2026

The browser transport was added through one call to `AddSalesRoomMedia` in `src/VirtualCompany.Infrastructure.Sales/Sales/SalesModuleRegistration.cs` and two pinned package references in the Sales project file. Existing Teams source/settings/contracts were not replaced. The distinct `SalesBrowserRoom` configuration and `browser_livekit_room` route do not alter `TeamsPresenter` or the single-user `browser_webrtc` route. No database changes or live Teams resource operations occurred.

The 746-path baseline and content-hash comparison are under `/docs/verification/browser-sales-room/`; full verification, package provenance and remaining live/Azure gates are recorded in `/docs/verification/browser-sales-room-implementation.md`. Teams media/readiness/package/runtime/deployment regressions and the Windows API build remain executable. Live Teams readiness and SDK freshness on a future reactivation date still require the original runbooks and gates.

## Prompt 4 preservation evidence — 9 September 2026

Human browser rooms add separate public/organizer pages, typed clients and a local LiveKit browser module. `VirtualCompany.Web/App.razor` gains only the early browser-room fragment bootstrap. Shared Sales lead/preparation surfaces gain an explicit organizer-room link; Teams pages, layout, `teams-meeting.js`, single-user realtime JavaScript and their routes retain their implementation. Web client registration and the test project include the new browser capability. Browser room contracts/services gain safe audience fields, and browser-only status actions use a separate polling budget.

No Teams setting, package, migration, infrastructure asset or stored record was changed. The existing migrations and model snapshot remain intact. Focused Teams Web/API regressions pass in the Prompt 4 check record. See [implementation evidence](verification/browser-sales-room-implementation.md), [baseline](verification/browser-sales-room/prompt4-baseline.json) and [comparison](verification/browser-sales-room/prompt4-preservation-comparison.json). No live Teams operation or reactivation occurred; its original live-readiness requirements remain in force.

Prompt 7 extends the shared Realtime PCM session request with an opt-in manual-input-commit mode for browser-room local VAD. The existing Teams call site keeps the default provider-turn behavior. The Prompt 7 preservation run passed 61 Teams/shared-media tests, including realtime normalization, application-hosted media, media-host runtime, deployment prerequisites, presenter packaging and output fencing. No Teams-specific source, deployment asset, setting, test, or historical migration changed, and no Teams resource was invoked or reactivated.

Prompt 8 adds browser-only floor state, response generations, client playback-stop receipts, controller endpoints and host/guest UI. It reuses the route-neutral presentation conductor and shared preemption contract. The affected Teams call-control, realtime, application-hosted-media, media-host and conductor regression selection passed 37 tests. The additive migration does not alter a Teams table or historical migration. No Teams flag, credential, resource, route or stored status value was changed or invoked.


## Prompt 9 preservation evidence — 10 September 2026

All 792 baseline paths remain present. Changed shared Sales files are SalesModuleRegistration (capture/retention registration), SalesMeetingTranscriptIngestionDispatcher (exclude BrowserRoom from Graph matching), SalesMeetingQuestionAnsweringService (canonical browser-evidence guard), SalesMeetingCaptureService (reject fabricated browser sources), SalesMeetingClosingService (browser extraction and completion replay), SalesMeetingChangeProposalService (share binding calculation), SalesMeetingCustomerMinutesDeliveryDispatcher (revalidate approval/evidence before send), SalesMeetingCaptureEnums (append BrowserRoom without renumbering), SalesMeetingSession (browser closing transition), and SalesMeetingQuestion (browser-only expiry). The EF snapshot includes additive nullable browser provenance columns and its canonical FK/index. Existing migration files and Teams settings/assets/routes are preserved. Shared closing, transcript reconciliation and delivery regression tests add browser isolation and stale/revoked approval cases.

SQL Server Express fresh/upgrade validation passed, including preserved Teams call state, invitation URLs, transcript source/content and minutes content. The Teams/shared-media and expanded retention selection passed 57 tests. The broader capture/closing/Graph/lifecycle selection passed 67. [Prompt 9 report](verification/browser-sales-room/prompt9-uat.md) distinguishes these checks from unrun live calls and sends. No Teams reactivation, deployment or external message occurred.

## Prompt 10 preservation evidence — 10 September 2026

Prompt 10 adds browser-room metrics, a synthetic full-call coordinator/analyzer and an isolated benchmark infrastructure template. The meter is route-neutral infrastructure but contains no tenant, room, participant or user tags. The benchmark driver contract prohibits Teams targets and traffic; the disposable App Service template explicitly sets `TeamsPresenter__Enabled` and `TeamsMediaHost__Enabled` false.

No Teams-specific source, configuration, package, deployment asset, test or historical migration changed. The shared realtime/media/deployment/package regression selection passed as part of 63 focused tests, with one unrelated credential-gated narration live test skipped. No Azure resource was created, no call or load ran, and no Teams flag, credential, resource or route was invoked.

## Prompt 11 preservation evidence — 10 September 2026

The final comparison retains all **792** baseline paths: **790** hashes are unchanged. `src/VirtualCompany.Infrastructure.Sales/Sales/SalesModuleRegistration.cs` is intentionally changed to register and validate browser readiness, drain, capacity and rate settings while retaining the existing Teams registrations and option names. This preservation guide is the second changed baseline path because it records this reconciliation. `src/VirtualCompany.Web/Localization/Sales/SalesResources.sv-SE.resx` was outside the 792-path baseline and received one Teams-facing regression fix: `SalesPresenterReady` again contains the `{0}` presenter-count placeholder required by the English contract. No path moved or is missing.

Browser deployment assets are isolated under `infra/browser-sales-room/` and do not set, disable or overwrite `TeamsPresenter` or `TeamsMediaHost`. Browser settings are `SalesBrowserRoom` and `SalesRoomAgent`; the four-way configuration test proves browser-only and Teams-only binding. Browser rollback disables those browser keys and leaves Teams configuration, SQL schema and stored Teams data intact. Routine database downgrade is prohibited.

The supported Windows API build passed. All three `infra/teams-media` Bicep templates compile. The reviewed SDK lock for `Microsoft.Graph.Communications.Calls.Media` **1.2.0.17950** passed its 92-day freshness gate at 70 days. The supported package command generated a disabled three-entry package from synthetic IDs, proving manifest/settings validation and packaging without enabling Teams. The artifact SHA-256 is `97453722844DFB9516E017C880ED350A9ADFCDB3B3D3950401BCC318A8259094`.

The broader browser/shared/Teams API selection passed 184 tests. The Teams/browser SQL Server selection passed five fresh/upgrade tests against disposable LocalDB databases, including preservation of legacy Teams records and the rule that migrations never manufacture UAT approval. The focused browser/Teams/localization Web selection passed 57 tests, keeping admin and meeting surfaces executable. EF reports no pending model changes.

Current live blockers remain: there is no authorized Teams environment reactivation, current tenant/app/certificate/consent attestation, installed-package verification, media networking probe or first live UAT. Old retained evidence does not satisfy those gates. No live call was made. Use the existing production, identity, package, call-control, stage, media-runtime and live-UAT runbooks after separate authorization.
