# Prompt 4 UAT issue ledger

| ID | Severity | Flow | Type | Summary | Evidence | Acceptance / regression | Status |
|---|---|---|---|---|---|---|---|
| P4-UAT-001 | P1 | FLOW-002, FLOW-003 | environment defect | Realtime-voice migration referenced snake-case principal columns that do not exist on the legacy `agents` table. | `evidence.md` startup and SQL Server record | A clean/local SQL Server upgrade applies `20260904072045_AddSalesMeetingRealtimeVoicePilot` and later migrations, API listens on 5301, and focused migration tests pass. | verified |
| P4-UAT-002 | P2 | FLOW-001 | defect | Meeting invitation rows previously required query-string knowledge to reach presenter controls. | Component and navigation tests | Authoritative projection renders prepare/continue/open labels with exact tenant and resource context. | verified |
| P4-UAT-003 | P1 | FLOW-004 | safety defect | Preparation lacked an environment-aware final review and could not explain browser versus Teams limits at launch. | Preparation page tests | Browser action appears only when diagnostics is enabled and ready; disabled mode directs the user to Teams without exposing stage tokens. | verified |
| P4-UAT-004 | P2 | FLOW-003 | recovery defect | A removed active deck could leave the private side panel holding stale snapshot state after a command conflict or reconnect. | Teams meeting surface regression test | Missing authoritative state clears snapshot, preview, and grant and directs the user back to preparation. | verified |

Next review slice: refresh and replay FLOW-001 through FLOW-004 at desktop and
390px width, then save verified screenshots under
`docs/design/references`.
