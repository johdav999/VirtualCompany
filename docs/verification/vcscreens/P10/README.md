# P10 evidence packet

P10 implements the canonical seven-state agent work board, typed durable detail, scoped retained reads/commands, lifecycle/evidence explanation and contextual returns. Begin with `implementation.md` and its P11 continuation, then `uat.md`, `issue-ledger.md`, `profile.md` and `verification.json`.

Accepted automated runs: `p10-api-final-accepted.trx`, `p10-case-state-final.trx`, `p10-lifecycle-scope-final.trx`, `p10-web-final-accepted.trx`, `p10-wire-final-accepted.trx`. These cover 60 distinct API, 72 Web and one wire test. Broader/diagnostic attempts are retained separately; do not add repeated runs to coverage totals. Final source build outputs are `api-build-final-accepted.txt`, `uat-build-final-accepted.txt` and `web-build-final-accepted.txt`.

Browser evidence is indexed in `browser-captures.json` with screenshots/DOM in `screenshots/`. `reconcile.ps1` is repeatable read-only localhost source validation; `reconciliation/results.json` records 45 checks and hashes against the final browser fixture. `reconciliation-scope-final/results.json` repeats them after the final scoped-access correction in a fresh fixture. `verify-evidence.ps1` seals accepted artifacts and preservation. `working-tree-before.txt`, `preservation.json` and `cleanup.json` record preservation and disposable runtime cleanup. Recorded process IDs are historical after cleanup; never blindly stop them in a later run.

Status: implemented and locally verified; named human approval Pending. Independent real-tenant/SQL Server/provider/native/physical-output/statutory gates remain Unverified. P11 can continue in this same uncommitted checkout.

2026-10-03 user-review follow-up: the flat outcome grid is replaced by a seven-column Kanban. Preserve this layout and the department indicator cards in P11. Read [current UI verification](../release-1/department-cards-2026-10-03/README.md) alongside this historical P10 packet.

2026-10-03 paging follow-up: Agent team now requests bounded pages per state, preventing newer completed history from hiding older active work. Read [root cause, contract and current verification](../release-1/kanban-paging-2026-10-03/README.md). Preserve `PerState` through Application/API/typed Web transport and the filtered column paging/return behavior in P11. Legacy list requests keep their global page semantics.
