# Implementation prompt guidance

These instructions apply when creating implementation prompts, prompt packs,
phased delivery prompts, or prompts intended for another coding agent. They also
apply when the resulting prompt document is stored outside `/docs`.

## Authoritative sources

- Always require the implementation to follow `/production-implementation.md`.
- Architecture-sensitive prompts MUST reference and follow
  `/docs/architecture-rules.md`.
- `/.design-addin/architecture.md` and `/docs/architecture-overview.md` are
  background planning context only and must not override the architecture rules
  or current implementation.
- UI prompts MUST reference and follow `/docs/design.md`. Use
  `/ui-instructions.md` only as a concrete implementation companion; when it
  overlaps or conflicts, `/docs/design.md` wins.
- Reference these files by path instead of copying their rule sets into generated
  prompts. Add only task-specific constraints that are not already defined there.

## Prompt generation standard

- Inspect the current repository before writing prompts. Describe the existing
  implementation accurately; do not assume missing systems are absent or
  completed.
- Order multi-prompt packs by dependency and risk. Each prompt must state its
  prerequisites and the behavior delivered independently at that stage.
- Make every prompt self-contained enough to execute without conversational
  context. Shared instructions may be referenced only when they are included in
  the same prompt document or are stable repository files named by path.
- Do not combine unrelated outcomes merely to reduce prompt count.
- Do not create prompts that produce only scaffolding, analysis, or a plan when
  implementation is requested.

Use this structure for every implementation prompt:

1. **Title and outcome**: name one bounded outcome and explain the user or
   business value.
2. **Current context**: identify relevant existing modules, services, entities,
   endpoints, UI surfaces, tests, and known gaps.
3. **Dependencies**: list earlier prompts, migrations, integrations,
   configuration, or external credentials required; write `None` when there are
   none.
4. **Implementation requirements**: specify concrete backend, data, workflow,
   integration, UI, authorization, audit, observability, and documentation work
   that is in scope.
5. **Constraints and preservation rules**: state applicable boundaries such as
   tenant isolation, approval and policy requirements, idempotency, security,
   compatibility, and behavior that must remain unchanged. Reference canonical
   repository rules instead of reproducing them.
6. **Acceptance criteria**: provide observable, testable conditions using
   `Given / When / Then` or equally precise statements; avoid subjective criteria
   such as "works well."
7. **Verification**: name required unit, integration, authorization,
   tenant-isolation, migration, build, and UI or browser checks in proportion to
   the change.
8. **Definition of done**: require production implementation with no scaffolding,
   mock production data, silent failures, unhandled intermediate states, or
   deferred in-scope TODOs.

## Task-specific prompt requirements

- UI prompts must state whether the mandatory workflow in `/docs/design.md`
  applies and require that workflow by reference when it does.
- Database prompts must identify expected schema or persistence effects and
  require the applicable implementation and verification rules from the
  `Database and EF Core` section of `/docs/architecture-rules.md`.
- External-side-effect prompts must identify the side effect and require the
  applicable boundaries from the `Workflow and Approval` and
  `External Side Effects and Outbox` sections of
  `/docs/architecture-rules.md`.
