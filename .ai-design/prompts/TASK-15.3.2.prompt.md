# Task Prompt: TASK-15.3.2

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
Implement "TASK-15.3.2: Create alert and task generation pipeline for detected transaction anomalies".
Story: US-15.3 - ST-F303 — Implement transaction anomaly detection alerts and follow-up tasks

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
- Laura evaluates transactions against configured thresholds and historical baselines available in the mock financial domain.
- Transactions flagged as anomalous generate a structured output with anomaly type, explanation, confidence, and recommended action.
- Each detected anomaly creates an alert record and a follow-up task assigned to the finance workflow queue.
- Duplicate alerts are not created for the same transaction and anomaly type within the configured deduplication window.

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
