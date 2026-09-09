# Microsoft Teams transcript reconciliation

## Purpose and boundary

Virtual Company uses Microsoft Graph after a meeting to improve the locally captured meeting transcript. Graph remains an external evidence source: provider DTOs are normalized in `VirtualCompany.Infrastructure.Sales`, while Domain and Application depend only on provider-neutral transcript contracts. The integration does not capture Teams audio and does not require a raw-media bot.

The reconciler can add missing unreviewed segments and correct speaker labels on unreviewed segments. It never silently replaces a reviewed segment. A disagreement with reviewed evidence becomes an explicit conflict with before/after provenance. A material change marks customer minutes and internal intelligence stale; it does not regenerate or send either artifact.

## Selected Microsoft Graph contract

The integration uses delegated access through the meeting organizer's existing Microsoft 365 calendar connection.

- Resolve the online meeting from its stored join URL with `GET /users/{organizer}/onlineMeetings?$filter=JoinWebUrl eq '{url}'`.
- Subscribe to creation events on `communications/onlineMeetings/{onlineMeetingId}/transcripts`.
- List transcript metadata with `GET /users/{organizer}/onlineMeetings/{onlineMeetingId}/transcripts` and follow only HTTPS `graph.microsoft.com/v1.0` continuation links.
- Fetch metadata and WebVTT content from `GET /users/{organizer}/onlineMeetings/{onlineMeetingId}/transcripts/{transcriptId}` and `/content`.
- Request attributed WebVTT first. If Graph returns `SpeakerAttributionNotAllowed`, request the documented unattributed representation and retain an unknown speaker rather than failing the transcript.

The delegated scopes are `OnlineMeetings.Read` and `OnlineMeetingTranscript.Read.All`. The former resolves the online meeting; the latter lists and retrieves transcripts. Tenant administrator consent and the tenant transcript-access policy must allow the operation. A `GraphAccessToTranscriptsDisabled` response is treated as an operator-visible permission failure, not an indefinitely retried transport error.

References:

- [Get callTranscript](https://learn.microsoft.com/en-us/graph/api/calltranscript-get?view=graph-rest-1.0)
- [List callTranscripts](https://learn.microsoft.com/en-us/graph/api/onlinemeeting-list-transcripts?view=graph-rest-1.0)
- [Meeting transcript change notifications](https://learn.microsoft.com/en-us/graph/teams-changenotifications-callrecording-and-calltranscript)
- [Subscription resource limits](https://learn.microsoft.com/en-us/graph/api/resources/subscription?view=graph-rest-1.0)

## Configuration

Configure `SalesMeetingTranscripts` through deployment secrets and environment-specific configuration:

```json
{
  "SalesMeetingTranscripts": {
    "Enabled": true,
    "NotificationUrl": "https://api.example.com/api/integrations/microsoft-graph/meeting-transcripts/notifications",
    "LifecycleNotificationUrl": "https://api.example.com/api/integrations/microsoft-graph/meeting-transcripts/lifecycle",
    "ClientState": "a-high-entropy-deployment-secret",
    "SubscriptionLifetimeHours": 71,
    "RenewalLeadMinutes": 360,
    "RenewalPollMinutes": 30,
    "MaximumNotificationsPerRequest": 100,
    "MaximumWebhookBytes": 256000
  }
}
```

Both webhook URLs must be public HTTPS endpoints. `ClientState` must be a high-entropy secret and must not be committed. Only its SHA-256 digest is stored with a subscription. Graph transcript subscriptions have a maximum lifetime of three days, so the default requests 71 hours and renews six hours before expiry. A lifecycle URL is included because the requested expiration exceeds one hour.

The calendar connection must be Microsoft 365/Teams, active, owned by the meeting organizer, and consented for all required scopes. Subscriptions must be created before transcription starts; Graph does not send the creation notification retroactively.

## Webhook handling and recovery

For initial endpoint validation, the API returns the URL-decoded `validationToken` as `text/plain` with status 200. For notifications, it validates payload size, resolves the tenant and meeting exclusively from the persisted Graph subscription ID, compares `clientState` in constant time, and acknowledges accepted, duplicate, and safely rejected batches with status 202. It never accepts a company ID from the payload and does not log the payload or access token.

An authenticated notification creates the ingestion record and company outbox message in the same database transaction. The idempotency key includes company, provider meeting ID, transcript ID, and provider version/change token. Provider source version and cue provenance supply a second idempotency boundary, so replayed or out-of-order messages cannot duplicate evidence.

Graph recommends a response within three seconds if a notification is queued and within ten seconds if it is processed. Graph retries failed delivery for up to four hours. See [webhook delivery](https://learn.microsoft.com/en-us/graph/change-notifications-delivery-webhooks) and [lifecycle notifications](https://learn.microsoft.com/en-us/graph/change-notifications-lifecycle-events).

The background executor applies these failure rules:

- HTTP 429 and transient 5xx responses are retryable. Honor `Retry-After`; use the platform's bounded exponential retry when the header is absent.
- Timeout and transport failures are retryable.
- Missing/revoked consent, expired retention, 401/403, tenant policy denial, or permanent provider rejection are terminal until an operator or user changes the underlying state.
- `reauthorizationRequired` and `missed` lifecycle events move the subscription to renewal-required; `subscriptionRemoved` marks it expired.

## Consent, retention, and deletion

Consent must be granted and retention active when a subscription is created, when a webhook is accepted, when content is fetched, and when normalized evidence is stored. A denial, revocation, or expiry prevents ingestion. The purge worker deletes expired provider metadata, provenance, ingestion rows, and provider-added transcript segments. Locally captured user evidence is not deleted merely because the external Graph source expires.

## Operations

Readiness health check `microsoft-graph-meeting-transcripts` is healthy when the feature is disabled or configured with no subscriptions requiring attention, degraded when subscriptions require renewal/reconnect, and unhealthy when enabled configuration is invalid.

The private status endpoint is `GET /api/sales/meeting-sessions/{sessionId}/transcript-reconciliation`. It exposes safe subscription/ingestion status, counts, stale-artifact indicators, and review conflicts. Manual subscription and renewal endpoints are available under the same route. Audit events record subscription creation/renewal, accepted authenticated notification, and reconciliation counts; they exclude transcript text, provider payloads, and tokens.

Useful operator checks:

1. Confirm the webhook endpoints are externally reachable by HTTPS and Graph validation succeeds.
2. Confirm the organizer's Microsoft 365 connection has the required delegated scopes.
3. Inspect readiness health, then the session reconciliation status for permission, renewal, permanent-ingestion, or conflict states.
4. Reconnect or grant tenant policy access for authentication failures. Do not manually replay permanent failures until the cause has changed.
5. Review stale artifacts and explicit transcript conflicts before regenerating or sending minutes.

## Verification status

Deterministic adapter, webhook, reconciliation, tenant, retention, and migration tests run without external credentials. Live Graph verification still requires an explicitly supplied Entra application, tenant administrator consent, an externally reachable webhook, and a real Teams meeting whose transcription starts after subscription creation. No fake production adapter substitutes for that external verification.
