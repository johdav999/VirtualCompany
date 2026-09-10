# Browser sales room operations

Status: implementation complete; production enablement and live-provider acceptance remain gated. This runbook does not authorize deployment, external invitations, provider traffic or Teams reactivation.

## Runtime topology and ownership

Deploy the API as a dedicated Windows App Service pool with at least two instances, shared SQL Server, shared Redis and durable document storage. SQL is authoritative for room state, command receipts, agent owner, lease expiry, agent generation and turn generation. The in-process channel only wakes a local worker. A serializable start transaction plus the room concurrency token prevents two starts from acquiring the same room. Every renewal, transcript commit and output publication rechecks owner and generation.

Each instance reconciles active leases on startup and every `SalesRoomAgent:ReconciliationIntervalSeconds`. A healthy unexpired lease owned by another instance is preserved. An expired lease is stopped and its queued or processing speech becomes `interrupted`; it is never replayed. Concurrent reconcilers rely on the SQL concurrency token, and the loser records `reconcile_fenced`. Graceful shutdown first stops locally owned leases and abandoned speech, then cancels media/provider work. An unexpected process loss is recovered after lease expiry.

## Required configuration

Browser media and lifecycle use `SalesBrowserRoom`; agent execution uses `SalesRoomAgent`. Enabled routes validate on startup. Disabled optional routes remain startup-safe and independent from Teams.

| Setting | Operation |
| --- | --- |
| `SalesBrowserRoom:Enabled` | Browser credential/media route. Keep false until the provider endpoint and secrets are ready. |
| `SalesBrowserRoom:Lifecycle:Enabled` | Admission and room lifecycle. |
| `SalesBrowserRoom:Lifecycle:DrainEnabled` | Reject new rooms/tokens and new lifecycle work during deployment or incident response. |
| `SalesRoomAgent:Enabled` | Allows room-owned AI after the other gates pass. |
| `SalesRoomAgent:EmergencyDisabled` | Immediately stops active AI and blocks new AI work. Human calling, manual slides and typed questions remain available. |
| `SalesRoomAgent:DrainEnabled` | Stops active AI and blocks new AI work for a controlled deployment drain. |
| `MaximumActiveAgentsGlobal`, `MaximumActiveAgentsPerCompany` | SQL-counted active unexpired leases. |
| `MaximumSessionMinutes`, `MaximumInputAudioSeconds`, `MaximumOutputAudioSeconds` | Hard per-call duration and audio limits. |
| `MaximumSpendPerCallUsd`, `MaximumMonthlySpendPerCompanyUsd` | Hard spend admission/runtime limits. Month-boundary rooms are conservatively included when created or last started in the current month. |
| `MaximumInputTokenCostPerMillionUsd`, `MaximumOutputTokenCostPerMillionUsd`, `TranscriptionCostPerMinuteUsd` | Conservative current maximum rates applied to provider-reported billed audio and token usage. |
| `ProviderRateCheckedUtc`, `ProviderRateMaximumAgeDays`, `ProviderRateCardReference` | Dated evidence. Missing, future or stale evidence fails agent readiness. |

Rate values and limits must come from approved current provider terms and Prompt 10 full-call evidence. Prompt 10 has no permitted completed load run, so this repository supplies no production capacity or spend defaults. The deployment template requires explicit values and starts disabled/draining.

Secrets stay in Key Vault. The deployment template uses versionless App Service Key Vault references so an approved rotation is picked up without storing values in source. Review provider region, subprocessors, retention and data terms before enabling.

## Readiness and telemetry

Use `/health/live` for process health and `/health/ready` for admission. `sales-browser-room` reports admission/drain state, media configuration/native readiness, provider/speech availability, lifecycle ambiguity, active owners, stale owners, unhealthy voice sessions, capacity limits, spend limits, currency and rate-review date. It contains no company, room, user or participant identifiers.

The `VirtualCompany.Sales.BrowserRoom` meter emits:

- `sales.browser_room.admission`, `lifecycle` and `agent.ownership` for joins, lifecycle outcomes and fencing;
- `latency` for token/lifecycle/media-connect duration;
- `media.events` for dropped frames and reconnects;
- `audio.duration`, `provider.tokens` and `estimated_spend` for measured usage and cost estimates;
- `quota`, `failures`, `sessions.active` and `queue.depth` for limits, failures and capacity.

Alert on readiness failures, any stale owner after two reconciliation intervals, reconciliation rooms, unhealthy voice on a live room, emergency/drain stops, spend or duration limits, sustained frame drops, reconnect bursts, queue growth and capacity saturation. Dashboards may aggregate by low-cardinality result/kind/stage tags. Do not add tenant, room, participant, user, transcript or invitation tags.

## Rollout

1. Compile the template and run `Deploy-BrowserSalesRoom.ps1` without `-Apply`. Review the complete what-if. Deployment itself requires authorization.
2. Populate Key Vault, durable storage and approved parameters. Leave admission and agent disabled, emergency disable and drain enabled. Confirm Teams configuration is untouched.
3. Deploy the package, apply forward-only migrations through the normal database workflow, and verify `/health/live`. Resolve all non-control readiness failures.
4. Clear emergency disable while retaining both drains. Verify two-instance health and that an unexpired owner survives another instance startup.
5. Clear the lifecycle drain and enable browser lifecycle for an internal allowlisted cohort. Keep agent disabled; verify invite/admit/present/close and human fallback.
6. Enable the agent for a smaller cohort, then clear its drain. Watch ownership, voice health, latency, frame drops, quotas and spend. Stop expansion on any target miss.
7. Complete the Prompt 11 UAT matrix and Prompt 10 authorized full-call/load evidence before increasing limits or claiming production readiness.

## Drain, emergency stop and recovery

For a planned deployment, set both drain flags true and verify active owner count reaches zero before replacing instances. Browser admission rejects new rooms/tokens; active AI stops and pending speech is interrupted. Existing human calls retain manual slides and typed controls where their room/media session remains available.

For an AI incident, set `SalesRoomAgent:EmergencyDisabled=true`. All instances observe the options change, cancel output, stop their active leases and mark pending speech interrupted. Verify `sessions.active=0`, no unexpired agent owners, and the ownership/lifecycle stop metrics. Keep browser lifecycle enabled only if the human room is safe to continue.

For provider loss, consent withdrawal, host loss, VAD failure, unchecked audio, quota exhaustion or spend exhaustion, the worker cancels customer-visible speech and persists a typed reason. Operators should use the health result and low-cardinality metric; customer content must not enter logs. Restart requires a new organizer command and generation.

## Rollback

Rollback configuration first: set browser lifecycle drain and agent drain true, set emergency disable true, wait for owners to reach zero, then set `SalesRoomAgent:Enabled=false`, `SalesBrowserRoom:Lifecycle:Enabled=false` and `SalesBrowserRoom:Enabled=false`. Roll back the application package only after the pool is drained and the earlier package is known compatible with the current schema.

Do not routinely downgrade the database. Prompt 4–9 migrations are additive and must remain in migration history. Preserve Teams tables, values, configuration, package assets and infrastructure. A browser rollback must not change `TeamsPresenter`, `TeamsMediaHost`, tenant registrations, Teams calls, permissions or packages.

## Evidence boundary

Automated checks prove configuration binding, SQL fencing, stale-owner selection, interrupted-speech behavior, migrations, build/package paths and deterministic browser state. They do not prove LiveKit media, TURN/corporate networks, physical devices, provider billing, long-call p95, real invitations or customer delivery. Record those only from an authorized isolated target with timestamped sanitized artifacts.
