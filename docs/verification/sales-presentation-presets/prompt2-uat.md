# Presentation presets Prompt 2 UAT

## Product profile

- Product: Virtual Company Presentation Presets
- Type: web
- Environment: local, company-member route
- Launch: `dotnet run --project src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-build --no-launch-profile --urls http://localhost:5066`
- Evidence: `.codex-build/uat/sales-presentation-presets/`
- Reference: `docs/design/references/sales-presentation-presets-reference.png`

## Evidence packets

### FLOW-001 — Open the preset library

Expected: The Sales entry point loads the library or a controlled authorization/error state without breaking the shell.

Observed: The initial run returned an unhandled 500 when the current member could resolve company context but received 403 from optional agent-roster enrichment. After UAT-001, the same route returned HTTP 200 and a controlled, actionable 403 state. The Sales shell, navigation, search, filters, and reload action remained usable.

Artifacts: `desktop-after.png`; result: pass after fix.

### FLOW-002 — Responsive library state

Expected: Core controls remain visible and usable at 390×844 with horizontal collections scrolling within their own region.

Observed: The initial mobile capture showed the full-width primary action exceeding its container because padding was added outside its declared width. UAT-002 applies border-box sizing and a stretched auto width.

Artifacts: `mobile-after.png`; result: pass after CSS regression check and rebuild.

### FLOW-003 — Production-backed lifecycle

Expected: Create, upload, process, preview, publish, version, duplicate, and archive against Prompt 1 APIs.

Observed: Blocked in this local session. The isolated API health endpoint returned 503 and the supplied local member context received 403 from the preset endpoint. No credentials or production access were invented. Typed transport/component tests cover request routes, payloads, cancellation, conflict mapping, safe failure, backend corrective actions, and retry visibility.

Result: blocked by local API/database/auth environment.

## Issue ledger

| ID | Severity | Flow | Type | Summary | Acceptance / regression | Status |
|---|---|---|---|---|---|---|
| UAT-001 | P1 | FLOW-001 | defect | Optional presenter roster 403 caused an unhandled page 500 | Roster enrichment failure leaves the preset API flow available and never escapes page initialization | Verified via HTTP 200 replay |
| UAT-002 | P2 | FLOW-002 | defect | Mobile primary action overflowed its container | At 390 px the primary action stays within the workspace | Verified by scoped CSS and focused build |
