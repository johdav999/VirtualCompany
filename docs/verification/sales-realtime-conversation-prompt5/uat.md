# Prompt 5 — browser sales conversation controls UAT

Date: 2026-09-27. Product: Virtual Company browser sales meeting. Type: web/AI workflow. Base revision: `66e29aeb` plus uncommitted Prompts 1–5 work. Environment: local deterministic component and relational worker tests. Role: organizer; a separate guest contract is tested for privacy. Feature flag: `SalesRoomAgent:HybridConversationEnabled`, default off. No customer meeting, microphone, calendar, provider account, quota or consent was changed.

## Evidence packets

### FLOW-01 — Autonomous conversation status

Preconditions: active room, consented participants, approved content, healthy provider and autonomous floor. Steps: project a current question, answer/bridge and queued narration; render host room. Expected: checking sources, answering, bounded listening for a reply, then queued continuation with no routine approval button. Observed: deterministic projection and component cases pass; the listening projection expires after 45 seconds or lease loss. The label for queued narration explicitly does not claim audio playback. Live microphone/playback: not exercised. Result: automated pass; live UAT blocked pending an authorized active test meeting and provider availability.

### FLOW-02 — Assisted/manual approval

Preconditions: assisted or manual floor with a pending addressed question. Steps: project backend pending turn; render host controls. Expected: host approval state/action, no automatic speech authorization from UI. Observed: both floor modes project `awaiting_approval`; existing private evidence and approval controls remain. Actual server approval policy was covered by earlier Prompt 1–4 tests and unchanged here. Result: automated pass.

### FLOW-03 — Recovery, failure and rollback

Preconditions: operator disables conversation flag, provider unavailable, exhausted room AI allowance or expired room. Steps: project each state; attempt queued follow-up after flag-off; render host panel. Expected: distinct actionable states, no stale live-listening claim, no bridge playback after rollback, human/manual controls preserved. Observed: projection tests cover off/configuration/provider/quota/expiry; worker playback test covers flag-off bridge withholding; UI tests cover state labels and retained controls. Live process hot-reload and provider outage were not exercised. Result: automated pass; live recovery blocked by unavailable safe test audience.

### FLOW-04 — Guest privacy and device controls

Preconditions: guest or organizer opens browser room. Steps: render both routes and inspect typed clients. Expected: conversation status/private evidence/host controls are organizer-only; microphone selector, mute, Enable sound, consent/retention, Stop, takeover and slides remain. Observed: component and client tests assert guest contract has no conversation field and guest view has no private controls; host view retains controls. No microphone activation was performed. Result: automated pass.

## Issue ledger

| ID | Severity | Flow | Type | Summary | Evidence | Acceptance / regression | Status |
|---|---|---|---|---|---|---|---|
| UAT-P5-01 | P1 | FLOW-01 | defect | Broad ready/speaking label hid answer, approval and reply phases | Host component and projection tests | Organizer sees the authoritative phase and a bounded listening window | Fixed; automated pass |
| UAT-P5-02 | P1 | FLOW-03 | defect | Flag-off could leave a queued conversational bridge eligible for speech | Relational playback test | Bridge is withheld when the flag is disabled; active profile worker stops safely | Fixed; automated pass |
| UAT-P5-03 | P2 | FLOW-03 | defect | Stale command error or expired room could be described generically | Host component tests and status projection | Healthy refreshed state clears stale command error; expiry and unavailable states are distinct | Fixed; automated pass |
| UAT-P5-04 | P1 | FLOW-01–04 | live verification | Real microphone, output audio and provider outage need authorized live UAT | No active test audience/credentials supplied in this task | Run Prompt 6 live scenario; capture screenshots, latency and playback receipts without altering user meetings | Open; external prerequisite |

Verification: focused API projection, conversational bridge and rollback worker tests (13 passed); `dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore --filter FullyQualifiedName~SalesHumanRoomTests` (34 passed); API and Web `dotnet build --no-restore` (both succeeded, zero build warnings/errors on the final pass). Existing test-project analyzer warnings appeared during the test builds.

Safe browser check: neither the expected local Web listener on port 5062 nor API listener on port 5301 was running. Starting only the Web app would not exercise the original authenticated meeting flow, and starting an API/database or activating the user's microphone would have changed the test environment. The browser flow is therefore marked blocked, with the actual host/guest Blazor component render tests, typed HTTP client contract test and relational worker playback test as the strongest safe substitute. The original live user flow cannot be declared end-to-end verified from these deterministic tests.
