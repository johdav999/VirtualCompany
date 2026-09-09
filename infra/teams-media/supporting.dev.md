# Azure supporting resources — development

Subscription: `6934b887-87e8-4fd9-a17e-0ebfe55d374d` (Pay-As-You-Go).
Azure tenant: `8acf7923-17e5-492d-a8c6-756ca23599af`.
Resource group: `vc-dev-teams-media-rg`. Region: Sweden Central.
Provisioned for Teams market-assistant testing on 2026-09-09.

## Resources

| Service | Name | Configuration |
| --- | --- | --- |
| Azure SQL server | vco-dev-sql-etr7qjsd | Entra-only authentication, Johan Davidsson as administrator, TLS 1.2 minimum |
| SQL database | VirtualCompany | Basic, 5 DTUs, 2 GiB maximum, local backup redundancy |
| Azure Managed Redis | vco-dev-redis-etr7qjsd | Balanced B0, no HA, TLS, NoCluster, NoEviction; access-key authentication matches current application |
| Key Vault | vco-dev-kv-etr7qjsd | RBAC, seven-day soft-delete retention |
| Storage | vcoteamsetr7qjsd | Standard LRS, HTTPS, no public blobs or shared-key access, SMB OAuth enabled |
| Blob container | deployment-artifacts | Private; no packages uploaded yet |
| SMB shares | keys, documents | Transaction optimized, 100 GiB quota each; initially empty |
| Managed identity | vco-dev-media-id | Key Vault Secrets User; SMB MI Admin scoped to each application share |
| Log Analytics | vco-dev-logs | 30-day retention |
| Application Insights | vco-dev-appi | Workspace based, local authentication disabled |
| Services VNet | vco-dev-services-vnet | 10.43.0.0/16; private-endpoints subnet 10.43.1.0/24 |
| Private endpoints | vco-dev-{sql,redis,vault,blob,file}-pe | Private DNS zones linked to the services VNet |

SQL, Redis, Key Vault, and storage have public network access disabled.
These resources incur Azure charges. Development tiers are not production availability settings.
The shares' quota is a capacity ceiling, not preallocated stored data.
Existing Prosa resources were outside this deployment.

Managed identity client ID: `13d13e6c-dd53-4584-8cd4-96c9fe2a4637`.
Managed identity principal ID: `7340d419-60d5-4f3c-a731-a6e644ef7dd1`.

## Reproduce the supporting deployment

Run from the repository root with an Azure CLI account authorized for this subscription:

```powershell
az deployment group what-if --subscription 6934b887-87e8-4fd9-a17e-0ebfe55d374d --resource-group vc-dev-teams-media-rg --template-file infra/teams-media/supporting.bicep --parameters infra/teams-media/supporting.dev.parameters.json
az deployment group create --subscription 6934b887-87e8-4fd9-a17e-0ebfe55d374d --resource-group vc-dev-teams-media-rg --name supporting --template-file infra/teams-media/supporting.bicep --parameters infra/teams-media/supporting.dev.parameters.json
```

The initial deployment is `supporting-20260909`. A follow-up `supporting-files-20260909` assigns the share permissions. SMB OAuth was initially enabled with `az storage account update --enable-smb-oauth true`; the final supporting template includes the same verified ARM property and roles.

## Configuration still required before deploying applications

The supporting resources are not a running API, web app, or Teams bot.

1. Connect the future `vco-dev-vnet` media-host VNet to `vco-dev-services-vnet` with reciprocal peering, and link all five private DNS zones to the media-host VNet. Establish this before VM bootstrap accesses Key Vault or artifact storage. The current `main.bicep` does not yet implement that connection.
2. Give the deployment runner private network access for uploading signed artifacts, writing configuration secrets, and running migrations. No public firewall exception was added.
3. Create the API managed identity as a contained database user and grant the required runtime database permissions through the migration/admin pipeline. Apply all active SQL Server migrations. SQL administrator configuration alone does not grant the API database access.
4. Put application settings into Key Vault using the existing owners: `ConnectionStrings--VirtualCompanyDb` and `Observability--Redis--ConnectionString`. Use the SQL managed-identity connection option with the client ID above. Obtain the Redis key only inside the protected secret-writing process; never print or commit it.
5. Prepare the Windows Server image/bootstrap with Microsoft's Azure Files SMB Managed Identity Client. Initialize and renew credentials in the actual Windows service security context, and verify read/write after reboot and token renewal before enabling media. Use the API's user-assigned identity. The current installer does not configure this client.
6. The durable paths are `\\vcoteamsetr7qjsd.file.core.windows.net\keys` and `\\vcoteamsetr7qjsd.file.core.windows.net\documents`. Put them into `KeyRingPath` and `ObjectStorageRootPath` in the Teams host configuration.
7. Supply the Teams app/bot registrations, intended Microsoft 365 test tenant, delegated DNS name, trusted TLS PFX, signed bootstrap, and package. The Azure subscription tenant does not itself establish a Microsoft 365 Teams test tenant.
8. Deploy the Windows media API and Blazor web host, then perform the runbook's live connectivity and Teams acceptance checks. Keep live-media approval flags disabled until evidence passes.

Azure Files supports SMB managed identities without a Windows domain for non-domain-joined clients. This supersedes the older domain-only storage assumption for this development deployment; the client setup and service-context validation remain required.

References: [Azure Files managed identities](https://learn.microsoft.com/en-us/azure/storage/files/files-managed-identities), [Azure Managed Redis private networking](https://learn.microsoft.com/en-us/azure/redis/private-link), and the repository [media operations runbook](../../docs/runbooks/teams-media-azure-runtime.md).

## Verification

Both Azure deployments completed with `Succeeded`. SQL reports `Online`, and Redis reports `Succeeded`. All five private endpoints report `Succeeded` with `Approved` connections. Each private DNS zone has the expected A record in 10.43.1.0/24. SQL, Redis, storage, and Key Vault all report public network access disabled. Storage reports SMB OAuth enabled and shared-key access disabled.

The final Bicep template compiles. The initial Azure what-if preview contained only creates inside this resource group. Non-secret verification output is saved in `artifacts/teams-supporting-verification.json`. These are Azure control-plane checks; application connectivity, SQL migrations, service-context SMB access, and live Teams behavior have not been tested against these new resources.
