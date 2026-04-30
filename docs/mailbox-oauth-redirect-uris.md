# Mailbox OAuth Redirect URIs

Mailbox OAuth callbacks are stable per provider and environment. Register one redirect URI per provider per environment in Gmail and Microsoft Entra; do not create tenant-specific or company-specific callback URLs.

Tenant and user context is carried in protected OAuth state, not in the redirect URI path.

## Local Development

Use the API development host:

- Gmail: `http://localhost:5301/api/mailbox-connections/gmail/callback`
- Microsoft 365: `http://localhost:5301/api/mailbox-connections/microsoft365/callback`

## Production

Use the public API host for the target environment with the same provider callback paths:

- Gmail: `https://<api-host>/api/mailbox-connections/gmail/callback`
- Microsoft 365: `https://<api-host>/api/mailbox-connections/microsoft365/callback`

Replace `<api-host>` with the externally reachable API hostname for the environment. Each deployed environment needs its own pair of provider redirect URIs.

## Provider Registration Rule

Exactly one redirect URI per provider per environment is required. For example, a development environment registers the two localhost URIs above, and a production environment registers the two production API-host URIs. Do not add `/api/companies/{companyId}/...` callback URLs for new provider registrations.
