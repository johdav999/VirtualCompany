# P13 issue ledger

| ID | Severity / flow | Finding and owner | Acceptance and regression evidence | Status |
|---|---|---|---|---|
| P13-01 | P1 / all | JSON Guid values were read through quoted ToString output, preventing typed associations | Parse typed Guid/string values; all six authenticated journey cases retain associations; final API/wire/SQL checks | Verified |
| P13-02 | P2 / F13-01 | Source return lost Sales proposal tab; retained review reason was absent and manual review implied an amount limit | Exact Alex v2 → Back to business record preserves tab=files; owning approval reason shown with no invented threshold; `sales-returned-proposal`, focused return tests | Verified |
| P13-03 | P1 / F13-03b | Operational bill IDs were linked to intake-only detail; intake IDs were absent from typed validation/backfill | Both actual record types accepted, same-company association only, correct owning route and source filter; native intake API and SQL backfill, operational browser decision/return/refresh | Verified |
| P13-04 | P2 / F13-05 | Bill header/detail min-content and warning reference overflowed narrow desktop/mobile columns | Shrinkable detail grid, wrapping header and native alert text; final mobile document width equals scroll width, desktop replay; earlier failed assertion retained as diagnostic | Verified |
| P13-05 | P2 / F13-04 | Automatically selected first brief caused campaign review to show brief-only work | Campaign-only query uses campaign panel; explicit brief query uses exact brief panel; both browser decisions/refresh and Marketing tests | Verified |
| P13-06 | P3 / F13-01 | Empty retained worker output appeared blank | Explicit missing-output message; shared component tests and preserved P11 contribution data | Verified |

No open local P0/P1 finding. Real tenant migration/provider delivery, human Release 2 approval and earlier native/physical/statutory acceptance remain independent gates, not automatically satisfied by this packet. Historical P12 broad-suite Sales failures remain documented there; P13 does not claim the entire repository test matrix is green.
