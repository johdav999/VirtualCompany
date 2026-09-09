# Teams call-control runbook

Prompt 3 adds the durable signaling layer that lets an authorized meeting organizer request that Alex join or leave a scheduled Teams meeting. It does not open audio/video sockets and does not start the browser realtime adapter.

## Runtime flow

1. `POST /api/sales/meeting-sessions/{sessionId}/teams-call/join` validates the active company membership, meeting organizer, scheduled Microsoft 365 invitation, explicit meeting consent, retention window, tenant registration, and feature gate.
2. The API persists one company-owned `teams_meeting_calls` row and one idempotent company-outbox command. It does not call Microsoft Graph inline.
3. The outbox worker repeats the policy checks, applies per-company/per-host limits, and calls `POST /communications/calls` with the persisted Teams join identity and authenticated callback URL.
4. Microsoft callbacks are authenticated against the registered tenant, size-limited, reduced to safe lifecycle fields, deduplicated in `teams_call_notification_receipts`, acknowledged, and then applied by the outbox worker.
5. The authoritative state progresses through `requested`, `joining`, `waiting_in_lobby`, `connected`, `leave_requested`, `ending`, and a terminal state. Human admission remains authoritative; `waiting_in_lobby` is never reported as connected.

## Configuration

Set `TeamsPresenter:Enabled` and `TeamsPresenter:CallControlEnabled` only after Prompt 2 tenant consent and policy attestation are ready. Configure the exact public HTTPS `BotCallingCallbackUrl`, `CallControlProvider=microsoft_graph`, a stable `CallControlHostId`, concurrency limits, timeout, callback byte limit, and reconciliation delay. The callback registered with Graph adds only the opaque local call ID; tenant authority always comes from the validated callback token and stored registration.

The app-only identity requires the exact permissions recorded on the tenant registration. The adapter uses service-hosted signaling configuration for this phase. Raw media and presentation sharing remain disabled until their later prompts and infrastructure gates pass.

## Operations

- A `provider_timeout`, transport ambiguity, or inconclusive create response moves the call to `reconciliation_required`; never issue another create blindly.
- Use `POST .../reconcile` when a provider call ID exists. Callback delivery can also resolve the state after a service restart.
- Duplicate commands return the existing call. Duplicate callback resource/version/state tuples are receipts with no second transition.
- Consent revocation and tenant disable persist a forced `terminate` outbox command for every active call. Leave and terminate remain executable after the registration becomes disabled.
- A rejected or removed bot is terminal. Do not automate lobby bypass or repeated admission attempts.
- Monitor `teams.call.*` metrics and the `teams-call-control` readiness health check. Logs intentionally omit join URLs, access tokens, raw callbacks, and participant data.

## Development verification

Run the focused Teams tests and API/Web builds. A live development-tenant exercise additionally requires a public callback endpoint, a scheduled Teams invitation created through Microsoft 365, a ready tenant registration, and a meeting organizer who admits Alex. Confirm join request, lobby state, admission/connection, and leave. This repository cannot perform that external exercise without those tenant prerequisites.

Microsoft protocol references: [create call](https://learn.microsoft.com/en-us/graph/api/application-post-calls?view=graph-rest-1.0), [call states](https://learn.microsoft.com/en-us/graph/api/resources/call?view=graph-rest-1.0), [call notifications and authentication](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/calls-and-meetings/call-notifications), and [delete call](https://learn.microsoft.com/en-us/graph/api/call-delete?view=graph-rest-1.0).
