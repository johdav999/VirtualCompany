# Specialist navigation issue ledger

| ID | Severity | Flow | Type | Summary | Acceptance / regression | Evidence | Status |
|---|---|---|---|---|---|---|---|
| NAV-FINANCE-001 | P2 | Today Finance > Review | Navigation | Generic evidence adds a step before invoice/bill actions | Primary Review opens the selected record with Finance source and dashboard return, evidence separate | live-browser.json, fixture-browser.json, specialist-navigation.trx | Verified |
| NAV-MARKETING-001 | P2 | Today Marketing > Review | Navigation | Generic evidence adds a step before campaign/content actions | Primary Review opens owning campaign/content/report with retained IDs, content actions available, evidence separate | fixture-browser.json, departments-browser.json, specialist-navigation.trx | Verified in isolated fixture |
| NAV-SUPPORT-001 | P2 | Today Customers > Review | Navigation | Generic evidence adds a step before case actions | Primary Review opens selected case with drafting/triage/resolution controls, exact return and evidence separate | fixture-browser.json, departments-browser.json, specialist-navigation.trx | Verified in isolated fixture |
| NAV-DEPARTMENTS-001 | P2 | Company overview department > Review | Navigation | Department primary Review still opens generic evidence | Finance/Marketing/Support owning record opens directly and returns to Company overview | departments-browser.json, CompanyHealthTests | Verified in isolated fixture |
