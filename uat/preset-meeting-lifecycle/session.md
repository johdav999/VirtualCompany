# Preset application lifecycle — 2026-09-15

Product: local Virtual Company Web/API, current working tree. Role: meeting organizer. Entry: meeting invitation /prepare, select published preset. Baseline: user screenshot, 1595x992, reports "Prepare the presentation before the meeting starts."

## Root cause

ApplyCore checked session.Status != Ready before checking whether the requested version was already pinned. This rejected harmless retries after a meeting started. The same generic message also obscured legitimate first-attachment/replacement restrictions. The selector did not consume the existing session status, so it offered an action the backend would reject.

## Issue ledger

| ID | Severity | Flow | Acceptance / evidence | Result |
|---|---|---|---|---|
| PRESET-STATE-01 | P2 | Replay pinned preset after start | Return existing run without changing session version or creating another run. Backend lifecycle test. | Verified, isolated service fixture |
| PRESET-STATE-02 | P2 | New attachment / replacement after start | Preserve lifecycle safeguards; explain already-started or ended state, never demand repeated preset authoring. Backend lifecycle test and code review. | Verified, isolated substitute |
| PRESET-STATE-03 | P2 | Select preset in started/ended meeting | Show persistent state-specific notice; disable selection and suppress mutation controls. Component tests for presenting, completed, cancelled. | Verified, bUnit substitute |

Tests: SalesMeetingSessionServiceTests 10 passed; PresentationRunWorkflowTests 6 passed. Both affected hosts compiled through test builds. Scoped diff check passed; unrelated existing warnings remain.

No live meeting reset, approval action, database mutation, or external invitation was performed. Actual historical meeting status beyond the failing guard was not independently queried. This fix does not reopen meetings or attach a new preset to a session already in progress.

UAT used the existing selector notice treatment rather than redesigning the screen. Next live review after normal API/Web restart: verify the reported invitation now explains its lifecycle; use an unstarted meeting to attach a preset. Live browser verification remains pending.
