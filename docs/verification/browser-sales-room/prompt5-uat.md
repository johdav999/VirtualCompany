# Prompt 5 synchronized presentation UAT — 2026-09-09

## Scope and design evidence

The room presentation was reviewed against the generated [Prompt 5 reference](../../design/references/browser-sales-room-prompt-5-reference.png). The implemented host surface keeps the approved slide dominant, places notes and control modes in the existing private organizer rail, and shows audience render readiness beside the controls. The guest surface contains the same approved slide and meeting controls without the private rail.

## Browser checks

`tests/scripts/Capture-BrowserPresentation.cjs` rendered the exported host and guest component states at the same time in two isolated browser contexts. It ran once with installed Chrome and once with installed Microsoft Edge. Both runs verified:

- identical committed slide data for host and guest;
- zero guest elements for private presentation controls and zero guest occurrences of the confidential speaker note;
- one restored slide in both contexts after browser reload;
- no horizontal overflow at host widths 1440 and 390 or guest widths 1024 and 390.

Machine-readable results: [Chrome](prompt5-browser-checks.json) and [Edge](prompt5-edge-browser-checks.json).

Visual evidence:

- Chrome: [host desktop](prompt5-host-presentation-1440.png), [host mobile](prompt5-host-presentation-390.png), [guest desktop](prompt5-guest-presentation-1024.png), [guest mobile](prompt5-guest-presentation-390.png).
- Edge: [host desktop](prompt5-edge-host-presentation-1440.png), [host mobile](prompt5-edge-host-presentation-390.png), [guest desktop](prompt5-edge-guest-presentation-1024.png), [guest mobile](prompt5-edge-guest-presentation-390.png).

The screenshots use sanitized component fixtures and intentionally show the presentation connection recovery message because their SignalR client is offline. The production SignalR behavior is exercised separately by the API integration test.

## Functional acceptance

The SignalR integration creates an admitted guest, joins the room-scoped browser stage, acknowledges the exact committed revision, advances the host presentation, receives the new revision, acknowledges it, disconnects, reconnects, and restores the current revision. Separate host HTTP and guest hub scopes observe the same durable audience row. The same test rejects a stale host generation, a wrong-room connection, a wrong-deck asset request and a revoked guest, and verifies the audited host render override.

Runtime and conductor tests reject stale deck/version commands and stale acknowledgements. Route-neutral conductor options inherit explicit Teams render timeout, transition and dwell settings when the new section is absent. Existing Teams stage grants, acknowledgements and package behavior remain in the affected regression suite.

## Verification results

- Prompt 5 API/presentation/Teams affected suite: **34 passed, 0 failed, 0 skipped**.
- Prompt 5 Web/presentation/Teams affected suite: **26 passed, 0 failed, 0 skipped**.
- Focused browser-room SignalR integration after final authorization changes: **1 passed**.
- Chrome visual run: passed; Edge visual run: passed.
- API Release build: passed with 36 existing warnings.
- Web Release build: passed with 5 existing warnings and no Prompt 5 nullable warning.
- EF pending-model check: no changes since the latest migration.
- SQL Server-only migration tests: **1 passed, 2 skipped** because `VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION` was not configured in this process. No application database was migrated.

## Remaining external checks

No LiveKit Cloud session, Azure runtime, remote physical devices, Safari/iOS browser, corporate TURN path or connected customer meeting was available. The Chrome/Edge checks establish real browser rendering of sanitized component states; the in-process SignalR test establishes reconnect and durable cross-scope coordination. They do not establish internet/provider delivery latency or a multi-host deployment under load. Existing browser-room release gates remain in force.

