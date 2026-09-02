# UAT issue ledger

| ID | Severity | State | Evidence | Resolution / next check |
|---|---:|---|---|---|
| RESP-01 | P0 | Resolved | Repeating onboarding completion through the unscoped endpoint initially missed existing responsibility rows behind the ambient company filter and hit a unique constraint. | Responsibility reads remain explicitly company- and membership-constrained but ignore the unavailable ambient filter for this internal path. Integration test now completes twice and retains exactly 12 medium assignments. |
| RESP-02 | P0 | Resolved | Responsibility settings had no typed Web client or management surface. | Added typed read/preview/apply/upsert/remove transport and the company-scoped matrix page with backend-derived manage capability. |
| RESP-03 | P1 | Resolved | Replace-existing preset mode could otherwise overwrite assignments without a dedicated user acknowledgement. | Apply remains disabled until preview is present and the replacement checkbox is checked; rendered interaction test covers the gate. |
| RESP-04 | P1 | Resolved in code | Wide matrix tables would force horizontal mobile navigation. | Implemented stacked cards and single-column field/value rows below 680 px, plus 44 px controls and actions. |
| RESP-05 | P1 | Resolved | Onboarding did not carry company size through save, resume, completion, and routing. | Added localized size choices, typed request/progress/result fields, safe fill-missing completion, ambiguity routing, and repeat-completion integration coverage. |
| RESP-06 | P1 | Blocked by local runtime | SQL Server is not listening on localhost:1433 and the Web app is not running on localhost:5062; the in-app browser confirmed `ERR_CONNECTION_REFUSED`. | Component UAT covers desktop structure, member read-only behavior, and replace confirmation. Repeat authenticated desktop/mobile visual, keyboard, and focus verification when the repository runtime and database are available. |
| RESP-07 | P2 | Resolved | The settings surface needed an explicit explanation that assignment relevance is not authorization or tool access. | Added a persistent boundary note in both English and Swedish and covered it in the rendered owner test. |

