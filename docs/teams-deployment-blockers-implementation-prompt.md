# Implement the Teams deployment prerequisites

## 1. Title and outcome

Make Virtual Company deployable to Azure for an isolated first Teams test in which an explicitly selected marketing assistant can speak and present an existing deck. Repair the existing Windows API/media-host deployment, Teams embedding, and first-UAT admission path. Preserve the existing Alex Sales experience.

Implement the changes, focused tests, and operator documentation. Do not stop after analysis or a plan. Do not provision Azure resources, grant tenant permissions, install a Teams package into a tenant, or join a real meeting as part of this implementation request.

## 2. Current context

Read `/AGENTS.md`, `/src/AGENTS.md`, all applicable nearer instructions, `/tests/AGENTS.md`, and `/docs/AGENTS.md`. Follow `/production-implementation.md` and `/docs/architecture-rules.md`. Inspect the working tree first and preserve unrelated modified and untracked files.

The current implementation has:

- A .NET 9 ASP.NET Core API and a server-side Blazor web project. Inspect `/src/VirtualCompany.Api/Program.cs`, its project file, and `/src/VirtualCompany.Web/Program.cs`; older README descriptions of WebAssembly are not authoritative.
- An existing application-hosted Teams media route in `/src/VirtualCompany.Infrastructure.Sales/Sales/`, using the same API executable on Windows Server Azure Uniform VMSS. `/infra/teams-media/main.bicep` and `/infra/teams-media/subscription.bicep` supply infrastructure.
- `/scripts/Install-TeamsMediaHost.ps1`, which currently registers a PowerShell process with `sc.exe`, invokes `dotnet` without installing the runtime, and uses IMDS `compute.vmId` as `VmssInstanceId`.
- `/scripts/Deploy-TeamsMediaHost.ps1`, which declares `WhatIf` explicitly alongside `SupportsShouldProcess` and mixes Azure preview execution with PowerShell simulation semantics.
- `/src/VirtualCompany.Infrastructure.Sales/Sales/TeamsMeetingMediaCoordinator.cs`, which selects an active agent using `TemplateId == "alex"` and `Department == "Sales"`, then supplies Sales meeting instructions and tools.
- `/src/VirtualCompany.Infrastructure.Sales/Sales/TeamsPresenterRolloutPolicy.cs`, which requires `LiveUatApproved` for pilot calls, preventing the initial live UAT needed to obtain that evidence.
- Default Blazor Interactive Server embedding policy, without an explicit Teams frame-ancestor policy in web startup.
- A load balancer that probes `/health/media-host/ready` before forwarding public API traffic, while public-reachability approval begins false.
- An example parameter file whose `namePrefix` is `vc`, shorter than the template's minimum length of three.

Revalidate these findings against current code before editing. Read the existing Teams identity, stage, Azure runtime, production rollout, and live-UAT runbooks under `/docs`. Reuse the existing readiness contracts, organizer controls, tenant registration, media runtime, presentation services, and shared realtime agent gateway.

## 3. Dependencies

No earlier implementation prompt is required. Existing Teams code and migrations in this working tree are prerequisites and must be preserved.

Local verification needs the repository SDK/toolchain, PowerShell, and Bicep tooling. Real service lifecycle verification needs a suitable disposable Windows environment. Live verification later needs an authorized Azure subscription, DNS, certificates, Key Vault, database, Redis, AI-provider access, and an isolated Microsoft 365 tenant. Missing external credentials must not stop repository-local implementation or be replaced with invented evidence.

## 4. Implementation requirements

### A. Use a real Windows service and reproducible runtime provisioning

Choose and implement a supported service lifetime for the existing API executable, preferably ASP.NET Core Windows-service integration. Do not create another business service or register a plain PowerShell console process as an SCM service.

- Implement service startup, configuration, working directory, shutdown, and recovery behavior. Preserve normal local console hosting.
- Choose a coherent framework-dependent or self-contained publication strategy. If framework-dependent, install and verify the matching supported ASP.NET Core runtime before service start. If self-contained, launch the published executable and remove the external `dotnet` assumption.
- Verify current Microsoft media SDK/runtime compatibility; do not assume compilation proves native media compatibility or upgrade the framework indiscriminately.
- Preserve hash verification, artifact signing/trust, Key Vault secret handling, and TLS certificate validation. Supply the service identity with the required certificate private-key, file, and secret-store access.
- Make initial installation and repeat installation idempotent. Check native command exit codes and actual service readiness; fail clearly when installation/startup fails. Do not report success after a failed `sc.exe` call.
- Preserve persistent keys and documents across releases. Drain active calls before upgrades/restarts and integrate with the existing runtime termination behavior.
- Verify user-assigned managed identity selection for both bootstrap HTTP calls and application credential providers. Do not confuse the infrastructure identity with the bot's application identity.

### B. Make preview and deployment behavior unambiguous

- Remove the duplicate common parameter and define explicit Azure preview behavior, such as a separate `-Preview` switch.
- PowerShell `-WhatIf` must cause no Azure mutation. Azure preview must actually execute `az deployment sub what-if`; normal execution must respect `ShouldProcess` before deployment.
- Handle missing tools, invalid parameters, SDK freshness failure, Bicep compilation failure, and Azure CLI failure with reliable nonzero exits.
- Clean up temporary sensitive files on success and failure without exposing secrets or signed URLs in output. Do not weaken artifact validation to make previews pass.
- Correct the invalid example prefix and update all documented command examples to the implemented semantics.

### C. Resolve VMSS identity and bootstrap readiness correctly

- Resolve the Uniform VMSS instance identifier using supported Azure metadata/API fields. Keep the VM GUID distinct from the scale-set instance identifier and stable application host identity.
- Verify that instance-protection and drain operations address the correct VMSS VM resource. Fail closed on unavailable or inconsistent metadata.
- Remove the public-reachability/readiness circular dependency. Provide a bounded bootstrap/diagnostic path that allows actual TLS and endpoint verification without authorizing calls or weakening media admission.
- Preserve existing-call signaling during drain where necessary. Do not make media unready mean that required termination callbacks can never reach the application.
- Validate the applicable NSG, probe, certificate, and port configuration for that solution. Document the operator sequence from first boot through verified media readiness.

### D. Allow Teams embedding without opening unrestricted framing

- Configure the effective Blazor frame-ancestor policy for supported Teams hosts, including the current Microsoft cloud host domains from official documentation.
- Resolve conflicting CSP or X-Frame-Options headers rather than appending an ineffective permissive header. Do not use a wildcard-all embedding policy or disable security headers globally.
- Preserve normal browser operation, Teams initialization/SSO, and authenticated side-panel versus capability-protected stage behavior.
- Verify WebSocket connectivity and document App Service WebSockets/session-affinity requirements. Inspect browser-to-API requests and add an exact configured origin policy only where required; server-to-server requests do not require CORS.

### E. Select the marketing presenter explicitly and safely

- Replace hard-coded Alex selection with an explicit, company-owned presenter binding for the meeting, reusing any existing authoritative agent relationship where possible.
- Wire selection through the existing meeting preparation/organizer flow and API authorization; do not select the first marketing agent or accept an arbitrary client-supplied agent ID as authority.
- Resolve the selected agent's communication profile, permitted tools, and relevant instructions through existing shared agent services. Removing the Alex filter while retaining Alex-only instructions is insufficient.
- Require an active, permitted agent in the same company. Reject cross-company, missing, inactive, unauthorized, or unsupported presenters before any provider side effect.
- Keep active calls bound to their authorized agent. Do not silently swap the speaker mid-call. Preserve compatible existing Alex meetings through an explicit migration/backfill or documented deterministic compatibility rule.
- Keep shared-stage presentation in the existing deck workflow. Do not add bot video, desktop capture, a new marketing workspace, or unrelated marketing automation.
- If persistence changes are necessary, add migrations, snapshot changes, and upgrade verification under the `Database and EF Core` rules in `/docs/architecture-rules.md`; otherwise document why no migration is required.

### F. Add a controlled initial-UAT admission path

- Separate permission to conduct the first isolated UAT from approval of completed live-UAT evidence. Keep `LiveUatApproved` meaningful for ordinary pilot/release admission.
- Add an explicit, disabled-by-default, time-bounded authorization scoped to the test tenant, non-demo company, organizer, and bounded call capacity. Prefer existing policy/administration patterns rather than a generic bypass flag.
- Require platform-administrator authority to authorize/revoke the test. Record actor, scope, expiry, reason, and safe audit evidence. Expose the distinct test state and actionable denial reasons in existing readiness/administrator surfaces.
- Retain tenant consent, exact Graph permissions, package compatibility, automated evidence, organizer authorization, meeting consent, approved media route, certificate/reachability health, budget/capacity, and emergency-disable checks.
- Expiry, revocation, consent removal, and emergency disable must reject new operations and stop affected active test media through existing termination workflows.
- Normal pilot and production calls must still require completed live UAT. Never automatically set `LiveUatApproved=true`, infer approval from Development environment, or admit synthetic demo companies.

### G. Update deployment and operator documentation

Update existing runbooks and examples to match the final implementation. Include prerequisites, exact publish/install/preview commands, service account/runtime requirements, configuration precedence, presenter selection, initial-UAT authorization and expiry, readiness sequence, and rollback/drain procedures. Keep tenant-specific identifiers and secrets out of source.

## 5. Constraints and preservation rules

Follow the canonical architecture rules, particularly `Agent and AI Orchestration`, `Multi-Tenancy and Authorization`, `Workflow and Approval`, `External Side Effects and Outbox`, and `Audit and Observability`.

Teams joins/leaves, media start/stop, and presentation changes are external or meeting-visible effects. Preserve their authorization, approval, consent, idempotency, durable command/outbox, and provider-confirmation boundaries.

For presenter-selection and initial-UAT UI changes, read `/docs/design.md` and `/src/VirtualCompany.Web/AGENTS.md`. The mandatory design workflow applies to changed UI; `/ui-instructions.md` is only a companion. Use the required `$polish-uat-loop` skill for real-flow UAT. Do not redesign unrelated screens.

Keep audio hosted on supported Azure Windows infrastructure and slides in the existing Blazor shared stage. Preserve typed controls, manual takeover, stage render acknowledgement before narration, Sales compatibility, and default-disabled production gates. Do not log raw audio, tokens, private keys, callback bodies, or secret-bearing URLs.

## 6. Acceptance criteria

1. Given a clean supported Windows host, the reviewed package installs as a real service, becomes ready, survives reboot, and reports actionable errors on missing runtime/certificate/configuration. Repeat installation does not duplicate services or destroy persisted state.
2. Given deployment preview inputs, Azure preview runs without deployment; `-WhatIf` cannot create resources. Failures propagate and temporary sensitive artifacts are cleaned up.
3. Given different VM GUID and Uniform VMSS instance ID values, protection/drain targets the correct scale-set VM. Missing identity blocks media admission.
4. Given initial reachability approval is false, operators can verify public TLS/media prerequisites through the documented path, while joins remain denied until all admission conditions pass.
5. Given Teams web/desktop embedding, the side panel and stage load without frame-policy violations and maintain a working Blazor connection. An unrelated origin cannot frame the app.
6. Given an authorized marketing presenter selection, the meeting binds that agent and uses its profile and permitted tools. Cross-company/inactive selection is rejected with no provider call. Existing Alex meetings remain functional.
7. Given no completed live UAT but valid scoped first-UAT authorization, only the authorized test operation can proceed while every other admission gate remains enforced. Expired/revoked grants and unrelated tenants/users/companies are denied.
8. Given an ordinary pilot/production operation without completed live UAT, it remains denied. Creating or completing a test does not manufacture release approval.
9. Given a restart/drain or emergency stop, active-call cleanup uses existing durable workflows and no new media starts silently.

## 7. Verification

- Follow the repository test architecture and use the owning test projects. Extend existing Teams media-host, rollout-policy, tenant-registration, readiness, package, and web meeting-surface tests rather than creating a parallel harness without need.
- Add behavioral coverage for SCM bootstrap failure/idempotency, native exit handling, preview versus mutation, VM identity mapping, readiness bootstrap, presenter authorization/profile selection, and the full scoped-UAT denial matrix.
- Use deterministic Azure/Graph/SCM boundaries in tests only. Do not claim mocked lifecycle tests prove a real service installation or actual Teams audio.
- Verify effective response headers and framing behavior in a real browser/local safe harness; distinguish that evidence from a live Teams installation.
- Run migration upgrade tests if schema changes, focused tests first, then one broader API/Web build and appropriate publish validation. Compile Bicep and validate PowerShell parameter binding/control flow. Re-run checks only when changed code or failures justify it.
- If Windows lifecycle or external Teams/Azure verification is unavailable, complete every unaffected check and supply exact commands and an evidence checklist for remaining verification. Do not provision resources just to close the checklist.

## 8. Definition of done

Deliver implemented production code, required migrations, focused regression coverage, corrected scripts/templates, and updated operator documentation. No scaffolding, mock production data, silent failures, deferred in-scope TODOs, or fake readiness/UAT approval.

Finish with a concise report of changed behavior, files, executed verification/results, configuration or migration changes, and remaining environment-dependent checks. Explicitly distinguish repository readiness from verified Windows service operation and verified live Teams speech/presentation. Do not claim the Azure test succeeded without real evidence.
