# Company Today issue ledger

| ID | Severity | Flow | Type | Summary | Evidence | Acceptance / regression | Status |
|---|---|---|---|---|---|---|---|
| TODAY-FRESH-001 | P2 | Company Today > Refresh workspace > reload | Freshness defect | An eight-hour-old saved briefing makes a newly refreshed dashboard say it needs refresh | before-browser.json; user screenshot; after-browser.json; screenshots | Current summary replaces stale briefing copy; no misleading workspace stale banner after refresh/reload; source dates/warnings remain; 27 focused tests and real local browser pass | Verified |
