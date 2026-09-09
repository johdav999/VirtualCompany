# Alex Teams presenter application package, identity, and readiness

## Scope

Prompts 1–2 supply the versioned Microsoft Teams application package, secure application identity, company-to-Entra-tenant registration, tenant administrator consent, configuration validation, platform-administrator readiness API, and deterministic package command. They do not join a meeting, attach media, or render the final Teams meeting surfaces.

Live Teams capabilities remain fail-closed until the later prompts provide and verify:

- Microsoft Graph call control
- the Virtual Company Blazor meeting side panel and shared stage
- an approved, compatible Azure media host

The existing `browser_webrtc` voice pilot is not a Teams calling bot and is not accepted as a `TeamsPresenter:MediaRoute`.

## Microsoft application registrations

Use a dedicated development tenant and separate production registrations. Record identifiers in deployment configuration or secret management; do not edit the manifest template with tenant values.

### 1. Microsoft Entra web/application identity

1. In Microsoft Entra admin center, create or select the application that represents the Virtual Company Teams web application.
2. Record its application/client ID as `TeamsPresenter:WebApplicationId`.
3. Expose an API URI owned by the deployment, normally `api://<web-host>/<web-application-id>`, and configure the identical value as `TeamsPresenter:WebApplicationResource`.
4. Add only the redirect URLs required by the supported Teams SSO flow when the Prompt 5 surfaces are implemented.
5. Use the production credential guidance below. Do not put a client secret in the Teams package.

### 2. Azure Bot identity

1. Create or select an Azure Bot registration for Alex.
2. Record its Microsoft application/client ID as `TeamsPresenter:BotApplicationId`.
3. Configure the bot messaging/notification endpoint to the exact `TeamsPresenter:BotNotificationUrl` value.
4. Configure the calling webhook/callback URL to the exact `TeamsPresenter:BotCallingCallbackUrl` value. Prompt 2 authenticates this endpoint; Prompt 3 adds durable notification processing.
5. Do not enable calling/video declarations until the corresponding implementation, route approval, tenant permissions, and host readiness are complete.

### 3. Teams application identity

1. In Teams Developer Portal, create the Alex Sales Meeting Presenter application.
2. Record the Teams app ID as `TeamsPresenter:TeamsAppId`. This is distinct from the bot and web application IDs even if the portal initially suggests related values.
3. Use the generated ZIP from this repository rather than manually editing a packaged manifest.
4. Run Developer Portal validation before uploading to the development tenant.

## Exact URL mapping

The origins must be HTTPS and each child URL must use the exact scheme, host, and port of its configured origin.

| Configuration | Deployment mapping | Delivery prompt |
|---|---|---|
| `PublicApiOrigin` | Public Virtual Company API origin, with no path | Prompt 1 |
| `BotNotificationUrl` | `<PublicApiOrigin>/api/integrations/teams/notifications` | Prompt 3 |
| `BotCallingCallbackUrl` | `<PublicApiOrigin>/api/integrations/teams/calls` (authentication in Prompt 2; lifecycle in Prompt 3) | Prompts 2–3 |
| `AdminConsentRedirectUrl` | `<PublicApiOrigin>/api/platform/teams-presenter/admin-consent/callback` | Prompt 2 |
| `WebOrigin` | Public Virtual Company Web origin, with no path | Prompt 1 |
| `ConfigurationUrl` | `<WebOrigin>/teams/meetings/configure` | Prompt 5 |
| `SidePanelUrl` | `<WebOrigin>/teams/meetings/side-panel` | Prompt 5 |
| `StageUrl` | `<WebOrigin>/teams/meetings/stage` | Prompt 5 |

Do not point production packages at localhost. Local Teams installation requires an explicitly approved HTTPS development tunnel whose public origins and callbacks match this configuration exactly.

## Configuration

The API binds the `TeamsPresenter` section. Defaults are disabled and contain no identifiers or credentials.

```json
{
  "TeamsPresenter": {
    "Enabled": false,
    "TeamsAppId": "<teams-app-guid>",
    "BotApplicationId": "<bot-application-guid>",
    "WebApplicationId": "<web-application-guid>",
    "WebApplicationResource": "api://app.contoso.example/<web-application-guid>",
    "TenantMode": "single_tenant",
    "AllowedTenantIds": [
      "<development-tenant-guid>"
    ],
    "PublicApiOrigin": "https://api.contoso.example",
    "BotNotificationUrl": "https://api.contoso.example/api/integrations/teams/notifications",
    "BotCallingCallbackUrl": "https://api.contoso.example/api/integrations/teams/calls",
    "AdminConsentRedirectUrl": "https://api.contoso.example/api/platform/teams-presenter/admin-consent/callback",
    "WebOrigin": "https://app.contoso.example",
    "ConfigurationUrl": "https://app.contoso.example/teams/meetings/configure",
    "SidePanelUrl": "https://app.contoso.example/teams/meetings/side-panel",
    "StageUrl": "https://app.contoso.example/teams/meetings/stage",
    "MediaRoute": "disabled",
    "MediaRouteApproved": false,
    "CredentialMode": "certificate",
    "ManagedIdentityClientId": "",
    "WorkloadIdentityTokenFile": "",
    "CertificateReference": "keyvault://virtual-company/teams-presenter-certificate",
    "CertificatePasswordReference": "keyvault://virtual-company/teams-presenter-certificate-password",
    "ClientSecretReference": "",
    "ConsentStateLifetimeMinutes": 10,
    "TokenRefreshSkewMinutes": 5,
    "CallbackOpenIdConfigurationUrl": "https://api.aps.skype.com/v1/.well-known/OpenIdConfiguration",
    "CallbackIssuer": "https://api.botframework.com",
    "PackageVersion": "1.0.0",
    "CallControlEnabled": false,
    "AudioEnabled": false,
    "SharedStageEnabled": false,
    "VisualMediaEnabled": false
  }
}
```

`MediaRoute` accepts only `disabled`, `teams_application_hosted`, or `certified_provider`. `browser_webrtc` is intentionally rejected. Calling/audio/visual flags require an explicitly approved non-disabled Teams route. Audio also requires call control; visual media requires both call control and audio.

## Credential handling

`CredentialMode` accepts `managed_identity`, `workload_identity`, `certificate`, or `client_secret`. Client secrets are rejected outside Development and Testing and are always loaded from `ClientSecretReference`; they never belong in application settings, source, the Teams package, or business records.

Production must use one of:

- a managed identity, optionally selected by `ManagedIdentityClientId`;
- a workload identity with its federated token file supplied by the hosting platform; or
- a base64-encoded PKCS#12 certificate stored in the platform secret store and referenced by `CertificateReference`, with an optional separate password reference.

The reference is safe deployment metadata, not certificate contents. Private keys, client secrets, access tokens, raw SDP, and tenant-specific package ZIPs must not be committed.

## Build the Teams application package

Run the deterministic wrapper from the repository root. This example keeps all live capabilities disabled while producing a registration package:

```powershell
.\scripts\build-teams-presenter-package.ps1 `
  -TeamsAppId '<teams-app-guid>' `
  -BotApplicationId '<bot-application-guid>' `
  -WebApplicationId '<web-application-guid>' `
  -WebApplicationResource 'api://app.contoso.example/<web-application-guid>' `
  -TenantMode single_tenant `
  -AllowedTenantIds '<development-tenant-guid>' `
  -PublicApiOrigin 'https://api.contoso.example' `
  -WebOrigin 'https://app.contoso.example' `
  -BotNotificationUrl 'https://api.contoso.example/api/integrations/teams/notifications' `
  -BotCallingCallbackUrl 'https://api.contoso.example/api/integrations/teams/calls' `
  -AdminConsentRedirectUrl 'https://api.contoso.example/api/platform/teams-presenter/admin-consent/callback' `
  -ConfigurationUrl 'https://app.contoso.example/teams/meetings/configure' `
  -SidePanelUrl 'https://app.contoso.example/teams/meetings/side-panel' `
  -StageUrl 'https://app.contoso.example/teams/meetings/stage' `
  -PackageVersion '1.0.0'
```

The default artifact is `artifacts/teams/alex-presenter-<version>.zip`. Pass `-OutputPath` for another exact path and `-Force` only when intentionally replacing that package. The ZIP always contains exactly:

- `manifest.json`
- `color.png` at 192×192
- `outline.png` at 32×32 with transparency

Entries use a fixed timestamp and stable order/content. Repeating the command with identical inputs produces the same SHA-256 hash. The embedded offline validator checks the pinned Teams manifest contract, IDs, URLs, domains, feature relationships, icon dimensions, and outline transparency. Teams Developer Portal validation remains the final external schema/store check.

## Readiness API

An authenticated platform administrator can query:

```text
GET /api/platform/teams-presenter/readiness?companyId=<company-guid>
```

The response separates package, identity, URL, domain, media route, credential, tenant approval, permission, call-control, shared-stage, and media-host checks. It returns configured and effective capability flags but never returns app IDs, tenant IDs, certificate references, secret values, tokens, or detailed provider failures.

Ordinary company members receive `403 Forbidden`. The query is read-only: it never registers resources, grants consent, installs an app, joins a meeting, or modifies company state.

For a selected company, Prompt 2 reports persisted tenant-consent, exact permission, policy-attestation, token-evidence, and callback-authentication state. Call control, shared stage, and media host remain not implemented, so `liveCallingReady` and every effective capability remain false even after identity readiness succeeds.

See [Teams presenter identity and tenant consent](teams-presenter-identity-and-consent.md) for the administrator flow, endpoints, least-permission rules, credential rotation, and incident revocation.

## Validation and installation

1. Run the package command twice with identical inputs and confirm the printed SHA-256 values match.
2. Inspect `manifest.json` and confirm the three application IDs and every host belong to the intended development registration.
3. Upload the ZIP to Teams Developer Portal validation.
4. Do not install it into a customer or production tenant until the later prompts supply the callback and content routes.
5. When those routes exist, upload/install only in an allowlisted development tenant, query readiness, and capture the external validation result without production or customer data.

## Rollback and incident disable

1. Set `TeamsPresenter:Enabled=false` to keep every effective Teams presenter capability disabled.
2. Set all capability flags to false and `MediaRoute=disabled` before packaging a rollback version.
3. Increment `PackageVersion`, generate a new ZIP, and update or remove the Teams app through tenant administration.
4. Revoke the Azure Bot/Entra credential and tenant consent if identity compromise is suspected.
5. Preserve Sales meeting records, decks, audit history, browser voice configuration, and transcript reconciliation. Package rollback does not require a database rollback.

## Official references

- [Microsoft 365 app manifest schema](https://learn.microsoft.com/en-us/microsoft-365/extensibility/schema/?view=m365-app-1.29)
- [Register calls and meetings bots](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/calls-and-meetings/registering-calling-bot)
- [Teams app icon requirements](https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/design/design-teams-app-icon-store-appbar)
- [Build meeting tabs](https://learn.microsoft.com/en-us/microsoftteams/platform/apps-in-teams-meetings/build-tabs-for-meeting)
- [Teams meeting app APIs](https://learn.microsoft.com/en-us/microsoftteams/platform/apps-in-teams-meetings/meeting-apps-apis)
