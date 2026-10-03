# P02 issue ledger

| ID | Severity | Finding / fix | Verification |
| --- | --- | --- | --- |
| P02-01 | P1 | Focus's earlier five-item truncation could hide authorized overdue work. Today requests all authorized source candidates before stable ranking; normal Focus preserved. | FocusEngine and priority ordering tests |
| P02-02 | P1 | Unknown timestamps, approval target IDs and duplicate task/approval candidates were guessed/misclassified. Carry actual metadata, IDs and task dedup key. | API composition, wire contract and Web evidence tests |
| P02-03 | P1 | Finance Focus could appear in Company fallback without Finance responsibility. Filter through resolved visibility. | Authenticated member integration test; browser F02-08 |
| P02-04 | P2 | Today metadata used a CSS-hidden visibility class. Use visible ownership metadata and explicit agent gap. | Browser F02-01 / narrow Today |
| P02-05 | P2 | Work used server-local times unlike evidence. Reuse company formatter, initialize detail context, disclose timezone with UTC fallback. | Timezone test; matching deadline/source values in final task journey |
| P02-06 | P2 | Detail comparison consumed Today change marker. Compare without moving the Today baseline. | Tracker regression; final browser F02-02 reports removed item |
| P02-07 | P2 | Existing approval presenter described an internal task review as a payment exceeding limits. Task review uses owning reason and localized review title. | Rendering no-command regression; F02-03 |
| P02-08 | P1 | Marketing next action crashed when optional daily review returned HTTP 204. Handle absence explicitly in typed client. | 204 regression and original experiment journey F02-05 replay |

All listed implementation findings are fixed and verified. Live connected Finance/provider outcomes and human approval remain unverified prerequisites, not fabricated passes. Existing member approval read permission and non-atomic task-status concurrency are recorded boundaries; P02 did not create a new authorization or workflow engine.
