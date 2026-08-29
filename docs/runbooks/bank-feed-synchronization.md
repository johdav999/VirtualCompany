# Continuous bank-feed synchronization runbook

## Purpose and scope

This runbook covers the Enable Banking account-information adapter and the continuous bank-feed worker introduced by Financial App P2 Prompt 2. The implementation imports balances, booked transactions, and pending observations for explicitly mapped, ownership-verified company bank accounts.

Booked transactions become `BankTransaction` rows only after normalized data and protected source evidence commit in the same database transaction. Pending rows remain non-final source observations. Provider JSON is never the authoritative query model.

## Production prerequisites

- An Enable Banking application registered for the intended environment. Sandbox and production applications are separate.
- A callback URL registered with Enable Banking and routed to the bank-connection callback endpoint.
- The application ID and matching RSA private key available to the API process through the deployment secret mechanism.
- Contractual confirmation of scopes, supported Swedish ASPSPs, rate limits, and raw-data retention terms.
- The bank-connectivity foundation and continuous-bank-feed migrations applied in order.

Do not place a private key, access credential, or provider response in source control, `appsettings.json`, logs, screenshots, tickets, or audit summaries.

## Configuration

The checked-in defaults leave the provider disabled and contain no secret:

```text
EnableBanking__Enabled=true
ENABLE_BANKING_APPLICATION_ID=<application id>
ENABLE_BANKING_PRIVATE_KEY_PATH=<absolute path supplied by the secret mount>
```

`ENABLE_BANKING_PRIVATE_KEY_PEM` is supported when the deployment secret system injects multiline values safely. Prefer a protected secret-mounted file in environments where multiline environment variables are fragile. The configured `EnableBanking:BaseUri` selects the provider API endpoint; never point production at a test proxy.

Important worker defaults:

- Poll due work every 60 seconds.
- Synchronize each mapped account every 15 minutes.
- Use a 3-day overlap window and deterministic stable-identity deduplication.
- Lease a claim for 180 seconds and allow expired leases to resume from the last atomically committed cursor.
- Retry transient failures up to 5 attempts with bounded exponential delay, honoring a longer provider `Retry-After` value.
- Limit a run to 100 pages and a manual recovery to 366 days.
- Retain encrypted raw payloads for 90 days by default. After expiry, the worker purges the ciphertext while retaining the checksum and normalized trace. Set `RawEvidenceRetentionDays` to the contractually approved value before enabling production.

## Normal operation

The worker creates one checkpoint for every current explicit account mapping that has a provider access reference. It fetches a balance snapshot, then all booked pages, then pending pages. Each page stores:

- tenant and account checkpoint identity;
- synchronization run, phase, page, and protected continuation state;
- encrypted raw payload, SHA-256 checksum, content type, and retention deadline;
- normalized source transactions keyed by the provider `entry_reference`;
- final `BankTransaction` rows only for booked transactions;
- audit events and metrics using safe reason codes.

The Finance > Settings > Bank connections page displays healthy and attention-required counts, common coverage, maximum lag, per-account status, failures, and open recovery ranges. Operators with Finance edit capability can queue a synchronization or an exact bounded recovery.

## Health and alerting

Use `GET /api/finance/bank-feeds` in the current company context for operational health. Alert on:

- any checkpoint in `attention_required`;
- open gaps in `bank_feed_gaps`;
- sustained lag above the business SLO;
- repeated `bank_feed_rate_limited` or provider-outage reason codes;
- a growing number of expired leases;
- raw-evidence purge backlog beyond the configured retention deadline.

The meter is `VirtualCompany.BankFeeds`. Core counters are synchronization outcomes, committed pages, booked transactions, and pending observations. Logs contain checkpoint IDs, provider keys, phases, and safe reason codes; they must not contain access tokens, continuation tokens, private keys, or source payloads.

## Recovery procedures

### Provider outage or rate limiting

1. Confirm the checkpoint is `failed`, not `attention_required`.
2. Review the safe reason code and `NextAttemptUtc` in the UI or health API.
3. Allow bounded automatic retry. A provider `Retry-After` longer than local backoff wins.
4. Do not manually loop the sync action; this can prolong provider throttling.
5. If attempts are exhausted, investigate provider and consent health, then recover the exact open range after service is restored.

### Interrupted worker or process death

1. Restart or replace the worker process.
2. Do not edit the checkpoint cursor.
3. After the lease expires, another worker claims the same run and resumes from the protected continuation token committed with the previous page.
4. Verify that the checkpoint returns to `ready`, no gap is open, and stable identities remain unique.

### Cursor regression or page loop

1. Stop repeated manual synchronization for the account.
2. Record the provider request ID and safe reason code; do not copy raw payloads into the ticket.
3. Confirm the checkpoint is `attention_required` with a `cursor_regression` gap.
4. Review provider incident status and pagination behavior.
5. Once corrected, use **Recover range** for the exact retained gap. The worker starts a new run and will not overwrite an existing booked payload under the same stable identity.

### Missing balance marker

Enable Banking defines `last_committed_transaction` as the entry reference of the last transaction contributing to a balance. If that entry reference is absent after all booked and pending pages commit, the worker opens a `missing_range` gap and does not advance coverage.

1. Verify the requested window includes the balance reference date.
2. Check consent scope and the provider’s history limits.
3. Queue the exact gap range with an operator reason.
4. The gap closes only when the referenced stable identity is present and the recovery run completes atomically.

### Payload conflict

If a booked stable identity reappears with different normalized content, the existing booked data is retained. The feed becomes attention-required and the conflicting raw page remains protected evidence. Escalate to the provider; never edit the normalized row or force the checkpoint forward.

### Consent or ownership failure

Renew consent through the bank-connection flow. Account synchronization remains blocked while consent is expired, revoked, disconnected, missing required scope, or ownership is unverified/mismatched. Re-map only through the explicit verified mapping workflow.

## Key rotation and credential response

For planned RSA key rotation, register or upload the replacement certificate with Enable Banking, deploy the matching private key through the secret mechanism, restart the API, and verify institution discovery plus a non-production account sync before removing the old secret. Rotation of ASP.NET Data Protection keys must retain the previous key material long enough to decrypt stored credential and cursor envelopes.

For suspected compromise, disable the provider, revoke affected consents, rotate the Enable Banking key and data-protection material according to the security incident procedure, then require fresh consent. Never infer that a provider revocation succeeded without its successful response.

## Verification evidence

Before production enablement, capture dated evidence for:

- successful institution discovery, consent, account discovery, balances, booked and pending pages in Enable Banking sandbox;
- multiple transaction pages and continuation-key resume;
- a deliberately interrupted sync after page persistence, followed by expired-lease recovery;
- repeated overlap polling with one booked row per `entry_reference`;
- rate-limit behavior, malformed response failure, pending-to-booked promotion, cursor regression, payload conflict, and bounded replay;
- Finance-view versus Finance-edit authorization and cross-company isolation;
- migration apply/upgrade and no-pending-model checks on SQL Server.

Repository tests provide deterministic non-production evidence for these failure paths. They are not a substitute for real Enable Banking sandbox evidence. Do not enable production until sandbox credentials are supplied and the external evidence is attached to the release record.
