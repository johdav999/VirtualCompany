# Task Prompt: TASK-16.4.2

## Context Summary
- Stack hint: .net
- Workspace root: c:\Users\Johan\source\repos\Virtual Company
- Backlog generated at: 2026-04-15T20:38:28.905Z
- Top relevant files:
  - docs/postgresql-migrations-archive/README.md
  - README.md
  - src/VirtualCompany.Api/VirtualCompany.Api.csproj
  - src/VirtualCompany.Application/VirtualCompany.Application.csproj
  - src/VirtualCompany.Domain/VirtualCompany.Domain.csproj
  - src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj
  - src/VirtualCompany.Mobile/VirtualCompany.Mobile.csproj
  - src/VirtualCompany.Shared/VirtualCompany.Shared.csproj
  - src/VirtualCompany.Web/VirtualCompany.Web.csproj
  - tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj

## Objective
Implement "TASK-16.4.2: Map finance events to the shared event contract used by the trigger system".
Story: US-16.4 - ST-F404 — Emit financial domain events from seeded and simulated data changes

## Constraints
- Only touch files in this repository/workspace.
- Avoid broad refactors; keep changes focused on this task.
- Preserve existing behavior unless required by acceptance criteria.
- Keep naming and code style consistent with surrounding code.

## Implementation Plan Checklist
- [ ] Inspect current code paths and identify exact files to edit.
- [ ] Implement the smallest viable change for this task.
- [ ] Verify the implementation against acceptance criteria.

## Acceptance Criteria
- Creating a new transaction emits a `finance.transaction.created` event with company ID, record ID, amount, category, and timestamp.
- Creating a new invoice emits a `finance.invoice.created` event with company ID, invoice ID, supplier/customer reference, amount, due date, and timestamp.
- Configured anomaly or threshold conditions emit a `finance.threshold.breached` event with breach type, affected record ID, and evaluation details.
- Events are published for both initial simulation-generated changes and ongoing time progression changes without duplicate delivery from a single write operation.
- Published events are consumable by the trigger system from EP-7 through the existing event contract, and an integration test verifies end-to-end delivery.

## Validation Commands
- dotnet build

## Files To Touch (Guessed)
- docs/postgresql-migrations-archive/README.md

## Definition Of Done
- Acceptance criteria above are satisfied.
- Code changes are complete and consistent with the repository style.
- Prompt consumer returns:
  - List of touched files
  - Brief summary of what changed and why

## Output Requirements
- Report exactly which files were modified.
- Provide a concise change summary.
- Mention any follow-up risks or TODOs.
