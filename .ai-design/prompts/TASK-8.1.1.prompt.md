# Goal
Implement backlog task **TASK-8.1.1** for story **ST-201 Agent template catalog and hiring flow** by adding **versioned seed templates** for at least these agent roles:

- finance
- sales
- marketing
- support
- operations
- executive assistant

The implementation should fit the existing **.NET modular monolith** architecture and use configuration/data-driven templates rather than hardcoded role behavior.

Deliver the minimum complete slice needed so the system can provide these templates through the backend domain/application/infrastructure layers, with a path for the hiring flow to consume them.

# Scope
In scope:

- Add or extend the **agent template domain model** if needed to support seeded role templates.
- Add **persistence mapping** for `agent_templates` if not already present.
- Create **seed data / migration / startup seeding** for the required six templates.
- Ensure templates include sensible defaults aligned with architecture and story notes:
  - role name
  - department
  - default persona
  - default objectives
  - default KPIs
  - default tools
  - default scopes
  - default thresholds
  - default escalation rules
- Add an **application query/service** to retrieve available templates for the hiring flow if one does not already exist.
- Keep template definitions **versionable and maintainable**.
- Add tests covering seed presence and retrieval behavior.

Out of scope unless already trivial and clearly adjacent:

- Full UI hiring wizard
- Full agent creation flow from template
- Avatar upload implementation
- Rich template management UI
- Role-specific orchestration logic outside config
- Non-required template categories beyond the six listed roles

# Files to touch
Inspect the solution first, then touch the smallest correct set of files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - agent template entity/value objects/enums if missing or incomplete
- `src/VirtualCompany.Application/**`
  - queries/DTOs/handlers for listing agent templates
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration
  - seeding implementation
  - migrations
  - repository/query implementation
- `src/VirtualCompany.Api/**`
  - endpoint/controller/minimal API exposure for template catalog if not already present
- `src/VirtualCompany.Shared/**`
  - shared contracts if this solution uses shared DTOs
- `src/VirtualCompany.Web/**`
  - only if a minimal existing page already consumes template data and needs wiring
- `README.md`
  - only if there is an established section for seed/setup behavior

Also inspect for existing files such as:

- DbContext and EF migrations
- seed/bootstrap classes
- agent management module folders
- existing `AgentTemplate` entity/configuration
- existing query endpoints for roster/hiring

# Implementation plan
1. **Inspect current architecture in code**
   - Find whether `AgentTemplate` already exists in domain and persistence.
   - Find how seed data is currently handled:
     - EF `HasData`
     - startup seeder
     - SQL migration
     - JSON seed loader
   - Find whether there is already an endpoint/query for listing templates.

2. **Model the template shape cleanly**
   - If `agent_templates` already exists, align to it.
   - If missing, implement a domain/persistence model matching the architecture:
     - `id`
     - `role_name`
     - `department`
     - `default_persona_json`
     - `default_objectives_json`
     - `default_kpis_json`
     - `default_tools_json`
     - `default_scopes_json`
     - `default_thresholds_json`
     - `default_escalation_rules_json`
     - `created_at`
   - Prefer strongly typed domain structures where the codebase already uses them; otherwise use disciplined JSON-backed config objects rather than raw string blobs scattered through the code.

3. **Create the six required seed templates**
   Add templates for:
   - Finance
   - Sales
   - Marketing
   - Support
   - Operations
   - Executive Assistant

   Each template should include realistic but conservative defaults. Example expectations:
   - **Finance**: budgeting/reporting/reconciliation-oriented objectives, finance KPIs, conservative thresholds/escalations
   - **Sales**: pipeline/follow-up/revenue support objectives
   - **Marketing**: campaign/content/lead-gen objectives
   - **Support**: ticket triage/response quality/customer satisfaction objectives
   - **Operations**: process tracking/vendor coordination/internal execution objectives
   - **Executive Assistant**: scheduling/briefing/follow-up/coordination objectives

   Keep defaults safe and generic:
   - no broad execute permissions by default
   - conservative scopes
   - approval-oriented thresholds
   - escalation rules that route sensitive work to humans

4. **Make seed data versionable**
   - Use the project’s established migration/seeding pattern.
   - Avoid ad hoc runtime duplication.
   - Ensure reruns are idempotent.
   - If using fixed IDs, define stable GUIDs for each template so future migrations can update them safely.

5. **Expose template catalog retrieval**
   - If no application query exists, add one such as:
     - `GetAgentTemplatesQuery`
     - returning a lightweight DTO for hiring/catalog use
   - Include fields needed by the hiring flow:
     - template id
     - role name
     - department
     - persona summary / display summary if available
     - defaults needed for prefill
   - Keep tenant-independent system templates separate from company-owned agents.

6. **Wire API endpoint if needed**
   - Add a read endpoint under the existing API conventions, e.g. agent management/templates.
   - Ensure authorization follows existing patterns.
   - Since templates are system-provided catalog data, they should be readable in the proper authenticated company context, but not tenant-mutated here.

7. **Add tests**
   Add focused tests for:
   - seed contains at least the six required role templates
   - retrieval query returns those templates
   - template records are stable and non-duplicating across seed runs if applicable
   - required fields are populated for each template

8. **Keep implementation aligned with story notes**
   - Seed data should be versioned/migrated.
   - Avoid hardcoding role behavior outside config where possible.
   - Prepare for later hiring flow to copy template defaults into company-owned `agents` records.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If EF migrations are used, create/apply or verify migration:
   - confirm `agent_templates` exists and seed rows are inserted
   - verify exactly the required role templates are present at minimum

4. Validate retrieval path:
   - run the API locally if needed
   - call the template catalog endpoint or application handler
   - confirm returned templates include:
     - finance
     - sales
     - marketing
     - support
     - operations
     - executive assistant

5. Validate data quality:
   - each template has non-empty role name and department
   - each template has populated default config payloads
   - defaults are conservative and suitable for later policy enforcement

6. Validate idempotency/versioning behavior:
   - rerun app startup or migration path
   - confirm no duplicate template rows are created

# Risks and follow-ups
- **Unknown existing seeding pattern**: The repo may already have a preferred bootstrap mechanism. Follow that instead of introducing a new one.
- **JSON config drift**: If template defaults are stored as raw JSON without typed validation, future changes may become brittle. Prefer typed config objects where feasible.
- **Catalog vs tenant ownership ambiguity**: System templates should remain distinct from company-owned hired agents. Do not accidentally scope templates as tenant-created records unless the existing design explicitly does so.
- **Future hiring flow dependency**: This task likely precedes or supports the “create agent from template” flow. Keep DTOs and IDs stable so later work can copy defaults into `agents`.
- **Migration safety**: If using `HasData`, updates to seeded JSON can be awkward. If the codebase already uses explicit seed services or SQL migrations, that may be safer for versioned template evolution.
- **Display metadata gap**: The story only requires seed templates, but the UI may later need icons, descriptions, and avatar defaults. Consider a follow-up task for catalog presentation metadata if not already modeled.