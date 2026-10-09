# P22 issue ledger

| ID | Severity / flow | Finding and owning correction | Acceptance / state |
| --- | --- | --- | --- |
| P22-01 | P1 / F22-01 | Duplicate/conflicting source versions and repeated/overlapping outcomes could produce misleading sums. MarketingManagementService deduplicates costs, reconciles native allocations/model versions and withholds conflicts/incomplete economics. | Native model/version/duplicate/overlap/currency/guardrail tests and browser source reconciliation: verified |
| P22-02 | P1 / F22-02 | Mutable/live-only budget detail would lose assumptions and allow conflicting revisions. Immutable proposal owner stores report/assumptions/results/checksum and audit with unique request/series/successor keys. | Native save/reopen/correction/retry/access and real SQL Server migration/concurrency/immutability: verified |
| P22-03 | P1 / F22-03–04 | Retained links or late/forbidden responses could restore the wrong cohort/company. Typed client and pages validate context, cancel stale reads and separate retained/current data. | Rendered company-switch/filter-rejection/forbidden/export tests and exact native browser returns: verified |
| P22-04 | P2 / F22-04 | Initial CSV call named a nonexistent JS function. Web now calls native downloadReport with freshly authorized server CSV. | Rendered JS handoff and revoked access tests: verified |
| P22-05 | P2 / F22-05 | First captures showed card text/control edges too close to borders. Added native panel/control padding, compact headings, contained focusable table and narrow stacking. | Final rebuilt browser replay and visual captures: verified |
| P22-06 | Environment / all | CUA and normal shell fail before inventory/startup at Windows deny-read ACL helper. | CUA remains unavailable; approved working shell and fresh headless Edge provide explicitly scoped substitutes |

Initial API/Web assertion failures were decimal-format and canonical-route test expectations; corrected to parsed numeric reconciliation and the existing Marketing route contract. Initial UAT rebuild raced the owned testhost lock; accepted UAT build compiles only the adapter against completed dependencies. These diagnostic runs are retained and excluded from final acceptance.
