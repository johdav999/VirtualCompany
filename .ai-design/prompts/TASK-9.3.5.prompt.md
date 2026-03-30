# Goal
Implement backlog task **TASK-9.3.5 — Store summaries, not raw chain-of-thought** for story **ST-303 Agent and company memory persistence**.

The coding agent must ensure the system persists only **sanitized memory summaries** and never stores raw model chain-of-thought, hidden reasoning, scratchpads, or equivalent internal deliberation in durable memory records.

This change should align with the architecture and backlog notes:
- Memory persistence belongs to the **Knowledge & Memory Module**
- Auditability stores **rationale summaries**, not raw reasoning
- Chat/orchestration must avoid exposing or persisting chain-of-thought
- Multi-tenant boundaries must be preserved

# Scope
In scope:
- Review current memory persistence flow for `memory_items` creation and update paths
- Introduce or enforce a clear application/domain rule that only summary-safe content is stored in memory
- Prevent raw chain-of-thought-like fields from being persisted into:
  - `memory_items.summary`
  - related metadata fields if currently used to stash raw reasoning
  - any memory creation command/service DTOs that may carry unsafe reasoning text
- Add sanitization/validation at the application boundary so memory writes are safe by default
- Update orchestration/memory-writing code paths to pass only summary content
- Add tests covering allowed summary persistence and rejection/transformation of unsafe reasoning payloads

Out of scope unless required by existing code structure:
- Broad redesign of the entire orchestration engine
- UI redesign
- Large schema redesign if current schema already supports summary-only storage
- Retrofitting historical production data beyond a minimal migration if clearly necessary

Implementation intent:
- Prefer **explicit summary fields** and **safe contracts**
- Prefer **default-deny** behavior for unsafe memory payloads
- Keep tenant scoping intact
- Keep the solution consistent with Clean Architecture/module boundaries

# Files to touch
Inspect the solution first, then update the most relevant files in these areas as needed:

- `src/VirtualCompany.Domain/**`
  - Memory-related entities/value objects/policies
  - Any domain invariants for memory content
- `src/VirtualCompany.Application/**`
  - Commands/handlers/services for creating/updating memory items
  - Orchestration-to-memory mapping logic
  - Validators and DTOs
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations/repositories
  - Persistence adapters for memory storage
  - Any background worker or ingestion pipeline that writes memory
- `src/VirtualCompany.Api/**`
  - Request contracts/endpoints if memory write APIs exist
- Potentially `src/VirtualCompany.Web/**` or `src/VirtualCompany.Shared/**`
  - Shared contracts/view models if they currently expose unsafe fields
- Tests in corresponding test projects if present

Also inspect:
- Existing migration files and DbContext mappings for `memory_items`
- Any orchestration services that generate memory from chat/task/tool outputs
- Any audit/explainability code to ensure it already uses summaries and not raw reasoning

# Implementation plan
1. **Discover current memory write paths**
   - Search for:
     - `memory_items`
     - `MemoryItem`
     - `rationale_summary`
     - `chain`
     - `reasoning`
     - `scratchpad`
     - `summary`
     - memory creation handlers/services/repositories
   - Identify every place where durable memory is created:
     - direct API commands
     - orchestration pipeline
     - background jobs
     - task/workflow completion hooks
   - Document whether raw LLM output or hidden reasoning can currently flow into persistence.

2. **Define the rule in code**
   - Add a domain/application rule such as:
     - memory persistence accepts only a **summary**
     - raw chain-of-thought / reasoning fields are not part of the persistence contract
   - If there is a memory entity factory or constructor, enforce:
     - non-empty summary
     - optional max length if project conventions support it
     - rejection of explicitly unsafe fields if they exist in the contract
   - If there is no central rule, create one in the application layer with a focused policy/helper.

3. **Harden contracts**
   - Update request/command DTOs so they do not accept fields like:
     - `chainOfThought`
     - `reasoning`
     - `internalNotes`
     - `scratchpad`
     - raw model transcript fields intended for hidden reasoning
   - If such fields already exist and cannot be removed safely, mark them ignored/deprecated and ensure they are never persisted.
   - Rename ambiguous fields where needed so intent is explicit:
     - prefer `Summary` / `RationaleSummary`
     - avoid generic `Content` if it currently invites misuse

4. **Sanitize or reject unsafe content**
   - Implement one of these approaches based on current architecture:
     - **Preferred:** reject unsafe payloads at validation time
     - **Fallback:** sanitize/transform to a safe summary before persistence if the system already depends on broader payloads
   - Add a focused helper/policy, e.g. `MemoryContentPolicy`, `MemorySummarySanitizer`, or equivalent.
   - The rule should ensure:
     - only concise summary text is stored
     - obvious chain-of-thought markers are not persisted
     - metadata does not become a loophole for raw reasoning storage

5. **Update orchestration integration**
   - Inspect the shared orchestration subsystem and any memory extraction logic.
   - Ensure memory-writing code stores only:
     - summary
     - memory type
     - salience
     - validity window
     - source entity references
     - safe metadata
   - Do not persist:
     - hidden model reasoning
     - intermediate planning text
     - tool deliberation traces unless already explicitly approved and summary-safe
   - If prompts or structured outputs currently include both final answer and reasoning, map only the safe summary into memory persistence.

6. **Review persistence model**
   - Confirm the existing schema already supports summary-only storage:
     - `memory_items.summary`
     - `metadata_json`
   - If schema changes are needed, keep them minimal.
   - If there are columns that currently store raw reasoning for memory, remove or stop using them.
   - Avoid storing unsafe content in JSONB metadata.

7. **Add tests**
   - Unit tests for memory creation policy:
     - accepts valid summary-only memory item
     - rejects empty summary
     - rejects or strips unsafe reasoning fields/content depending on chosen design
   - Application tests for handlers/services:
     - orchestration-generated memory persists summary only
     - metadata remains safe
     - tenant scoping remains enforced
   - If integration tests exist:
     - verify persisted `memory_items` rows contain summary text only
     - verify no raw reasoning field is written anywhere in the memory persistence path

8. **Keep audit/explainability aligned**
   - Verify related audit/explainability flows already use concise summaries.
   - If memory creation reuses rationale fields, ensure they remain summary-safe and user-facing.
   - Do not expand audit storage to include raw chain-of-thought.

9. **Document with code comments only where necessary**
   - Add brief comments near the policy/validator explaining:
     - why summary-only storage is required
     - that raw chain-of-thought must never be persisted

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add/verify targeted tests for this task:
   - memory creation accepts safe summary content
   - unsafe reasoning payloads are rejected or sanitized
   - orchestration-to-memory mapping persists only summary-safe fields
   - no raw chain-of-thought is stored in memory metadata

4. Manually review code paths:
   - confirm all memory write paths go through the new rule/policy
   - confirm no API/shared contract still encourages raw reasoning persistence
   - confirm tenant/company scoping is unchanged

5. If EF migrations are introduced:
   - ensure migration is minimal and consistent
   - verify application still builds and tests pass after migration changes

# Risks and follow-ups
- **Risk: hidden persistence paths**
  - Memory may be written from background jobs, orchestration callbacks, or import pipelines not obvious at first glance.
  - Mitigation: search broadly and centralize memory creation logic.

- **Risk: ambiguous existing contracts**
  - Generic fields like `Content` or `Notes` may currently carry mixed-purpose text.
  - Mitigation: tighten naming and validation.

- **Risk: metadata loophole**
  - Even if `summary` is safe, raw reasoning could still be serialized into `metadata_json`.
  - Mitigation: explicitly validate/whitelist metadata keys if needed.

- **Risk: breaking existing flows**
  - Some orchestration code may currently depend on richer payloads.
  - Mitigation: preserve internal transient use if necessary, but block durable persistence of unsafe content.

- **Follow-up recommendation**
  - Consider a broader cross-cutting policy for all persistence surfaces so the same “no raw chain-of-thought” rule also applies to:
    - messages
    - audit events
    - task outputs
    - tool execution records
  - This task should at minimum secure **memory persistence**, but the same principle likely belongs platform-wide.