# Goal
Implement backlog task **TASK-8.1.3** for **ST-201 Agent template catalog and hiring flow** so that **newly hired agents appear in the company roster with `active` status by default**.

This change should ensure the default active status is applied consistently at the correct domain/application boundary, persisted to the database, and surfaced correctly in roster/query/UI flows without requiring manual status selection during hiring.

# Scope
In scope:
- Identify the current hire-agent flow across Web/API/Application/Domain/Infrastructure.
- Ensure newly created `agents` records default to status **`active`** when hired from a template.
- Ensure template-to-agent copy logic does not leave status null, empty, or in a non-active initial state unless explicitly intended elsewhere.
- Ensure roster queries/views include newly hired agents as active immediately after creation.
- Add or update automated tests covering the default status behavior.
- Make the implementation tenant-safe and aligned with the modular monolith / CQRS-lite architecture.

Out of scope:
- Broader agent lifecycle management for paused/restricted/archived beyond what is needed for default creation behavior.
- Redesigning the full hiring UX.
- Adding new policy/autonomy behavior unless required by existing validation paths.
- Large schema refactors unless the current model cannot safely represent the default.

# Files to touch
Inspect first, then modify only the minimal necessary set. Likely areas:

- `src/VirtualCompany.Domain/**`
  - Agent entity/value objects/enums/constants for status
- `src/VirtualCompany.Application/**`
  - Hire/create agent command, handler, DTOs, validators
  - Roster query models if status mapping is missing
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - Migrations if a DB default or nullability correction is needed
  - Repository implementations
- `src/VirtualCompany.Api/**`
  - Endpoint wiring only if request/response contracts need adjustment
- `src/VirtualCompany.Web/**`
  - Hiring flow page/form and roster page if UI currently assumes status is user-supplied or fails to display active agents
- Tests in any of:
  - `tests/**`
  - `src/**/Tests/**`
  - existing test projects in solution

Also review:
- `README.md` for conventions
- solution/project structure from `VirtualCompany.sln`

# Implementation plan
1. **Discover the current hiring path**
   - Find the command/endpoint/page used to hire an agent from a template.
   - Trace the flow from UI/API request -> application command -> domain creation -> persistence -> roster query.
   - Identify where `status` is currently set, omitted, or defaulted.

2. **Establish the canonical default in the domain/application layer**
   - Prefer a single authoritative default at creation time.
   - If there is an `AgentStatus` enum/constant set, use `Active`.
   - If status is a string today, centralize allowed values and avoid magic strings.
   - Ensure agent creation from template always assigns `active` unless an explicit status is intentionally passed by a trusted internal flow.
   - Do not rely only on UI defaults.

3. **Harden persistence behavior**
   - Verify the `agents.status` column mapping.
   - If appropriate, add a database default of `active` as a defensive measure, but still keep the application/domain default explicit.
   - If the column is nullable, consider tightening it if consistent with the existing codebase and migration safety.
   - Ensure existing inserts from the hiring flow persist `active`.

4. **Verify template copy behavior**
   - Confirm template defaults copied into company-owned agent records do not overwrite or omit the status incorrectly.
   - Keep status as a company-owned runtime property of the hired agent, not a template-driven accidental null/blank field.

5. **Ensure roster visibility**
   - Review the roster query/filter logic.
   - Confirm newly hired agents are returned and displayed with status `active`.
   - If the roster defaults to filtering active agents only, verify the new agent satisfies that filter immediately after creation.

6. **Update UI only if needed**
   - If the hire form currently exposes status selection, remove it unless required by existing product behavior.
   - If the roster badge/text mapping is missing or inconsistent, fix it.
   - Keep UX aligned with the story: hired agents should appear active by default.

7. **Add tests**
   Add focused tests at the highest-value layers available in the repo:
   - Application test: hiring an agent from a template creates an agent with status `active`.
   - Persistence/integration test: saved agent record has `active` status.
   - Query/UI test if present: roster includes the newly hired agent with active status.
   - Optional regression test: if request omits status, handler still sets `active`.

8. **Keep implementation minimal and consistent**
   - Follow existing naming, architecture, and test patterns.
   - Avoid introducing new abstractions unless the current code clearly needs one.
   - Preserve tenant scoping and authorization assumptions.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify the flow if the app is runnable locally:
   - Hire an agent from a seeded template.
   - Confirm the created `agents` record has status `active`.
   - Confirm the company roster shows the new agent immediately with active status.

4. If EF migrations were added:
   - Verify the migration is clean and targeted only to the status default/nullability change.
   - Confirm the app still builds and tests pass after migration generation/application.

5. Review for regressions:
   - Existing agent status transitions still compile and behave as before.
   - No tenant scoping or authorization logic was bypassed.
   - No UI/API contract now requires status input for standard hiring unless intentionally supported.

# Risks and follow-ups
- **Risk: default only in UI**
  - If only the form sets the default, API or background creation paths may still create non-active/null agents.
  - Mitigation: enforce default in domain/application creation logic.

- **Risk: stringly typed statuses**
  - If statuses are raw strings, typos and inconsistent casing may cause roster filtering bugs.
  - Mitigation: reuse existing enum/constants or introduce a minimal centralized status definition if one does not exist.

- **Risk: DB contains legacy null/invalid statuses**
  - New behavior may work while old records remain hidden from roster filters.
  - Mitigation: do not broaden this task unnecessarily, but note if a data backfill is needed.

- **Risk: roster query filters**
  - Even with correct persistence, the roster may exclude the new agent due to query timing, tenant filter, or status mapping issues.
  - Mitigation: validate end-to-end from creation through query projection.

- **Follow-up suggestion**
  - If not already covered elsewhere in ST-201, consider a separate task to enforce:
    - non-null `agents.status`
    - constrained allowed status values
    - seed/template hiring integration tests across the full flow