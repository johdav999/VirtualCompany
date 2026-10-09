# Meeting booking on pipeline issue ledger

| ID | Severity | Flow | Type | Summary | Acceptance / regression | Evidence | Status |
|---|---|---|---|---|---|---|---|
| SALES-MEETING-001 | P2 | Converted lead > Pipeline card > Opportunity | Data continuity | Lead bookings are absent from pipeline and opportunity because their queries do not project meeting invitations | Show the same existing invitations on card and opportunity, including bookings without DealId created before conversion; retain company isolation, explicit deal precedence, status/timezone, and record/dashboard returns | before-browser.json, after-browser.json, screenshots, API and Web TRX | Verified on rebuilt localhost app and isolated tests |
