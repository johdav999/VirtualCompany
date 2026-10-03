# P07 issue ledger

| ID / severity | Observation | Resolution and evidence |
| --- | --- | --- |
| P07-01 / P1 | Accounting inner links discarded period/source/Overview context; journal links used an ignored entryId. | Scoped accounting route helper and actual journalId; Web regression checks and exact browser statement → journal → statement replay. |
| P07-02 / P1 | Retained accounting controls rendered statically. | Existing handlers use InteractiveServer; close completion persisted and reloaded through the owning API. |
| P07-03 / P1 | Statement snapshot/comparison/account selection was not durable and CSV could use cached authorized data. | URL selection, exact snapshot pinning, refreshed authorized export and denied-export no-download tests; browser reload and owning CSV source reconciliation. |
| P07-04 / P1 | Always persisting the same statement URL caused server-rendered 302 self-redirect loops on return/reload. | Equality guard with a navigation-history regression; rebuilt browser journal round trip and reload succeed. |
| P07-05 / P2 | Close attention count included successful category_ready checks; pending tasks were omitted from not-started. | Owning projection excludes success checks; browser refresh shows two true blockers, zero attention warnings and correct pending/completed counts. |
| P07-06 / P2 | Close action-center /inbox and accountant utility links fell back to Overview. | Canonical /work and explicit utility route handling; route tests and browser Work/portfolio destinations. |
| P07-07 / P1 | Report/journal reads could retain prior scope or broaden missing fiscal periods. | Read-version/location guards, company evidence clearing and explicit unavailable period error; scope/revocation/late-response tests and browser restricted/company checks. |
| P07-E1 / recovered fixture | Initial snapshot regeneration rejected a locked period; subsequent incomplete mappings failed; journal seed lacked native PostingDate. | Correct fixture order: mappings, dated posted ledger, regenerate while closed but unlocked, then owning lock command. Production lock and statement engines unchanged. Initial failure logs retained. |
| P07-E2 / recovered fixture | Accountant membership without a grant was denied; creator self-approval was refused by the existing independent-approval domain rule (surfaced as HTTP 500). | Fixture creates a grant and a distinct authorized synthetic administrator approves it through the owning API. No grant or read policy bypass. Existing HTTP error mapping for that refused grant command is outside this accounting read/close adaptation. |
| P07-E3 / recovered environment | Windows sandbox denied process/TCP inspection and Web key/EventLog access; stale owned hosts locked outputs. | Normal builds/tests retained; bounded foreground localhost hosts ran across the verified process boundary, with exact PID cleanup. |
| P07-G1 / acceptance unverified | Browser download event timed out after the Export control ran. | Current authorized CSV payloads match source calculations; refreshed handoff/revocation tests pass. Physical saved-file completion remains unverified. |
| P07-G2 / independent gates | Native print/PDF, production SQL Server, deployed tenant, live provider and human/statutory acceptance not exercised. | Existing print callback/output styles and authority tests remain; no physical output, settlement, statutory opinion or release approval claimed. |

No unresolved local P0/P1 implementation blocker remains within P07 scope. Previous-prompt files and changes are preserved.
