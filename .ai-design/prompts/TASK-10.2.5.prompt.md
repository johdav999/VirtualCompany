# Goal

Implement backlog task **TASK-10.2.5 — Start with opinionated predefined workflows, not arbitrary builder UX** for story **ST-402 Workflow definitions, instances, and triggers** in the existing .NET solution.

The coding agent should deliver a **v1 workflow capability centered on predefined, versioned workflow templates/definitions** that can be started manually, by schedule, or by internal event, while **explicitly avoiding** any generic drag-and-drop or arbitrary workflow builder experience.

This task should establish the product and technical shape for workflows so that:
- workflow definitions are persisted as structured/versioned records
- only approved predefined workflow types can be created/activated
- workflow instances can be started and tracked
- the UI/API presents a curated catalog and launch/configuration flow rather than a freeform builder
- the design remains extensible for future workflow types without committing to arbitrary user-authored workflow graphs

# Scope

In scope:
- Add or complete domain/application/infrastructure/web support for **predefined workflow definitions**
- Model workflow definitions as **catalog/template-backed, versioned JSON definitions**
- Support workflow instance creation from:
  - manual start
  - schedule trigger
  - internal event trigger
- Persist and query:
  - workflow definition metadata
  - workflow instance state
  - current step
  - trigger source
  - context payload
- Provide a **curated workflow catalog UX/API** for admins
- Ensure the UX and API **do not expose arbitrary workflow composition/building**
- Seed or register a small initial set of opinionated workflow definitions
- Add validation/guardrails so only supported predefined workflow schemas/types are accepted
- Surface blocked/failed workflow instances for review in a basic query/view if the relevant story scaffolding already exists

Out of scope:
- Any drag-and-drop builder
- Arbitrary node/edge editing by end users
- Full BPMN-style engine
- Complex visual designer
- User-authored custom step graphs
- Broad workflow automation marketplace
- Full approval/escalation implementation beyond what is minimally needed to support workflow state
- Mobile work for this task unless required by shared contracts

Assumptions to honor:
- Architecture is a **modular monolith**
- Use **PostgreSQL** persistence patterns already present in the repo
- Keep **tenant isolation** enforced
- Prefer **CQRS-lite**
- Background execution/scheduling should align with existing worker patterns if present
- Version workflow definitions so in-flight instances are not broken by later changes

# Files to touch

Touch the minimum necessary files, but expect changes in these areas if they exist:

- `src/VirtualCompany.Domain/**`
  - workflow entities/value objects/enums
  - validation rules for predefined workflow types
- `src/VirtualCompany.Application/**`
  - commands/queries/DTOs for workflow catalog, definition retrieval, instance start, instance listing
  - validators
  - application services/handlers
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories
  - migrations
  - seed data for predefined workflow definitions
  - background scheduling/event trigger plumbing if already scaffolded
- `src/VirtualCompany.Api/**`
  - endpoints/controllers for workflow catalog, start workflow, list instances, get instance detail
- `src/VirtualCompany.Web/**`
  - admin workflow pages/components for curated predefined workflow catalog
  - start/configure workflow UI
  - remove/avoid any builder-oriented placeholder UX if present
- `src/VirtualCompany.Shared/**`
  - shared contracts/view models if used across API/Web/Mobile
- `README.md`
  - brief note on workflow v1 constraints if documentation exists for product capabilities

Also inspect for existing workflow-related files before adding new ones:
- `WorkflowDefinition*`
- `WorkflowInstance*`
- `Task*Workflow*`
- scheduler/worker infrastructure
- seed/migration folders
- navigation/menu/page registrations in web app

# Implementation plan

1. **Inspect current workflow implementation and align with existing patterns**
   - Search the solution for existing workflow/task/approval/scheduler code.
   - Reuse established conventions for:
     - entity base classes
     - tenant scoping
     - MediatR/command handlers if used
     - EF Core mappings
     - API endpoint style
     - Blazor page/component patterns
   - Identify whether `workflow_definitions` and `workflow_instances` already exist in code or only in architecture docs.

2. **Define the v1 product constraint explicitly in code**
   - Introduce a clear concept such as:
     - `WorkflowTemplateCode`
     - `WorkflowDefinitionType`
     - `PredefinedWorkflowCatalog`
   - Ensure definitions are created from a **known supported set** rather than arbitrary user-authored structures.
   - If there is already a generic `definition_json`, keep it, but validate it against a supported schema per predefined workflow type.
   - Add comments/docs where useful to make the v1 constraint obvious.

3. **Model workflow definitions for predefined workflows**
   - Ensure the domain supports:
     - `Id`
     - `CompanyId` nullable for system templates if that pattern already exists
     - `Code`
     - `Name`
     - `Department`
     - `Version`
     - `TriggerType`
     - `DefinitionJson`
     - `Active`
     - timestamps
   - Add validation rules such as:
     - code required and stable
     - version > 0
     - trigger type must be one of manual/schedule/event
     - definition payload must match one of the supported predefined workflow schemas
   - Prefer a typed internal representation for supported workflows even if persisted as JSON.

4. **Create a small predefined workflow catalog**
   - Seed a minimal set of opinionated workflows relevant to the product, for example:
     - daily executive briefing
     - invoice approval review
     - support escalation triage
     - lead follow-up
   - Keep the set small and realistic.
   - Each predefined workflow should include:
     - stable code
     - display name
     - description if the UI supports it
     - supported trigger types
     - version
     - default step metadata in JSON
   - Do not implement a generic “create any workflow” path.

5. **Implement workflow instance start flows**
   - Add application commands/endpoints for:
     - start predefined workflow manually
     - start workflow from schedule trigger
     - start workflow from internal event trigger
   - Persist:
     - workflow definition reference
     - trigger source
     - trigger ref if available
     - state
     - current step
     - context JSON
     - started/updated/completed timestamps
   - Ensure instances bind to the exact definition version used at start time.

6. **Implement workflow state/query support**
   - Add queries/endpoints for:
     - list available predefined workflows for a tenant/admin
     - get workflow definition detail
     - list workflow instances
     - get workflow instance detail
   - Include fields needed to review failures/blocked states.
   - If there is an existing admin dashboard area, integrate there rather than inventing a new pattern.

7. **Add failure/blocked visibility**
   - Ensure workflow instances can represent at least:
     - pending/running
     - blocked
     - failed
     - completed
   - If step execution is not fully implemented yet, still support setting and surfacing these states.
   - Add a basic exception/review view or status badge in the web UI if feasible within current structure.

8. **Deliver curated web UX, not builder UX**
   - In Blazor web, implement/administer:
     - a workflow catalog page showing predefined workflows
     - a details page or panel showing what the workflow does, trigger type, and version
     - a launch/start action for manual workflows
   - If there is any placeholder for “build your own workflow” or arbitrary JSON editing:
     - remove it
     - hide it
     - or replace it with copy that says workflows are currently predefined and curated
   - Keep the UX opinionated and simple.

9. **Enforce tenant and authorization boundaries**
   - All workflow definitions/instances must be tenant-scoped where appropriate.
   - System templates may be global, but instance creation must always resolve into tenant-safe usage.
   - Restrict catalog management/start actions to appropriate admin/manager roles based on existing authorization patterns.

10. **Add persistence and migration**
   - Add/update EF Core mappings and create a migration.
   - Seed predefined workflow definitions in a migration or startup seed path consistent with the repo.
   - Make seeding idempotent if the project already supports repeatable startup seeding.

11. **Keep extension points clean**
   - Structure the implementation so future tasks can add:
     - more predefined workflow types
     - richer step execution
     - approvals/escalations
     - scheduler/event dispatch integration
   - But do not over-engineer a generic builder abstraction now.

12. **Document the constraint**
   - Add a concise note in code comments and/or README:
     - v1 supports predefined workflows only
     - arbitrary workflow builder UX is intentionally deferred

# Validation steps

Run and verify the following:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Migration sanity**
   - Confirm the new migration applies cleanly.
   - Verify seeded predefined workflow definitions exist in the database.

4. **Manual/API verification**
   - Confirm an admin can retrieve a curated workflow catalog.
   - Confirm there is no endpoint/UI path for arbitrary workflow graph creation.
   - Start a manual workflow and verify:
     - instance row is created
     - definition version is captured
     - trigger source is persisted
     - initial state/current step are set
   - Trigger schedule/event-based start through the available application path or test harness if present.
   - Verify blocked/failed states can be queried/displayed.

5. **Tenant isolation**
   - Verify one company cannot list or access another company’s workflow instances/definitions except allowed global templates resolved through tenant-safe logic.

6. **Regression checks**
   - Ensure task/approval/scheduler areas still build and existing navigation works.
   - Ensure web pages render without exposing builder-oriented UX.

Include in your final implementation summary:
- files changed
- migration name
- seeded predefined workflows
- any intentionally deferred items

# Risks and follow-ups

- **Risk: overbuilding a generic engine**
  - Avoid introducing abstractions that imply arbitrary workflow authoring.
  - Favor a constrained catalog + typed validation approach.

- **Risk: schema drift between JSON definitions and code**
  - Mitigate by validating definition JSON against supported predefined workflow types and versions.

- **Risk: breaking future in-flight instances**
  - Ensure instances reference immutable definition versions.

- **Risk: tenant leakage**
  - Audit all queries and endpoints for `company_id` enforcement.

- **Risk: UI accidentally suggests unsupported capability**
  - Remove or reword any “builder,” “designer,” or “custom workflow” language.

Recommended follow-up tasks after this one:
- richer workflow execution engine for predefined step handlers
- scheduler integration hardening with distributed locking
- internal event trigger registry/dispatcher
- workflow exception review UX
- approval step integration into predefined workflows
- audit trail linkage for workflow lifecycle events