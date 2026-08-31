# Finance Update P3 Implementation Prompts

Priority: P3 — Durable, bounded Finance agent autonomy  
Prompt order: execute Prompts 1–8 in order after P0, P1, and P2 are complete and green.

## Shared execution contract

- Every prompt is a production implementation task. Read and follow `production-implementation.md`, `docs/architecture-rules.md`, repository `AGENTS.md` files, and current code. UI work also follows `docs/design.md` and `ui-instructions.md`.
- Reuse company orchestration, shared agent AI, effective authority, Finance capability catalogue, tool planner/executor, workflows, approvals, outbox, audit, observability, tasks, and background infrastructure. Do not create a second Finance autonomy engine.
- Autonomous work is an explicit company grant over named capabilities, triggers, actions, limits, and time windows. Agent autonomy level alone never grants Finance authority.
- Finance truth and eligibility remain deterministic. Models may explain, rank, or propose within fixed bounds but cannot calculate balances, tax, posting eligibility, close readiness, approval, or provider outcome.
- Long-running and external work is durable, leased, idempotent, restart-safe, observable, and reconciled. No recursive self-scheduling, unbounded loops, silent retry, or fabricated completion is allowed.
- Permanent human-only boundaries remain: payment initiation, credentials/connection authority, final accounting posting where policy requires human approval, final period lock/reopen/year-end authority, statutory filing/sign-off, professional approval, self-approval, and ambiguous provider resolution.
- Database changes use additive SQL Server EF migrations with representative upgrade, concurrency, rollback/recovery, and no-pending-model verification.
- Finish every prompt with production implementation, tests, documentation, operations evidence, and no deferred in-scope TODOs.

---

## Prompt 1 — Versioned Finance autonomy grants and policy

### 1. Title and outcome

Implement an explicit company policy that defines exactly which Finance capabilities Laura may run proactively, under what triggers, limits, approvals, and stop conditions.

### 2. Current context

- Agents have autonomy levels, tools, scopes, approval thresholds, escalation rules, schedules, and effective capability state.
- Company orchestration has goals, budgets, kill switches, cycles, validations, and governed actions.
- `RoleAgentCadenceBackgroundService` currently runs scheduled analysis but not bounded Finance action workflows.

### 3. Dependencies

- All P0, P1, and P2 prompts.

### 4. Implementation requirements

- Add versioned company-owned Finance autonomy grants bound to agent, capability, allowed triggers, allowed action classes/tools, record/amount/count limits, schedule/timezone/window, evidence freshness, confirmation/approval behavior, escalation route, expiry, and status.
- Define conservative levels: read/monitor, recommend/draft, supervised internal execute, and scheduled bounded execute. Map existing autonomy values compatibly without granting new authority during migration.
- Validate grants against the P2 capability/risk catalogue and actor/delegation policies; reject unknown, human-only, overly broad, contradictory, or unconfigured capabilities.
- Require explicit activation/review for elevated grants and retain immutable versions/history; edits create a new prospective version.
- Add company/agent/capability emergency stop and pause/resume with reason, actor, audit, and immediate execution checks.
- Expose authorized APIs and safe effective-policy queries for UI and workers.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and Company Orchestration boundaries.
- New/existing agents default to conservative read/recommend authority.
- A grant cannot relax the underlying P0 permission, risk, approval, or Finance eligibility policy.

### 6. Acceptance criteria

- Given no active grant, when a proactive Finance action is considered, then it is denied even if Laura has the tool.
- Given a grant for read monitoring, then recommend or execute work remains unavailable.
- Given a capability becomes human-only or higher risk, then existing incompatible grants fail closed until reviewed.
- Given emergency stop, then no new step begins and resumable work remains visibly paused.

### 7. Verification

- Add version, activation, migration, expiry, scope, limit, role, human-only, risk-change, pause/stop, tenant, and authorization tests.
- Add contract tests comparing effective grants with P0/P2 authority.
- Run company orchestration and agent profile/capability regressions.

### 8. Definition of done

Finance autonomy is a versioned, reviewable company decision over named capabilities—not an implied consequence of hiring Laura or changing one enum.

---

## Prompt 2 — Durable autonomous Finance run and step lifecycle

### 1. Title and outcome

Persist autonomous Finance runs as recoverable workflows with immutable inputs, bounded steps, current policy references, and explicit terminal/intermediate states.

### 2. Current context

- Company operating cycles, orchestration runs, conversational plans, tool attempts, workflows, tasks, approvals, leases, events, and audit records already exist.
- Scheduled role cadence currently invokes analysis in process and relies on orchestration-run records for duplicate suppression.

### 3. Dependencies

Prompt 1.

### 4. Implementation requirements

- Extend the existing orchestration/workflow model with an autonomous Finance run linked to company, agent, grant/version, trigger/event, correlation/idempotency key, evidence snapshot/hash, plan/version, budgets, and originating goal/task when applicable.
- Model planned, validating, running, awaiting approval, queued, reconciling, blocked, paused, completed, partially completed, cancelled, failed, dead-lettered, and superseded states with reason/history.
- Persist step dependencies, leases, attempts, tool/policy/authority versions, source references, requested/actual effects, approvals, and outputs using existing artifacts where possible.
- Make creation/coalescing idempotent per company, grant, trigger/window, and authoritative event/version.
- Support cancellation, supersession, retention/redaction, and safe operator replay from a specifically permitted checkpoint.
- Add indexed operator queries and audit linkage without storing hidden reasoning or sensitive raw provider content.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and database rules in `docs/architecture-rules.md`.
- Do not create a parallel generic workflow engine or use chat messages as run state.
- Completed external effects cannot be represented as rolled back by run cancellation.

### 6. Acceptance criteria

- Given duplicate triggers, then one logical run is created for the same window/event version.
- Given a host restart during any intermediate state, then the run resumes or remains safely blocked without losing evidence or repeating effects.
- Given superseded evidence or policy, then pending steps cannot execute under the old snapshot.
- Given retention, then required audit/approval/source linkage remains while sensitive content is redacted according to policy.

### 7. Verification

- Add migration, lifecycle, transition, coalescing, lease, restart, cancellation, supersession, retention, tenant, and audit tests.
- Add SQL Server concurrency/rollback and multi-worker claim tests.
- Verify compatibility with existing company and conversational orchestration runs.

### 8. Definition of done

Every autonomous Finance activity is a durable, inspectable workflow that survives process and infrastructure failure safely.

---

## Prompt 3 — Scheduled and event-driven Finance triggers

### 1. Title and outcome

Create bounded schedule and business-event triggers that start eligible Finance runs once, at the correct company-local time or authoritative state transition.

### 2. Current context

- Role cadence, workflow scheduler, scheduled triggers, company operating events, outbox, Finance domain events, and company timezone configuration exist.
- Current Finance cadence produces daily/weekly/monthly analysis but does not use the complete P3 grant/run lifecycle.

### 3. Dependencies

Prompts 1–2.

### 4. Implementation requirements

- Route Finance cadence through the durable autonomous run model and active grant policy.
- Support reviewed schedule triggers and a narrow allowlist of authoritative events such as new uncategorized transactions, overdue receivables, stale cash evidence, close-task blocker changes, failed reconciliation/import, expiring compliance obligation, and completed background work.
- Persist trigger cursor/window/event version and coalesce bursts deterministically.
- Handle company timezone, daylight-saving transitions, missed windows, disabled periods, maintenance, late events, and multi-host leases.
- Enforce per-grant minimum interval, maximum runs/window, debounce/coalescing, and evidence freshness before creating work.
- Surface trigger failures and dead letters with safe recovery actions.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Events are signals to validate, not proof that an action is still eligible.
- External provider webhooks remain normalized through owning integration adapters before becoming Finance triggers.

### 6. Acceptance criteria

- Given a daily trigger across a DST boundary, then one run occurs in the configured company-local window.
- Given repeated equivalent events, then one coalesced run covers the bounded set and retains source-event references.
- Given a paused/revoked grant, then no run starts from schedule or event.
- Given a missed window after restart, then configured catch-up behavior is deterministic and bounded.

### 7. Verification

- Add timezone/DST, duplicate, burst, ordering, late event, cursor, catch-up, pause, grant expiry, multi-host lease, and dead-letter tests.
- Add integration tests for each initial Finance event source and cross-company isolation.
- Run scheduler/outbox/Finance-event regressions.

### 8. Definition of done

Finance autonomy starts only from reviewed, durable, deduplicated schedules or authoritative events and never from an unbounded polling guess.

---

## Prompt 4 — Autonomy budgets, exposure limits, and kill switches

### 1. Title and outcome

Enforce company and agent limits for model/tool usage, records, money/materiality exposure, generated work, retries, and elapsed time before and during autonomous Finance runs.

### 2. Current context

- Company orchestration contains task/model/tool/money budgets and emergency controls.
- Natural-language planning has per-run step, call, time, and cost bounds.
- Finance policies provide materiality, cash reserve, approval, and workflow limits.

### 3. Dependencies

Prompts 1–3.

### 4. Implementation requirements

- Reuse and extend authoritative company orchestration budgets for Finance-specific records evaluated, drafts/tasks created, execute attempts, aggregate amount/materiality, object/export volume, model/tool calls, estimated cost, retries, and wall-clock duration.
- Reserve budget atomically before a step and reconcile actual usage afterward; prevent concurrent runs from overspending shared limits.
- Enforce per-run, per-day/window, per-agent, per-capability, and company-wide limits with stable reason codes and escalation behavior.
- Count failed, retried, approval-pending, and partially completed work according to documented rules; do not reset exposure through retries or new correlation IDs.
- Add automatic circuit breakers for repeated policy denials, invalid model plans, provider ambiguity, error bursts, stale evidence, and audit/outbox failure.
- Provide authorized usage/remaining-budget queries and alerts without exposing sensitive payloads.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Budgets limit authority; exceeding a budget cannot be solved by asking the model to split the action.
- Monetary budgets do not authorize money movement or replace Finance cash/materiality policy.

### 6. Acceptance criteria

- Given concurrent runs near a shared limit, then atomic reservation prevents combined overspend.
- Given a request split into multiple steps/runs, then aggregate record and financial exposure remains bounded.
- Given repeated ambiguous or invalid outcomes, then the circuit breaker pauses the capability and creates an operator-visible escalation.
- Given emergency stop, then workers recheck it before every new step.

### 7. Verification

- Add boundary, concurrency, reservation/reconciliation, retry accounting, split-action, rollover/timezone, circuit-breaker, stop-race, tenant, and authorization tests.
- Add SQL Server stress tests for accumulated usage and simultaneous workers.
- Add safe budget metrics and alert verification.

### 8. Definition of done

Autonomous Finance work has enforceable aggregate limits and immediate stop controls rather than prompt-level guidance or best-effort counters.

---

## Prompt 5 — Leased Finance autonomy executor and recovery

### 1. Title and outcome

Execute eligible autonomous Finance steps through durable leases, idempotent tool calls, bounded retries, and operator-visible recovery without duplicate or ambiguous effects.

### 2. Current context

- Background execution, company execution scope, outbox, workflow leases, tool attempts, approvals, provider adapters, reconciliation, and worker operations exist.
- P3 run/trigger/budget models define what is eligible to execute.

### 3. Dependencies

Prompts 1–4.

### 4. Implementation requirements

- Add or extend a hosted worker that claims eligible Finance run steps using company-scoped leases, heartbeats, expiry, bounded batch size, and fair ordering.
- Before every step, recheck active grant, kill switch, budget reservation, actor/delegation authority, agent authority, tool/risk policy, target/evidence versions, Finance eligibility, and approval state.
- Execute through the existing trusted tool executor; never invoke Finance command services or providers through a worker-only bypass.
- Classify validation/authorization/policy failures as permanent, transient infrastructure failures as bounded retryable, and ambiguous provider outcomes as reconciliation-required.
- Preserve stable business idempotency across retries/restarts and store safe attempt/failure/provider-reference summaries.
- Add recovery for expired leases, worker restart, database rollback, outbox failure, missing/corrupt object artifacts, and reconciled provider outcomes.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and External Side Effects and Outbox rules in `docs/architecture-rules.md`.
- Never retry permanent or ambiguous outcomes automatically.
- Do not hold database transactions open across model or provider calls.

### 6. Acceptance criteria

- Given two workers race for a step, then one lease owner executes it.
- Given restart after provider request but before local acknowledgement, then reconciliation determines outcome before any retry.
- Given policy or grant changes while leased, then the pre-step recheck blocks execution.
- Given corrupt or missing generated evidence, then the run becomes explicitly blocked/failed and cannot claim success.

### 7. Verification

- Add multi-worker, lease expiry, heartbeat, restart, duplicate delivery, idempotency, transient/permanent/ambiguous failure, rollback, outbox, reconciliation, object corruption, and tenant tests.
- Run SQL Server concurrency and coordinated SQL/object recovery rehearsals.
- Verify worker registration uniqueness and startup/shutdown behavior.

### 8. Definition of done

Autonomous Finance execution is recoverable and at-most-once in business effect even when infrastructure fails at the worst boundary.

---

## Prompt 6 — Approval, escalation, and human control during autonomous runs

### 1. Title and outcome

Integrate autonomous Finance runs with exact-action approval, segregation of duties, escalation, expiry, and human intervention without losing run continuity.

### 2. Current context

- P0 binds approvals to exact actions and revalidates continuation.
- P1 persists plans that wait for confirmation or approval.
- Company orchestration supports approvals, decisions, tasks, escalations, and review workspaces.

### 3. Dependencies

Prompts 1–5.

### 4. Implementation requirements

- Create approval requests from autonomous steps using the exact plan/step, grant, actor/delegation, authority, policy, payload/target, evidence, budget, and expiry context.
- Pause dependent steps while approval is pending and continue only after the P0 revalidation path succeeds.
- Enforce independent approvers and prohibit Laura, the delegated automation identity, or initiating user from self-approving where policy requires separation.
- Support approve, reject, request changes, cancel, expire, revoke, and supersede with explicit run transitions and user-visible next steps.
- Escalate unresolved approvals, evidence gaps, circuit-breaker trips, reconciliation cases, and repeated failure to configured human roles through tasks/notifications.
- Allow authorized humans to narrow or cancel pending work; scope expansion requires a new plan/grant review.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Approval never overrides an expired grant, exhausted budget, stale record, failed eligibility rule, or human-only boundary.
- Notifications are not approvals and delivery success is not business-action success.

### 6. Acceptance criteria

- Given an autonomous step requires approval, then no mutation or dependent step proceeds while pending.
- Given approval arrives after target/grant/policy changes, then continuation is stale and requires a new request.
- Given rejection or expiry, then the run and user-visible remediation remain consistent and no automatic replacement approval is created.
- Given requested changes narrow the plan, then a new validated revision is retained before review.

### 7. Verification

- Add approval lifecycle, segregation, stale continuation, scope change, escalation, expiry, cancellation, duplicate decision, notification, tenant, and audit tests.
- Add restart tests while approvals are pending and decided.
- Rerun the P0 adversarial matrix and P1 conversational continuation tests.

### 8. Definition of done

Human control remains authoritative and traceable throughout autonomous work, including waits, changes, failures, and recovery.

---

## Prompt 7 — Initial low-risk Finance autonomous workflows

### 1. Title and outcome

Deliver a conservative initial allowlist of useful proactive Finance workflows that monitor evidence, prepare recommendations, and create review work without sensitive autonomous execution.

### 2. Current context

- P2 provides broad read and proposal tools across cash, receivables/payables, transactions, close, compliance, reconciliation, reports, schedules, FX, and assets.
- Current cadence can prepare Finance priorities.
- P3 supplies explicit grants, durable runs, triggers, budgets, execution, approval, and escalation.

### 3. Dependencies

Prompts 1–6.

### 4. Implementation requirements

- Implement reviewed templates for: stale cash/bank evidence monitoring; uncategorized transaction review preparation; overdue receivables plan refresh; due-payables/cash-reserve review; close blocker refresh; reconciliation/import exception review; expiring compliance evidence reminder; and failed background Finance work escalation.
- Each template declares trigger, evidence, tools, limits, outputs, stop conditions, expected tasks/drafts, owners, approval behavior, and unsupported effects.
- Default templates to read/recommend and idempotent task/draft creation only. Do not enable posting, payments, final close/year-end, filing, provider changes, or external communication.
- Deduplicate insights/tasks/drafts by company, capability, target/version, and review window; resolve or supersede prior work transparently.
- Link every output to sources, run, policy/grant version, and next human action.
- Provide activation previews and sensible conservative defaults without activating elevated autonomy silently.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Template wording cannot expand tool authority; backend metadata and policy define behavior.
- Missing/stale evidence produces review work or a blocked state, never a healthy conclusion.

### 6. Acceptance criteria

- Given no material exception, then a scheduled review creates no noisy duplicate task and records why no action was needed.
- Given stale balances or close blockers, then one bounded, source-linked review item is created for the correct owner.
- Given the same evidence/event replays, then no duplicate work is created.
- Given an unsupported sensitive outcome is requested through a template, then validation rejects activation.

### 7. Verification

- Add deterministic fixtures for every template across healthy, exception, stale, missing, duplicate, resolved, reopened, tenant, and limit states.
- Add schedule/event, task/draft idempotency, user ownership, localization, and audit tests.
- Run relevant Finance domain regressions and supported small-volume performance tests.

### 8. Definition of done

Laura performs useful proactive Finance monitoring and preparation with low noise, strict limits, and no autonomous high-impact accounting or external action.

---

## Prompt 8 — Finance autonomy operations console and P3 release proof

### 1. Title and outcome

Give authorized users complete operational control and evidence for Finance autonomy, then qualify the supported low-risk profile through fault, capacity, security, and live UAT gates.

### 2. Current context

- The product has agent settings, company operations, approvals, worker operations, audit, tool transparency, and Finance readiness/evidence surfaces.
- Prompts 1–7 add grants, runs, triggers, budgets, workers, approvals, and initial workflows.

### 3. Dependencies

Prompts 1–7.

### 4. Implementation requirements

- Apply the mandatory screenshot-first workflow in `docs/design.md` for the new autonomy operations surface.
- Add an authorized console for effective grants, schedules/events, active/paused runs, step/approval/reconciliation state, budgets/usage, circuit breakers, failures, audit links, pause/cancel/resume/replay actions, and emergency stop.
- Show requested versus actual effects, source/evidence age, policy/grant/tool versions, owners, next checkpoint, and safe remediation in plain language.
- Add EN/SV localization, desktop/narrow responsiveness, keyboard/screen-reader support, local date/time/money, and complete loading/empty/error/stale/degraded states.
- Add metrics and alerts for run outcomes, queue/lease age, approvals, reconciliation, budgets, policy denials, duplicate suppression, model/tool latency/cost, and emergency controls without sensitive labels.
- Produce revision-bound P3 release evidence for authorization, autonomy policy, scheduling, budgets, recovery, SQL/object consistency, capacity, UI, and human/professional boundaries.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and `docs/design.md`.
- Console actions call authoritative commands and cannot edit persisted run state directly.
- Do not describe the low-risk autonomous profile as autonomous accounting, statutory compliance, or human replacement.

### 6. Acceptance criteria

- Given an authorized operator, then every active grant/run/approval/budget/failure can be understood and traced to evidence and audit.
- Given emergency stop, then the console reflects it promptly and no new step begins in concurrent-worker tests.
- Given recovery from SQL/object/worker/provider failure, then final state is reconciled without duplicate business effects.
- Given a restricted user, then no autonomy configuration, sensitive run detail, or control is exposed.

### 7. Verification

- Run focused P3 policy/run/trigger/budget/worker/approval/template/UI suites and all P0–P2 regressions.
- Run full Release build, hermetic matrix, SQL Server fresh/upgrade/migration/concurrency/rollback lanes, and coordinated SQL/object/worker recovery rehearsal.
- Run supported small-volume capacity and long-running soak/multi-worker tests with documented budgets; retain larger unsupported results honestly.
- Perform authenticated EN/SV desktop/narrow/accessibility UAT for configure, activate, scheduled run, approval, pause, emergency stop, failure, reconciliation, and recovery.
- Generate a checksummed release manifest tied to the exact revision and keep qualified accountant/provider-scope approvals separate.

### 8. Definition of done

P3 is complete only when the initial low-risk Finance autonomy profile is explicitly granted, bounded, durable, recoverable, observable, human-supervised, and backed by current green release evidence.
