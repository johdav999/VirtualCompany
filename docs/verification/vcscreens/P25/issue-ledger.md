# P25 issue ledger

| ID | Finding | Resolution | Evidence |
| --- | --- | --- | --- |
| Q25-01 | Native route/header company disagreement returns 400 before authorization, while a consistent foreign context must be forbidden | Test now separately asserts both boundaries | Final API tenant test |
| Q25-02 | Wrapped select labels included option text; quarter expressions rendered literally | Explicit accessible control names and explicit Razor expressions | Initial browser diagnostic; final replay required |
| Q25-03 | Prerendered form was exercised before its interactive event handlers attached | Native interactive readiness marker; replay waits for actual interactive state | Prerender diagnostic; final replay required |
| Q25-04 | Reopened milestone timestamps lost UTC kind, blocking unchanged date revisions | Persistence UTC value conversion; preserve unchanged instants through date editing | API retained-input regression; UTC browser diagnostic; final replay required |
| Q25-05 | Roslyn migration compilation encountered an internal CLR error | Retry with tiered compilation disabled; no production behavior change | Initial and accepted build logs |

Diagnostic attempts are retained and excluded from acceptance. Source changes require the final replay and affected tests to pass before this packet is marked complete. Human review and deployed acceptance are separate.
