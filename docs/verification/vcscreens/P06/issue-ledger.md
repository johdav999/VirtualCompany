# P06 issue ledger

| ID / severity | Flow and observation | Resolution / acceptance / regression |
| --- | --- | --- |
| P06-01 / P1, verified | Report rows ignored configured Finance source policy; inherited Fortnox UI filtering hid Today's operational drill-down records. | Owner applies FinanceRecordSourcePolicy; typed reads forward configured/explicit source and links carry financeSource. Nine owner and nine Web journey checks plus browser source detail replay pass. |
| P06-02 / P1, verified | Retained review/filter controls rendered statically; intended save/filter handlers could not run. | Existing pages use InteractiveServer. Actual invoice save/reload, policy refusal and reconciliation keyboard filter/reload pass. No command owner replaced. |
| P06-03 / P2, verified | Due-obligation priorities lacked DueUtc, losing their deadline/ranking evidence. | Contributor includes recorded due time; composed API Today deadline assertions pass. |
| P06-04 / P1, verified | Local booked supplier bill was displayed as provider-confirmed Sent to Fortnox. | Provider completion requires imported Fortnox source or recorded successful provider action. Manual booking says retained booking records. New manual-booking regression and original provider-backed action tests pass; browser step is Upcoming. |
| P06-05 / P2, verified | Narrow shared Today header metadata caused document width 368 at CSS viewport 312. | Metadata wraps at narrow breakpoint; measured document width 300 after rebuild/reload. Desktop and mobile screenshots inspected. |
| P06-06 / environment, recovered | Initial fixture seed foreign-key failure; Web sandbox DPAPI/EventLog startup failure; transient Roslyn UAT compiler failure. | Fixture uses canonical bank/approval binding and approved parent state. Serial build passes. Owned foreground Web hosts ran outside verified sandbox boundary; exact PIDs and cleanup recorded. API /health reports degraded optional media; Web roots return 200. API root 404 is not a readiness endpoint. |
| P06-G1 / acceptance unverified | CSV payload/handoff passes; physical browser download completion timed out. | Keep physical save unverified; no user approval inferred. Latest-authorized payload and revoked-scope rejection verified automatically. |
| P06-G2 / P07 prerequisite | Native accounting setup/period/close not configured in current browser fixture. | Review/entry path verified; P07 must seed governed ledger/accounts/period and verify posting, locked periods, close and separate statements. |
| P06-G3 / external acceptance | Controlled provider, production SQL Server/deployed tenant, human/statutory approval not exercised. | Treasury policy/authorization/duplicate/recovery tests pass locally; no live payment, external dispatch or release approval claimed. |

No open local P0/P1 implementation blocker remains within P06 scope. Existing earlier-prompt working-tree changes remain intact.
