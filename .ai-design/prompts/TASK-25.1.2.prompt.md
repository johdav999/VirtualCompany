# Goal
Implement backlog task **TASK-25.1.2 — Add tenant-scoped reconciliation configuration for tolerances and scoring weights** for story **US-25.1 Build reconciliation matching engine for bank transactions, payments, invoices, and bills**.

Deliver a production-ready implementation in the existing **.NET modular monolith** that adds **per-tenant reconciliation scoring configuration** and applies it in the reconciliation matching engine so that:

- bank transaction → payment candidates are ranked using:
  - amount exact match
  - amount near match
  - date proximity
  - reference similarity
  - counterparty similarity
- invoice/bill → payment candidates are ranked for linkage suggestions
- every suggestion includes:
  - normalized confidence score in **[0.00, 1.00]**
  - per-rule scoring details in the result payload
- **near-match amount tolerance** and **date proximity windows** are configurable **per tenant**
- automated tests cover:
  - exact match
  - near amount match
  - date-only match
  - reference-only match
  - counterparty-only match
  - no-match

Use the architecture and backlog guidance already present in the repo. Keep the implementation tenant-safe, deterministic, and testable.

# Scope
In scope:

- Add a tenant-scoped reconciliation configuration model for:
  - amount near-match tolerance
  - date proximity window
  - scoring weights for each rule
- Persist or otherwise resolve this configuration through the existing application/infrastructure patterns already used in the solution
- Update the reconciliation scoring engine to consume tenant configuration instead of hardcoded tolerances/weights
- Ensure confidence score normalization is stable and bounded to 0.00–1.00
- Include per-rule scoring breakdown in returned suggestion payloads
- Add/extend automated tests for all required scenarios
- Keep implementation aligned with shared-schema multi-tenancy using `company_id`/tenant context enforcement

Out of scope unless required by existing code structure:

- Full UI for editing reconciliation settings
- New mobile functionality
- Broad refactors unrelated to reconciliation
- New external integrations
- Reworking unrelated accounting domain models

If there is already partial reconciliation functionality in the repo, extend it rather than replacing it. Prefer minimal, cohesive changes.

# Files to touch
Inspect the solution first, then update the most relevant files in these areas as needed.

Likely targets:

- `src/VirtualCompany.Domain/**`
  - reconciliation domain models/value objects
  - tenant/company settings abstractions
  - scoring result models
- `src/VirtualCompany.Application/**`
  - reconciliation services/handlers
  - DTOs/contracts for suggestion results
  - tenant-scoped settings retrieval interfaces
- `src/VirtualCompany.Infrastructure/**`
  - persistence for tenant/company reconciliation settings
  - EF Core entity mappings/configurations
  - repository/settings provider implementations
- `src/VirtualCompany.Api/**`
  - only if request/response contracts or DI registration must be updated
- `tests/**`
  - unit tests for scoring logic
  - application/service tests for tenant-scoped config application

Also inspect for existing equivalents before creating new files:

- company settings/configuration models
- finance/accounting/reconciliation modules
- scoring/matching services
- test fixtures/builders for tenant/company-scoped entities

Prefer touching existing files over creating parallel abstractions if the repo already has a pattern.

# Implementation plan
1. **Discover existing reconciliation and tenant settings patterns**
   - Search for:
     - reconciliation
     - matching engine
     - bank transaction
     - payment
     - invoice
     - bill
     - company settings / tenant settings / configuration JSON
   - Identify:
     - where tenant context is resolved
     - whether company settings already live in a JSON column or dedicated table
     - current scoring algorithm and result payload shape
   - Reuse existing patterns for settings persistence and DI.

2. **Design the tenant-scoped reconciliation configuration**
   - Add a configuration model with at least:
     - `AmountNearMatchTolerance`
     - `DateProximityWindowDays`
     - scoring weights for:
       - exact amount
       - near amount
       - date proximity
       - reference similarity
       - counterparty similarity
   - Include sensible defaults for tenants with no explicit config.
   - Validate:
     - tolerances/windows are non-negative
     - weights are non-negative
     - total weight is valid for normalization
   - If the codebase already stores flexible company settings in JSON/JSONB, prefer adding a reconciliation section there rather than inventing a new persistence model.

3. **Add tenant-scoped settings retrieval**
   - Introduce or extend an application-facing provider such as:
     - `IReconciliationConfigurationProvider`
     - or existing company settings service
   - The provider must resolve configuration by tenant/company id.
   - Ensure no cross-tenant reads are possible.
   - Apply fallback defaults when tenant config is absent or incomplete.

4. **Update the reconciliation scoring engine**
   - Refactor scoring to accept resolved tenant configuration.
   - For each candidate suggestion, compute rule-level contributions for:
     - exact amount match
     - near amount match within configured tolerance
     - date proximity within configured window
     - reference similarity
     - counterparty similarity
   - Ensure exact and near amount logic are coherent:
     - exact match should not double-count as near match unless current design explicitly intends additive behavior
     - prefer mutually exclusive amount rule handling if not already defined
   - Normalize final confidence score to `[0.00, 1.00]`.
   - Round only at API/serialization boundary if possible; keep internal precision higher.
   - Include per-rule details in the result payload, for example:
     - rule name
     - raw signal/value
     - weight
     - awarded score
     - explanation/reason
   - Keep ranking deterministic for equal scores, using a stable tie-breaker if needed.

5. **Support all required source-target scenarios**
   - Verify the engine supports:
     - bank transaction → payment candidates
     - invoice → payment candidates
     - bill → payment candidates
   - If these already share a common scoring pipeline, extend that pipeline.
   - If separate paths exist, centralize common scoring logic to avoid divergence.

6. **Add validation and guardrails**
   - Prevent invalid tenant config from causing divide-by-zero or impossible scoring.
   - If all weights are zero, either:
     - reject config at validation time, or
     - fall back to defaults
   - Make behavior explicit and test-covered.
   - Ensure null/empty references and counterparties are handled safely.

7. **Persist configuration**
   - Implement storage using the repo’s established pattern:
     - EF Core entity update
     - JSON settings blob
     - migration if schema changes are required
   - If a migration is needed:
     - add it in the correct project/location used by this solution
     - keep it minimal and reversible
   - Do not add a migration if settings are already stored in extensible JSON and no schema change is necessary.

8. **Register dependencies**
   - Update DI registrations for any new provider/service classes.
   - Keep service lifetimes consistent with existing patterns.

9. **Add automated tests**
   - Add focused tests for scoring behavior using tenant-specific config.
   - Cover at minimum:
     - exact match
     - near amount match
     - date-only match
     - reference-only match
     - counterparty-only match
     - no-match
   - Also add tests for:
     - tenant A and tenant B receiving different scores due to different tolerances/windows/weights
     - confidence score always bounded to 0.00–1.00
     - per-rule scoring details included in payload
   - Prefer unit tests for scoring logic plus application-level tests for config resolution.

10. **Keep code quality high**
   - Use clear names and small methods.
   - Avoid magic numbers; move defaults into a dedicated config/defaults class.
   - Preserve backward compatibility where possible.
   - Add concise comments only where logic is non-obvious.

# Validation steps
1. Inspect and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify reconciliation tests specifically cover:
   - exact match
   - near amount match using configured tolerance
   - date-only match using configured date window
   - reference-only match
   - counterparty-only match
   - no-match
   - tenant-specific config differences

4. Manually review result payload shape to confirm each suggestion includes:
   - normalized confidence score in `[0.00, 1.00]`
   - per-rule scoring details

5. If persistence changed, verify:
   - tenant/company-scoped settings are read correctly
   - default config is applied when tenant config is missing
   - no cross-tenant leakage is possible

6. If a migration was added, ensure it is included in the correct project and does not break build/test.

# Risks and follow-ups
- **Risk: existing settings model may be unclear**
  - Follow existing company settings conventions if present; avoid introducing a second competing settings system.

- **Risk: double-counting amount rules**
  - Be explicit whether exact amount and near amount are mutually exclusive. Prefer mutually exclusive scoring unless current engine semantics require otherwise.

- **Risk: unstable normalization**
  - Guard against zero total weight and ensure confidence is clamped to `[0,1]`.

- **Risk: tenant leakage**
  - All config resolution must require tenant/company id and use existing tenant-scoped repository/query patterns.

- **Risk: over-refactoring**
  - Keep changes localized to reconciliation and settings plumbing.

Follow-ups after this task, if not already covered elsewhere:
- admin/web UI for editing tenant reconciliation settings
- audit trail for reconciliation configuration changes
- richer similarity algorithms for reference/counterparty matching
- thresholding/auto-link policies based on confidence bands