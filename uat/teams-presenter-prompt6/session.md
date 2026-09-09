# Prompt 6 infrastructure validation session

- Date: 2026-09-04
- Scope: Azure Windows VMSS media runtime, host affinity, drain, SDK gate, secrets, monitoring, cost ceiling, and runbooks.
- External environment: not supplied; no Azure resources or Teams calls were created.

## Repository evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Supported topology recorded | Pass | `docs/teams-media-azure-runtime-decision.md` selects Windows Server VMSS and rejects Web App/Linux. |
| IaC is reproducible | Pass | Subscription/resource-group Bicep templates create network, compute, identity, Key Vault access, DNS, monitoring, autoscale, and budget. |
| Secrets excluded | Pass | Example parameters omit secure admin password and package URI; bootstrap fetches certificate/configuration from Key Vault using managed identity. |
| Instance affinity | Pass | Durable call starts pending, is pinned once by the admitting media host, and media binding authorization requires that exact host. |
| Graceful drain | Pass | Admin and Azure Scheduled Events start the same drain state; readiness stops new traffic and deadline queues durable termination. |
| SDK freshness | Pass | Project, runtime constant, lock file, and PowerShell gate use Microsoft-owned package `1.2.0.17950`, published 2026-07-02 and still inside Microsoft's three-month window. The 2026-09-03 package was not selected because its public-feed transitive dependencies did not restore reproducibly. |
| Monitoring | Pass | Azure Monitor distro exports custom meters; workbook, application/VM alerts, action group, and subscription budget are declared. |
| Fallback preserved | Pass | Disabled media remains healthy/optional and typed/shared-stage flows do not depend on VMSS deployment. |
| Dependency advisory scan | Pass | The first scan traced a high-severity `System.Text.RegularExpressions 4.3.0` leaf through Microsoft's media SDK; the project now pins patched `4.3.1`, and the repeated transitive scan reports no known vulnerable packages. |

## External acceptance still required

The following are explicitly unverified until an approved Azure subscription and non-demo Microsoft 365 tenant are supplied: subscription deployment, DNS delegation, certificate import/rotation, public callback and media reachability, VMSS instance protection, scaling/draining under a real call, bounded concurrent-call capacity, region recovery, and live Teams audio. Prompt 7 must attach that evidence before production enablement.
