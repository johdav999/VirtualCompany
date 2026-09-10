# Prompt 11 operations UAT record

Date: 10 September 2026. Environment: local Windows source/build/test environment. The required `$polish-uat-loop` evidence workflow was used. No Azure deployment, LiveKit session, external invitation, customer send or Teams call was authorized or performed.

## Product and journey scope

The full intended journey is invite, admit, consent, present a real deck, interrupt, answer, takeover, close, review and approved follow-up. Operational variants include English/Swedish, desktop/mobile Safari/Chrome/Edge, microphone/speaker changes, corporate/TURN networks, long calls, application/provider loss, worker crash, stale-owner cleanup, deployment drain and consent races.

## Executed flows

| Flow | Evidence | Result |
| --- | --- | --- |
| Browser-only and Teams-only configuration | Four-way `SalesRoomMediaTransportTests` matrix with valid browser settings and independent Teams/legacy flags | Passed |
| Multi-instance ownership | Lease/generation domain tests plus reconciliation predicate: valid foreign lease retained, expired lease selected, emergency/drain selects all; SQL concurrency token remains enabled | Passed |
| Emergency/drain fallback | Worker and coordinator paths cancel media, stop owned agent, interrupt queued/processing speech and retain human/manual/typed fallback | Passed by focused code tests/build; live option propagation blocked |
| Cost and limits | Deterministic provider-billed-audio/token calculation, stale rate evidence and invalid capacity tests | Passed |
| Observability | Meter listener validates admissions, lifecycle, ownership, quotas, media drops and absence of tenant/room/user tags | Passed |
| Deployment | Browser Bicep and all three Teams Bicep templates compile | Passed |
| Teams recovery | SDK lock freshness gate and disabled package generation with synthetic identifiers | Passed |
| Full browser journey and device/network/long-call matrix | Requires authorized isolated target, LiveKit credentials, real/synthetic participants, devices and send approval | Blocked |
| Browser load/soak and p95 | Prompt 10 prerequisites remain unavailable; no samples exist | Blocked |

Prompt 9 browser captures remain the strongest UI evidence for the close/review surfaces; no Prompt 11 UI changed, so new screenshots would not test the new operator behavior. Prompt 6 contains real English/Swedish approved narration generation, but it is not full-call evidence. Static/component evidence is not relabelled as live UAT.

## Defect ledger

| ID | Severity | Finding | Resolution |
| --- | --- | --- | --- |
| OPS-001 | Critical | Startup recovery stopped every active browser agent, including a healthy lease owned by another instance. | Replaced with periodic expiry-based reconciliation, SQL concurrency fencing and explicit emergency/drain selection. |
| OPS-002 | High | Browser route/agent configuration could be enabled without fail-fast media, lifecycle, rate-evidence or capacity validation. | Enabled routes now validate on startup; agent policy requires current referenced rate evidence and bounded limits. |
| OPS-003 | High | No global/company active-agent limit, runtime spend stop, emergency stop or deployment drain existed. | Added admission/runtime enforcement and safe stop/interruption semantics. |
| OPS-004 | Medium | Browser operations had benchmark metrics but no route-specific readiness result for stale owners, voice health or lifecycle ambiguity. | Added tagged readiness health check and low-cardinality operator metrics. |

## Sampling and targets

No live samples were collected. Measured p50/p95/p99 join, interruption, answer, slide, reconnect, dropped-frame, CPU, memory, concurrency and cost values remain unavailable. The release targets and stop rules in the Prompt 10 benchmark report remain unchanged and are not claimed as met.

To unblock, provide a named authorized isolated Sweden Central target and separate load generator, LiveKit credentials and region/data approval, permitted participant identities/devices, a spend/concurrency envelope, real representative decks, English/Swedish scripts, network profiles and explicit approval for any external invitation/follow-up. Then run the frozen Prompt 10 protocol plus this journey matrix, preserve raw sanitized samples and record measured p95 targets/misses.
