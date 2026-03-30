# Goal
Implement backlog task **TASK-9.4.3 — Returned context is normalized into structured prompt-ready sections** for **ST-304 Grounded context retrieval service**.

The coding agent should add or complete the application-layer retrieval flow so that grounded context returned by the retrieval service is no longer an unstructured bag of results, but a deterministic, typed, prompt-ready structure composed from:
- documents / knowledge chunks
- memory items
- recent task history
- relevant records already available in the current architecture

The implementation must fit the existing **.NET modular monolith** and keep retrieval separate from prompt assembly. The result should be reusable by the orchestration subsystem’s prompt builder and suitable for downstream audit/explainability persistence.

# Scope
In scope:
- Introduce or refine a **structured retrieval result contract** in the application layer.
- Normalize retrieved context into explicit sections with stable ordering and clear metadata.
- Ensure the structure is **prompt-ready** but **not itself a final prompt string**.
- Preserve tenant and agent scoping assumptions already established by ST-304.
- Include source references in the normalized result so downstream services can persist them for audit/explanation.
- Add unit tests for normalization behavior and deterministic ordering.
- Update any retrieval service implementation and mappings needed to support the new contract.

Out of scope:
- UI/controller prompt assembly.
- LLM invocation changes.
- New vector search algorithms beyond what already exists.
- Broad audit persistence implementation unless minimally required by current code paths.
- Major schema changes unless the current implementation absolutely requires a small additive persistence field/model.

If the codebase already contains partial ST-304 work, extend it rather than duplicating it. Prefer additive, backward-compatible changes where practical.

# Files to touch
Inspect first, then modify only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Application/...`
  - retrieval service interfaces/contracts
  - DTOs/models for grounded context
  - handlers/use-cases for orchestration context retrieval
- `src/VirtualCompany.Domain/...`
  - value objects or domain contracts only if the project currently models retrieval structures there
- `src/VirtualCompany.Infrastructure/...`
  - retrieval service implementation
  - repository/query composition for documents, memory, tasks, and relevant records
  - mapping from persistence/query results into normalized sections
- `src/VirtualCompany.Api/...`
  - only if an API contract currently exposes retrieval results and must be updated
- `tests/...` or corresponding test projects
  - unit tests for normalization
  - integration tests if a retrieval pipeline test suite already exists

Also inspect:
- `README.md`
- solution/project structure under:
  - `src/VirtualCompany.Application/`
  - `src/VirtualCompany.Infrastructure/`
  - any existing test projects in the solution

Do not rename large areas of the codebase unless necessary.

# Implementation plan
1. **Discover existing ST-304 implementation**
   - Search for:
     - `ContextRetriever`
     - `PromptBuilder`
     - `Retrieval`
     - `GroundedContext`
     - `Memory`
     - `KnowledgeChunks`
     - `RecentTasks`
   - Identify:
     - current retrieval request contract
     - current retrieval response shape
     - where prompt builder consumes retrieval output
     - whether source references already exist

2. **Define a normalized prompt-ready contract**
   Create or refine a typed result model that clearly separates sections. Prefer a shape similar to:

   - `GroundedContextResult`
     - `CompanyId`
     - `AgentId`
     - `GeneratedAtUtc`
     - `Sections` or explicit properties:
       - `Instructions` or `OperatingContext` only if already part of retrieval scope
       - `DocumentContext`
       - `MemoryContext`
       - `RecentTaskContext`
       - `RelevantRecordContext`
     - `SourceReferences`
     - optional summary metadata such as counts

   Each section should be structured, not free-form:
   - section name/key
   - ordered items
   - concise normalized text/content
   - source metadata
   - relevance/score if available
   - optional recency/salience indicators if already present

   Prefer explicit section types over a generic dictionary if the codebase style supports it. The contract must be deterministic and easy for prompt builder code to consume.

3. **Normalize retrieval outputs into stable sections**
   Update the retrieval service implementation so raw results from different sources are transformed into consistent section models:
   - **Documents**
     - normalize chunk/document title, excerpt/content, document type, source document id/reference
   - **Memory**
     - normalize memory type, summary, salience, validity window, source entity reference
   - **Recent tasks**
     - normalize title, status, rationale summary/output summary, timestamps, task id
   - **Relevant records**
     - normalize record/entity type, display label, key fields, source id/reference

   Rules:
   - exclude null/empty content
   - trim and sanitize whitespace
   - cap oversized excerpts if the codebase already has token/length helpers; otherwise use conservative character truncation with clear naming
   - preserve source attribution per item
   - keep section ordering fixed
   - keep item ordering deterministic, e.g. by relevance desc then recency desc then id asc

4. **Keep retrieval deterministic and testable**
   Implement normalization as pure mapping logic where possible.
   - Avoid hidden randomness
   - Avoid dependence on current thread culture/timezone formatting
   - Use invariant formatting for any generated labels
   - If scores are floating-point, round/format consistently only at presentation boundaries, not in core models

5. **Support downstream audit/explainability**
   Ensure the normalized result exposes enough source reference data for later persistence:
   - source type
   - source entity/document id
   - human-readable label/title
   - optional chunk id / task id / memory id
   - optional rank/score

   If the current retrieval service already returns references separately, align them with the normalized sections rather than duplicating inconsistent metadata.

6. **Integrate with prompt builder consumers**
   Update any consuming application service so it uses the normalized sections instead of raw retrieval blobs.
   Important:
   - do not assemble a final prompt string in the retrieval service
   - do not move prompt builder responsibilities into controllers or API endpoints
   - keep boundaries clean:
     - retrieval service returns structured context
     - prompt builder decides how to render/use it

7. **Add tests**
   Add focused tests covering:
   - retrieval result is split into expected sections
   - empty sources produce empty sections, not null reference failures
   - ordering is deterministic
   - source references are preserved
   - whitespace/content normalization works
   - mixed-source retrieval does not collapse into one unstructured text block

   If there is an existing test style, follow it. Prefer unit tests around the normalization mapper/service. Add integration tests only if the repository/query pipeline is already covered that way.

8. **Preserve backward compatibility where needed**
   If an existing interface is already consumed in multiple places:
   - either evolve it carefully
   - or add a new normalized result property/contract and migrate consumers
   - remove obsolete paths only if safe and local

9. **Document with concise code comments**
   Add brief comments only where the normalization rules are non-obvious, especially around:
   - deterministic ordering
   - truncation/sanitization
   - why the result is structured but not prompt text

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there is a targeted test project for application/infrastructure retrieval logic, run it first for faster iteration.

4. Manually verify in code:
   - retrieval service returns a typed normalized result
   - result contains distinct prompt-ready sections for:
     - documents
     - memory
     - recent tasks
     - relevant records
   - each item includes source attribution/reference metadata
   - prompt builder consumes structured sections rather than raw concatenated text

5. If an API endpoint or orchestration handler exposes retrieval output internally, verify serialized output shape is stable and sensible.

6. Confirm no layering violations:
   - no UI/controller prompt assembly
   - no infrastructure-specific types leaking into higher layers unless already established by project conventions

# Risks and follow-ups
- **Risk: existing code already mixes retrieval and prompt rendering**
  - Follow-up: separate concerns incrementally without broad refactors.

- **Risk: “relevant records” may not yet have a concrete implementation**
  - Follow-up: create the normalized section contract now and return an empty section or adapt current available records, rather than inventing unsupported data sources.

- **Risk: source reference persistence may belong to a later task**
  - Follow-up: expose complete references in the retrieval result now so downstream audit persistence can consume them later.

- **Risk: breaking existing consumers**
  - Follow-up: preserve compatibility or update all local consumers in the same change set.

- **Risk: token/length management is not yet standardized**
  - Follow-up: use conservative truncation now and note a future enhancement for token-budget-aware section compaction.

- **Risk: acceptance criteria are implicit**
  - Treat the following as the operational definition of done:
    - retrieval output is normalized into structured prompt-ready sections
    - sections are deterministic and typed
    - source references are preserved
    - tests cover normalization behavior