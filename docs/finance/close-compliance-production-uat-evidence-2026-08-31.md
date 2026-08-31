# Close and compliance production UAT evidence — 2026-08-31

## Product profile

- Product: Virtual Company finance web application
- Type: web/full-stack
- Revision: current Prompt 3–10 worktree
- Roles: finance owner/manager, accounting administrator, explicitly granted external accountant
- Locales and layout: English and Swedish; desktop and narrow
- Required assistive checks: keyboard-only, visible focus, landmarks/names, screen-reader status/error announcement, reduced motion
- Business timezone: Europe/Stockholm with UTC evidence timestamps

## Flow inventory

| Flow | Expected acceptance | Current evidence | Result |
| --- | --- | --- | --- |
| UAT-CC-001 month close | Finance user resolves every task/reconciliation, regenerates reports, reviews tax/compliance, signs, packages, and locks the exact hash. | Automated domain, policy, API, and Web contracts are in the proof matrix. | Live authenticated replay required. |
| UAT-CC-002 year-end | Independent review precedes lock; rollover posts once, reconciles, completes, and exposes original sources. | Deterministic correction/restore policy scenario plus year-end domain suite. | Live authenticated replay and SQL proof required. |
| UAT-CC-003 stale or incomplete evidence | Stale report, missing sign-off/evidence, incomplete package, ambiguity, or failed rollover shows a no-go and safe remediation; no destructive action is offered. | Readiness policy tests. | Automated pass; visual/announcement check required. |
| UAT-CC-004 accountant isolation | Accountant sees only explicitly granted company/period evidence and cannot lock, roll over, or infer another company. | Policy and isolation suites. | Automated proof required; signed-in accountant replay required. |
| UAT-CC-005 recovery | Worker restart is idempotent; missing/corrupt object blocks release; coordinated restore retains original hash and all source links. | Fail-closed verifier and runbook. | Operator recovery rehearsal required. |
| UAT-CC-006 subsequent event | New evidence reopens readiness, invalidates old report/package/sign-off, and a correction produces a new independently approved hash. | Deterministic policy scenario. | Automated pass; live flow required. |
| UAT-CC-007 locale/date/accessibility | EN/SV meaning matches, Swedish dates remain correct around DST/month/year boundary, narrow reading order remains intact, and status/errors are announced. | Resource and structural Web suites. | Browser/AT proof required. |

## Evidence capture procedure

Use an owned isolated host and seeded non-sensitive company. Capture route, role, locale, viewport, company and fiscal-period IDs, UTC time, Stockholm-local display time, readiness hash, source links, screenshot/recording checksums, and reviewer. Exercise 1440 px and 375 px widths. Include keyboard-only navigation, focus after refresh/error, 200% zoom, a screen reader, reduced motion, and dates immediately before/after a Stockholm DST transition plus month/year boundary.

Run every flow once as finance and the relevant read/review flows as the granted accountant. Repeat a cross-company URL/id substitution and confirm forbidden/not-found behavior reveals no existence or name. Do not use already-running unknown developer hosts or production data.

## Issue ledger

| ID | Severity | Flow | Summary | Status / release effect |
| --- | --- | --- | --- | --- |
| UAT-CC-001 | P1 | all browser flows | The preceding Prompt 9 session started isolated API/Web hosts, but the in-app browser security policy rejected the localhost reload before authenticated DOM, visual, keyboard, or screen-reader assertions. | Open environment limitation; current Prompt 10 browser evidence is absent and release is no-go. |
| UAT-CC-002 | P1 | recovery | No current-revision coordinated SQL/object restore with worker interruption and corrupt/missing object injection has been retained. | Open; release is no-go. |
| UAT-CC-003 | P1 | professional/provider | Qualified Swedish accountant approval and explicit approval of the export/manual-evidence-only provider boundary are absent. | Open; release is no-go. |

No live browser defect is claimed from an environment that could not execute the flows. The polish/UAT process therefore records the limitation as a release stop instead of substituting source inspection for authenticated user acceptance.
