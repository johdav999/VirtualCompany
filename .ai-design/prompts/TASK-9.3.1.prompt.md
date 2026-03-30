# Goal
Implement backlog task **TASK-9.3.1** for story **ST-303 — Agent and company memory persistence** so that memory items can be persisted as either:

- **company-wide memory** (`agent_id = null`)
- **agent-specific memory** (`agent_id = <agent uuid>`)

The implementation must fit the existing **.NET modular monolith** architecture, preserve **tenant isolation**, and align with the documented `memory_items` schema and story intent around durable memory summaries/preferences.

# Scope
Implement the minimum end-to-end backend support required for this task in the Knowledge & Memory area:

- Domain model support for memory items with optional `agent_id`
- Persistence mapping and database migration updates for `memory_items`
- Application command/query handling for creating and retrieving memory items with company-wide vs agent-specific scope
- Validation rules ensuring:
  - every memory item belongs to a company
  - `agent_id` is optional
  - if `agent_id` is provided, the agent must belong to the same company
- Retrieval behavior that can distinguish:
  - company-wide memory
  - agent-specific memory
  - combined retrieval for a given agent (agent-specific + company-wide)
- Basic delete/expire support only if there is already an established pattern in the module; otherwise do not expand scope beyond what is necessary for this task

Out of scope unless already trivially present in the codebase:

- Full semantic retrieval/ranking implementation
- UI work in Blazor or MAUI
- Background embedding generation workflows
- Full policy administration UX
- Broad refactors unrelated to memory persistence

# Files to touch
Inspect the solution first and then update the relevant files in the existing project structure. Likely touch points include:

- `src/VirtualCompany.Domain/...`
  - memory entity/aggregate/value objects
  - enums/constants for memory type/scope if present
- `src/VirtualCompany.Application/...`
  - commands/queries/DTOs/validators/handlers for memory create/read
- `src/VirtualCompany.Infrastructure/...`
  - EF Core entity configuration
  - repository implementation
  - migration(s)
- `src/VirtualCompany.Api/...`
  - API endpoints/controllers/minimal API mappings for memory operations if they already exist
- `src/VirtualCompany.Shared/...`
  - contracts shared across layers if applicable
- tests in the corresponding test projects if present in the workspace

Also inspect:

- existing tenant scoping patterns
- agent repository/query patterns
- conventions for Result/Error handling
- existing migration naming conventions
- any existing Knowledge/Memory module folders

# Implementation plan
1. **Inspect current memory implementation**
   - Search for `memory_items`, `MemoryItem`, `Knowledge`, `Memory`, `agent_id`, and related handlers/endpoints.
   - Determine whether ST-303 already has partial implementation.
   - Reuse existing architectural patterns rather than inventing new ones.

2. **Model company-wide vs agent-specific memory explicitly**
   - Ensure the domain model represents memory ownership/scope through:
     - required `CompanyId`
     - optional `AgentId`
   - Do **not** create separate tables for company and agent memory.
   - If helpful and consistent with the codebase, add a derived property/helper such as:
     - `IsCompanyWide => AgentId == null`
     - `IsAgentSpecific => AgentId != null`
   - Preserve existing memory fields from the architecture:
     - `memory_type`
     - `summary`
     - `source_entity_type`
     - `source_entity_id`
     - `salience`
     - `valid_from`
     - `valid_to`
     - `metadata_json`
     - embedding fields if already modeled

3. **Update persistence mapping**
   - Ensure EF configuration maps `AgentId` as nullable.
   - Ensure foreign key relationship to `agents` is optional.
   - Ensure indexes support expected retrieval patterns. Prefer adding/confirming indexes such as:
     - `(company_id, agent_id, created_at)`
     - `(company_id, memory_type)`
     - any existing vector/validity indexes already used by the project
   - If the table already exists but does not support nullable `agent_id`, add a migration to fix it safely.

4. **Enforce tenant-safe validation**
   - In create/update flows, validate:
     - `company_id` is required
     - `agent_id` may be null
     - if `agent_id` is not null, the referenced agent exists and belongs to the same `company_id`
   - Reject cross-tenant references.
   - Follow existing application validation/error conventions.

5. **Implement create memory flow**
   - Add or update a command/handler for creating memory items.
   - Support both:
     - company-wide creation: `agent_id = null`
     - agent-specific creation: `agent_id = provided`
   - Ensure the persisted record clearly reflects the intended scope without extra flags unless the codebase already uses one.

6. **Implement retrieval behavior**
   - Add or update query methods to support:
     - retrieving only company-wide memory for a company
     - retrieving only agent-specific memory for an agent
     - retrieving combined memory for an agent, including:
       - records where `company_id = X` and `agent_id is null`
       - records where `company_id = X` and `agent_id = targetAgentId`
   - Keep retrieval deterministic and tenant-scoped.
   - If there is already a retrieval service abstraction, extend it there rather than duplicating query logic.

7. **Handle validity/expiration filtering if already part of current retrieval**
   - If the current code already filters active memory, preserve that behavior.
   - Typical active filter:
     - `valid_from <= now`
     - `valid_to is null || valid_to > now`
   - Do not introduce broad new expiration features unless needed for this task.

8. **Expose API surface if missing**
   - If memory endpoints already exist, extend them to accept nullable `agentId`.
   - If no endpoint exists but the module pattern expects one, add minimal endpoints for:
     - create memory item
     - list memory items by scope
   - Keep request/response contracts simple and aligned with existing API style.

9. **Add tests**
   Add focused tests covering the task behavior:
   - create company-wide memory with `agent_id = null`
   - create agent-specific memory with valid `agent_id`
   - reject agent-specific memory when agent belongs to another company
   - retrieve company-wide memory only
   - retrieve agent-specific memory only
   - retrieve combined memory for an agent returns both company-wide and that agent’s records
   - ensure another company’s memory is never returned

10. **Keep implementation narrow**
   - Do not add speculative abstractions.
   - Do not implement full semantic ranking unless already present.
   - Prefer incremental changes that fit the current solution structure.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used in this repo, create/apply/verify the migration for the `memory_items` table update:
   - confirm `agent_id` is nullable
   - confirm FK remains valid
   - confirm indexes exist as intended

4. Manually verify behavior through tests or API calls:
   - create a memory item with `agentId = null` and confirm it persists as company-wide
   - create a memory item with a valid `agentId` in the same company and confirm it persists as agent-specific
   - attempt create with an `agentId` from another company and confirm validation failure
   - query combined memory for an agent and confirm both scopes are returned
   - query from another tenant context and confirm no leakage

5. Ensure code quality checks pass if configured in the repo:
   - formatting/analyzers already used by the solution
   - no warnings introduced unnecessarily

# Risks and follow-ups
- **Schema drift risk:** The actual codebase may already differ from the architecture doc. Inspect first and adapt to existing entities/migrations rather than forcing the documented schema literally.
- **Tenant isolation risk:** The most important failure mode is allowing an `agent_id` from another company or returning memory across tenants. Validate this explicitly in both write and read paths.
- **Query semantics risk:** “Agent memory retrieval” can mean either only agent-specific records or agent-specific plus company-wide defaults. Implement both explicit modes if possible, and ensure the combined mode is the one used for agent runtime retrieval.
- **Migration risk:** If existing data assumes non-null `agent_id`, make the migration backward-compatible and avoid destructive changes.
- **Future follow-up:** Subsequent tasks will likely require salience, recency, semantic relevance, expiration, and deletion policy controls. Keep the repository/query design extensible for those filters without implementing them prematurely now.