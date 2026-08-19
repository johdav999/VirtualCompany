## Architecture



When changing backend, data, workflow, agent, integration, approval, or AI orchestration code:



- MUST follow `/docs/architecture-rules.md`

- Use `/docs/architecture-overview.md` only as background context

- Existing repository implementation wins if it conflicts with older planning documents

## Database Compatibility

When updating database schema, data models, EF migrations, seed data, backup/restore scripts, or local database setup:

- Always preserve a clear path to restore and run the database in Docker.
- Keep Docker SQL Server restore flows compatible with local SQL Server changes unless explicitly told otherwise.
- If a change requires local SQL Server-specific handling, also document or implement the equivalent Docker path.



## Design / UI

When changing frontend UI, layout, styling, dashboards, agent cards, approval views, finance views, or user-facing text:

- MUST follow `/docs/design.md`
- Reuse existing design tokens, components, spacing, typography, colors, and interaction patterns defined there
- Do not introduce a new visual style unless explicitly requested
- Existing implemented design system wins if it conflicts with older planning documents
- Keep user-facing language plain English; avoid internal workflow names, enum names, or technical identifiers

## Local Web Verification

When building, starting, or verifying `VirtualCompany.Web`:

- Never use a nested detached command such as `cmd.exe /c start ...`, especially when launch, output redirection, and listener polling are combined in one command. This pattern can leave an untracked process, hold build artifacts open, and cause the agent run to hang.
- Check whether the expected Web port is already listening before starting another host. Reuse the existing host when it belongs to this repository.
- Prefer build and focused component/integration tests when runtime browser verification is not required.
- For normal interactive startup, use `.\client.ps1` in its own terminal and allow its build to finish before starting another build.
- When an automated runtime host is required, start `dotnet` directly with PowerShell `Start-Process -PassThru -WindowStyle Hidden`; do not wrap it in `cmd.exe`, do not use `start`, and do not combine startup with a polling loop in the same command.
- Record the returned process ID immediately. Poll health in a separate bounded command with a short per-request timeout and a fixed overall deadline of at most 30 seconds.
- If startup fails or verification finishes, stop only the recorded process ID. Never stop every `dotnet` process.
- Do not redirect a detached host's output through nested shell syntax. If logs are required, use a repository run script with explicit log handling or run the host in a managed foreground terminal.
- If the bounded health check fails, inspect the available process/build output and continue with static verification. Do not repeat detached launch attempts.

## Multi-prompt implementation persistence

When implementing a sequence of prompts or a multi-part implementation plan:

- Do not stop at intermediate checkpoints.
- Do not stop after only reporting build, test, or analysis status.
- Continue through the ordered prompts until the full requested implementation sequence is complete, genuinely blocked, or the user explicitly asks to pause or stop.
- If a build or test fails, diagnose and fix the failure when it is in scope for the current implementation sequence, then continue.
- Only stop when further progress requires missing external credentials, unavailable services, an unsafe/destructive action without approval, or a user decision that cannot reasonably be inferred.

## Prompt generation standard

When creating implementation prompts, prompt packs, phased delivery prompts, or prompts intended for another coding agent:

- Inspect the current repository before writing prompts. Prompts must describe the existing implementation accurately and must not assume missing systems are absent or completed.
- Order multi-prompt packs by dependency and risk. Each prompt must state its prerequisites and the behavior delivered independently at that stage.
- Make every prompt self-contained enough to execute without relying on conversational context. Shared instructions may be referenced only when they are included in the same prompt document.
- Use this structure for every implementation prompt:
  1. **Title and outcome**: name one bounded outcome and explain the user or business value.
  2. **Current context**: identify relevant existing modules, services, entities, endpoints, UI surfaces, tests, and known gaps.
  3. **Dependencies**: list earlier prompts, migrations, integrations, configuration, or external credentials required; write `None` when there are none.
  4. **Implementation requirements**: specify concrete backend, data, workflow, integration, UI, authorization, audit, observability, and documentation work that is in scope.
  5. **Constraints and preservation rules**: state architecture boundaries, tenant isolation, approval and policy requirements, idempotency, security, compatibility, and behavior that must remain unchanged.
  6. **Acceptance criteria**: provide observable, testable conditions using `Given / When / Then` or equally precise statements; avoid subjective criteria such as "works well."
  7. **Verification**: name required unit, integration, authorization, tenant-isolation, migration, build, and UI/browser checks in proportion to the change.
  8. **Definition of done**: require production implementation with no scaffolding, mock production data, silent failures, unhandled intermediate states, or deferred in-scope TODOs.
- Always include and follow `production-implementation.md` in generated implementation prompts.
- Include and follow `architecture-inst.md` and `/docs/architecture-rules.md` for architecture-sensitive prompts.
- Include and follow `ui-instructions.md` and `/docs/design.md` for UI prompts, including the mandatory screenshot-first workflow when it applies.
- For database prompts, explicitly require an EF migration when needed and equivalent local SQL Server and Docker restore/run compatibility.
- For external side effects, explicitly require approval boundaries where applicable, outbox/background execution, idempotency, retries, reconciliation, and safe operator-visible failures.
- Do not combine unrelated outcomes merely to reduce the number of prompts. Do not create prompts that only produce scaffolding, analysis, or a plan when implementation is requested.

## Sandbox efficiency

For repository-local reads and searches, use the normal workspace sandbox.
Do not request escalated permissions for `rg`, `Get-Content`, `git status`,
`git diff`, or `git log`.

For repository-local builds and focused tests, try the normal workspace
sandbox first. Escalate only after a concrete sandbox failure and state the
specific boundary requiring it.

Reserve escalation for process lifecycle, databases, network or external
services, writes outside the workspace, or a verified sandbox limitation.

<!-- design-addin-instructions:start -->
# Design Add-in Workspace Instructions

- When a prompt asks for architecture-sensitive implementation, include and follow `architecture-inst.md`.
- When a prompt asks for UI, UX, styling, layout, components, navigation, or frontend implementation, include and follow `ui-instructions.md` if it exists.
- Always include and follow `production-implementation.md` in every prompt.
- Treat these generated instruction files as project context. If they conflict with explicit user instructions, follow the user and note the conflict.
<!-- design-addin-instructions:end -->

## Execution Discipline

For large implementation tasks, use the following default workflow:

1. Perform one broad repository inspection.
2. Identify the relevant files and implementation boundaries.
3. Implement the requested change in one coherent pass.
4. Run focused tests for the affected area.
5. Run one full build or broader validation when appropriate.

Do not repeat repository-wide searches, unchanged test runs, or full builds unless:

- new evidence changes the working diagnosis
- implementation changes invalidate earlier results
- a test or build failure requires another investigation
- final verification requires rerunning the command

Quality and correctness remain the stopping criteria. These limits prevent redundant work; they must not be used to leave required implementation or verification incomplete.

## Repository Inspection

Use `rg` or `rg --files` for the initial repository scan.

Batch related searches and file reads whenever practical:

- gather relevant paths with one targeted search
- search for related symbols or patterns together
- read related files in the same inspection step
- avoid reopening unchanged files
- avoid many small searches when one bounded query can provide the same evidence

Prefer targeted follow-up searches after the initial scan. Repeat broad inspection only when new evidence shows that the original scope was incomplete.
