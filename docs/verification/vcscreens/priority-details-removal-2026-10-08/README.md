# Remove Priority details — 2026-10-08

User review Comment 1 requested removal of Priority details from Company, Finance, Sales, Support and Marketing panels, and removal of the detail screens from the solution. The original observed route was the existing Company dashboard on port 5062 for company `43e6a825-d1b7-429a-8608-7e668087d005`.

The standalone `PriorityDetails` page, `PriorityEvidenceDetail` component and stylesheet were deleted. Their route builders, localized detail labels, shared layout return banner and Finance/Sales/Marketing/Support/Work return parameters were removed. Department/priority reviews and Company health risk rows now open the owning record, with Overview and Company health returns retained. A missing record link falls back to the owning department workspace. Existing Work evidence timestamps and explicit follow-up completion remain available.

Obsolete detail-screen tests were removed. Priority change tracking and its shared fixture were retained as `TodayPriorityChangesTests`. Updated assertions verify direct records and absence of the removed route. The current route inventory and screen register reflect the removal; prior release evidence remains historical.

## Product profile and acceptance

Local interactive Blazor Web with the existing company and API on port 5301. Five lenses: Company, Finance, Sales, Customers/Support and Marketing. Desktop 1857×837 matches the supplied comment viewport; narrow 390×844. All browser actions are read-only. Native source data and authorization are unchanged.

| ID | Type / scope | Expected and observed | Regression / evidence | State |
| --- | --- | --- | --- | --- |
| PD-01 | Requested removal / five role panels | No Priority details links and no link targeting the retired page; direct review remains usable | Five-role rendered tests; before/after browser JSON and desktop captures | See final verification JSON |
| PD-02 | Requested removal / solution routes | Detail page/component/style and route helpers removed; old URL has no registered page | Route attribute test, source audit, retired-route capture | See final verification JSON |
| PD-03 | Navigation compatibility / Company health and Work | Health risk opens actual source with report return; existing independent Work follow-up still completes only explicitly | CompanyHealthTests, PriorityWorkActionTests, journey route tests | 105 focused rendered/navigation tests passed |

Web build passed with six existing nullable warnings. The initial focused test run found one obsolete assertion expecting a Company review to use the removed page; the final run passed all 105 cases with no skips. `focused-web-final.trx` is authoritative; the earlier TRX is diagnostic. All five running browser perspectives show zero retired/detail links, three available direct review destinations remain usable, the Company view fits 390px, and the deleted route returns HTTP 404. The first after script expected an HTML not-found page, whereas the native server correctly returns an empty 404 response; the final adapter verifies that status directly. `after-browser.json` and `verification.json` record final acceptance with zero browser page errors.

In-app browser automation failed at Windows sandbox ACL initialization before interaction. Fresh installed headless Microsoft Edge is the explicit safe substitute, using the same existing Web/API/company. The before script records the original links and screenshots; the after script checks removal and direct review routes, narrow containment and the retired URL. Source/reference design is reused for this bounded deletion; no new screen or major redesign was introduced.

Only the confirmed old Web PID 21384 was replaced. API PID 5636 and company records were preserved. The new Web host is intentionally left running on 5062 for the user's browser; exact new DLL/PID is in `web-host.json`. Reload the open dashboard to see the change.
