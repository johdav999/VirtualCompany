# Company Finance indicator navigation — 2026-10-08

The Company Overview Finance indicators now link directly to Customer invoices, Supplier bills, and Bank reconciliation. Links remain available at zero counts. Existing company scope and exact Overview return navigation are retained. Labels use the existing primary link color, underlining, and keyboard focus styling.

## Product profile

- Product: Virtual Company, local Blazor Web application.
- Baseline: HEAD `9a356c7901abc2044f6ecb6a4e8a5ebf64ceab5b` plus the existing uncommitted P11–P30 and review work; preserved in the same checkout.
- Role and data: development Alice Admin, existing company VC (`43e6a825-d1b7-429a-8608-7e668087d005`). Observed counts: 3 overdue invoices, 0 due supplier bills, 0 reconciliation exceptions.
- Environment: Web port 5062, existing API port 5301. See `web-host.json` for the exact tested DLL and recorded process; only the identity-verified prior Web host was replaced. The new host remains running for user review.
- Evidence adapter: read-only headless Microsoft Edge through installed Playwright. The interactive browser adapter previously failed during ACL initialization; this is the documented substitute. Viewports: 1857×837 and 390×844.
- Build: `dotnet test` compiled Web and dependencies successfully; six existing Web nullable warnings, no build errors.

## Acceptance evidence

The baseline `before-browser.json` and `before-finance-card.png` show three plain-text indicators and no indicator links. The after run navigated each link with keyboard Enter, checked the canonical destination route and selected company, and clicked the exact Company Overview return. Supplier bills and reconciliation remain navigable at zero counts. All three journeys passed with no browser page errors. Desktop and narrow card screenshots were visually inspected; all three underlined labels fit and the narrow dashboard has no horizontal overflow.

| Entry | Destination | Result |
|---|---|---|
| Overdue invoices | `/finance/invoices` | Passed |
| Supplier bills due | `/finance/supplier-bills` | Passed |
| Reconciliation exceptions | `/finance/accounting/reconciliation` | Passed |

Regression command:

```powershell
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore --filter 'FullyQualifiedName~CompanyHealthTests|FullyQualifiedName~FinanceJourneyRoutesTests' --logger 'trx;LogFileName=finance-links.trx' --results-directory docs/verification/vcscreens/company-finance-links-2026-10-08
```

18 tests passed, none failed or skipped. New component coverage checks all three canonical routes, company scope, a filtered exact Overview return, and zero counts. Existing coverage checks unavailable counts and restricted/unavailable departments.

Browser replay: `node verify-browser.mjs after` using the repository environment's installed Playwright runtime. Machine-readable results are in `after-browser.json`; screenshots capture each destination and both card sizes. No business actions or provider operations were executed.

## Issue ledger

| ID | Severity | Flow | Finding and owner | Acceptance / regression | Evidence | Status |
|---|---|---|---|---|---|---|
| CF-001 | P2 | Company Finance indicator → owning Finance screen → Company Overview | Aggregate indicator labels lacked direct navigation; CompanyHealthSummary/CompanyHealthPresentation | All three labels link to canonical screens, retain company and exact return, work at zero, support keyboard activation, and fit on narrow screens; CompanyHealthTests plus browser replay | `before-browser.json`, `after-browser.json`, `finance-links.trx`, card and destination PNGs | Verified |

Scope complete. Subsequent reviews should continue from this checkout and the running host recorded here; earlier P30 and Priority-details retirement evidence remains historical.
