# P17 supervision reports

P17 adds `/agents/staff/reports` and the Agent team entry. Four URL-filtered views cover work, outcomes/reliability, bottlenecks and authority. Company, inclusive local dates, responsibility, task type, optional agent, view and measure survive exact work/decision/history links and validated same-company report returns. Detail rows are paginated in the UI; export includes the entire included permitted row set.

## Definition and access owner

`AgentSupervisionReportService` in Infrastructure.Operations owns definitions, eligible cohorts, retained source reads, normalization, timing, detail and CSV. The Application records form its typed contract. A thin company-context authenticated controller calls this owner. The Web client repeats wire shapes, not calculations. Export performs fresh authorization and returns its report snapshot together with its CSV; the page updates displayed rows from that same response before invoking the existing download module.

Active Owner/Admin/Manager membership is required. `CompanyWorkVisibility` and the real responsibility/Finance policies govern sources. Explicit assignments take precedence over fallback roles. Hidden shared initiatives and their descendants are suppressed before source limits and task-type options. Both ends of a handoff require permitted source receipts and root work. No restricted count, title, type or CSV row is supplied. Company control-command history requires executive scope. Scoped history requires a permitted agent. Foreign companies/agents and denied responsibility filters fail safely.

## Units and source rules

| Measure | Numerator / identity | Cohort and time |
| --- | --- | --- |
| Work in progress / blocked work | Current authoritative root task or initiative, once; unstarted initiatives have their actual identity | Included permitted company work created before period end; state observed now, not reconstructed at a historical end |
| Contributions | Latest retained version per parent/source-task/agent/sequence; versions retained as evidence | Contribution streams created in the event window; separate from company outcomes |
| Completed retained outputs | Completed root task with nonempty retained output, once per company work | Included permitted company work completed in the event window; prepared output only |
| Reviewed outcomes / escalations | CloseSuccessful review with actual evidence / Escalate review; once per initiative | Included initiatives with review events in the period; actual review is not independent economic measurement |
| Provider-confirmed payments / settlements | Owning ProviderCompletedUtc / SettledUtc, once per payment batch across instruction versions | Permitted batches with the corresponding timestamp; company-only, because durable agent attribution is absent |
| Support sends | Owning attributed draft SentUtc, once per draft | Permitted Support draft and case; recorded send is distinct from independently verified delivery/benefit |
| Execution failures | Failed completed tool attempts grouped by actual root work; retry evidence retained | Included work with attempts created/completed inside the event window |
| Approval wait | Unique linked task approval request | Included intervals intersecting the period; clip to `[start, min(end, observed))`; missing terminal times stay null |
| Approval turnaround | Linked task approvals decided inside the period | Included approval interval cohort; full nonnegative CreatedUtc-to-DecidedUtc calendar seconds; average only measurable resolved intervals |
| Corrections | Contribution versions >1, Revise reviews and immutable request-changes audits; once per affected company work | Included permitted work cohort; events inside the period; all included correction evidence retained |
| Blocked handoffs | Failed immutable handoff receipt, once per receipt | Included permitted handoff receipts in the period; receipt minus input creation is explicitly receipt latency, not blocked duration |
| Policy denials | Exact immutable agent.tool_execution.denied audit, once per audit ID | Included permitted tool attempts with period activity/linked audit events; event count is not a business-outcome count or percentage |
| Supervisor interventions | Immutable pause/resume command, once per command ID | Included permitted control commands in the period; exact owning control/history destination |
| Historical blocked duration / combined budget usage | Unavailable | No complete typed blocked transitions or comparable company/task/agent historical budget units; no invented zero, currency sum or savings |

Current source data and event timestamps can legitimately disagree: completing an output is not completing its owning initiative, approving is not executing, and resuming is not confirming a provider effect. The report preserves those distinctions. Agent-filtered company-work measures use the accountable root owner; contribution/handoff measures use their contributor/receiver. Native approval histories without a retained task link and unlinked policy events are outside this measured cohort and are disclosed as such.

## Boundaries and reproducibility

Company timezone converts inclusive DateOnly filters to half-open UTC boundaries, including daylight saving. Event reads end at observed time. Future-start, unordered, invalid or >366-day periods are rejected. Current work is explicitly current, even with a past event window. Sources cap at 2,000 plus a detection row, with deterministic ordering; permitted ancestry is bounded to eight levels. A capped source or unresolved included ancestry produces a partial report. Filters do not pretend to recover bounded-out history.

All cards and their filtered detail/CSV derive from the same retained row collection. Denominators describe eligible cohorts, not additional outcomes. Snapshot hash binds query, observed time, returned detail and measures; this is a reproducible returned-fact packet, not a stored historical database snapshot or cross-module transaction. Fresh export may change the observed snapshot and therefore replaces the displayed report. CSV includes definitions, filters, UTC bounds, timezone, coverage, identity, intervals and evidence IDs. Potential spreadsheet formula cells, including leading whitespace/control characters, are escaped.

No entity, schema migration, synthetic lifecycle event or backfill is added. P11–P16 typed records already support these explicitly bounded definitions. Missing pre-instrumentation history, terminal timestamps, attribution, comparable budgets and blocked intervals remain unavailable. Reading reports creates no approval, tool attempt, execution, provider request or control command.

References were generated and viewed before UI implementation: the overview and filtered approval reference are in `docs/design/references/agent-supervision-p17-*`. Their illustrative values were not copied into production.
