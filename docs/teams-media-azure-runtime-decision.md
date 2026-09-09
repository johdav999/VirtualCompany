# Teams application-hosted media Azure runtime decision

Status: implemented, production rollout prohibited until Prompt 7 approval and live-tenant evidence.

Decision date: 2026-09-04.

## Decision

When `TeamsPresenter:MediaRoute` is `teams_application_hosted`, deploy the existing Virtual Company API and Sales media module to a Windows Server 2022 Azure Virtual Machine Scale Set in Uniform orchestration mode. Each instance has its own public IPv4 address for the Microsoft real-time media flow. Signaling and authenticated callbacks enter through a Standard public load balancer over TLS. Active calls are pinned in `TeamsMeetingCall.MediaHostInstanceId` to the instance that prepared the media socket.

The API remains a .NET modular monolith. There is no second business service, persistence owner, orchestration stack, or non-.NET media implementation. The normal API/Web deployment remains supported with Teams media disabled.

This topology follows Microsoft's current requirements: application-hosted media uses C#/.NET on Windows Server in Azure, cannot run on Azure Web App, needs direct internet access and an instance public port, and keeps media on the VM that created the call. The media package must be newest or no more than three months old. See [Microsoft's application-hosted media requirements](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/calls-and-meetings/requirements-considerations-application-hosted-media-bots).

## Why VM Scale Sets

- The current solution already contains the call-control, durable affinity, consent, presentation, and media adapters. Hosting that same executable avoids duplicating domain rules.
- Uniform VMSS supplies deterministic instance identity, per-instance public IP configuration, bounded autoscaling, health probes, rolling infrastructure primitives, and scale-in protection.
- Application-hosted media is stateful per call. The runtime protects an instance from scale-in while it owns a call and removes protection after the last call ends.
- The API's anonymous media-host probe contains only `ready` or a stable reason code. Tenant data, meeting identities, participant data, and media never appear in probes or Azure resource metadata.

## Rejected alternatives

| Alternative | Decision | Reason |
| --- | --- | --- |
| Azure App Service | Rejected | Microsoft explicitly disallows Azure Web App for application-hosted real-time media. |
| Linux containers | Rejected | The supported Microsoft media SDK requires Windows Server and C#/.NET. |
| AKS Windows nodes | Deferred | Supported in principle, but adds pod/node pinning, public per-pod media addressing, drain coordination, and certificate distribution without a demonstrated operational benefit for the initial bounded capacity. |
| Separate media microservice | Rejected for this phase | It would create another deployment boundary and increase the risk of duplicating call policy or persistence ownership. Reconsider only with explicit architecture approval and shared Application contracts. |
| Raw media through SQL, Redis, or a generic bus | Rejected | Media must remain in memory on the owner instance. SQL stores only durable control state; Redis may coordinate locks but never transports raw media. |
| Shared stage only | Preserved fallback | This remains the primary visual route and works when raw audio/media is unavailable. |
| Certified meeting-media provider | Supported alternative gate | Use this if Microsoft, security, privacy, legal, or tenant administrators do not approve application-hosted media. |

## Affinity and failure semantics

The durable join starts as `pending`. The outbox worker that passes media-host readiness reserves capacity, turns on Azure VMSS instance protection, pins the call, prepares the media socket, and submits the Graph join. Provider callbacks can be received by any healthy signaling instance; the owner-instance reconciliation loop observes durable state and starts or stops only its own in-memory bridge.

There is no transparent media failover. If the owner VM is lost, the media session ends. Reconciliation records the provider outcome, and the organizer must explicitly invite Alex again. Moving raw media to another node is neither attempted nor claimed.

## Deployment safety gates

The runtime refuses admission unless all of these are true:

- the feature, call control, audio route, route approval, deployment approval, and tenant policy gates are enabled;
- the process is 64-bit Windows Server and the topology is `azure_vmss_windows`;
- the repository-pinned media SDK is within the 92-day freshness window;
- the VMSS instance identity, directly reachable public IPv4 address, service FQDN, and ports are valid, and external reachability evidence has been explicitly approved;
- the LocalMachine certificate exists, contains a private key, and remains valid for at least seven days;
- the instance is not draining and is below its configured call ceiling.

Production enablement still requires Prompt 7's organizer controls, tenant policy, security/privacy/legal approval, live development-tenant calls, and rollback evidence.
