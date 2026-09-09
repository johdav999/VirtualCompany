# Teams media Azure operations runbook

This runbook operates the optional application-hosted audio route. The Teams-hosted shared stage and typed meeting controls remain the safe fallback throughout every procedure.

## Prerequisites

- Explicit architecture, product-owner, tenant-administrator, security, privacy, legal, and operations approval.
- An isolated, non-demo Microsoft 365 development tenant. Demo tenants deny external side effects.
- Azure subscription, delegated DNS zone, Key Vault with RBAC, a code-signing pipeline, and an Azure Monitor operations mailbox.
- A publicly trusted TLS certificate whose subject/SAN covers the configured service FQDN. Store the base64 PFX as the Key Vault secret named by `mediaCertificateSecretName`; do not store it in a repository, artifact log, Bicep parameter file, or VM image.
- A JSON Key Vault secret named by `teamsConfigurationSecretName`. It contains non-secret flags and IDs: `Enabled`, `CallControlEnabled`, `AudioEnabled`, `SharedStageEnabled`, `MediaRouteApproved`, `MediaHostPublicReachabilityApproved`, `MediaRoute`, `BotApplicationId`, `TeamsAppId`, `WebApplicationId`, and `AllowedTenantId`. Keep `MediaHostPublicReachabilityApproved` false until the external checks below pass.
- Existing SQL Server and Redis connection strings stored using the repository's Key Vault configuration naming convention. Network paths must be private/VNet-scoped; neither service receives a public inbound rule from this template.

## Build and review

1. Run `scripts/Test-TeamsMediaSdkFreshness.ps1`. It compares the project package with `infra/teams-media/media-sdk-lock.json` and fails when the reviewed release exceeds Microsoft's three-month support window.
2. Publish `VirtualCompany.Api` self-contained for `win-x64` (exact commands below), create `VirtualCompany.Api.zip`, calculate its SHA-256, run dependency and malware scans, and sign the deployment artifacts.
3. Code-sign `Install-TeamsMediaHost.ps1`. The VM extension deliberately uses PowerShell `AllSigned`; an unsigned bootstrap is rejected.
4. Upload both artifacts to an approved private artifact store and generate short-lived read URLs.
5. Copy `infra/teams-media/parameters.example.json` outside the repository. Replace placeholders. Do not add `adminPassword` or `apiPackageUri` to the checked-in file.
6. Set `VC_TEAMS_MEDIA_ADMIN_PASSWORD` and `VC_TEAMS_MEDIA_PACKAGE_URI` only in the protected deployment-agent environment.
7. Run `scripts/Deploy-TeamsMediaHost.ps1 -Location <region> -ParameterFile <secure-file> -Preview` and review every resource and RBAC change.
8. Remove `-Preview` only after the change record is approved. Delegate the Azure DNS name servers at the external registrar through its audited automation/API.

The deployment creates a Windows Server 2022 VMSS, Standard load balancer, public callback address, per-instance public IPv4 addresses, DNS record, NSG, user-assigned managed identity, Key Vault access, VMSS protection permission, autoscale bounds, Log Analytics, Application Insights, workbook, alerts, and subscription budget.

## Post-deployment validation

Do not enable a production tenant from template output alone.

1. Confirm `/health/media-host/live` returns HTTP 200 on every instance through Azure Run Command or the private operator path.
2. From an external approved probe, validate the callback FQDN/TLS endpoint and every instance public media address/port. Record the Azure resource IDs and timestamp without recording tokens. Set `MediaHostPublicReachabilityApproved=true` in the Key Vault configuration only after this evidence passes, then restart through the deployment pipeline.
3. Confirm `/health/media-host/ready` returns HTTP 200. A 503 response includes only a stable reason code; resolve it before any live call.
4. Query the protected `GET /api/platform/teams-media-host` endpoint and record OS, SDK, certificate, public endpoint, capacity, and instance-protection checks in the change ticket.
5. Verify an authenticated Bot Framework callback using the development tenant.
6. Confirm SQL and Redis are reachable privately and are not exposed by public endpoints.
7. Start one consented development call. Confirm the durable `MediaHostInstanceId` matches the host status and VMSS protection becomes enabled.
8. Scale an unrelated empty instance in and out. Confirm the active call continues and no raw audio appears in SQL, Redis, logs, traces, or support exports.
9. Exercise the configured per-host ceiling. The next call must fail with `teams_media_host.capacity_reached`, and typed/stage controls must continue.
10. Run the bounded concurrent-call test at the approved audio-only capacity. Record CPU, network, setup latency, provider throttling, frame jitter/drop count, and realtime latency; lower the ceiling if any threshold is breached.
11. Complete a controlled drain and rollback before enabling a broader pilot.

## Graceful deployment and scale-in

1. Call `POST /api/platform/teams-media-host/drain` as a platform administrator, supplying a 1–120 minute deadline and a safe reason.
2. The readiness probe immediately returns 503, so the load balancer and new-call admission stop selecting that instance.
3. Existing calls stay on the owner instance. VMSS protection prevents ordinary scale-in and scale-set actions while active calls remain.
4. Wait for active calls to reach zero. At the deadline, the runtime creates durable, idempotent forced-termination requests for remaining calls.
5. Verify each provider call reaches a terminal state. Then apply the VMSS model to that instance or replace it.
6. If a VM disappears before draining completes, do not claim recovery. Reconcile the durable call and ask the organizer to invite Alex again.

Azure Scheduled Events for `Preempt`, `Terminate`, `Reboot`, `Redeploy`, and `Freeze` automatically start the same drain path. Notice may be shorter than a normal meeting; the shutdown handler requests termination but cannot guarantee a graceful provider acknowledgement.

## Certificate rotation

1. Import a new publicly trusted PFX version into the same Key Vault secret. Retain the prior enabled version during validation.
2. Publish a new signed deployment artifact or trigger an instance-by-instance bootstrap rerun.
3. Drain one instance, import the new certificate into `LocalMachine/My`, restart the service, and verify readiness reports the new thumbprint indirectly through certificate health without returning it.
4. Complete a development call through that instance, then rotate the remaining instances one at a time.
5. Disable the prior Key Vault version only after every instance and callback endpoint is verified. Business records and audit history are never deleted during rotation.
6. Roll back by re-enabling the previous Key Vault version and redeploying the last known-good artifact.

## Emergency disable

Set `TeamsPresenter:AudioEnabled=false` and, when call creation must also stop, `TeamsPresenter:CallControlEnabled=false` in Key Vault configuration. Restart or refresh the instances through the approved deployment pipeline. New calls fail closed after the updated configuration is loaded on each host. Drain active calls when possible; use the forced termination endpoint for an urgent security incident. Do not roll back the database schema.

## Incident matrix

| Incident | Safe behavior | Operator action |
| --- | --- | --- |
| Region outage or owner VM loss | The pinned raw-media call terminates; stage and typed controls remain available elsewhere. | Reconcile Graph state, fail over normal API/Web, deploy media capacity in the approved recovery region, and have the organizer re-invite Alex. |
| SQL outage | New joins and authorization stop; active in-memory audio is terminated when its authorization cannot be re-proved. | Restore SQL connectivity, reconcile ambiguous calls, and do not replay a join without organizer intent. |
| Redis outage | Database-backed durable state remains authoritative; coordination degrades. | Disable new media admission if coordination guarantees cannot be met; restore Redis and inspect duplicate/retry telemetry. |
| Microsoft Graph/Teams outage or throttling | Joins remain pending/reconciliation-required; no fake success. | Respect bounded retry guidance, monitor provider latency/throttling, and use stage/typed fallback. |
| Certificate missing/expired | Readiness returns 503 and admission stops. | Rotate through Key Vault. Never bypass validation or install an untrusted certificate. |
| SDK emergency deprecation | Freshness script and readiness block deployment/admission. | Review the newest Microsoft-owned NuGet release, update the lock/project/runtime constant together, run all media tests, and redeploy drained instances. |
| Tenant disabled/revoked | New joins stop and active-call policy requests termination. | Confirm tenant registration is disabled, reconcile calls, retain audit evidence, and require fresh admin approval before re-enable. |
| CPU/network ceiling | Admission stops at configured call capacity; alerts fire. | Drain overloaded nodes, scale within the approved bound, and lower per-host capacity until a measured test approves a higher limit. |

## Monitoring and alerts

Use the `Alex Teams media operations` workbook. Alerts cover VM CPU, network ingress, application/runtime failures, dropped frames, provider/media latency, and the monthly forecast/actual budget. The runtime emits active calls, admission rejections, drain events, setup latency, frame jitter/drop count, Graph request latency, reconciliation, and failures. Certificate validation failures are logged only as stable reason codes and alert through the application failure query. No metric dimension contains company, meeting, participant, transcript, access token, SDP, or media content.

## Rollback

Drain instances, deploy the last signed package SHA, and verify the SDK is still inside the support window. If it is not, disable raw media and keep the shared-stage/typed workflow instead of rolling back to an unsupported library. IaC rollback must not remove Key Vault history, SQL data, audit events, or monitoring evidence.

## Evidence status

Repository validation can prove compilation, static policy, SDK freshness, and Bicep syntax. Azure subscription deployment, DNS delegation, public callback reachability, certificate rotation, instance protection, scale tests, and live Teams calls require external credentials and are not production-verified until their evidence is attached to the Prompt 7 acceptance record.

## Deployment prerequisites implemented on 2026-09-08

### Reproducible publication and Windows service

Run from the repository root with the installed .NET 9 SDK:

```powershell
dotnet publish src/VirtualCompany.Api/VirtualCompany.Api.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o artifacts/teams-api-win-x64
Compress-Archive -Path artifacts/teams-api-win-x64/* -DestinationPath artifacts/VirtualCompany.Api.zip -Force
Get-FileHash artifacts/VirtualCompany.Api.zip -Algorithm SHA256
pwsh -NoProfile -File scripts/Test-TeamsMediaSdkFreshness.ps1
```

The ZIP must contain the publish output at its root, including `VirtualCompany.Api.exe`, `VirtualCompany.Api.dll`, `coreclr.dll`, `hostfxr.dll`, and the media SDK's native assets. Keep trimming and single-file publication disabled. The service launches the executable directly; a separately installed `dotnet` runtime is unnecessary. Publishing successfully does not establish native media compatibility: record an actual SDK initialization, media session, and reboot test on Windows Server 2022.

Sign the bootstrap through the approved code-signing pipeline. Its publisher and certification chain must already be trusted by the organization's Windows image/policy before the Custom Script Extension executes it with `AllSigned`. The template does not modify Windows publisher trust. A stock image without that prerequisite must not be treated as ready; arrange approved image/policy provisioning before running the extension. Unsigned or untrusted scripts must fail rather than prompting indefinitely or bypassing policy.

The bootstrap uses the real ASP.NET Core Windows service lifetime, automatic SCM startup, and bounded restart recovery under LocalSystem. It checks native exit codes and waits for both SCM Running and signaling liveness. Install/reinstall uses one service name, `VirtualCompanyTeamsMedia`, and a staged release directory under `C:\VirtualCompany\releases`. A failed staged package leaves the old service running. Shared keys/documents are never stored inside a release directory. LocalSystem reads the LocalMachine private key and deployment files; the host directory is restricted to SYSTEM and administrators.

For a previously prepared, approved Windows host, the extension-equivalent installation command is:

```powershell
.\Install-TeamsMediaHost.ps1 -ApiPackagePath .\VirtualCompany.Api.zip -ApiPackageSha256 <reviewed-sha256> -KeyVaultName <vault-name> -CertificateSecretName teams-media-tls-pfx -ConfigurationSecretName teams-media-host-configuration -ServiceFqdn <callback-fqdn> -MediaInternalPort 8445 -MediaPublicPort 8445 -ManagedIdentityClientId <infrastructure-uami-client-id>
```

The infrastructure UAMI must have the required Key Vault and VMSS permissions. It is distinct from the bot Entra application identity and its Graph credentials. IMDS token requests, Azure configuration credentials, platform secret-store credentials, and VMSS protection explicitly select the infrastructure identity.

### Configuration and durable storage

Add `KeyRingPath` and `ObjectStorageRootPath` to the existing `teams-media-host-configuration` JSON secret. Both must be durable shared UNC paths, for example `\\approved-file-server\virtualcompany\keys` and `\\approved-file-server\virtualcompany\documents`. Provision network access and the VM computer identity's filesystem rights through the approved domain/storage setup. The VMSS template does not create or domain-join that storage infrastructure. Do not substitute the replaceable OS disk or embed storage credentials.

The bootstrap requires an exportable base64 PFX with a private key, a trusted chain, more than seven days of remaining validity, and its primary DNS name exactly equal to ServiceFqdn. Use a dedicated certificate for this name; the installer deliberately does not infer wildcard/SAN equivalence.

Configuration order is normal ASP.NET Core defaults (including environment variables), then per-instance `appsettings.MediaHost.json`, then individual Azure Key Vault configuration secrets. Key Vault uses `--` for `:`, for example `TeamsPresenter--FirstUatEnabled`. Keep host-specific identity, IP, certificate, ports, and VMSS instance fields out of common Key Vault settings so they cannot overwrite IMDS-derived values. The generated host JSON contains no tokens or private keys. Global rollout flags, budget, pilot allowlists, package/evidence flags, SQL Server/Redis connections, and provider credential references belong to their existing configuration owners. The bootstrap JSON secret copies its documented host flags/IDs; it does not interpret arbitrary nested application settings.

Settings are loaded at process startup. Changes to runtime feature flags require the documented drain/restart sequence; do not claim that editing Key Vault instantly changes every running process. Grant expiry/revocation is database-backed and rechecked during call/media operations.

### Simulation, Azure preview, and deployment

Use PowerShell 7 for the deployment wrapper. These modes are different:

```powershell
# Local simulation: no Azure CLI call, secrets file, or deployment.
pwsh -NoProfile -File scripts/Deploy-TeamsMediaHost.ps1 -Location swedencentral -ParameterFile <secure-parameters.json> -WhatIf

# Executes Azure's read-only deployment what-if for review.
pwsh -NoProfile -File scripts/Deploy-TeamsMediaHost.ps1 -Location swedencentral -ParameterFile <secure-parameters.json> -Preview

# Executes the approved deployment.
pwsh -NoProfile -File scripts/Deploy-TeamsMediaHost.ps1 -Location swedencentral -ParameterFile <secure-parameters.json>
```

Set the documented password and package-URI environment variables only in the protected deployment agent. Preview requires valid artifact inputs too. The wrapper checks tool/freshness/compiler/CLI failures and restricts temporary files to the current deployment identity (and SYSTEM on Windows); it removes them on success and failure. CLI failure bodies are suppressed because they may echo signed URLs. Use the Azure deployment operation record for diagnostics.

### Bootstrap, callback availability, and drain sequence

1. Keep ordinary pilot/production admission and `MediaHostPublicReachabilityApproved` disabled while bootstrapping.
2. Verify local `http://127.0.0.1:8080/health/media-host/live`. The operator listener is loopback-only and is not an NSG/LB port.
3. The load balancer probes HTTPS 8443 `/health/media-host/live`, forwarding public HTTPS 443 to 8443. It remains available during bootstrap and drain so authenticated signaling and termination callbacks can arrive.
4. Verify public callback TLS with the real hostname and certificate chain. Independently verify each instance public media IPv4 and TCP/UDP 8445 with the approved media probe. An HTTPS liveness response does not prove UDP or native media readiness.
5. Record evidence, then set reachability approval and restart through the controlled sequence. `/health/media-host/ready` remains 503 until all actual admission prerequisites pass. Liveness never authorizes a call.
6. Confirm protected runtime status shows the numeric Uniform VMSS instance ID from IMDS `compute.resourceId`. `compute.vmId` remains a separate VM GUID used in the host identity. Missing or inconsistent metadata fails installation.
7. Before an upgrade or rollback, call the authenticated platform drain endpoint for the specific owner host. Wait for loopback `/health/media-host/upgrade` to return 200: it requires drain mode, zero local call reservations, and no nonterminal durable call owned by that host. A load-balanced request may hit a different instance; use the approved per-instance operator route.
8. Install the staged, reviewed package. The installer refuses to stop a running service until that upgrade check succeeds. Verify Running, liveness, readiness, certificate, instance protection, shared key access, and persisted slide assets after restart and after a controlled reboot.

Keep automatic scale-in/repair maintenance aware of the existing instance-protection and scheduled-events flow. Never treat an emptied in-memory audio list as proof that Graph terminated a call.

### Database rollout

Apply the active SQL Server migrations through the normal migration pipeline before enabling these application changes:

- `AddTeamsDeploymentPrerequisites`: nullable meeting/call presenter binding plus audited first-test scope/expiry fields.
- `AddTeamsFirstTestCallBinding`: durable provenance linking a call to its first-test authorization.

The backfill binds existing meetings to the same first active Sales Alex (SQL ordering by agent ID) selected by the old implementation, then copies the binding into existing calls. It grants no tools or UAT approval. Companies without a compatible Alex remain unselected. New meetings always need explicit selection. Keep the schema during application rollback; do not drop operational evidence to roll back an executable.

The focused SQL test executes the actual idempotent upgrade script twice against representative prior tables in a fresh disposable SQL Server database. The normal full migration pipeline and a backup/restore rehearsal remain deployment requirements.
