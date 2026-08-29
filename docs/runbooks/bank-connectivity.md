# Bank connectivity operations runbook

This runbook covers the provider-neutral consent and account-ownership foundation. It does not assert that a production bank provider is configured. Provider-specific endpoints, scopes, certificates, rate limits, and escalation contacts must be added to the deployment record when a provider adapter is enabled.

## Operating states and first response

| State or reason | Meaning | Operator action |
| --- | --- | --- |
| `missing_consent` | No current consent or protected credential is available. | Ask a finance administrator to connect or renew the bank. Do not run synchronization. |
| `expired_consent` | The current consent expiry is in the past. | Start renewal from **Settings → Finance → Bank connections**. Synchronization remains blocked until the provider callback is acknowledged. |
| `scope_loss` | Required accounts or transactions capability is absent. | Renew consent and verify the provider application scopes. Do not infer access from a previously granted scope. |
| `account_ownership_mismatch` | Provider ownership evidence did not match the company. | Suspend the connection, verify the legal entity and bank mandate, and reconnect only after the discrepancy is resolved. Never map the account while it is unverified. |
| `provider_outage` | Provider health or a safe provider error reports an outage. | Leave the connection in attention-required state, check the provider status channel, and retry a bounded refresh after recovery. Do not reinterpret a timeout as success. |
| `reconciliation_required_setup` | Setup facts cannot be reconciled safely. | Review discovered accounts, currencies, and explicit internal mappings. Keep synchronization blocked until the setup is resolved. |
| `connection_suspended` | A finance administrator intentionally stopped provider access. | Determine why it was suspended. Renewal can reactivate it only after a new provider acknowledgement. |
| `connection_disconnected` / `consent_revoked` | Future local access is stopped and remote revocation is queued or complete. | Monitor the revocation task. Imported transactions, journals, source identities, mappings, and audit evidence are retained. |

## Consent renewal

1. Open **Settings → Finance → Bank connections** for the correct company.
2. Confirm the institution, current reason code, expiry, and required capabilities.
3. Select **Renew consent**. Complete the provider-hosted authorization without changing company or user session.
4. Confirm that the callback returns to the same company and the status shows the provider-acknowledged consent expiry.
5. Review account ownership again. Map only accounts with verified ownership; discovery alone never creates an accounting mapping.
6. Run a manual refresh. If access is still blocked, use the stable reason code rather than provider payloads or transport details.

Callback state is encrypted, company- and user-bound, expires after ten minutes, and is one-time. A replay or state from another company must be treated as a security event; it must not be retried with the same state.

## Disconnect and compromised consent

1. If compromise is suspected, immediately suspend or disconnect the connection for the affected company.
2. Disconnect changes local state before remote work is attempted, so synchronization stops immediately.
3. A durable revocation task calls the provider in the background with bounded retry. Check `bank_consent_revocation_tasks` for `pending`, `running`, or `failed` work and its safe failure summary.
4. On confirmed revocation, the protected credential envelope is removed. Never copy token material into tickets, logs, audit summaries, or SQL queries.
5. Rotate the provider application credential or signing certificate through the deployment secret store when the compromise may affect the shared provider application.
6. Reconnect through a new consent session. Do not restore old protected credential rows or reuse callback state.

## Credential and key rotation

- Bank access, refresh tokens, and provider credentials are serialized only inside `bank_connection_credentials.encrypted_envelope`, protected with tenant-scoped ASP.NET Data Protection purpose strings.
- Rotate the Data Protection key ring using the platform key-management procedure. Retain the prior decrypt-only keys until all active connections have renewed or credentials have been re-encrypted through normal provider refresh.
- If an envelope cannot be decrypted, treat it as `missing_consent`, block provider access, and renew. Do not expose cryptographic exceptions to users.
- Provider client secrets and certificates belong in the platform secret store/provider application configuration, never in bank connection business tables.

## Provider outage

1. Confirm the outage using provider status evidence and connectivity telemetry; never log authorization codes or tokens.
2. Leave affected connections visible as attention required. Do not mark them disconnected or revoke consent solely because the provider is unavailable.
3. Pause manual refresh attempts if they amplify provider load. Refresh is intentionally blocked for expired, suspended, revoked, disconnected, scope-loss, or ownership-mismatch states before any provider call.
4. After recovery, run one bounded health/account refresh and confirm the provider consent ID, capabilities, ownership results, and account count.
5. Escalate if a provider reports success but local persistence failed. Start a new consent flow; callback replay is intentionally rejected.

## Audit and telemetry

- Business events are retained in `bank_connection_audit_events` with company, actor, operation, outcome, stable reason code, correlation ID, and bounded before/after state.
- Technical metrics use meter `VirtualCompany.BankConnectivity`, including `bank.connection.operations` and `bank.connection.blocks` tagged by operation, outcome, and stable reason code.
- Logs may contain company and connection identifiers, safe summaries, and correlation IDs. They must not contain callback state, authorization codes, credential envelopes, provider payloads, access tokens, refresh tokens, or certificates.

## Recovery verification

After any incident, verify that:

- cross-company status, mapping, callback, and credential access remains rejected;
- a consumed callback cannot be replayed;
- expired or revoked consent blocks provider calls;
- the current mapping is explicit, versioned, company-scoped, and linked to a verified discovered account;
- historical imported rows, acknowledgements, journals, and audit events remain present after disconnect;
- pending remote revocation either completes or remains visibly failed with an operator-safe summary.
