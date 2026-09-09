# Controlled sales demo UAT evidence

### FLOW-DEMO-001 — Preview and reset the exact demo tenant

Revision: `f9be926` plus working tree; environment: local automated substitute; role: Demo operator.

Steps:
1. Provision the embedded `northstar-sales` version 1 scenario with synthetic-data confirmation.
2. Preview the exact tenant and inspect counts, disabled integrations, invariants, audit-retention flag, and preview token.
3. Reset twice, then inspect restored lead state, stable identifiers, generation, and audit evidence.
4. Attempt non-demo and stale-token resets.

Expected: Preview is read-only; repeated reset is deterministic and cannot target a normal or changed company.

Observed: Focused service/catalog/migration tests passed. The reset implementation restores meeting-referenced base records in place and removes only deterministic scenario-created deal/activity records.

Result: pass using the automated substitute.

### FLOW-DEMO-002 — Run the ordered product demonstration

Revision: `f9be926` plus working tree; environment: local automated substitute; role: Demo operator.

Steps:
1. Link a company-owned meeting, start the run, and submit an arbitrary command.
2. Execute qualify, convert, and move-to-proposal commands with stable idempotency keys.
3. Replay the first key, inspect the other demo company, reset, and run all three actions again.

Expected: Only the current allowlisted command runs; state is real, idempotent, deterministic, and company-scoped.

Observed: Nine Prompt 10 API tests and the 47-test auth/tenant regression slice passed. The repeated run produced the same deterministic deal ID with no state change in the second company.

Result: pass.

### FLOW-DEMO-003 — Block unauthorized or externally visible effects

Revision: `f9be926` plus working tree; environment: local automated substitute; roles: Demo operator and Ordinary member.

Steps:
1. Request controls as an Employee and link a missing/wrong-company meeting.
2. Evaluate an external provider action for both demo and normal companies.
3. Load the same run in two contexts and submit stale concurrent updates.

Expected: Backend role/company/concurrency policy denies unsafe requests; demo-provider access is blocked before dispatch.

Observed: Permission, meeting ownership, deny-by-default external-side-effect policy, and optimistic-concurrency assertions passed. The outbox boundary records a denied audit event before permanently rejecting demo-tenant delivery.

Result: pass.

### FLOW-DEMO-004 — Operate the private side panel

Revision: `f9be926` plus working tree; environment: local automated substitute; role: Demo operator; responsive target: 430px panel and mobile stack.

Steps:
1. Render the meeting-linked component with real typed clients.
2. Verify the synthetic-data badge, exact tenant/version, three ordered steps, safety validations, and command-boundary label.
3. Start, execute the next command, preview reset, verify reset is disabled, confirm the exact company, and reset.
4. Build the Web project and compare component structure/styles to the generated reference.

Expected: The operator sees one private, unmistakable control surface; destructive reset requires preview plus exact confirmation; loading/success/error and mobile states remain legible.

Observed: Three focused Web client/component tests passed and the Web build succeeded. The component uses the generated reference's dark card hierarchy, two-column sticky controls, warning-edged reset card, and single-column mobile actions. A live authenticated browser pass could not run because neither the configured Web listener (`5062`) nor SQL Server (`1433`) is available, and no credentials or test data were invented.

Artifacts: `docs/design/references/sales-meeting-demo-controls-reference.png`; `tests/VirtualCompany.Web.Tests/SalesMeetingDemoControlPanelTests.cs`.

Result: pass using the strongest safe automated substitute; live browser capture is blocked by local runtime availability.
