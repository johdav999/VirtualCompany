# Specialist Review navigation

Product: Virtual Company Web. Date: 2026-10-05 Europe/Stockholm. Build: rebuilt LocalRun Web snapshot, process 56916, real client localhost:5062 with existing API/database retained. Existing uncommitted work, including prior Sales navigation and freshness fixes, preserved.

## Cause and change

Today priority Review navigation selected the owning record only for the Sales lens. Finance, Marketing and Customers continued using the generic priority-evidence page, requiring an extra click to the action workspace. Company overview department cards also hard-coded the priority-evidence route.

Extend direct Review navigation to all four specialist lenses. Resolve authorized source links with the existing workspace-context helper, retaining company, record identifiers, Finance source selection, campaign filters, and dashboard return context. Company overview department Review buttons now use those same owning record links. Priority details remains explicitly available as a separate secondary link. Missing action links retain the existing evidence fallback. Company-wide ranked-priority and health-report evidence entrypoints retain their existing behavior. Existing destination permissions and business action policies remain authoritative; no new execution permissions or actions are introduced.

## Verification

90 focused Web tests passed, 0 failed/skipped. Coverage includes Review targets, actual click telemetry, invoice/supplier bill source filters, campaign/variant identifiers, case IDs, separate evidence links, missing-action fallback, department cards, authorization visibility, and existing specialist journeys. Obsolete tests expecting the extra navigation step were replaced with assertions for the requested direct behavior. LocalRun Web and isolated UAT builds succeeded; Web build has 6 existing nullable warnings. Scoped git diff --check passed.

Actual browser flows (headless Edge, 1985x837):

- Real company VC: Finance Review opens invoice e98428ac-44d9-4a18-810c-56c911073e8e directly; Save status and Submit for approval are present and enabled. Sales Review continues to open its existing opportunity directly.
- Existing isolated SQLite fixture: Finance opens the selected P06 invoice with Save status enabled; Marketing opens P05 campaign review with Approve content and Request revision enabled; Customer Support opens P08 case with Draft reply, Run triage and Resolve case enabled.
- All five specialist flows verify exact dashboard return and separately accessible Priority details.
- All three Company overview department Review flows open owning records directly and return to the Company lens.
- 390x844 Company overview has no page overflow.
- Browser page errors: 0. Captured Finance, Marketing, and Support destination screenshots inspected.

The current real company has no Marketing/Support priorities. Those browser checks therefore use the repository's existing disposable fixture, with configured test identity and disabled workers/provider operations. No records were added to the real company. Business action controls were inspected for availability, not executed; this is navigation acceptance rather than approval, payment, publication, reply delivery or provider validation. In-app browser control kernel was unavailable earlier in this session, so the user's existing tab was not operated.

Evidence: live-browser.json, fixture-browser.json, departments-browser.json, *-overview.png, *-action.png, fixture-company-mobile.png, results/specialist-navigation.trx.

Commands:

- `dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --filter "FullyQualifiedName~TodayWorkspaceComponentTests|FullyQualifiedName~CompanyHealthTests|FullyQualifiedName~FinanceOperationalJourneyTests|FullyQualifiedName~MarketingOperationalJourneyTests|FullyQualifiedName~SupportOperationalJourneyTests|FullyQualifiedName~SalesOperationalJourneyTests" --logger "trx;LogFileName=specialist-navigation.trx" --results-directory docs/verification/vcscreens/specialist-review-navigation-2026-10-05/results -v minimal`
- `node docs/verification/vcscreens/specialist-review-navigation-2026-10-05/probe-browser.mjs live`
- With fixture API 5339 and fixture Web 5078: `node docs/verification/vcscreens/specialist-review-navigation-2026-10-05/probe-browser.mjs fixture` then `node docs/verification/vcscreens/specialist-review-navigation-2026-10-05/check-departments.mjs`.

An initial broader fixture probe expected a Sales deal among its top-five priorities; this fixture instead ranks internal Work tasks/approvals there. The requested three specialist flows were verified in the fixture, and Sales regression was verified against the real company's opportunity. No production change was made to force fixture ranking.
