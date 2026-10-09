# P21 issue ledger

| ID | Severity | Finding | Acceptance | State |
| --- | --- | --- | --- | --- |
| P21-01 | P1 | Management cohort definitions and source reasons absent | Period/currency scoped native report with included opportunity evidence and explicit gaps | Verified by native/SQL/rendered/wire substitute; live browser blocked |
| P21-02 | P1 | Forecast aggregates do not retain opportunity inputs | Future native captures reconcile; legacy coverage explicit | Verified by native/SQL/rendered/wire substitute; live browser blocked |
| P21-03 | P1 | No bounded reproducible capacity proposal | Versioned assumptions/results, current Sales scope, concurrency and audit | Verified by native/SQL/rendered/wire substitute; live browser blocked |
| P21-04 | P1 | Report/review/planning navigation absent | Typed authenticated UI with exact opportunity/review returns and retained snapshot payload | Verified by native/SQL/rendered/wire substitute; live browser blocked |
| P21-05 | P2 | Extremely small positive hours per opportunity cause decimal overflow and HTTP 500 | Unrepresentable capacity returns 400 with no proposal/audit; valid fractional assumptions save and reopen unchanged | Verified by authenticated API regression tests on 2026-10-05; see revalidation-2026-10-05 |
| P21-06 | P2 | Structurally incomplete retained JSON causes unhandled errors instead of explicit coverage/integrity failure | Missing/null forecast inputs produce aggregate-only evidence; incomplete or invalid proposals with matching hashes return 422 | Verified by authenticated API regressions and broader native/SQL/wire checks on 2026-10-05; browser remains blocked |

Verification corrections: native requests require the company header; EF proposal metadata now retains explicit UTC on reload; the report component now receives the evaluated navigation URL rather than a literal string. These production corrections were exercised by the accepted final runs. Earlier diagnostic failures are preserved separately.
