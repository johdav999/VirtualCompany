# Release 1 review package

2026-10-03 Kanban root-cause follow-up: [per-state paging correction](kanban-paging-2026-10-03/README.md). This supersedes the earlier UI follow-up's global paging limitation; historical release approval fields remain unchanged.

2026-10-03 UI follow-up: [department indicators and restored Agent team Kanban](department-cards-2026-10-03/README.md). This additive packet records current UI verification after P10; the original P09 evidence and human review fields below remain historical and unchanged.

Implementation readiness: **Complete within recorded local Release 1 scope; P10 ready in the same checkout.** Human release approval: **Pending**. Baseline `37834c7f`, P01–P09 changes uncommitted, review date 2026-10-02. No deployment or statutory certification.

Review [screen/report register](../../../vcscreens/screen-report-register.md), [status and P10 handoff](../../../vcscreens/implementation-status.md), [P09 changes](../P09/implementation.md), [verification results](../P09/verification.json), [issue ledger](../P09/issue-ledger.md), [reference/capture index](screenshots.md), [report reconciliation](report-reconciliation.md), and [repeatable acceptance script](acceptance-script.md). P01–P08 packets remain authoritative for their earlier scoped tests and specialist/provider boundaries.

Final evidence: 352 passing scoped test executions across overlapping suites, API/Web/UAT builds passed, five persisted local daily journeys, 39 authenticated source checks and verified evidence hashes. [Package checks](../P09/package-checks.json) validate the final test files and local links. This is scoped evidence, not a full repository test run.

| Role / decision scope | Local daily journey | Approve or revise | Reviewer | Date | Conditions / issue IDs |
| --- | --- | --- | --- | --- | --- |
| Company/CEO: priority, human follow-up, health | F09-01 verified | **Pending** | Unassigned | Not supplied | External release gates below |
| Sales: opportunity, internal commitment, activity/forecast | F09-02 verified | **Pending** | Unassigned | Not supplied | Live Sales/provider/media acceptance unverified |
| Marketing: current version/revision, governed launch connection, delivery/spend/leads | F09-03 verified | **Pending** | Unassigned | Not supplied | Controlled publication/provider confirmation unverified |
| Finance/accounting: invoice review, obligations/forecast, close/statement evidence | F09-04 verified | **Pending** | Unassigned | Not supplied | Settlement, native outputs and qualified statutory approval unverified |
| Support: case/source/handoff, SLA/backlog/unresolved/aging | F09-05 verified | **Pending** | Unassigned | Not supplied | Controlled customer reply/channel confirmation unverified |
| Cross-role release, authority, accessibility, exports | F09-06–08 verified within local scope | **Pending** | Unassigned | Not supplied | Final Today mobile replay, physical outputs, deployed tenant/SQL Server unverified |

Record actual reviewer identity/date and **Approve** or **Revise**, with exact scope and conditions, only after that human decision. Passing tests and screenshots do not fill these fields automatically.

Implemented local navigation, persistence, source definitions and authorization are distinct from external validation. Opening/reporting never authorizes delivery; approval/request never claims provider success. No required local screen is replaced with a future-release label. Retained specialist utilities remain available with their existing owners; their full statutory/provider workflows are not newly certified by this packet.

Open release gates: deployed real tenant/authentication/SQL Server, configured controlled integrations/recipients and provider confirmation, final physical CSV save/native print/PDF, named product/release approval, and qualified statutory approval where applicable. No schema change was introduced by P01–P09; migration work is not invented to satisfy this review. These gates do not imply missing P10 credentials for local implementation.
