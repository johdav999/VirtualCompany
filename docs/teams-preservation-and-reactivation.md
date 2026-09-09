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
