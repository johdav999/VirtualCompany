# Agent dialogue prompt 1 — persistent conversation lifetime

Date: 2026-09-28. Baseline: `6c5532d7`, plus this working-tree implementation.
Scope: prompt 1 of `docs/agentdialog-prompts.md` only.

## Profile and evidence

Product: Virtual Company browser sales meeting. Role: admitted organizer with AI processing
and transcript-retention permission, active agent lease, autonomous presentation control.
Production flow: Start agent → presentation/paused or completed deck → idle/fresh input →
question processing → terminal stop. Existing grounded answers and lower-mode approvals remain.

Verification uses the production worker, relational SQLite fixtures, deterministic clock,
media/provider connection receipts and shared controller tests. No active customer meeting,
microphone, calendar, usage limit, consent or database record was changed. These transport
receipts are test-adapter evidence, not live LiveKit connections or audible browser playback.

| ID | Severity | Finding and change | Acceptance evidence | Status |
|---|---|---|---|---|
| AD1-01 | P1 | Previous-answer/window gating prevented initial or late conversation input. Fresh autonomous input now binds current authority independently of historical playback. | `Fresh_input_enters_conversation_independently_of_playback_or_answer_history`: initial, presenting, paused, deck complete and 60-second-late cases reach provider context/interpretation without moving the checkpoint. | Verified with relational substitute |
| AD1-02 | P1 | Availability and stale-action authority must remain separate. A shared session identity now accepts fresh bindings and bounds remembered reference IDs; old action bindings remain fenced. | Shared session tests; tenant/owner/budget/consent/mode denial; takeover during interpretation; existing obsolete-narration and continuation tests. | Verified automatically |
| AD1-03 | P1 | A long provider wait renewed the lease without checking terminal consent/meeting changes. Renewal now checks terminal authority and cancels pending work. | Consent/end/budget during a blocked provider operation cancel it; ownership replacement still cancels; normal lease renewal remains covered. | Verified with relational substitute |
| AD1-04 | P1 | Playback pause/completion must not imply participant disconnection. | `Running_worker_keeps_media_and_provider_connected_while_idle_and_disposes_on_terminal_event`: one media/provider connection, no idle disposal, renewed lease; one provider termination and media disposal after consent withdrawal, room end or host stop. | Verified with deterministic transport receipts |

## Executed validation

- API playback/conversation tools/agent lease suite: 127 passed.
- Shared `AgentConversationTests`: 22 passed.
- `dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore`: succeeded.
- Adjacent floor/status/capture/provider-context checks: 93 passed.
- Total: 242 passed, no failures or skipped tests in these focused runs; `git diff --check` passed.

The build includes the affected Domain, Application, Persistence, Sales and API composition.
No EF model/schema changes were made; no migration or SQL Server DDL is required.

## Delivered boundary

Fresh input uses the current session, consent, participant and generation checks. Completed
speech supplies optional historical context. Recoverable normalized input rejection leaves
the session available for a different fresh turn. Pending action expiry remains distinct from
input availability. Initial social turns enter context and interpretation but do not yet create
new conversational speech; that release path belongs to prompt 3. Existing answer-bound bridge
and continuation eligibility remains until the later action prompts.

The OpenAI session lifecycle was checked against the [official Realtime conversations guide](https://developers.openai.com/api/docs/guides/realtime-conversations).
The configured model/profile is preserved. The worker observes provider session expiry;
automatic session rollover belongs to the later recovery prompt.

Live RØDE microphone, real provider latency, browser audibility and full greeting-to-resume
acceptance remain unverified. No application process was restarted or deployed in this turn.
