# Task Prompt: TASK-6.3.4

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
Implement "TASK-6.3.4: Write integration tests for policy retrieval and controlled finance mutations".
Story: US-6.3 - Controlled finance write operations and policy configuration

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
- Internal finance service supports controlled write operations for invoice approval status changes and transaction category updates.
- Policy configuration supports per-tenant invoice approval limits, anomaly detection bounds, and cash runway thresholds.
- Write operations validate tenant scope and reject updates when the target record belongs to a different tenant.
- Policy configuration is retrievable through a typed service interface consumable by orchestration and policy components.
- Automated tests verify successful writes for authorized tenant context and rejection for invalid tenant or invalid state transitions.

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
