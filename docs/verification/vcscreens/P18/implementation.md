# P18 implementation

Read basis: root `vcscreens-prompts.md` shared contract, P18 and C1–C8; root/src/Web/tests/docs instructions; `production-implementation.md`, `docs/architecture-rules.md`, `docs/design.md`, `ui-instructions.md`; current status/register; P10–P17 evidence and P17 handoff. The incomplete P15 packet had diagnostic logs but no implementation/handoff summary. Its surviving production work was inspected and repaired rather than replaced. The required `polish-uat-loop` skill governed the bounded evidence/fix/recheck loop.

## Native catalogue integration

`TaskDraftToolAdapter` continues to use the existing Sales, Marketing and Support owners. Marketing now requests the owner's supported `sales_enablement` format. Empty Marketing output and unavailable/blank Sales analysis fail truthfully with retained owner evidence. Failed orchestration persists a blocked task instead of a completed output, preventing a false supervision prepared-output count.

Native draft execution carries the originating user's current active company membership into the existing company context, restores the prior context afterwards, and never invents a privileged actor. Support knowledge retrieval explicitly propagates that freshly reread membership to its existing authenticated source owner. Company/source visibility and delivery safety remain authoritative. Reassignment to a different initiative owner is refused before admission or tool execution.

## Reviewed internal work

The existing approval service used to invoke a native reviewed tool inside its serializable decision transaction; execution admission requires a separate committed transaction. Approved catalogue actions now enqueue `task_policy.reviewed_execution_requested` in the **existing company outbox**, with exact company/approval/attempt identity and an idempotency key. The approval transaction records the decision and queue together. Finance and separately governed external-action paths retain their own continuations.

After commit the owning worker rechecks the exact queued task/attempt, current reviewed material hash, current originating actor/role/responsibility, current effective tool permissions, policy version/revocation/expiry/budget and execution admission. The original attempt ID remains the admission identity. A paused queued message is deferred without spending its attempt budget. A pause racing admission is refused by the native gate. An already-admitted ambiguous step becomes reconciliation-required; even when a success-only draft schema wraps the refusal, its exact native admission conflict is preserved. Repeated dispatch does not replay that step.

The approval page says **Queued for internal execution**, distinct from executed. The retained task returns to planned with an exact queue receipt and decision link; board/detail shows the queue, not a need for a second approval. Completion/blocking/uncertainty reconciles only the bounded single-action catalogue task and its waiting dispatch. It does not complete the operating initiative, company outcome or customer delivery. Terminal decision links remain retained. New markers use existing JSON payloads; no new entity/table/column or P18 migration was added.

## Policy UI

The existing P15 reference and labeled native controls are retained. Reload clears old-company evidence; saved mode/limits/expiry load correctly. Queue requires a current non-disabled, unexpired policy and valid record/goal. Generations and cancellation suppress late preview/apply/queue reads and navigation. Apply checks returned identity; queue requires a real nonempty retained task ID; connection/identity failures remain explicit.

## Renewal evidence

Authenticated TestServer exercises real research queue/dispatch, Sales proposal owner, Finance collections owner (1000 SEK overdue invoice), Support draft owner, revised proposal and immutable Finance/Support input receipts. A controlled runner records those native results as collaboration versions; it does not claim an autonomous multi-agent conversation. One root decision is reachable from Today, Work, collaboration and the deal. Exact current review and idempotent replay retain one decision/audit. The separate Support edit/approval/source safety/send/outbox path produces one confirmed recording-adapter call and native sent message across two dispatches.

That Support draft is recorded as human-created, with no `CreatedByAgentId`. Its native sent record reconciles to one send, while the agent-attributed supervision metric correctly remains zero. No agent identity is backfilled to make a test count look successful. The report's measured cohort and attribution gap are explicit in the catalogue and reconciliation evidence.

## Persistence and preservation

Existing P11 contribution, P13 association, P15 policy and P16 execution-control migrations remain the schema owners. Required isolated SQL Server checks cover upgrade/downgrade, associations, review concurrency, policy activation concurrency, pause/admission serialization and report query/export reconciliation. Production tenant migration and live provider acceptance are separate. P01–P17 preservation is checked against entry hashes; no reset, clean, stash, branch transfer, commit or deployment was performed.
