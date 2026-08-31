# Unified close workspace UAT evidence — 2026-08-30

## Product profile

- Product: Virtual Company finance web application
- Type: web/full-stack
- Revision: current Prompt 3–9 worktree
- Local launch: `dotnet run --no-build --project src/VirtualCompany.Api --launch-profile http`, then `dotnet run --no-build --project src/VirtualCompany.Web --launch-profile http`
- Roles in scope: finance owner/manager and explicitly granted external accountant
- Evidence: generated desktop visual reference, compiled Razor output, focused policy/client/surface tests, and launch logs

## Flow evidence

### FLOW-CLOSE-001 — Finance owner opens and acts from the cockpit

Expected: company and period stay explicit; every blocker shows owner, status, evidence, timestamp, and safe next action; completion and lock use backend allowed actions.

Observed: focused Web and Finance tests passed. A fresh isolated SQL Server database applied all 280 migrations after correcting two argument/name defects in `20260830190000_CompleteFinancialReportSuite`, and both the API and Web hosts started on isolated local ports. The in-app browser opened a UAT tab, but its local-URL security policy rejected the final page reload before an authenticated DOM or visual assertion could run.

Result: automated acceptance and full migration-chain startup passed; authenticated browser replay was blocked by the browser environment.

### FLOW-CLOSE-002 — Stale lock rejection

Expected: a journal/provider change invalidates the displayed hash; lock is rejected; the error remains visible; authoritative evidence refreshes.

Observed substitute: client and component source test verifies the version/hash request, stable reason-code propagation, `accounting_close_evidence_stale` branch, refresh command, and workspace reload.

Result: automated acceptance passed; live replay was blocked by the browser environment.

### FLOW-CLOSE-003 — Accountant portfolio to isolated close evidence

Expected: company context remains explicit and links to the same selected-period evidence; accountant cannot acquire operational close authority.

Observed substitute: portfolio surface test plus policy tests verify the explicit company/period deep link, grant isolation, and absence of lock/package/rollover actions for the accountant role.

Result: automated acceptance passed; live replay was blocked by the browser environment.

### FLOW-CLOSE-004 — English/Swedish desktop and narrow accessibility

Expected: both locales provide equivalent labels; desktop and narrow layouts preserve reading order; keyboard and screen-reader landmarks are present.

Observed substitute: both resource sets compile; Razor contains labelled regions, status/alert semantics, explicit labels and native controls; the scoped CSS has 1200 px and 760 px breakpoints and reduced-motion behavior.

Result: structural acceptance passed; authenticated visual/keyboard/screen-reader replay was blocked by the browser environment.

## Issue ledger

| ID | Severity | Flow | Type | Summary | Evidence | Acceptance / regression | Status |
|---|---|---|---|---|---|---|---|
| UAT-CLOSE-001 | P1 | all | migration defect | `CompleteFinancialReportSuite` used the wrong fiscal-period table name and an ambiguous positional `AddForeignKey` overload that treated `id` as a schema. | failed isolated startup logs followed by a successful 280-migration startup | Fresh database applies every migration and starts API. | resolved |
| UAT-CLOSE-002 | P2 | all | test-environment limitation | The in-app browser URL policy rejected the final localhost page reload after both hosts started. | browser policy result; API/Web startup logs | Repeat authenticated desktop, narrow, keyboard, and screen-reader replay in an environment that permits the local UAT URL. | open; environment only |

No Prompt 9 product defect was found by the strongest executable checks. Live desktop/narrow screenshots and assistive-technology replay remain explicitly unverified because of UAT-CLOSE-002; the migration blocker itself is resolved.
