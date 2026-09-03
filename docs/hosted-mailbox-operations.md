# Hosted Mailbox Operations

## Architecture

Hosted-domain mailboxes use one `MailKitMailboxTransport` for secure IMAP and SMTP. Provider profiles supply trusted endpoint defaults, OAuth metadata, and capability defaults; they do not contain executable provider code. Gmail and Microsoft 365 continue to use their existing API adapters.

The application boundary separates:

- transport operations (`IMailboxTransport`)
- credential resolution (`IMailboxAuthenticationStrategy`)
- trusted profiles (`IMailboxConnectionProfileRegistry`)
- endpoint governance (`IMailboxEndpointPolicy`)
- business routing for Finance, Sales, and Support

Application passwords, access tokens, and refresh tokens are encrypted with a purpose bound to both company and mailbox connection. API and Web responses never return stored credential values.

## Zoho EU setup

1. In Zoho Mail, enable IMAP access for the mailbox.
2. Enable multi-factor authentication and create an application-specific password. Do not use the normal account password.
3. In Virtual Company, open Agent Management, choose the Finance, Sales, or Support function, and select **Hosted email**.
4. Select **Zoho Mail (Europe)**. The trusted `imappro.zoho.eu:993` and `smtppro.zoho.eu:465` TLS endpoints are supplied by the server profile.
5. Enter the full mailbox address as both email address and username, then enter the application password.
6. Test incoming mail and sending. Review discovered capabilities, then connect.

Zoho documents application-specific passwords for third-party IMAP/SMTP clients. Its documented Zoho Mail OAuth scopes apply to the REST API, so the Zoho EU IMAP/SMTP profile does not advertise OAuth SASL. Do not substitute a normal account password.

## Other providers

Use **Other hosted email** only for a standards-compatible provider that supports:

- IMAP on port 993 with implicit TLS
- SMTP on port 465 with implicit TLS, or port 587 with mandatory STARTTLS
- a provider-created application password, or OAuth 2.0 SASL only when a reviewed server-managed profile explicitly supports it

The endpoint policy resolves every host before connection and rejects loopback, unspecified, link-local, multicast, private, and metadata-network addresses. The transport connects its socket to one of those validated addresses and passes the original DNS name to TLS certificate validation, preventing a second DNS lookup from bypassing the decision. Certificate validation and hostname verification cannot be disabled. A private corporate mail server requires a deployment-level endpoint allowlist extension and security review; tenant input alone cannot permit it.

## Credential lifecycle

- **Replace or rotate:** reconnect the function mailbox and supply a new application password or OAuth grant. The encrypted envelope is replaced.
- **Disconnect:** use the mailbox page. Virtual Company attempts remote Google OAuth revocation where supported, clears every stored encrypted credential field regardless of the remote result, and marks the connection disconnected.
- **Provider revocation:** Microsoft session revocation is deliberately not called because it can invalidate unrelated application sessions and requires broader privileges. Application passwords cannot be revoked through generic IMAP/SMTP. Revoke those credentials in the provider console as well as disconnecting Virtual Company.
- **Lost data-protection key:** the connection moves to a reconnect-required state. Restore the persisted ASP.NET Core Data Protection key ring if it is available; otherwise reconnect each affected mailbox. Never attempt to recover plaintext credentials from database values.

### Data Protection key-ring deployment

Set `DataProtection__KeyRingPath` to an absolute directory on durable, access-controlled storage. The directory must live outside the replaceable application deployment directory and every API instance for the environment must mount the same storage path. Relative paths and missing production configuration stop startup. Development defaults to the current user's local application-data directory so repository rebuilds do not replace its keys.

Back up the key ring with the database and retain every key file for as long as encrypted mailbox, calendar, integration, or secret values may still reference it. Do not clear the directory during deployment, image replacement, rollback, database restore, or routine cleanup. The API validates that the directory is writable during startup and logs the resolved path without logging key material.

## Synchronization and recovery

Active hosted Support and Sales mailboxes are polled after startup and every two minutes. Each message enters the existing purpose-owned idempotent ingestion service; IMAP folder cursors advance only after business ingestion completes. Finance uses the existing supplier-bill scan workflow and is queued immediately when a hosted Finance mailbox is saved. A distributed five-minute connection lease prevents overlapping workers from scanning and advancing the same mailbox.

Transport operations are also bounded per process: 64 globally, 8 per company, 2 per connection, and 16 per destination host. Capacity exhaustion fails with a safe transient status rather than waiting indefinitely. These limits complement, rather than replace, the distributed scan lease and durable outbox claims.

Each hosted mailbox stores a cursor per folder: IMAP `UIDVALIDITY`, last processed UID, optional highest modification sequence, and sync status. Cursor advancement occurs only after the normalized Finance snapshot work is durable. A changed `UIDVALIDITY` puts the cursor into reconciliation instead of continuing from an invalid UID. Message snapshots remain idempotent by company, connection, and external message reference.

Transient health failures such as provider unavailability, throttling, timeout, or local capacity are checked again every 15 minutes. Authentication, certificate, endpoint-policy, and configuration failures stop and require the corrective action shown in Agent Management.

For sync failures:

1. Check the connection's safe health message and correlation ID.
2. Verify provider availability, DNS, TLS certificate validity, and provider rate limits.
3. Reconnect only for authentication or unreadable-key failures.
4. For cursor reconciliation, retain existing snapshots and perform a bounded folder rescan. Deduplication must complete before advancing the replacement cursor.

## Outbound ambiguity

SMTP cannot guarantee exactly-once delivery. A definitive 4xx response is retryable; a definitive 5xx response is permanent. A disconnect after message data may have been accepted puts the support draft in `reconciliation_required`, permanently stops automatic outbox retries, and instructs the operator to check the Sent folder before another send. Existing Sales and Support approval and outbox policies remain authoritative.

## Adding a profile

Normally a provider is added through the deployment-owned `MailboxIntegrations:StandardProfiles` configuration and conformance tests, not by adding a transport class.

```json
{
  "MailboxIntegrations": {
    "StandardProfiles": [
      {
        "ProfileKey": "example-hosted",
        "DisplayName": "Example hosted mail",
        "Region": "Europe",
        "ImapHost": "imap.example.com",
        "ImapPort": 993,
        "ImapTlsMode": "ImplicitTls",
        "SmtpHost": "smtp.example.com",
        "SmtpPort": 587,
        "SmtpTlsMode": "StartTls"
      }
    ]
  }
}
```

Configuration is validated at startup. Invalid keys, duplicate built-in keys, IP-literal hosts, plaintext endpoints, and unsupported port/TLS combinations stop startup with an actionable configuration error.

1. Verify standards-compliant IMAP/SMTP and TLS requirements.
2. Add a unique lowercase profile key, display name, region, IMAP DNS host on implicit-TLS port 993, and SMTP DNS host on implicit-TLS port 465 or mandatory-STARTTLS port 587.
3. Configured standard profiles support application passwords and cannot inject OAuth endpoints. A new OAuth SASL profile requires code review, a server-managed client registration, official protocol documentation, and an explicit trusted implementation.
4. Add profile, endpoint-policy, authentication, and transport conformance tests.
5. Review OAuth registration, data residency, provider limits, revocation, and customer instructions.

## Database deployment

Apply EF migrations after restoring either the local SQL Server or Docker SQL Server database. `AddStandardMailboxTransport`, `AddDurableMailboxOAuthState`, `AddSupportReplyDeliveryReconciliation`, and `AddMailboxHealthFailureCode` use SQL Server-compatible columns, constraints, indexes, foreign keys, and backfill SQL in both environments. Do not use `EnsureCreated` or startup DDL. The existing backup/restore scripts remain the supported way to switch between local SQL Server and Docker.

### Local SQL Server

1. Restore the backup with `./restore-local-sql-db.ps1 -UseWindowsAuthentication`.
2. Start with `./server-local-sql.ps1`. The startup migration service applies pending migrations in the local environment.
3. If Windows authentication reports `Failed to generate SSPI context`, repair the workstation/domain credential context or use the explicitly configured SQL-authentication path. This is a connection-authentication failure, not an EF migration failure. Do not weaken SQL transport security or put the `sa` password in source files.

### Docker SQL Server

1. Enable host virtualization and start Docker Desktop.
2. Set `VC_SQL_SA_PASSWORD` outside source control, then run `docker compose up -d sqlserver`.
3. Restore `virtualcompany.bak` through the repository Docker restore flow, then start the API through `server.ps1`.
4. Confirm the four mailbox migrations appear in `__EFMigrationsHistory` before enabling mailbox workers.

The schema, migration SQL, and application model are shared between these paths; do not create a local-only migration or hand-edit one database.

## Logging and metrics

Logs may identify company ID, connection ID, purpose, profile key, safe outcome, and correlation ID. Never log or use as metric labels: email address, username, OAuth code, token, password, subject, body, attachment name/content, or raw provider response.

The transport emits low-cardinality counters for protocol connection attempts, submission outcomes, and IMAP cursor resets. Mailbox addresses, destination hosts, tenant IDs, and message identifiers are not metric labels.
