# Goal
Implement backlog task **TASK-9.3.6 — Include validity windows for stale memory handling** for story **ST-303 Agent and company memory persistence** in the existing .NET solution.

The coding agent should update the memory persistence and retrieval flow so that memory items explicitly support and honor **validity windows** for stale-memory handling. This should align with the architecture’s `memory_items` model, which already includes `valid_from` and `valid_to`, and ensure retrieval excludes expired or not-yet-valid memory by default while preserving tenant isolation and agent/company scoping.

# Scope
In scope:
- Review the current implementation of memory persistence, domain models, EF mappings, repositories, queries, and retrieval services.
- Add or complete support for `valid_from` and `valid_to` on memory items if missing anywhere in the stack.
- Ensure memory creation paths set sensible validity defaults.
- Ensure memory retrieval paths filter by validity window using current UTC time.
- Preserve support for:
  - company-wide vs agent-specific memory
  - memory types such as preference, decision pattern, summary, role memory, company memory
  - retrieval by agent, recency, salience, and semantic relevance
- Add or update tests covering validity-window behavior.
- Keep implementation tenant-safe and consistent with modular monolith / clean architecture boundaries.

Out of scope unless required by existing code structure:
- New UI for editing validity windows
- Broad redesign of memory ranking
- Privacy/deletion workflows beyond what is necessary to avoid breaking existing behavior
- Large schema redesign unrelated to validity windows

# Files to touch
Inspect and modify only the files needed after discovery. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - Memory domain entity/value objects/enums
  - Domain validation or factory methods
- `src/VirtualCompany.Application/**`
  - Commands/handlers for creating or updating memory
  - Queries/handlers for retrieving memory
  - Retrieval service contracts and implementations
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - DbContext mappings
  - Repository/query implementations
  - Migrations if schema changes are required
- `src/VirtualCompany.Api/**`
  - Request/response contracts only if API surfaces memory validity fields
- `src/VirtualCompany.Shared/**`
  - Shared DTOs/contracts if used across layers
- Test projects under `tests/**` or existing test locations
  - Unit tests for domain/application logic
  - Integration tests for persistence/query filtering

Also review:
- `README.md` for conventions if relevant
- Existing migration history before adding a new migration

# Implementation plan
1. **Discover current memory implementation**
   - Locate all memory-related types by searching for:
     - `memory_items`
     - `MemoryItem`
     - `valid_from`
     - `valid_to`
     - `role_memory`
     - `company_memory`
   - Identify:
     - domain entity shape
     - persistence mapping
     - create/update flows
     - retrieval/query flows
     - semantic retrieval path
     - any existing expiration/delete logic

2. **Compare implementation to architecture**
   - Verify whether the current code already models:
     - `ValidFrom`
     - `ValidTo`
   - If the DB schema or EF mapping is missing either field, add them in the correct layer and create a migration.
   - If fields exist in persistence but not in domain/application contracts, propagate them cleanly through the stack.

3. **Define validity-window behavior**
   - Implement the following default semantics unless existing code already establishes a stronger convention:
     - `ValidFrom` is required logically and defaults to creation time in UTC when not explicitly provided.
     - `ValidTo` is optional; `null` means no scheduled expiry.
     - A memory item is considered active when:
       - `ValidFrom <= nowUtc`
       - and (`ValidTo == null` or `ValidTo > nowUtc`)
   - Treat expired and not-yet-valid memory as excluded from normal retrieval.
   - Do not physically delete expired memory as part of this task unless existing code already does so.

4. **Update domain model and validation**
   - Ensure the memory entity or factory enforces:
     - UTC-safe timestamps
     - `ValidTo` cannot be earlier than `ValidFrom`
   - If there is a domain constructor/factory, centralize defaulting there.
   - Avoid leaking persistence concerns into domain logic.

5. **Update persistence mapping**
   - Ensure EF configuration maps validity fields correctly to PostgreSQL timestamp columns.
   - If schema changes are needed:
     - add migration with minimal, safe changes
     - preserve existing data
     - backfill `ValidFrom` for existing rows sensibly, likely from `CreatedAt` if available
   - Keep tenant and agent scoping intact.

6. **Update memory creation flows**
   - For all code paths that create memory items:
     - set `ValidFrom` if omitted
     - allow optional `ValidTo`
   - If memory is generated from summaries or orchestration outputs, ensure those paths do not create invalid windows.

7. **Update retrieval/query logic**
   - Apply validity filtering to all standard memory retrieval paths, including:
     - direct memory queries
     - context retrieval service
     - semantic memory retrieval if separate
   - Ensure filters are composed with existing constraints:
     - `company_id`
     - optional `agent_id`
     - memory type
     - salience
     - recency
     - semantic relevance
   - Prefer filtering in the database query rather than in-memory where possible.

8. **Preserve explainability and future policy support**
   - If retrieval results are returned as DTOs, include validity fields only if already appropriate for internal consumers.
   - Do not expose raw chain-of-thought or unrelated metadata.
   - Keep implementation compatible with future “delete or expire memory items according to policy controls.”

9. **Add tests**
   - Add focused tests for:
     - memory with `ValidFrom` in the past and `ValidTo` null is returned
     - memory with future `ValidFrom` is excluded
     - memory with past `ValidTo` is excluded
     - memory with `ValidTo` after now is returned
     - invalid window (`ValidTo < ValidFrom`) is rejected
     - retrieval still respects tenant and agent scoping alongside validity filtering
   - Prefer deterministic tests using injected clock/time provider if the codebase already has one; otherwise introduce a minimal abstraction only if justified by existing patterns.

10. **Keep changes minimal and idiomatic**
   - Follow existing project conventions, naming, and architecture.
   - Avoid speculative abstractions.
   - If you discover missing acceptance details, implement the smallest coherent behavior that satisfies the story note: **“Include validity windows for stale memory handling.”**

# Validation steps
1. Restore and build solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations were added, verify they compile and are included in the correct project.

4. Manually verify code paths:
   - creation of memory without explicit validity sets `ValidFrom`
   - retrieval excludes expired memory
   - retrieval excludes future-dated memory
   - retrieval includes active memory
   - no tenant-scope regressions

5. If integration tests exist for retrieval/context composition, ensure they still pass with validity filtering enabled.

# Risks and follow-ups
- **Risk: hidden retrieval paths**
  - Memory may be queried from multiple services, including orchestration/context retrieval and semantic search. Missing one path could cause inconsistent stale-memory behavior.

- **Risk: timestamp consistency**
  - Mixing local time and UTC could create subtle bugs. Normalize to UTC and follow existing conventions.

- **Risk: migration/backfill assumptions**
  - If existing rows lack validity data, backfill carefully. Prefer `CreatedAt` as `ValidFrom` when available.

- **Risk: ranking side effects**
  - Adding validity filters may change retrieval result counts and downstream prompt composition. Keep tests focused on expected active-memory behavior.

- **Follow-up suggestion**
  - Consider a future task to add explicit policy-driven expiration defaults by memory type, plus admin/user controls for expiring memory items.