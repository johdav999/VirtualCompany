# Goal
Implement backlog task **TASK-7.3.3** for story **ST-103 Human user invitation and role assignment** by ensuring the system supports assigning the following human membership roles within a company workspace:

- `owner`
- `admin`
- `manager`
- `employee`
- `finance_approver`
- `support_supervisor`

The implementation must make these roles first-class, persisted, validated, and usable by subsequent authorization checks. Keep the design tenant-scoped and do **not** couple human roles to agent permissions.

# Scope
Focus only on the role assignment capability for company memberships. This task is complete when:

- The allowed membership role set is defined centrally in domain/application code.
- Membership create/update/invite flows can accept and persist only supported roles.
- Existing invitation and membership APIs/commands use the centralized role definition.
- Validation rejects unsupported role values with clear errors.
- Authorization-facing code can reliably read the assigned role from persisted memberships.
- Any UI role selectors involved in invite/edit membership flows expose the supported roles.

Out of scope unless required to make this task work end-to-end:

- Email delivery mechanics beyond existing invitation flow wiring.
- Full authorization policy matrix per role.
- Re-invite/revoke flows unless current code must be adjusted for compilation.
- Agent permission changes.
- Mobile-specific UX unless it already consumes shared role metadata.

# Files to touch
Inspect the solution first and then update the minimal correct set. Likely areas:

- `src/VirtualCompany.Domain/**`
  - membership role enum/value object/constants
  - membership aggregate/entity validation
- `src/VirtualCompany.Application/**`
  - invite user command/request DTOs
  - update membership role command/request DTOs
  - validators
  - query models returning available roles if such endpoint exists
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration/value conversion if role storage needs mapping
  - migrations if schema/value constraints are enforced in DB
- `src/VirtualCompany.Api/**`
  - request contracts/endpoints for invite/update membership role
  - OpenAPI/contract exposure if applicable
- `src/VirtualCompany.Web/**`
  - invite teammate form
  - edit membership role UI
  - role dropdown/select options
- `src/VirtualCompany.Shared/**`
  - shared contracts/constants if roles are shared between API and UI
- `README.md` or relevant docs only if role list is documented there

Also inspect for existing files related to:
- `CompanyMembership`
- `MembershipRole`
- `InviteUser`
- `UpdateMembership`
- `Authorization`
- `Policies`
- `Seed` or `Constants`

# Implementation plan
1. **Discover current membership model**
   - Find the company membership entity/aggregate and identify how `role` is currently represented:
     - raw string
     - enum
     - value object
     - shared constant list
   - Find all invite/update membership flows and all places where role strings are hardcoded.
   - Search for existing role values to avoid introducing duplicates or inconsistent naming.

2. **Define a single source of truth for membership roles**
   - Introduce or update a centralized role definition in the most appropriate layer, preferably domain-first.
   - Use stable persisted values. Prefer normalized machine-friendly values:
     - `owner`
     - `admin`
     - `manager`
     - `employee`
     - `finance_approver`
     - `support_supervisor`
   - Expose helper methods such as:
     - list all assignable roles
     - validate role value
     - parse/normalize incoming values if needed
   - If the codebase already uses a string-based pattern, keep it consistent rather than forcing a broad enum refactor.

3. **Enforce validation in commands and API contracts**
   - Update invite teammate flow so only supported roles are accepted.
   - Update membership role change flow so only supported roles are accepted.
   - Add field-level validation with clear messages, e.g. invalid role must mention allowed values.
   - Ensure null/empty role handling follows current conventions.

4. **Persist and map roles correctly**
   - Confirm the membership persistence model stores the role value without transformation bugs.
   - If EF configuration or converters are needed, add them.
   - If the database already stores role as text, avoid unnecessary schema churn unless there is an existing DB check constraint pattern.
   - If there are DB-level constraints for role values, update them and add a migration.

5. **Update authorization-facing usage**
   - Ensure any authorization or tenant access code that reads membership roles can recognize the new supported values.
   - Do not implement a full permission matrix unless already partially present and required for compilation.
   - Preserve the separation between human roles and agent permissions.

6. **Update UI role selection**
   - Update any web invite/edit membership forms to show the supported assignable roles.
   - Prefer binding to shared role metadata instead of duplicating string literals in Razor components.
   - Use human-readable labels while persisting canonical values:
     - Owner
     - Admin
     - Manager
     - Employee
     - Finance Approver
     - Support Supervisor

7. **Add or update tests**
   - Domain/application tests for valid role acceptance.
   - Validation tests for invalid role rejection.
   - Persistence tests if role mapping is non-trivial.
   - UI/component tests only if the repo already uses them and they are lightweight.

8. **Keep changes narrow**
   - Do not redesign the broader invitation system.
   - Do not introduce speculative role hierarchy logic unless required by existing code.
   - Prefer incremental consistency over large refactors.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add/verify automated coverage for:
   - inviting a user with each supported role succeeds
   - updating a membership to each supported role succeeds
   - unsupported role values fail validation
   - persisted membership role is returned unchanged on subsequent reads/queries

4. Manually verify, if web UI exists for this flow:
   - invite form shows all six roles
   - edit membership role form shows all six roles
   - submitting a valid role persists successfully
   - submitting an invalid role is blocked with a clear message

5. If API endpoints exist, verify request/response contracts:
   - role field accepts canonical values
   - API returns validation errors for unsupported values
   - subsequent authorization-related reads see the updated role

# Risks and follow-ups
- **Role naming mismatch risk:** Existing code may already use spaced labels like `finance approver` instead of canonical persisted values. Normalize carefully and update all references consistently.
- **Hidden hardcoded strings:** Authorization, seed data, UI dropdowns, and tests may each have their own role lists. Search thoroughly before finalizing.
- **DB constraint drift:** If the database has check constraints or seed assumptions, app-only changes may compile but fail at runtime.
- **Backward compatibility:** If any existing data uses older role names, consider a migration or compatibility parser.
- **Future follow-up:** A later task should define explicit authorization policies/capabilities per human role, but that is not part of this task unless needed for current flows.
- **Future follow-up:** Consider exposing role metadata from a shared endpoint or shared contract to avoid UI/API duplication across web and mobile.