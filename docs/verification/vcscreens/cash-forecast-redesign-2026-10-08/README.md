# Cash forecast redesign — 2026-10-08

The cash forecast now has a clear financial hierarchy: dated header and actions, aligned filters, four summary cards per currency, a readable cash-movement table, and a compact evidence/assumptions/action rail. Gradient styling and dense button navigation are replaced by a report-specific light surface and active report tabs. The shared operational report also gives receivables/payables aging the same panel and table treatment. Other Finance screens retain their default shell.

## Profile and reference

- Product: Virtual Company, local Blazor Web; existing VC company `43e6a825-d1b7-429a-8608-7e668087d005`, development Alice Admin.
- Baseline: HEAD `9a356c7901abc2044f6ecb6a4e8a5ebf64ceab5b` plus existing uncommitted phased/review work, preserved in this checkout.
- Environment: Web 5062 and existing API 5301. Only the recorded, identity-verified Web host was replaced; API PID 29816 and existing configuration/data were preserved. `web-host.json` records the final tested snapshot and PID 43920. The host remains running for user review.
- Evidence adapter: read-only headless Microsoft Edge via installed Playwright, plus saving the actual browser CSV download. The interactive browser adapter previously failed during ACL initialization; headless Edge is the explicit substitute.
- Viewports: desktop 1857×837, phone 390×844.
- Flows: forecast summary/evidence, filters/reload, record review/return, CSV export, empty currency state, keyboard disclosures and scrolling, and retained aging reports.
- Generated reference: `docs/design/references/finance-cash-forecast-reference.png`, generated before UI implementation with the built-in imagegen tool. Design prompt: `docs/design/references/finance-cash-forecast-reference-prompt.md`. The reference follows `docs/design.md` and is not a runtime asset. Final desktop/mobile captures were visually compared with it.

## Findings and implementation

The previous report relied on generic `.vc-panel` borders without supplying padding, typography or financial layout. Its shared shell also applied a large decorative gradient. The redesign adds scoped report styling and an opt-in `ReportStyle` variant on the existing Finance shell/navigation. It retains existing routes and authorization, calculation, source-filter and return helpers.

Actual company data contains 18 cash account sources, mostly zero balances, and 18 coverage notes. Rendering every source and note in full overloaded the first redesign iteration. The final version shows up to three nonzero cash accounts initially, while the complete dated account list remains accessible in a native disclosure. Coverage remains an amber warning with a note count and expandable complete evidence. Unknown opening/projected cash still reads **Unavailable**. The report does not combine currencies or introduce a statistical forecast.

The cash-movement table includes inflow/outflow labels, UTC due dates, right-aligned remaining amounts, recorded status and scoped Review links. Header actions provide Refresh and Download CSV. Long calculation meaning/assumptions remain available under Calculation details. CSV still rereads the current authorized report before download.

## Acceptance results

10 focused Web tests passed with no failures/skips; Web and dependencies built successfully. Existing warnings remain. Final TRX: `forecast.trx`. New component regression covers separate currencies, unavailable cash, negative projected cash, inflow/outflow classification, horizon exclusion, full collapsed source and coverage evidence, observation time unavailable and active navigation. Existing checks retain source context, filters, safe CSV, refreshed export, revoked access and durable returns.

```powershell
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore --filter 'FullyQualifiedName~FinanceOperationalJourneyTests|FullyQualifiedName~FinanceJourneyRoutesTests' --logger 'trx;LogFileName=forecast.trx' --results-directory docs/verification/vcscreens/cash-forecast-redesign-2026-10-08
```

The final browser replay passed nine acceptance groups with no page errors:

1. Four cash cards retain baseline amounts: SEK 109,750 starting cash, SEK 63,262 inflows, SEK 0 outflows and SEK 173,012 projected cash. The same three invoice records remain included.
2. Native keyboard disclosures reveal all 18 cash accounts and all 18 coverage notes; calculation details expand with Enter.
3. An actual refreshed `finance-forecast.csv` browser download was physically saved to `forecast.csv` and checked for the real forecast values.
4. SEK currency and a seven-day horizon survive Apply filters and page reload.
5. An invoice Review keeps the company and returns to the filtered forecast. Query values are compared semantically, allowing equivalent percent-encoding normalization.
6. JPY has no obligations; starting/projected cash remain unavailable, while known empty inflow/outflow totals can be zero. Capture: `after-empty.png`.
7. At 390px the page has no horizontal overflow; the table scrolls inside its focusable panel with the keyboard.
8. Receivables aging remains reachable with its correct title and report layout.
9. Payables aging remains reachable with its correct title and report layout.

Machine-readable evidence: `before-browser.json`, `after-browser.json`. Screenshots: `before-desktop.png`, `after-desktop.png`, `after-narrow.png`, `after-empty.png`. Replay: `node verify-browser.mjs after` with the installed runtime named by the script. No provider commands or business mutations were executed.

## Issue ledger

| ID | Severity | Flow / owner | Baseline finding | Acceptance / regression | Evidence | Status |
|---|---|---|---|---|---|---|
| CF-UI-001 | P2 | Operational report / FinanceOperationalReport and shared Finance shell | Flat, unpadded report blocks and decorative gradient obscure financial hierarchy | Reference-led cards, aligned controls, readable dense table, scoped light report surface; desktop/phone inspection | Reference image/prompt, before/after PNGs | Verified |
| CF-UI-002 | P2 | Source and assumption review | Long repetitive account/coverage information overloads the screen | Concise warning, meaningful initial account amounts, all source/coverage/calculation details available through keyboard-accessible disclosures | Browser JSON, component regression | Verified |
| CF-UI-003 | P2 | Report workflows | Redesign must preserve real amounts, filters, returns, scope, unavailable states and export | Nine browser groups, ten tests, physically saved CSV | TRX, browser JSON, CSV, empty/narrow captures | Verified |

Scope complete. Verification covers this local company and the isolated fixtures; it does not assert a release-wide production approval. Continue from the same checkout and the live host recorded in this packet.
