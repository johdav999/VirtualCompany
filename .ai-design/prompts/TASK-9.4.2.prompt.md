# Goal
Implement backlog task **TASK-9.4.2 — Retrieval respects agent data scopes and human/company permissions** for story **ST-304 Grounded context retrieval service**.

The coding agent should update the grounded context retrieval flow so that all retrieved context is filtered by:

- **company/tenant boundary**
- **agent-configured data scopes**
- **human caller permissions/membership**
- any existing **document access scope metadata** and related policy constraints

The result should ensure the retrieval service only returns prompt-ready context that the requesting actor is allowed to access, and that source references remain available for downstream audit/explainability.

# Scope
In scope:

- Identify the current grounded context retrieval implementation and its call path.
- Add or refine a **retrieval authorization/scoping layer** that is applied before or during retrieval.
- Enforce tenant/company isolation on all retrieval inputs and queries.
- Enforce agent data scopes from agent configuration (`data_scopes_json` or equivalent mapped model).
- Enforce human/company permissions from the current user membership/authorization context.
- Ensure retrieval of:
  - knowledge documents/chunks
  - memory items
  - recent tasks/history
  - relevant records if already part of the retrieval service
  all respect the same scoping rules.
- Preserve deterministic, testable retrieval behavior.
- Add/adjust tests covering allowed and denied retrieval cases.

Out of scope unless required by existing design:

- New UI screens
- Broad refactors unrelated to retrieval
- New permission systems if a usable one already exists
- Full audit UI work
- Large schema redesigns unless absolutely necessary to support scope enforcement

# Files to touch
Start by inspecting these areas and update the concrete files you find there:

- `src/VirtualCompany.Application`
  - retrieval/context service implementations
  - query handlers / orchestration services
  - DTOs/models for retrieval requests and results
  - authorization/policy abstractions
- `src/VirtualCompany.Domain`
  - agent, membership, document scope, and permission models/value objects
  - policy/scoping rules if domain-owned
- `src/VirtualCompany.Infrastructure`
  - repository/query implementations for documents, chunks, memory, tasks
  - EF Core or SQL query filters enforcing `company_id`
  - pgvector retrieval queries
- `src/VirtualCompany.Api`
  - DI registration if new services are introduced
  - request context plumbing if retrieval needs current human/company context
- Test projects in the solution
  - unit tests for scope evaluation
  - integration tests for retrieval filtering behavior

Likely file patterns to search for:

- `*ContextRetriev*`
- `*Retriev*Service*`
- `*Knowledge*`
- `*Memory*`
- `*Agent*`
- `*Permission*`
- `*Authorization*`
- `*Membership*`
- `*Scope*`
- `*Task*Query*`
- `*Orchestration*`

# Implementation plan
1. **Locate the current retrieval pipeline**
   - Find the service implementing ST-304 behavior.
   - Trace how a retrieval request is constructed:
     - who is requesting
     - target agent
     - company context
     - task/conversation context
     - retrieval sources included
   - Document the current trust boundary in code comments if unclear.

2. **Define an explicit retrieval access context**
   Introduce or refine a single internal model passed through retrieval, containing at minimum:
   - `CompanyId`
   - `AgentId`
   - human caller identity if present
   - caller membership/role/permissions if present
   - agent data scopes
   - optional task/workflow/conversation identifiers
   - retrieval purpose/source types requested

   This should avoid hidden ambient authorization logic and make retrieval deterministic and testable.

3. **Centralize scope evaluation**
   Create a dedicated policy/scoping component, e.g. a `IRetrievalScopeEvaluator` or similar, responsible for answering:
   - can this retrieval request run for this company?
   - which source categories are allowed?
   - which documents/memory/task records are visible?
   - what filters must be applied to each query?

   Prefer a single reusable component over duplicating checks in multiple handlers.

4. **Enforce tenant/company isolation first**
   For every retrieval query:
   - require `company_id` in the request/context
   - filter all document, chunk, memory, task, and related record queries by `company_id`
   - fail closed if company context is missing or inconsistent

   If any repository currently allows cross-tenant reads without explicit company filtering, fix that.

5. **Apply agent data scope restrictions**
   Use the agent’s configured data scopes to constrain retrieval. Based on the architecture/backlog, this likely includes:
   - allowed document categories/types
   - allowed departments/domains
   - allowed source types
   - allowed entity/task visibility
   - possible access to company-wide vs agent-specific memory

   If the exact shape of `data_scopes_json` is not yet strongly typed:
   - map only the currently used fields into a typed model
   - default to **deny or narrowest safe behavior** when config is missing/ambiguous
   - do not silently broaden access

6. **Apply human permission restrictions**
   If retrieval is initiated in a human-driven context, ensure the returned context does not exceed what the human is allowed to access.
   Examples:
   - a manager should not retrieve finance-restricted content unless permitted
   - a support supervisor should not automatically gain access to all company records
   - a user outside the company must never retrieve anything

   Use existing membership/role/permission models where available. If permissions are currently coarse-grained, enforce the strongest available boundary without inventing a large new RBAC system.

7. **Respect document access scope metadata**
   For knowledge retrieval:
   - inspect `knowledge_documents.access_scope_json` and any chunk metadata derived from it
   - ensure semantic search is filtered by access constraints **before or alongside ranking**
   - do not retrieve top-N globally and filter afterward if that could leak inaccessible candidates into scoring or references

   The same principle applies to memory/task retrieval where scope metadata exists.

8. **Normalize retrieval outputs without leaking denied sources**
   Ensure the final structured prompt-ready sections only include allowed items.
   Also ensure:
   - source references persisted for audit/explainability only include allowed sources
   - denied items are not included in summaries, counts, or citations
   - optional internal diagnostics do not leak restricted identifiers in user-facing outputs

9. **Keep behavior deterministic**
   Since ST-304 notes retrieval should be deterministic and testable:
   - keep filtering logic explicit
   - avoid hidden fallback behavior
   - use stable ordering/tie-breaking where practical after filtering
   - make “no accessible context found” a valid outcome

10. **Add tests**
   Add focused tests for:
   - tenant isolation
   - agent scope filtering
   - human permission filtering
   - combined agent + human restrictions
   - deny-by-default behavior for missing/ambiguous scope config
   - source references only containing allowed items

   Prefer unit tests for scope evaluation plus integration tests for repository/query behavior.

11. **Keep changes aligned with existing architecture**
   Follow the modular monolith boundaries:
   - domain rules in Domain/Application as appropriate
   - persistence filtering in Infrastructure
   - no UI/controller prompt assembly logic
   - no direct DB access from orchestration outside established abstractions

# Validation steps
1. Search the solution to identify the retrieval implementation and related tests.
2. Build after changes:
   - `dotnet build`
3. Run tests:
   - `dotnet test`
4. Add or update tests to cover at least these scenarios:

   - **Allowed same-company retrieval**
     - human belongs to company
     - agent belongs to same company
     - document/memory/task is in same company and within scope
     - result is returned

   - **Cross-company denial**
     - retrieval request references another company’s data
     - result is empty/forbidden according to existing patterns
     - no cross-tenant source references appear

   - **Agent scope denial**
     - agent lacks access to a document type/domain/source
     - item is excluded from retrieval results

   - **Human permission denial**
     - human caller lacks permission for a restricted source
     - item is excluded even if agent scope would otherwise allow it

   - **Combined restrictions**
     - agent allows, human denies => denied
     - human allows, agent denies => denied

   - **Missing/ambiguous scope config**
     - retrieval defaults to safe behavior, not broad access

   - **Semantic retrieval filtering**
     - inaccessible chunk with higher similarity does not appear ahead of accessible chunks because it is filtered out at query time or equivalent safe stage

5. If integration tests exist around pgvector or repository queries, verify filtering occurs in the data access layer and not only in post-processing.

# Risks and follow-ups
- **Ambiguous existing scope model**
  - `data_scopes_json` and `access_scope_json` may not yet have a strongly typed contract.
  - Follow-up may be needed to formalize these schemas and validation rules.

- **Permission model may be incomplete**
  - Human/company permissions may currently be role-based but not resource-granular.
  - Implement the strongest safe enforcement possible now and note gaps clearly.

- **Post-filter leakage risk**
  - Filtering after retrieval/ranking can leak inaccessible candidates indirectly.
  - Prefer query-time filtering wherever feasible, especially for semantic search.

- **Inconsistent enforcement across sources**
  - Documents, memory, tasks, and records may currently use different access patterns.
  - Ensure one shared evaluator or policy path is used to avoid drift.

- **Audit persistence dependency**
  - If source references are persisted elsewhere, verify those paths also only receive authorized references.
  - A follow-up task may be needed if audit persistence is implemented in a separate pipeline.

- **Performance impact**
  - Additional filters on retrieval queries may affect ranking/query speed.
  - If needed, add indexes or optimize query composition in a follow-up, but do not weaken authorization for performance.

- **Fail-closed behavior**
  - Missing company context, missing membership, or malformed scope config should not broaden access.
  - If this changes existing behavior, document it in code/tests so the stricter behavior is intentional.