# P16 implementation and scope

Baseline: `preservation-baseline.json` records every pre-existing dirty/untracked file and HEAD before production edits. P01–P14 evidence is historical and retained. P15 entered this phase as partial, unverified work; P16 does not claim completion of P15's full catalogue/editor acceptance.

## Owners and persistence

Company pause remains the existing `CompanyOperatingConfiguration` aggregate, including its independent emergency stop. Scoped agent pause, immutable command receipts and step admissions use three additive tables in `20261004064041_AddAgentExecutionControls`. Scope/revision/actor/reason/time and request hash are durable. A reused command must match the original request; a stale preview returns conflict. Mutation permission and visible responsibility scope are checked again at apply.

The Platform execution gate is shared by Operations and owning modules. SQL Server serializable admission locks the same company and operating-configuration rows as control changes. A committed pause wins over a waiting new admission. An admitted step can finish; cancellation or a missing acknowledgement never proves that its external effect did not happen. The admission acknowledgement describes the controlled boundary, while the owning business record describes provider completion.

## Controlled boundaries

- Operating dispatch excludes paused company/direct-agent candidates, checks persisted parent-task ancestry before claim and execution, and rechecks before admission. Paused descendants defer for a minute to avoid monopolizing the candidate batch. Resume goes through current assignment, operating validation, autonomy, native tool policy, approval and budget owners. Expired Running leases become Uncertain, never automatic replay.
- Finance autonomous claims and the shared production tool adapter use the same gate. Approval continuations reach the same tool boundary. Provider reconciliation remains available through existing authorized owners.
- Autonomous Support outbox delivery checks recorded agent attribution. Paused messages release their claim without counting a failed attempt. Resume rechecks actual Support safety and current low-risk eligibility; uncertain/prior admitted instructions cannot resend.
- Company pause also holds queued payment submissions, including reviewed human requests. Payment instructions have no recorded agent attribution, so scoped agent pause cannot cover them. Provider polling, reconciliation, cancellation and settlement remain owned by Finance. A new submission attempt is allowed only when the latest owning submission attempt records RetryableFailure and the execution is Queued; the provider's stable idempotency key is retained. Uncertain submissions need explicit owning reconciliation.

## UI and recovery

`/settings/agents/execution?companyId=…&agentId=…` uses typed no-store reads and preview/apply commands. Scope selection persists in the URL. The settings hub has an execution-controls entry. The reference image was generated before implementation. The page shows observed UTC time, bounded lists/counts, precise effect coverage, no-effect impact review, current restrictions and immutable actor/history. It distinguishes queued, paused-before-start, stopping between steps, in flight, completed internal work and uncertain outcomes. Already sent/settled business evidence remains intact.

Recovery links go to canonical task detail, Support case, Finance workbench and payment batch. Existing permissions, approval version/expiry, safe retry and reconciliation requirements govern those actions; this page grants no new execution permission. Exact task return retains the original execution scope. A network failure leaves a concrete refresh path; the command receipt makes retry after an ambiguous response idempotent.

## Retained P15 prerequisites repaired

The partial P15 queue previously referenced a missing Marketing brief property, an unsupported operating action, incomplete cycle transitions and an already-active initiative approval. The queue now uses real snapshots/validation and valid lifecycle ordering. For a persisted catalogue task, plan validation checks its exact native tool authority instead of an unrelated AI planning capability. Current task policy and daily reservation remain execution-time checks. Retained queued work binds its actual originating user and current company responsibility; callers cannot provide a replacement actor. Denied/failed tool results no longer produce a successful orchestration result. These are necessary P16 dependencies, not blanket P15 acceptance.

## Independent gates

Local automated/API/SQL/rendered/wire results are recorded in `verification.json` when accepted. The computer-use runtime could not initialize (`failed to write kernel assets`, Windows error 3), including after reset. Browser visual, keyboard, narrow-layout and live click acceptance remain Blocked; rendered and real composed API tests are the strongest available substitute. Provider/deployed production migration/native/physical/statutory acceptance remains Unverified. Human release approval remains Pending.
