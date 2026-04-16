# Task Prompt: TASK-16.2.2

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
Implement "TASK-16.2.2: Create anomaly registry schema and persistence model for traceability metadata".
Story: US-16.2 - ST-F402 — Inject traceable financial anomalies into generated datasets

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
- Dataset generation supports anomaly injection for unusually high invoice, duplicate vendor charge, category mismatch, and missing receipt scenarios.
- Each injected anomaly is stored with a unique anomaly ID, anomaly type, affected record IDs, and expected detection metadata.
- Anomaly injection can be enabled or disabled per company and configured by scenario profile.
- Automated tests verify that each anomaly type appears in generated data and can be queried by anomaly ID.
- Normal records remain valid after anomaly injection, and anomaly records are the only intentional validation exceptions.

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
