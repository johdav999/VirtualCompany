# Agentdialog prompt 6 — truthful presence and bounded recovery

Date: 2026-09-28. Scope: prompt 6 only, preserving the uncommitted prompts 1–5.
Product profile: Virtual Company browser meeting AI workflow. Primary roles: authorized organizer
and guest; autonomous, assisted and manual. Local relational fixtures exercise the production
worker/authorization and status projection. Browser fixtures render the actual Razor component
with current application/scoped CSS, not a mocked replacement UI.

## Flow and issue ledger

| ID | Severity | Regression / evidence | Result |
|---|---|---|---|
| AD6-01 | P1 | Continuous status with no previous answer, late replies, paused deck, confirmed interruption, pending lookup, answering and terminal stop | Session availability no longer inherits the legacy 45-second gate. Handoff pauses deck, not healthy conversation. Stale agent floor does not claim active speech. Deterministic verified. |
| AD6-02 | P1 | `SalesHumanRoomTests`; organizer/guest contract and manual/assisted approval controls | Plain-English phase/action guidance, muted-mic notice, takeover and manual slides retained; private host evidence absent from guest output. Deterministic verified. |
| AD6-03 | P1 | JS SDK lifecycle receipts | Agent tile stays for a connected participant with no active audio, including stopped playback; disappears when actual participant is removed/disconnected. No API-invented participant. Deterministic verified. |
| AD6-04 | P0 | Real running worker + deterministic provider expiry/transport replacement | At most two replacements; fresh session identifiers; old transports disposed; new session capped to 55 minutes; exhausted recovery pauses safely. No narration replay or automatic deck resume. Deterministic verified. |
| AD6-05 | P0 | Relational recovery tests | Current tenant, owner, consent, room time, presenter, admission, mode, budget, takeover and rollback rechecked. Stop/consent/rollback during backoff prevents a new connection. Usage/checkpoint/history retained, pending speech fenced and audit written. Deterministic verified. |
| AD6-06 | P0 | Configuration boundary tests/startup validator/runtime admission | Hybrid/semantic dependency, documented eagerness, 1–55 minute provider sessions, 0–3 retries and 1–10 second backoff validated. Legacy profile remains opt-in compatible. Deterministic verified. |
| AD6-07 | P1 | Desktop and narrow-width component browser inspection | Isolated fixture verification, separately recorded below. This is layout/status proof, not live microphone or endpoint audibility. |
| AD6-08 | P1 | Existing real room read-only inspection | Room is ended, with stopped voice and old historical floor. Cannot exercise a new live exchange without an eligible meeting. No meeting rescheduled/reopened, quotas reset, consent supplied or deployment flags enabled. Live gate explicitly unverified. |

## Verification

Broader affected backend matrix: **208 tests passed**. After final recovery/status changes,
the focused configuration, projection and rollover matrix passed **41 tests** (overlapping
regressions, not 249 unique tests).
Web component matrix: **40 tests passed**, JS media lifecycle: **13 tests passed**.
API and Web builds succeeded with zero errors. Fixtures are isolated; no live SQL/schema change
or migration required because only domain methods, projections and options changed.

```powershell
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~SalesRoomPlaybackWorkerTests|FullyQualifiedName~SalesRoomDialogueConfiguration|FullyQualifiedName~SalesRoomConversationStatusProjection|FullyQualifiedName~SalesRoomAgentLeaseTests|FullyQualifiedName~SalesBrowserRoomAgentTests|FullyQualifiedName~SalesRoomOperationsPolicy" -v:q -clp:ErrorsOnly
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~SalesRoomConversationStatusProjectionTests|FullyQualifiedName~Provider_recovery|FullyQualifiedName~Running_provider_rollover|FullyQualifiedName~Authority_change_during_recovery|FullyQualifiedName~SalesRoomDialogueConfiguration" -v:q -clp:ErrorsOnly
$env:VC_BROWSER_UAT_DIRECTORY = Join-Path $PWD 'artifacts/agentdialog-prompt6'
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore --filter FullyQualifiedName~SalesHumanRoomTests -v:q -clp:ErrorsOnly
node --test tests/VirtualCompany.Web.Tests/js/sales-human-room.test.mjs
dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore -v:q -clp:ErrorsOnly
dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore -v:q -clp:ErrorsOnly
```

## Browser packet and limitations

Entry: existing localhost room, inspected read-only; then `Serve-AgentDialogueUat.cjs`, a loopback-only
read-only server for exported component fixtures and built CSS. Harness has no API, provider,
recording, microphone, consent or control-command integration. Components use synthetic test
authority/readiness, intentionally not evidence of an actual media participant or live provider.
The real tab and user-owned hosts were not restarted or modified. No significant redesign/new
product UI; existing cards, responsive layout and controls retained per design guidance.

The paused fixture at 1280px and 390px shows a concise listening/deck-paused badge, full status
guidance and manual/assisted/autonomous controls without horizontal overflow. Reconnecting,
stopped and guest snapshots are inspected separately. Current-media tile behavior is verified by
the JS suite, not fabricated in static screenshots. The stub also displays its intentionally absent
presentation transport; that is not a newly introduced production connection failure.

Saved layout evidence: [narrow paused deck](narrow-paused.png),
[desktop reconnecting](desktop-reconnecting.png), [desktop stopped](desktop-stopped.png),
[narrow guest](narrow-guest.png). All inspected fixture widths had no document-level horizontal
overflow; guest output had no private panel/evidence. The existing real tab remains ended at
1710px, unchanged; this is a baseline observation of the already-running host, not a deployment
of the rebuilt component. Temporary fixture tabs and the read-only loopback server were closed.

Full RØDE microphone → semantic turn → generated social audio → approved narration → grounded
answer → follow-up → explicit resume and measured endpoint latency remain prompt 7. Existing
room expiry is a specific live verification blocker, not a justification to alter schedules/access.
No live provider session was held for 55 minutes; short-expiry deterministic receipts exercise
the production rollover loop and bound. Preserve actual usage and source/approval policy on rollout.
