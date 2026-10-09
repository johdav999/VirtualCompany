# Company weekly and monthly overview correction

Verified locally on 2026-10-04 for company `43e6a825-d1b7-429a-8608-7e668087d005`, week containing 2026-09-28 and October 2026. This is a focused user-review correction to P19/P20, not release approval.

## Root cause

The authenticated local API already returned all five authorized contributions/sections. Weekly rendered lengthy CEO commitments, source definitions and risks before the department business measures. Monthly put business sections below management/history content and limited headline results to `Take(4)`. The initial screen therefore looked like a Work summary despite the company evidence being present. There were no contribution diagnostics in the inspected response; some historical movement and SLA measures genuinely lack evidence and remain unavailable.

## Change

A shared Company overview now appears before detailed commitments and monthly review history. Five cards present Finance, Sales, Marketing, Customers and Company commitments, with recorded business values, timing, owning review/source links and expandable additional facts. Monthly no longer truncates its headline results. The overview uses the authorized existing period payload; currencies are separate and unavailable sources do not become invented zeros. A saved monthly review uses its retained payload and preserves its snapshot in source return URLs.

Source files: `CompanyPeriodOverview.razor`, its scoped CSS, `CompanyPeriodOverviewPresentation.cs`, `WeeklyWorkspace.razor` and `MonthlyWorkspace.razor`. No API contracts, database schemas or source calculations changed.

## Verification

- Final focused Web tests: **49 passed, 0 failed, 0 skipped**. Includes source links/company and period context, foreign-company clearing, failed/unauthorized contributions, separate currencies, untruncated results and retained monthly values. [Test output](tests.log), [TRX](results/company-period-overview.trx).
- Final Web snapshot build: success, zero errors, six existing warnings. Normal repository `client.ps1 -Port 5062` restart serves the final change on the existing local Web port. PID at verification: 51464; API 5301 was preserved. [Build log](web-build-runtime.log).
- Real running Web/API, configured development identity, disposable headless Edge: both periods show all five areas and actual company/date headings before detailed content, keyboard focusable company-context source links, weekly cash drill-down and exact return, weekly-to-monthly company-context navigation. No page errors. [Browser record](after-browser.json), [replay script](verify-browser.mjs).
- Figures reconcile with inspected authorized source payload: cash **109750 SEK** in both periods; monthly receivables **263262 SEK**, payables **94585 SEK**, current sales pipeline **1000 USD**. These use recorded source cutoff semantics, not future forecasts or combined currency totals.
- Desktop 1996×881 and narrow 390×844: responsive cards and no page-level horizontal overflow. Visually inspected: [weekly desktop](after-weekly-viewport.png), [monthly desktop](after-monthly-viewport.png), [weekly mobile](after-weekly-mobile-viewport.png), [monthly mobile](after-monthly-mobile-viewport.png). Full-page variants show all cards and preserved detailed sections. Before images remain for comparison.

Control of the user's in-app tab could not initialize because of the Windows sandbox ACL helper failure. The browser evidence above comes from fresh headless Edge against the actual running company. Earlier broad P19/P20 five-role acceptance and independent provider, deployment, physical output, statutory and human approval gates remain separate.

## Design reference

Used built-in imagegen for the required visual reference. [Exact prompt](../../../design/references/company-period-overview-2026-10-04-prompt.md); [saved reference image](../../../design/references/company-period-overview-2026-10-04-reference.png). Native Razor/CSS uses the reference for card hierarchy, spacing, colors and source actions.

## Handoff

Changes remain in this checkout alongside existing uncommitted P11–P21 work. Preserve the overview and retained-review semantics during later prompts. No P22–P30 implementation is included.
