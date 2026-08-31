# Accounting close orchestration

Prompt 1 of Financial App P4 introduces a durable, versioned close work plan. It complements fiscal-period validation, close, lock, and reopen controls; it does not replace them.

## Model and invariants

- A close template has immutable versions. Sections, task definitions, dependencies, evidence requirements, owner defaults, due offsets, sign-off rules, and materiality settings belong to a specific version.
- Editing a template creates a draft version. Activation supersedes the prior active version without modifying that version or any close already started from it.
- A close instance binds one company, fiscal period, template, and template version. Starting it snapshots one generated task/dependency graph and creates a reference to the existing `WorkTask` record for every generated task.
- Tasks retain owner, due date, evidence, note, blocker, approval, completion, and status-history records. Sign-off uses the existing approval system with the exact `accounting_close_task` target.
- Evidence remains an existing company knowledge document. The link is accepted only when both tenant ownership and the current member's knowledge-access policy allow the exact document.
- Template graphs reject missing predecessors, self-dependencies, and cycles. A dependent task cannot complete until every predecessor is complete.
- Only the assigned owner or a company manager can complete a task. Assignment rejects users without an active membership in the same company.
- A task without an explicit default owner is assigned to the manager who starts the close. A configured role must resolve to an active company member; otherwise close generation fails without exposing another tenant's membership.
- A positive materiality threshold requires a reported amount before completion. Meeting or exceeding the threshold creates and requires the exact task approval; amounts below the threshold do not leave a false open approval, and omitting the amount cannot bypass sign-off.
- Cancelling an individual task is recoverable while its close remains active. A manager may reopen it, while a cancelled close instance remains terminal.
- Template, instance, and task changes use explicit versions. Every mutation also requires a stable idempotency key; replay with the same payload returns the retained result, while key reuse with a different payload is rejected.

## API

The company-scoped surface is rooted at:

`/api/companies/{companyId}/finance/accounting-close`

Template operations include list, get, preview, create, create version, copy, activate, and retire. Instance operations include list, get, start, cancel, assign, complete, reopen or cancel a task, and add or resolve blockers. Read operations require `AccountingView`; task work requires `FinanceEdit`; template governance, start, reopen, retirement, and cancellation require `AccountingAdmin`.

Stable failure codes include:

- `accounting_close_dependency_cycle`
- `accounting_close_predecessor_incomplete`
- `accounting_close_evidence_required`
- `accounting_close_evidence_access_denied`
- `accounting_close_owner_outside_company`
- `accounting_close_completion_forbidden`
- `accounting_close_reported_amount_required`
- `accounting_close_sign_off_required`
- `accounting_close_version_conflict`
- `accounting_close_idempotency_conflict`

## Operations and evidence

Audit events are written for template lifecycle, close start/cancellation, task assignment/completion/reopen/cancellation, and blocker changes. The `VirtualCompany.AccountingClose` meter reports template, instance, task, and generated-task activity without company identifiers or document content.

The `AddAccountingCloseOrchestration` migration creates tenant-indexed template, graph, instance, task, evidence, blocker, history, and idempotency tables. Release verification must include the focused finance/API tests and:

```powershell
dotnet ef migrations has-pending-model-changes --project src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj --startup-project src/VirtualCompany.Api/VirtualCompany.Api.csproj --context VirtualCompanyDbContext
```
