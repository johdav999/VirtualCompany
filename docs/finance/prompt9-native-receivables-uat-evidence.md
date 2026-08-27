# Prompt 9 native receivables UAT evidence

Date: 2026-08-26  
Operator: Codex  
Environment: ASP.NET Core Web + API, isolated SQL Server LocalDB database `virtualcompany-prompt9`, desktop in-app browser

## Product profile

- Application: Virtual Company finance workspace
- Area: customer billing, native invoice draft/issue, recurring billing, collections, and issued-invoice lifecycle
- Primary actor: finance manager or company administrator
- Safety boundary: calculations, readiness, approval linkage, issue, delivery, correction eligibility, and collection recommendations remain server-authoritative
- Locales: English and Swedish (`en-US`, `sv-SE`)
- Viewports checked: desktop default and 390 × 844

## Screenshot-first comparison

The implementation was compared with the checked-in references:

- `docs/design/references/native-invoice-editor-issue-reference.png`
- `docs/design/references/native-receivables-collections-reference.png`

The delivered UI preserves the references' important hierarchy: calm light workspace, compact receivables tabs, left-to-right invoice progression, editor/preview separation, KPI cards, collections queue/detail split, and restrained blue/orange/red status language. Product-native finance navigation and Laura evidence/action boundaries were retained instead of copying decorative reference details literally.

## Browser evidence

| Flow | Evidence | Result |
|---|---|---|
| New native invoice | `/finance/invoices/new?companyId=...` rendered details, lines, authoritative preview empty state, readiness, approval, statutory series/period controls, and disabled issue action while accounting setup was incomplete | Pass |
| Receivables collections | `/finance/receivables?view=collections&companyId=...` rendered KPIs, filters, queue/detail empty states, company-preserving navigation, and explicit accounting prerequisite error | Pass |
| Company context | All five receivables navigation links retained the selected company ID | Pass |
| Localization | Dynamic invoice title initially exposed a raw resource key; English and Swedish keys were added and the browser then rendered `New customer invoice` | Fixed and passed |
| Desktop visual | Invoice editor and collections workspace matched the reference hierarchy without overlapping controls or clipped primary actions | Pass |
| 390 px responsive | Collections had no document-level overflow. Invoice line items initially forced a 994 px document; the line editor was converted to an accessible two-column mobile card layout | Fixed and passed (`innerWidth=390`, `document.scrollWidth=375`) |
| Accessibility | Semantic headings/nav/search/table markup, status/alert regions, explicit mobile line-input labels, keyboard-native buttons/links/inputs, and visible focus-capable controls | Pass |
| Runtime console | Fresh browser runs after the final rebuild reported no warnings or errors | Pass |

## State coverage

The isolated browser database intentionally contained a newly created company and therefore exercised honest prerequisite and empty states rather than fabricated finance data. Data-bearing and sensitive transitions are covered by focused typed-client and surface tests for company scoping, offline mutation rejection, server readiness/correction checks, approval links, localization parity, and screenshot-reference presence.

## Residual observations

- Full issue, delivery, correction, dispute, promise, reminder, and recurring status transitions require an accounting-configured company with customers and issued invoices. The UI exposes these only when the backend returns eligible state.
- The standard Docker SQL Server dependency was unavailable on this workstation, so browser UAT used LocalDB with the same SQL Server migrations and an isolated database.
