# Initial Finance autonomy workflow templates

Template catalogue version: `2026-09-01.prompt7.v1`.

The initial catalogue contains eight reviewed, code-owned workflows. They use existing Finance read or recommendation tools and may create or update only internal review work. A template never expands the selected tool's backend authority. Posting, categorization execution, invoice approval, payment or money movement, final close or year-end, statutory filing or sign-off, provider or credential changes, external communication, self-approval, and resolution of ambiguous provider outcomes remain outside every template.

| Template | Trigger | Capability and tool | Human owner |
| --- | --- | --- | --- |
| Stale cash and bank evidence | Weekday schedule or `stale_cash_evidence` | `finance.daily_cash` / `get_cash_balance` | Finance manager |
| Uncategorized transaction review | Weekday schedule or `new_uncategorized_transaction` | `finance.transaction_review` / `list_uncategorized_transactions` | Finance operator |
| Overdue receivables plan refresh | Weekday schedule or `overdue_receivable` | `finance.natural_language_queries` / `resolve_finance_agent_query` | Accounts receivable owner |
| Due payables and cash reserve | Weekday schedule | `finance.role_analysis` / `analyze_finance_capability` | Accounts payable owner |
| Close blocker refresh | Weekday schedule or `close_task_blocker_changed` | `finance.close_year_end` / `finance.close.read_readiness` | Accounting close owner |
| Reconciliation/import exception review | Weekday schedule or `reconciliation_failed` / `import_failed` | `finance.bank_reconciliation` / `finance.advanced.read_reconciliation` | Reconciliation owner |
| Expiring compliance evidence | Weekday schedule or `compliance_obligation_expiring` | `finance.compliance_audit` / `finance.compliance.read_obligations` | Compliance owner |
| Failed background Finance work | `background_work_completed` failure signals | `finance.advanced_accounting` / `finance.advanced.read_schedules` | Finance operations owner |

## Preview and activation

`GET /api/companies/{companyId}/finance/autonomy/workflow-templates` lists the immutable declarations, including English and Swedish names and next actions. `POST .../preview` evaluates the selected Finance agent's effective authority and returns the exact prospective grant, constraints, unsupported effects, and blocking reasons. Previewing writes an audit event but changes no grant.

An owner, admin, or manager may call `POST .../draft`. This creates a prospective grant or prospective version only. It does not activate the version. Existing grant activation remains the separate reviewed operation described in [finance-autonomy-grants.md](finance-autonomy-grants.md). Requests for effects outside `read`, `recommend`, `create_internal_task`, and `create_internal_draft` make the preview not ready and the draft request fail validation.

## Run and output behavior

The trigger service stamps the template code, version, owner, approval behavior, and next human action into the immutable run evidence and plan. Template request payloads are bounded by the declared record/action/run limits. The executor still performs current evidence, policy, authority, budget, and lease checks before dispatching the existing tool.

Business-event templates represent an authoritative exception and therefore create review work after a successful evidence read. Scheduled results are classified from structured health, missing, stale, blocker, exception, overdue, failure, and bounded collection signals. A result with no trustworthy evidence is `missing`; evidence older than the template freshness limit is `stale`. Neither can be reported as healthy. A healthy scheduled result creates no task and records a `no_action_required` audit disposition.

Review work is deduplicated by company, capability, template, target identity, target version, and run review window. The task payload links the source references and hashes, run and step, policy version, grant and grant version, human owner role/user, localized title, and next human action. A replay returns the existing task. A healthy result resolves an open task; recurrence reopens it. A newer target version in the same review window completes prior open work with the explicit `superseded_by_new_source_version` resolution before creating the new item.

Stale or missing evidence creates blocked review work; an exception creates new review work. Output persistence is part of successful step handling: if the idempotent task/audit write cannot be trusted, the read/recommend step stays retryable rather than being marked complete.

## Operations and recovery

Use the existing trigger, run, executor, budget/circuit, and approval operational surfaces. Template output audit actions are:

- `finance.autonomy_workflow_template.previewed`
- `finance.autonomy_workflow_template.draft_created`
- `finance.autonomy_workflow_template.outcome_materialized`

The output disposition distinguishes `created`, `duplicate_suppressed`, `resolved`, `reopened`, and `no_action_required`. Provider ambiguity, unsafe retry decisions, and sensitive actions must continue through their existing human-controlled reconciliation or approval runbooks; operators must not broaden a template grant to work around a stop condition.
