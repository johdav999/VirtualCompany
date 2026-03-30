# Goal
Implement backlog task **TASK-9.4.5 — Keep retrieval deterministic and testable** for **ST-304 Grounded context retrieval service**.

The coding agent should make the grounded context retrieval pipeline deterministic in behavior, ordering, and outputs so it can be reliably unit/integration tested and safely reused by orchestration flows.

This task should strengthen the retrieval service so that:
- retrieval results are produced in a stable, explicit order
- tie-breaking is deterministic
- time-dependent behavior is injectable/testable
- randomness/non-deterministic iteration is removed from retrieval composition
- prompt-ready context sections are assembled from deterministic inputs
- source references remain stable and auditable
- tests clearly prove determinism across repeated runs

# Scope
In scope:
- The application-layer grounded context retrieval service for ST-304
- Any supporting contracts/models needed to make retrieval deterministic
- Deterministic ordering rules for:
  - documents/chunks
  - memory items
  - recent tasks/history
  - relevant records if already implemented
  - final prompt-ready sections
  - persisted source references
- Injection of clock/time provider if retrieval currently depends on `DateTime.UtcNow` or equivalent
- Removal of hidden randomness or unstable ordering from LINQ/materialization
- Unit tests and, where appropriate, integration tests covering deterministic behavior
- Small refactors needed to isolate retrieval composition from infrastructure concerns

Out of scope unless required by existing code:
- New product features beyond determinism/testability
- UI/controller prompt assembly
- Large architecture rewrites
- New caching behavior unless a tiny adjustment is required to preserve deterministic outputs
- Reworking unrelated orchestration components

If the retrieval service does not yet fully exist, implement the minimum deterministic retrieval composition layer aligned to ST-304 rather than expanding into broader orchestration work.

# Files to touch
Inspect the solution first and then update the most relevant files under these likely areas:

- `src/VirtualCompany.Application/**`
  - retrieval service interfaces/implementations
  - query handlers or orchestration-facing services
  - DTOs/models for prompt-ready context sections and source references
- `src/VirtualCompany.Domain/**`
  - value objects or enums only if deterministic ordering/source typing belongs in domain
- `src/VirtualCompany.Infrastructure/**`
  - repository/query implementations if ordering currently happens there or needs explicit SQL ordering
  - time provider wiring if needed
- `src/VirtualCompany.Api/**`
  - DI registration only if required
- test projects in the repo, likely under `tests/**` or matching `*.Tests`
  - unit tests for retrieval composition
  - integration tests for repository/query ordering if present

Before coding, identify the actual files that implement:
- context retrieval
- memory retrieval
- semantic search/chunk retrieval
- recent task/history retrieval
- source reference persistence
- DI/service registration
- existing tests

Prefer touching the smallest set of files necessary.

# Implementation plan
1. **Discover the current retrieval path**
   - Find the implementation for ST-304 or nearest equivalent:
     - `ContextRetriever`
     - grounded context service
     - orchestration runtime context builder
     - memory/document retrieval query services
   - Trace how it currently gathers:
     - company docs/chunks
     - memory items
     - recent tasks/history
     - relevant records
     - source references
   - Identify all non-deterministic behavior:
     - missing `OrderBy`
     - ordering by non-unique fields without tie-breakers
     - dictionary/hash-set iteration affecting output order
     - `DateTime.UtcNow` usage
     - random sampling/top-N selection without stable secondary sort
     - SQL queries lacking explicit `ORDER BY`

2. **Define explicit deterministic ordering rules**
   Apply stable ordering rules and document them in code comments near the retrieval composer. Use the same rules in tests.

   Recommended ordering approach:
   - **Document chunks / semantic results**
     - primary: descending relevance/similarity score
     - secondary: source/document priority if already modeled
     - tertiary: document id
     - quaternary: chunk index
     - final tie-breaker: chunk id
   - **Memory items**
     - primary: descending semantic relevance if applicable
     - secondary: descending salience
     - tertiary: descending valid-from or created-at recency
     - final tie-breaker: memory item id
   - **Recent tasks/history**
     - primary: descending updated/completed/created timestamp per existing business rule
     - final tie-breaker: task id
   - **Relevant records**
     - use business-relevant timestamp or rank
     - final tie-breaker: entity id
   - **Final prompt-ready sections**
     - fixed section order, e.g.:
       1. task/request context
       2. recent history
       3. company knowledge
       4. agent/company memory
       5. relevant records
       6. policies/scopes metadata if included
   - **Source references**
     - persist in the same order as the final included items, or normalize to a fixed source-type order plus stable item ordering

   Do not rely on provider/default ordering anywhere.

3. **Make time-dependent behavior injectable**
   - If retrieval filters by recency, validity windows, or “current time”, replace direct calls to `DateTime.UtcNow`/`DateTime.Now` with an injected abstraction.
   - Prefer .NET `TimeProvider` if the codebase already uses it; otherwise add a minimal application abstraction.
   - Ensure tests can freeze time and verify deterministic inclusion/exclusion of memory items and recent history.

4. **Separate retrieval composition from raw data access**
   - If current code mixes repository calls and prompt section assembly in a hard-to-test method, extract a pure composition step.
   - Suggested shape:
     - repositories/query services return candidate items with all ranking metadata
     - a deterministic composer normalizes, sorts, truncates, and maps to prompt-ready sections
   - Keep this composer side-effect free where possible so it is easy to unit test.

5. **Normalize ranking and truncation behavior**
   - Ensure top-N selection happens **after** deterministic sorting.
   - If scores are floating-point values, avoid unstable comparisons by:
     - sorting by score then deterministic tie-breakers
     - not assuming exact equality semantics beyond ordering
   - If multiple retrieval sources are merged, define explicit merge rules rather than concatenating provider outputs in incidental order.

6. **Stabilize source reference generation**
   - Ensure source references persisted for audit/explanation are generated from the final deterministic result set.
   - Include stable fields such as:
     - source type
     - source entity id
     - document id/chunk id where applicable
     - display label/title
     - rank/order index
   - If rank/order is stored, make it explicit and deterministic.

7. **Add/adjust tests**
   Add focused tests that prove determinism. Prefer small, readable fixtures.

   Minimum test coverage:
   - **Repeated-run determinism**
     - same inputs produce identical ordered outputs across multiple executions
   - **Tie-break determinism**
     - equal scores still produce stable ordering via ids/chunk index/etc.
   - **Time-window determinism**
     - frozen clock yields stable inclusion/exclusion for recency/validity filters
   - **Section ordering**
     - final prompt-ready sections always appear in fixed order
   - **Source reference ordering**
     - persisted/emitted references match final deterministic order
   - **Repository/query ordering**
     - if SQL/EF queries are involved, verify explicit ordering or verify composer reorders correctly after materialization

   Good test patterns:
   - shuffle input collections before passing to composer and assert same output
   - run the same retrieval 10+ times and compare serialized result snapshots/objects
   - use fixed GUIDs/timestamps for tie-break scenarios

8. **Keep API/contracts backward compatible where possible**
   - Avoid breaking public contracts unless necessary.
   - If adding rank/order metadata to source references or context items, do so in a non-breaking way if possible.
   - If a breaking change is unavoidable, keep it minimal and update all internal callers/tests.

9. **Document assumptions in code**
   - Add concise comments where deterministic ordering is intentional.
   - If there are unresolved ranking ambiguities, leave a clear TODO tied to ST-304/TASK-9.4.5 rather than guessing.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there is a targeted test project, run it directly as well.

4. Manually verify in code review that:
   - every retrieval query/composition path has explicit ordering
   - all top-N truncation occurs after sorting
   - no direct `DateTime.UtcNow` remains in retrieval composition logic
   - no hash-based collection iteration determines output order
   - source references are derived from final ordered results
   - section order is fixed and intentional

5. If integration tests exist for EF/PostgreSQL-backed retrieval, confirm:
   - semantic/document retrieval path has deterministic ordering even on equal scores
   - recent task/history queries include explicit `ORDER BY`
   - memory validity/recency behavior is stable under a fake/frozen clock

6. Include in the final implementation summary:
   - files changed
   - deterministic ordering rules introduced
   - tests added/updated
   - any assumptions or follow-up gaps

# Risks and follow-ups
- **Unknown current implementation shape**: ST-304 may be partially implemented or spread across orchestration services. Keep changes localized and avoid broad refactors unless needed for testability.
- **Floating-point score ties**: semantic similarity scores may tie or vary slightly by provider/query path. Always use stable secondary and tertiary tie-breakers.
- **Database/provider behavior**: SQL/EF may return rows in arbitrary order without explicit ordering. Do not assume insertion order.
- **Time-based flakiness**: recency and validity filters will remain flaky unless all current-time access is injected.
- **Over-coupling to prompt format**: keep deterministic retrieval separate from prompt wording/assembly so tests remain stable.
- **Caching interactions**: if Redis caching already exists, ensure cached payloads preserve the same deterministic ordering and do not mask bugs.
- **Audit/source reference schema gaps**: if source references do not currently store rank/order, consider a follow-up task to persist explicit ordering metadata for explainability consistency.
- **Potential follow-up**: add snapshot-style tests for full prompt-ready context payloads once the deterministic composer is stable.