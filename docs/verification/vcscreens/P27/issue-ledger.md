# Findings and acceptance

| ID | Finding | Fix and evidence |
| --- | --- | --- |
| SC27-01 | Decimal serialization scale differed after persistence and invalidated saved checksums | Canonical decimal hashing and numeric output equality; API save/duplicate/reopen and SQL Server reproduction pass |
| SC27-02 | Missing numeric fields could otherwise default to zero | Required JSON driver/cash properties, nullable editor fields and explicit validation; missing opening cash and year cash inputs reject |
| SC27-03 | A late preview could arrive after an input edit | Preview request revision guard; company cancellation and retained value clearing are exercised by rendered tests |
| SC27-04 | Browser evidence showed a literal version expression in option text | Explicit revision label; rebuilt browser replay checks option labels |
| SC27-05 | Checkpoint detail initially displayed a raw owner ID | Retained owner names in relational checkpoints and typed preview, checksum-bound; native owner rename does not change the captured name |
| SC27-06 | Filtering hidden source versions could hide later history pages | Explicit cursor/next flag; 21-version bounded paging test and retained comparison choices |
| SC27-07 | SQL test initially tried to mutate an EF primary key before reaching the database constraint | Native foreign forecast fixture and direct SQL update now exercise composite source FK rejection; child-company FK and concurrent successor checks retained |
| SC27-08 | Fixed test clock gave all versions equal timestamps; test incorrectly assumed the original would be on page two | Assert complete, distinct cursor coverage with stable ID tie ordering, and reopen the exact original |

Diagnostic compiler/test/browser artifacts are retained separately from accepted final results. One diagnostic test rebuild overlapped an active test host and hit a DLL lock; it was rerun after that host completed. No user process was terminated.

Local browser acceptance uses fresh headless Edge and real Web/composed API. In-app automation's Windows ACL helper remains unavailable. Production migration deployment and named human release approval remain separate gates.

SC27-09: Native period-picker tests still expected three historical periods. Updated the expectations for Today/Week/Month/Quarter/Year/Multi-year and explicitly checked the multi-year event, including all five weekly role lenses.
