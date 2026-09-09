# Controlled sales demo UAT issue ledger

| ID | Severity | Flow | Type | Summary | Evidence | Acceptance / regression | Status |
|---|---|---|---|---|---|---|---|
| DEMO-UAT-01 | P1 | FLOW-DEMO-001 | regression | Reset must preserve meeting-required base records while restoring deterministic state. | `evidence.md#flow-demo-001--preview-and-reset-the-exact-demo-tenant` | Repeated reset keeps the meeting valid, restores the same lead ID/state, removes generated deal/activity records, and preserves audit evidence. | Verified by automated substitute |
| DEMO-UAT-02 | P1 | FLOW-DEMO-002 | regression | Demo commands must remain ordered, allowlisted, idempotent, and tenant-isolated. | `evidence.md#flow-demo-002--run-the-ordered-product-demonstration` | Both complete runs pass with stable IDs and no second-company mutations. | Verified |
| DEMO-UAT-03 | P0 | FLOW-DEMO-003 | safety | A demo tenant must never reach production external providers. | `evidence.md#flow-demo-003--block-unauthorized-or-externally-visible-effects` | The centralized outbox policy denies demo-company delivery and records auditable evidence. | Verified by automated substitute |
| DEMO-UAT-04 | P1 | FLOW-DEMO-004 | environment | Live authenticated desktop/mobile browser capture is unavailable locally. | `evidence.md#flow-demo-004--operate-the-private-side-panel` | Repeat the start/step/preview/confirm/reset flow at desktop and mobile widths when Web and SQL listeners plus an authorized synthetic company are available. | Blocked by local runtime |
