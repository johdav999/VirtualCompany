# Goal
Implement backlog task **TASK-9.4.4 — Retrieval source references are persisted for downstream audit/explanation** for story **ST-304 Grounded context retrieval service**.

The coding agent should extend the grounded context retrieval flow so that every retrieval operation can persist the **source references** used to build prompt-ready context. These references must be suitable for later audit/explainability use, align with the architecture’s emphasis that **auditability is a domain feature**, and remain **tenant-scoped, deterministic, and testable**.

This task is specifically about persisting retrieval provenance, not redesigning the whole retrieval pipeline.

# Scope
In scope:
- Identify where the current retrieval service returns or assembles source references for:
  - knowledge/document chunks
  - memory items
  - recent tasks/history
  - other relevant records already supported by ST-304
- Add a persistence model for retrieval source references if one does not already exist.
- Persist retrieval source references as part of the retrieval/orchestration flow in a way that downstream audit/explanation features can query later.
- Ensure persisted references are:
  - tenant-scoped
  - linked to the relevant execution context where possible (for example task, conversation, workflow instance, orchestration run, or correlation ID depending on current codebase patterns)
  - normalized enough to support human-readable explanation later
- Update application/domain/infrastructure code paths and EF Core mappings/migrations as needed.
- Add or update tests covering persistence behavior and tenant isolation assumptions.

Out of scope:
- Building the full audit/explainability UI.
- Reworking prompt composition beyond what is necessary to capture source references.
- Adding broad caching changes unless required by existing retrieval flow.
- Persisting raw chain-of-thought or hidden reasoning.
- Large architectural refactors unrelated to this task.

# Files to touch
Inspect first, then modify only what is necessary. Likely areas:

- `src/VirtualCompany.Domain/**`
  - retrieval-related domain models/value objects
  - audit/explainability entities if already present
- `src/VirtualCompany.Application/**`
  - context retrieval service interfaces and implementations
  - orchestration handlers/commands using retrieval
  - DTOs/models carrying retrieval results and source references
- `src/VirtualCompany.Infrastructure/**`
  - EF Core DbContext
  - entity configurations
  - repository implementations
  - migrations
  - persistence models for retrieval provenance
- `src/VirtualCompany.Api/**`
  - only if DI wiring or endpoint contracts need adjustment
- `README.md`
  - only if there is an established pattern of documenting schema/runtime changes

Likely concrete file categories to inspect:
- `DbContext` and EF configurations
- retrieval service interface/implementation
- orchestration pipeline classes
- task/workflow/audit persistence services
- existing migration folder
- unit/integration test projects if present

If an existing audit entity already stores “data sources used”, prefer extending it over introducing a parallel concept unless that would create coupling or ambiguity.

# Implementation plan
1. **Discover the current retrieval and persistence flow**
   - Find the grounded context retrieval service introduced for ST-304.
   - Identify:
     - input contract
     - output contract
     - where source references are currently produced, if at all
     - where retrieval is invoked from orchestration/task/chat flows
     - what execution identifiers already exist (task ID, workflow instance ID, conversation/message ID, correlation ID, etc.)
   - Also inspect whether the codebase already has:
     - an `audit_events` entity/table
     - any “data sources used” field
     - an orchestration run or prompt context persistence model

2. **Choose the persistence shape that best fits the existing model**
   Prefer one of these approaches based on the codebase:
   - **Preferred:** a dedicated retrieval source reference table/entity, because this task is specifically about persisted provenance and likely needs one-to-many records.
   - **Acceptable:** a structured JSON column on an existing retrieval/orchestration/audit record, if the codebase already persists retrieval executions and this is the cleanest fit.

   If creating a dedicated table/entity, model it around these concepts:
   - `id`
   - `company_id`
   - linkage to execution context, using whichever is already present and appropriate:
     - `task_id` nullable
     - `workflow_instance_id` nullable
     - `conversation_id` or `message_id` nullable
     - `correlation_id` or `retrieval_request_id`
   - `source_type` (document_chunk, memory_item, task, record, etc.)
   - `source_entity_id`
   - optional `parent_entity_id` (for chunk -> document)
   - human-readable fields for explanation:
     - title/label
     - excerpt/summary/snippet
     - locator metadata (document title, chunk index, task title, memory type, etc.)
   - ranking/ordering metadata:
     - `ordinal`
     - optional `score`
   - optional `section` or `usage_category` to indicate which prompt-ready section it contributed to
   - timestamps

   Keep the schema pragmatic and aligned with existing naming conventions.

3. **Implement domain and application contracts**
   - Extend retrieval result models so source references are represented explicitly and consistently.
   - If not already present, introduce a small normalized application model such as `RetrievalSourceReference`.
   - Ensure the retrieval service returns both:
     - prompt-ready context sections
     - normalized source references suitable for persistence
   - Keep this deterministic:
     - stable ordering
     - explicit source typing
     - no hidden/non-reproducible fields

4. **Persist source references at the correct boundary**
   - Persist references at the point where retrieval is finalized for use by orchestration, not deep inside low-level vector query code.
   - This boundary should have enough context to attach:
     - tenant/company
     - actor/agent if applicable
     - task/workflow/conversation/correlation identifiers
   - If the retrieval service is pure and currently side-effect free, preserve that design by:
     - returning normalized references from retrieval
     - persisting them in the orchestration/application layer immediately after retrieval
   - If the codebase already treats retrieval as an application service with persistence responsibilities, follow the existing pattern instead of forcing purity.

5. **Add EF Core persistence**
   - Add entity/configuration and migration.
   - Ensure indexes support likely downstream queries, for example:
     - by `company_id`
     - by `task_id`
     - by `workflow_instance_id`
     - by `correlation_id` / request identifier
   - Follow existing multi-tenant conventions rigorously.

6. **Integrate with audit/explainability semantics**
   - If there is an existing audit event model with “data sources used”, do one of:
     - store a summary there and link detailed references in the new table, or
     - populate the existing field from the persisted references
   - Do not duplicate large blobs unnecessarily.
   - The persisted references should be human-usable later, e.g. “SOP: Invoice Approval Policy, chunk 3” rather than only opaque IDs.

7. **Testing**
   Add or update tests for:
   - retrieval source references are persisted when retrieval succeeds
   - persisted references are linked to the correct tenant and execution context
   - multiple source types can be persisted in one retrieval
   - ordering/ordinal is stable
   - no cross-tenant leakage in queries/repositories
   - null/optional linkage behavior if retrieval occurs outside a task/workflow context
   - migration/build sanity if integration tests exist

8. **Implementation constraints**
   - Follow existing solution architecture and naming conventions.
   - Keep changes minimal and cohesive.
   - Avoid introducing a new subsystem if an existing audit or orchestration persistence pattern already fits.
   - Do not expose raw reasoning or chain-of-thought.
   - Preserve backward compatibility for existing retrieval callers where possible.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used in the normal workflow:
   - generate/apply the migration as appropriate for the repo conventions
   - verify the new schema maps cleanly

4. Manually verify in code/tests that:
   - a retrieval operation returns normalized source references
   - those references are persisted
   - persisted rows include `company_id`
   - persisted rows link to the relevant execution context available in the current flow
   - persisted references are human-readable enough for future audit/explanation
   - no raw chain-of-thought is stored

5. If there are integration tests or local execution paths for orchestration/chat/task flows:
   - trigger a representative retrieval-backed flow
   - confirm source references are written once and not duplicated unexpectedly
   - confirm ordering and source typing are preserved

# Risks and follow-ups
- **Risk: no existing orchestration run identifier**
  - If the current code lacks a stable retrieval execution identifier, use the nearest existing context (task/workflow/conversation/correlation ID) and note the limitation in code comments or follow-up notes.

- **Risk: duplicate provenance across repeated retrievals**
  - If the same retrieval can run multiple times for one task/message, decide whether to allow append-only history or deduplicate by request/execution identifier. Prefer consistency with existing audit patterns.

- **Risk: over-coupling retrieval to persistence**
  - Keep retrieval result generation separate from persistence unless the codebase already combines them.

- **Risk: insufficient human-readable metadata**
  - Opaque IDs alone will not satisfy downstream explainability needs. Persist labels/snippets/locator metadata alongside identifiers.

- **Risk: tenant isolation gaps**
  - Ensure repositories and queries always filter by `company_id`.

Suggested follow-ups, if not completed here:
- Add a query API/service for retrieving persisted source references by task/workflow/message.
- Surface persisted references in ST-602 audit trail and explainability views.
- Standardize a reusable orchestration/retrieval execution ID if the current codebase lacks one.
- Consider retention/versioning rules for persisted retrieval provenance if prompt contexts are regenerated frequently.