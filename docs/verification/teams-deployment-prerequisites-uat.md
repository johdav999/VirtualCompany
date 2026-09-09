# Teams deployment prerequisite verification — 2026-09-08

## Scope and result

Repository implementation is complete for Windows service hosting, deployment preview, Uniform VMSS identity, signaling/readiness separation, Teams framing, explicit presenter selection, and scoped first-test authorization. This record does not approve production or claim live Teams audio/presentation.

Environment: local Windows workstation; isolated SQLite application fixtures; fresh disposable SQL Server LocalDB database; published API/web artifacts; headless Edge. No Azure resources, tenant permissions, app installation, real meeting, or live provider media were created.

Roles: organizer and platform administrator are deterministic test identities only. No real tenant/user credentials are included.

## Executed checks

| Check | Result | Evidence |
| --- | --- | --- |
| Teams API/domain/service tests | 74 passed, 0 failed, 0 skipped | `artifacts/teams-deployment-tests-final.log` |
| Teams and meeting preparation web tests | 24 passed, 0 failed | `artifacts/teams-deployment-web-final.log` |
| Component capture after fixture export support | 4 passed | `artifacts/teams-ui-capture.log` |
| Actual generated SQL Server upgrade and idempotent reapplication | Passed inside the API suite; disposable database removed | `TeamsDeploymentMigrationSqlServerTests.cs` |
| EF pending model changes | None | `artifacts/teams-model-validation.log` |
| PowerShell syntax and mocked Azure/SCM boundaries | Passed | `artifacts/teams-script-tests.log` |
| Bicep subscription template compilation | Passed; no deployment executed | `artifacts/teams-bicep-validation.log` |
| Self-contained win-x64 API publish | Passed; executable, coreclr, hostfxr, media assembly and included .NET/ASP.NET Core 9.0.19 present | `artifacts/teams-api-publish.log` |
| Web publish and generated scoped stylesheet | Passed | `artifacts/teams-web-publish.log` |
| Browser CSP and direct Blazor WebSocket | Passed on published web; Teams/cloud origins accepted and unrelated origin denied | `artifacts/teams-browser/result.json` |
| Component desktop/narrow visual checks | Passed at 1000px and 360px; no horizontal overflow; final mobile actions stacked | `artifacts/teams-ui/` |

API compilation retains existing unrelated nullability warnings. No claims are made about the unexecuted full repository test matrix.

## Flow packet

### FLOW-01 — Select the meeting presenter

Preconditions: same-company active organizer, prepared meeting, active Marketing assistant with an available presentation tool.

Steps: load choices, select Maya, save, verify company header and expected meeting version, refresh the parent preparation state, resolve runtime identity and tools.

Observed: binding/version persisted; shared communication profile and role brief used; unavailable/approval-required tools excluded. Missing, foreign-company, paused, unsupported, unauthorized organizer, stale-version, and active-call selection cases reject before a provider side effect.

Evidence: `TeamsDeploymentPrerequisiteTests`, `TeamsDeploymentControlsTests`, `artifacts/teams-ui/presenter-1000.png`, `artifacts/teams-ui/presenter-360.png`.

Result: pass using real services/components with isolated database/transport substitutes. No live provider invocation.

### FLOW-02 — Authorize/revoke the first isolated test

Steps: platform administrator enters exact organizer/meeting, bounded duration and reason; submit current registration version; evaluate the scoped call; revoke or advance the test clock beyond expiry.

Observed: precise scope and safe audit events; ordinary company-wide/production UAT gate unchanged. Package, automated evidence, allowlists, consent policy, budget, capacity, demo denial, pilot disable and emergency denial remain effective. Active calls retain their grant provenance and cannot be revived by later release approval.

Evidence: API denial matrix, web submission/revocation tests, `artifacts/teams-ui/first-test-1000.png`, `artifacts/teams-ui/first-test-360.png`.

Result: pass with fixture roles/data. Screenshots are rendered component snapshots; browser click behavior is covered by bUnit, not claimed as a tenant-installed session.

### FLOW-03 — Embed the Teams stage

Launch: published `VirtualCompany.Web.dll --urls https://localhost:5298`.

Steps: open the actual stage fallback without credentials, verify effective headers, observe a real local Blazor WebSocket, frame unchanged server responses under controlled Teams and Microsoft cloud origins, then an unrelated origin.

Observed: HTTP 200; bounded frame-ancestors CSP; no X-Frame-Options conflict; direct WebSocket receives frames; Teams origins accepted; unrelated origin blocked.

The browser harness proxies unmodified server responses to a public-shaped test origin to avoid localhost network restrictions. Framing runs without JavaScript to isolate CSP; the direct-page phase separately checks the real circuit. All other external requests are blocked. This does not verify Teams SSO, third-party cookie behavior, installed app policy, meeting roles, or Graph media.

Evidence: `tests/scripts/Verify-TeamsFraming.cjs`, `artifacts/teams-browser/`.

Result: pass within the stated local boundary.

### FLOW-04 — Upgrade without interrupting a live call

Steps: request upgrade before drain; simulate drained versus active service; mute an active durable call; later record provider termination.

Observed: SCM stop is denied before drain completion; repeat registration configures the existing service; native failures propagate; muting retains instance protection; protection is released after durable termination. Readiness separately checks local reservations and provider-confirmed durable state.

Result: pass with deterministic SCM/provider boundaries. Real Windows service startup, reboot, certificate/private-key access, scheduled maintenance and recovery still require the approved Windows environment.

## Issue ledger

| ID | Severity | Finding | Resolution / regression | Status |
| --- | --- | --- | --- | --- |
| TD-01 | P1 | Antiforgery emitted SAMEORIGIN alongside Teams CSP | Suppress only that header; real published response and browser checks | Fixed |
| TD-02 | P1 | New-column backfill failed SQL Server batch compilation | Deferred backfill SQL; actual upgrade/reapplication test | Fixed |
| TD-03 | P1 | Media reconciliation could restart a denied call | Skip denied call IDs; scoped policy remains enforced | Fixed |
| TD-04 | P1 | Muting could release protection while Graph call remained active | Retain reservation through terminal provider state; service regression | Fixed |
| TD-05 | P2 | New tests omitted by explicit web compile list | Added to owning project; all 24 focused web tests execute | Fixed |
| TD-06 | P2 | Narrow admin buttons wrapped excessively | Stack actions at narrow widths; final visual capture | Fixed |
| TD-07 | P2 | Source-root Production harness lacked generated CSS | Validate published web artifact and stylesheet; no unsupported source-hosting claim | Resolved harness limitation |
| TD-08 | P1 | Clean host must trust reviewed bootstrap publisher | Retain AllSigned; require organization-approved image/policy prerequisite | External prerequisite |
| TD-09 | P1 | Live speech/stage acceptance requires Azure and isolated M365 tenant | Execute the existing live-UAT matrix after deployment | Not run |

Automatic approval review rejected a proposed automated LocalMachine TrustedPublisher update because it changes host code-trust boundaries across instances. That proposal was not applied. The implementation retains AllSigned, now noninteractive, and does not bypass code trust.

## Remaining deployment evidence

Follow the updated runtime and rollout runbooks. Verify approved publisher trust, signed artifact validation, dedicated TLS certificate, shared durable key/document storage and computer identity access, SQL/Redis/provider configuration, UAMI access, clean Windows service install/reinstall/reboot/recovery, VMSS instance protection, public callback and media ports, DNS, package installation/SSO, and actual marketing speech plus slide acknowledgement inside Teams. Record evidence before approving LiveUatApproved or ProductionEnabled.

The SQL test uses representative prior tables and actual migration SQL. The organization's full migration-chain/backup rehearsal remains a deployment gate. The web public home route was not used as acceptance evidence because the local harness lacked its backend configuration; the scoped Teams stage was verified directly.
