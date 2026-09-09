# Alex Teams presenter identity and tenant consent

## Security boundary

Alex uses a dedicated application identity for Teams call control. It is separate from the organizer-delegated Microsoft 365 calendar and transcript connections. An app-only Graph token proves the application and tenant; it does not authorize a meeting action, admit Alex through a lobby, or authorize a Virtual Company user.

Every live operation added in later prompts must independently require:

- a server-authorized company and organizer action
- a ready company-to-Entra-tenant registration
- meeting consent and policy checks
- the relevant rollout gate
- a durable, auditable command

Prompt 2 deliberately leaves call creation unavailable. An authenticated call callback receives `503 teams_presenter.call_control_not_implemented` until Prompt 3 installs the call lifecycle.

## Supported tenant relationship

The supported relationship is one-to-one:

- one Virtual Company company can have at most one Teams tenant registration
- one Microsoft Entra tenant can be associated with at most one Virtual Company company

Both rules are database-enforced. Changing a company to a different tenant is never an implicit update. Disable and investigate the current association, then use a separately reviewed migration or administration procedure if an intentional rebind is required.

Callbacks never accept a company ID as authority. The server first validates the signed Bot Framework token, obtains its `tid`, and resolves the company through the persisted tenant and configured bot application identity. Unassociated, disabled, pending, and revoked registrations fail closed.

## Least Microsoft Graph application permissions

Configure only the permissions needed by the selected route:

| Implemented behavior | Application permission |
|---|---|
| Join a scheduled group call | `Calls.JoinGroupCall.All` (`f6b49018-60ab-4f81-83bd-22caeabfed2d`) |
| Send or receive raw application-hosted media | `Calls.AccessMedia.All` (`a7a681dc-756e-4909-b988-f160edc6655f`) |

`Calls.AccessMedia.All` is required only when the approved route is `teams_application_hosted` and audio or visual media is enabled. A certified-provider route must use that provider's approved boundary and does not gain raw-media permission through this implementation.

Do not add `Application.Read.All`, directory-wide read permissions, or unrelated calling permissions to make verification convenient. The implementation requests the Microsoft Graph `/.default` scope and validates the trusted app token's `roles` claim against the exact configured set. Missing and additional roles both block readiness. Because `/.default` represents the statically configured application grants, the Entra registration itself must contain only the approved permissions.

Admin consent is required for both listed application permissions.

## App registration

1. In Microsoft Entra admin center, open the application whose client ID is `TeamsPresenter:BotApplicationId`.
2. Under API permissions, add Microsoft Graph **Application permissions** only for the currently enabled behavior in the table above.
3. Do not reuse the organizer's delegated calendar connection or its tokens.
4. Register this exact redirect URI as a Web redirect URI:

   `https://<public-api-host>/api/platform/teams-presenter/admin-consent/callback`

5. Set the same absolute value in `TeamsPresenter:AdminConsentRedirectUrl`. It must share the exact scheme, host, and port of `TeamsPresenter:PublicApiOrigin`.
6. Configure the Azure Bot calling webhook as the exact `TeamsPresenter:BotCallingCallbackUrl` value.
7. Keep the tenant ID in `TeamsPresenter:AllowedTenantIds`. The persistent association still requires a separate platform-administrator action.

## Production credentials

Use `managed_identity`, `workload_identity`, or `certificate` in production.

### Managed identity

Set `CredentialMode=managed_identity`. Set `ManagedIdentityClientId` for a user-assigned managed identity; when omitted, the bot application ID is used. Configure the Entra identity relationship and permissions outside the application.

### Workload identity

Set `CredentialMode=workload_identity` and supply `WorkloadIdentityTokenFile` from the hosting platform. Configure the federated credential on the bot application. Do not copy the federation token into configuration or persistent storage.

### Certificate

Set `CredentialMode=certificate`. Store a base64-encoded PKCS#12 certificate, including its private key, in the platform secret store and put only its reference in `CertificateReference`. If it is password-protected, store the password separately and set `CertificatePasswordReference`.

### Development-only client secret

`CredentialMode=client_secret` is accepted only when the host environment is Development or Testing. Put only a server-side secret-store reference in `ClientSecretReference`. Production rejects this mode before reading the secret store.

Access tokens are held only in a bounded in-memory cache. They refresh before expiry using `TokenRefreshSkewMinutes`, honor cancellation, and are removed from the cache after identity or permission failures. Tokens and credential values are not persisted, audited, returned by APIs, or logged.

## Administrator consent flow

All management routes require the `PlatformAdministration` policy and a resolved user identity.

1. Associate a company and allowlisted tenant:

   `PUT /api/platform/teams-presenter/companies/{companyId}`

   ```json
   {
     "entraTenantId": "<tenant-guid>",
     "approvedMediaRoute": "teams_application_hosted"
   }
   ```

2. Start consent:

   `POST /api/platform/teams-presenter/companies/{companyId}/admin-consent`

   The response contains a tenant-specific Microsoft admin-consent URL. The server creates 32 random bytes of state, returns the state only in that URL, and persists only its SHA-256 hash. The state is tied to the initiating administrator, expires after `ConsentStateLifetimeMinutes`, and is single-use.

3. Open `authorizationUrl` in the same signed-in administrator browser and complete Microsoft tenant-wide consent.

4. Microsoft redirects to the exact registered callback. The server checks state ownership, expiry, replay, `admin_consent=true`, and the returned tenant. It then forces a fresh app-only Graph token and accepts consent only when `tid`, Graph audience, and the exact permission-role set match.

5. After the tenant's required Teams policies have been independently configured and checked, record the platform-administrator attestation:

   `POST /api/platform/teams-presenter/companies/{companyId}/policy-attestation`

   ```json
   { "approved": true }
   ```

6. Query safe readiness:

   `GET /api/platform/teams-presenter/readiness?companyId={companyId}`

The response exposes safe status and stable failure codes, not tenant IDs, app IDs, credential references, access tokens, raw Microsoft errors, or callback payloads.

## Tenant policy

Consent alone is insufficient. Confirm the tenant permits the Teams application and bot for the intended users and meetings. Where the Graph operation requires a Teams application access policy, create and grant the narrow policy through Teams PowerShell, wait for propagation, test with a development organizer, and then record the attestation. Do not use the attestation endpoint as a substitute for configuring or testing the Microsoft policy.

Policy revocation immediately removes the registration from ready state. Later call-control work must recheck this state immediately before every external join and terminate any active call when it changes.

## Callback authentication

`POST /api/integrations/teams/calls` requires a bearer JWT. Validation is strict:

- signature keys come from `https://api.aps.skype.com/v1/.well-known/OpenIdConfiguration`
- issuer must equal `https://api.botframework.com`
- audience must equal `TeamsPresenter:BotApplicationId`
- expiration and not-before are checked with a bounded two-minute clock skew
- only RSA SHA-256/384/512 signatures are accepted
- `tid` must be a non-empty GUID and resolve with the configured bot ID to one ready registration

Invalid tokens and unassociated tenants return `401` without revealing registration data. Prompt 3 must persist and process provider notifications after this authentication boundary.

## Rotation, removal, and incident revocation

For planned certificate or secret rotation:

1. Add the new certificate credential to the Entra application.
2. update the platform secret-store value or version behind the configured reference
3. force verification by repeating the administrator consent verification flow, or restart the service if immediate cache eviction is required
4. verify credential, token, permission, tenant-policy, and callback readiness
5. remove the old Entra credential only after the new identity succeeds

Changing a credential does not modify company, meeting, or deck records and requires no database reset.

For consent removal or an incident:

1. Disable new operations immediately:

   `POST /api/platform/teams-presenter/companies/{companyId}/disable`

   ```json
   { "consentRevoked": true }
   ```

2. Revoke tenant admin consent and the compromised credential in Microsoft Entra.
3. Disable or remove the Teams application through tenant administration when required.
4. Preserve business and audit records; never delete the database as an identity-recovery action.
5. Prompt 2 has no active Teams calls. Once Prompt 3 exists, its termination workflow must stop any active call on disable/revocation and reconcile ambiguous provider outcomes.

## External verification checklist

Automated tests use deterministic tokens and do not prove Microsoft tenant configuration. In an authorized, non-demo development tenant:

1. complete the admin-consent flow
2. confirm the Graph token contains exactly the expected role set
3. attest and test the applicable Teams policies
4. send a genuine signed Bot Framework callback and confirm tenant resolution
5. revoke consent and confirm readiness and callbacks fail closed
6. record the evidence without copying access tokens, tenant identifiers, or callback bodies into logs or support artifacts

## Official references

- [Microsoft Graph permissions reference](https://learn.microsoft.com/en-us/graph/permissions-reference)
- [Microsoft identity platform admin consent endpoint](https://learn.microsoft.com/en-us/entra/identity-platform/v2-admin-consent)
- [Microsoft Graph app-only authentication](https://learn.microsoft.com/en-us/graph/auth-v2-service)
- [Authenticate Teams calling and meeting bot notifications](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/calls-and-meetings/call-notifications)
- [Teams application access policy for online meetings](https://learn.microsoft.com/en-us/graph/cloud-communication-online-meeting-application-access-policy)
