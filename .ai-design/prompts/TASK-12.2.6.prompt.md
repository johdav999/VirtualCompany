# Goal
Implement backlog task **TASK-12.2.6 — Keep data source references human-readable** for **ST-602 Audit trail and explainability views**.

The coding agent should update the audit/explainability flow so that any persisted and displayed “data sources used” are understandable to end users and reviewers, rather than exposing only opaque internal IDs, raw storage paths, vector chunk identifiers, or low-level technical references.

This work should fit the existing architecture:
- ASP.NET Core modular monolith
- Audit & Explainability as a domain feature
- PostgreSQL-backed business data
- Blazor web UI for audit views
- Tenant-scoped, role-aware access
- Concise operational explanations only, with no chain-of-thought exposure

# Scope
In scope:
- Identify where audit events, explainability records, retrieval source references, or related DTO/view models currently store or expose source references.
- Introduce a human-readable source reference shape for audit/explainability usage.
- Ensure source references can represent common source types clearly, such as:
  - knowledge documents
  - document chunks/snippets
  - tasks
  - workflows
  - approvals
  - tool executions
  - messages/conversations
  - integration-originated records if already present
- Preserve machine-linkable identifiers where needed internally, but ensure user-facing audit/explainability surfaces use readable labels.
- Update mapping/formatting logic so references include meaningful names/titles and optional contextual descriptors.
- Ensure tenant scoping and role-based access remain enforced.
- Add or update tests for formatting/mapping behavior and any affected query/application logic.

Out of scope unless required by existing code structure:
- Large redesign of the entire audit schema
- New audit UI beyond what is necessary to display readable references
- Broad refactors unrelated to audit/explainability
- Adding brand new source entity types not already represented in the codebase
- Mobile-specific changes unless shared DTOs require them

Definition of done:
- Audit/explainability records no longer surface only opaque references where a human-readable label can be resolved.
- User-facing source references are concise, stable, and understandable.
- Existing links/navigation to underlying entities still work if already supported.
- Tests pass.

# Files to touch
Start by inspecting these likely areas and adjust based on actual repository structure:

- `src/VirtualCompany.Domain/**`
  - Audit event entities/value objects
  - Explainability/source reference models
- `src/VirtualCompany.Application/**`
  - Audit queries/handlers
  - Explainability DTOs/view models
  - Mapping/formatting services
  - Retrieval source persistence logic if source references are normalized here
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - repositories/query projections
  - persistence models for audit events / retrieval references
- `src/VirtualCompany.Api/**`
  - audit/explainability endpoints if contracts change
- `src/VirtualCompany.Web/**`
  - audit trail pages/components
  - action detail/explainability components that render source references
- `tests/**`
  - application/query tests
  - API contract tests if applicable
  - UI/component tests if present and lightweight

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If schema changes are required, follow the project’s existing migration approach rather than inventing a new one.

# Implementation plan
1. **Discover the current audit/explainability source-reference flow**
   - Find all usages of terms like:
     - `audit`
     - `explainability`
     - `data sources used`
     - `source references`
     - `rationale`
     - `retrieval`
   - Identify:
     - where source references are created
     - how they are stored
     - how they are returned from application/API layers
     - how they are rendered in the web UI
   - Determine whether the current issue is:
     - raw IDs shown directly
     - JSON blobs shown unformatted
     - storage URLs/paths exposed
     - chunk-level references without document titles
     - missing labels for linked entities

2. **Define a human-readable source reference contract**
   - Introduce or refine a DTO/value object for user-facing source references.
   - Prefer a shape along these lines if compatible with the codebase:
     - `SourceType`
     - `DisplayName`
     - `SecondaryText` or `Description`
     - `EntityId` or `ReferenceId` for internal linking
     - `EntityType`
     - optional `Snippet` or `Excerpt`
     - optional `OccurredAt` / `Version` / `Section`
   - Example outputs:
     - `SOP: Refund Handling`
     - `Playbook: Q4 Enterprise Outreach`
     - `Task: Investigate failed invoice sync`
     - `Approval: Spend threshold override`
     - `Workflow: Daily finance reconciliation`
     - `Conversation: Support inbox`
   - For chunked knowledge sources, prefer document-first labeling, optionally with chunk context:
     - `Document: Employee Onboarding SOP — section excerpt`
   - Do not expose raw vector IDs, storage URLs, or internal table keys as the primary label.

3. **Implement formatting/resolution logic**
   - Add a dedicated formatter/resolver in the application layer if one does not already exist.
   - This resolver should:
     - accept raw source reference data
     - resolve entity names/titles from the appropriate repositories
     - produce a stable user-facing label
     - gracefully handle missing/deleted entities with a fallback like:
       - `Document (unavailable)`
       - `Task (deleted or inaccessible)`
   - Keep this logic deterministic and testable.
   - Avoid embedding formatting rules directly in Razor/Blazor components if possible.

4. **Update persistence or projection shape only as needed**
   - Prefer minimal change:
     - if raw references are already stored, resolve readable labels at query/projection time
     - only add persisted display fields if necessary for performance or historical stability
   - If persisting display text is necessary, ensure:
     - tenant-safe resolution at write time
     - backward compatibility for existing records
     - old records still render via fallback resolution
   - If schema changes are needed, keep them minimal and aligned with existing conventions.

5. **Update audit/explainability queries and API contracts**
   - Ensure audit history and action detail queries return readable source references.
   - Preserve internal IDs for navigation where useful, but separate them from display text.
   - Confirm role-based filtering still applies so users do not learn about inaccessible entities through labels alone.
   - If a source is not accessible, return a safe generic label rather than leaking restricted details.

6. **Update Blazor UI rendering**
   - Replace any direct rendering of raw source IDs/JSON/URLs with the new readable fields.
   - Keep the UI concise and operational.
   - If links exist, render:
     - readable text as the label
     - internal route/entity ID as the navigation target
   - Ensure empty states and fallback labels are clean.

7. **Add tests**
   - Add unit tests for the formatter/resolver covering:
     - document source
     - task source
     - workflow source
     - approval source
     - tool execution source if applicable
     - missing entity fallback
     - inaccessible/restricted entity fallback
     - chunk/snippet formatting
   - Add query/handler tests to verify audit detail/history returns readable references.
   - Add API or component tests only if the repository already has a pattern for them.

8. **Keep backward compatibility in mind**
   - Existing audit records may contain older raw references.
   - Ensure the new logic can interpret legacy data where possible.
   - Do not break existing consumers unnecessarily; if contracts change, update all internal callers.

# Validation steps
1. Inspect and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify audit/explainability behavior in the web app if feasible:
   - open an audit history page
   - open an action detail/explainability view
   - confirm source references show readable labels instead of opaque IDs/paths

4. Validate representative cases:
   - knowledge document source shows document title
   - chunk-based source shows document title plus concise excerpt/section context if available
   - task/workflow/approval/tool execution sources show meaningful names
   - missing or deleted source renders safe fallback text
   - restricted source does not leak sensitive details across roles/tenants

5. If schema changes were introduced:
   - verify migrations/configuration follow repository conventions
   - verify old records still load without errors

# Risks and follow-ups
- **Risk: leaking restricted entity names**
  - Human-readable labels must still respect authorization and tenant boundaries.
  - Use generic fallback labels when the current user should not see details.

- **Risk: over-coupling UI to persistence**
  - Keep formatting/resolution in application/domain-support layers, not only in Blazor components.

- **Risk: unstable labels for historical audit records**
  - If labels are resolved live from mutable entities, names may drift over time.
  - Consider a later follow-up to snapshot display labels at audit-write time if audit immutability requires it.

- **Risk: legacy data shape inconsistency**
  - Older audit records may not contain enough metadata for perfect resolution.
  - Implement graceful fallback behavior rather than failing rendering.

- **Risk: performance regressions**
  - Resolving many source labels in audit history could create N+1 queries.
  - Batch-load referenced entities or project labels efficiently.

Suggested follow-ups if not completed in this task:
- snapshot human-readable source labels at audit event creation for stronger historical fidelity
- standardize a shared `SourceReferenceDisplay` contract across retrieval, audit, and explainability modules
- add richer source descriptors such as section names, timestamps, and integration-origin labels where useful
- add UI affordances like hover details or drill-through links for source references