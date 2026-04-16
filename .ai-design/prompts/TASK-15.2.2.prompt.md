# Task Prompt: TASK-15.2.2

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
Implement "TASK-15.2.2: Persist structured invoice review outputs and link them to tasks and approvals".
Story: US-15.2 - ST-F302 — Implement invoice review and approval recommendation workflow

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
- When an invoice enters awaiting-approval state, Laura can execute an invoice review workflow against the invoice payload and related vendor/payment context.
- The workflow produces a structured output containing invoice classification, risk level, recommended action, rationale, and confidence score.
- If the recommendation requires human approval, an approval request is created and linked to the invoice and workflow run.
- Workflow results are persisted in task/output payloads and are queryable by invoice ID and company ID.

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
