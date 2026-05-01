# Goal
Implement backlog task **TASK-32.2.3** for story **US-32.2 Database migration and provider foundation for real Fortnox data separation** by:

- introducing or extending the finance integration provider abstraction so a provider can be resolved by key
- implementing and registering **FortnoxFinanceIntegrationProvider** in dependency injection
- wiring provider resolution for provider key **`fortnox`**
- ensuring the implementation aligns with the new finance integration persistence model introduced by the related migration work, without breaking existing manual or simulated finance flows

This task is complete only when the provider abstraction is usable through DI and a Fortnox provider can be resolved by provider key in application/infrastructure code.

# Scope
In scope:

- Inspect the current finance integration/provider architecture across Domain, Application, and Infrastructure.
- Introduce or extend **`IFinanceIntegrationProvider`** if needed so it supports provider-key-based resolution and Fortnox-specific behavior behind the abstraction.
- Add **`FortnoxFinanceIntegrationProvider`** implementation.
- Register the provider and any resolver/factory needed in dependency injection.
- Ensure the provider key is consistently represented as **`fortnox`**.
- Update any related contracts/options/service registration needed for compile-time integration.
- Add or update tests proving:
  - the provider is registered in DI
  - the provider can be resolved by provider key `fortnox`
  - the resolved implementation is `FortnoxFinanceIntegrationProvider`

Out of scope unless strictly required for compilation or acceptance coupling:

- building the full Fortnox sync workflow
- implementing OAuth/token exchange logic beyond what is necessary for the provider abstraction
- broad refactors unrelated to finance integration provider registration
- changing unrelated finance domain behavior
- authoring the full migration if it belongs to a separate task, but do align names/contracts with the migration model already being introduced

Important acceptance alignment:

- The task must support the acceptance criterion: **“A provider implementation behind the finance integration abstraction is registered in dependency injection and can be resolved by provider key fortnox.”**
- Be careful not to introduce behavior that could overwrite or conflate manual/simulated records with Fortnox-synced records.

# Files to touch
Inspect first, then update the minimal correct set. Likely candidates include:

- `src/VirtualCompany.Application/**`
  - finance integration contracts/interfaces
  - application services that depend on provider abstraction
- `src/VirtualCompany.Domain/**`
  - provider key constants/value objects/enums if domain-owned
- `src/VirtualCompany.Infrastructure/**`
  - Fortnox provider implementation
  - DI registration/extensions
  - provider resolver/factory implementation
- `src/VirtualCompany.Api/**`
  - startup/program composition if DI is registered there
- `tests/VirtualCompany.Api.Tests/**`
  - integration/DI registration tests
- possibly:
  - `tests/**` in Application or Infrastructure test projects if provider resolution tests belong there

Before editing, search for:

- `IFinanceIntegrationProvider`
- `FinanceIntegrationProvider`
- `Fortnox`
- `ProviderKey`
- `AddInfrastructure`
- `AddApplication`
- any existing resolver/factory patterns such as:
  - `IIntegrationProviderResolver`
  - `IProviderFactory`
  - keyed service registration
  - strategy collections via `IEnumerable<T>`

Prefer extending existing patterns over inventing a new one.

# Implementation plan
1. **Discover the current finance integration design**
   - Find existing finance integration abstractions, provider contracts, and DI registration points.
   - Determine whether `IFinanceIntegrationProvider` already exists and what methods/properties it exposes.
   - Identify whether provider selection currently happens by:
     - provider key string
     - enum/value object
     - direct concrete injection
     - not yet implemented
   - Identify where finance integration persistence entities for connections/tokens/sync state are being introduced so naming stays consistent.

2. **Define the provider abstraction shape**
   - If `IFinanceIntegrationProvider` already exists, extend it minimally to support provider identification and resolution.
   - Preferred pattern:
     - expose a stable provider key, e.g. `string ProviderKey { get; }`
     - optionally expose capability methods already expected by the application layer
   - If there is no resolver yet, add a small resolver/factory abstraction, for example:
     - `IFinanceIntegrationProviderResolver`
     - method like `IFinanceIntegrationProvider GetRequired(string providerKey)`
   - Normalize provider key handling:
     - canonical key must be `fortnox`
     - resolution should be case-insensitive if that matches existing conventions
     - avoid magic strings scattered across the codebase; use a constant/shared definition if a suitable location exists

3. **Implement `FortnoxFinanceIntegrationProvider`**
   - Add the concrete provider in Infrastructure unless the project structure clearly places providers elsewhere.
   - Implement the abstraction and return provider key `fortnox`.
   - Keep implementation focused on provider identity/foundation unless existing contract requires more methods.
   - If methods are not yet fully implemented, use the project’s established pattern:
     - no-op only if safe and already accepted
     - otherwise throw `NotSupportedException`/`NotImplementedException` only where unreachable in current tests
   - Do not add fake behavior that could blur manual/simulated/Fortnox data separation.

4. **Add provider resolution**
   - If the codebase already uses `IEnumerable<IFinanceIntegrationProvider>`, add a resolver that selects by `ProviderKey`.
   - If keyed DI is already used in the solution, follow that pattern instead.
   - Resolver requirements:
     - resolve `fortnox` to `FortnoxFinanceIntegrationProvider`
     - fail clearly for unknown provider keys
     - avoid ambiguous duplicate registrations; if multiple providers share the same key, throw a meaningful exception

5. **Register in dependency injection**
   - Update the appropriate DI extension method(s) in Infrastructure/Application/API.
   - Register:
     - `FortnoxFinanceIntegrationProvider`
     - `IFinanceIntegrationProvider` mapping
     - resolver/factory if introduced
   - Use the existing service lifetime conventions from the surrounding finance/integration services.
   - Ensure the registration path is actually invoked by the API startup composition.

6. **Align with finance integration persistence model**
   - Confirm naming consistency with the migration-related entities/tables:
     - `FinanceIntegrationConnections`
     - `FinanceIntegrationTokens`
     - `FinanceIntegrationSyncStates`
     - `FinanceExternalReferences`
     - `FinanceIntegrationAuditEvents`
   - If the provider abstraction references provider/source fields, ensure it uses the same provider key semantics expected by the migration and future sync logic.
   - Preserve separation assumptions:
     - Fortnox-synced records should be identifiable as provider-backed
     - manual/simulated records must remain untouched by provider registration changes

7. **Add tests**
   - Add focused tests for DI and resolution.
   - Minimum test coverage:
     - service provider builds successfully
     - `IFinanceIntegrationProviderResolver.GetRequired("fortnox")` returns a provider
     - returned provider is `FortnoxFinanceIntegrationProvider`
     - provider reports `ProviderKey == "fortnox"`
     - unknown provider key throws expected exception
   - If the solution has DI composition tests at API level, prefer placing tests there.
   - If there are existing architecture/unit tests around service registration, follow that style.

8. **Keep changes minimal and production-safe**
   - Do not refactor unrelated modules.
   - Do not rename public contracts unless necessary.
   - If introducing new abstractions, keep them small and additive.

# Validation steps
Run and verify in this order where possible:

1. **Code search sanity**
   - Confirm there is exactly one canonical Fortnox provider key definition or a clearly intentional usage pattern.
   - Confirm no duplicate provider registrations for `fortnox`.

2. **Build**
   - Run:
     - `dotnet build`
   - Fix all compile errors and warnings introduced by the change.

3. **Tests**
   - Run targeted tests first if practical, then full suite:
     - `dotnet test`
   - Ensure DI/provider resolution tests pass.

4. **Manual DI verification**
   - Inspect startup registration path to confirm the Infrastructure/Application registration method containing the Fortnox provider is called by the API.
   - Verify the resolver can obtain the provider by key `fortnox`.

5. **Acceptance traceability check**
   - Confirm the implementation satisfies:
     - provider abstraction exists and is usable
     - Fortnox provider is registered in DI
     - provider can be resolved by provider key `fortnox`

6. **Non-regression check**
   - Ensure no existing manual/simulated finance code paths were altered in a way that changes current behavior.
   - Ensure no migration or persistence code was accidentally changed to overwrite existing finance data.

# Risks and follow-ups
- **Risk: abstraction mismatch**
  - Existing `IFinanceIntegrationProvider` may already be used in ways that make adding `ProviderKey` or a resolver non-trivial.
  - Mitigation: extend minimally and preserve backward compatibility.

- **Risk: duplicate provider registration**
  - Multiple implementations could accidentally claim the same provider key.
  - Mitigation: make resolver detect duplicates and fail fast.

- **Risk: wrong layer placement**
  - Putting provider contracts in Infrastructure instead of Application/Domain could violate current architecture.
  - Mitigation: follow existing dependency direction in the solution.

- **Risk: stringly typed provider keys**
  - Scattered `"fortnox"` literals can create drift with migration/provider/source tracking fields.
  - Mitigation: centralize the provider key constant if a suitable shared location exists.

- **Risk: future migration/provider coupling**
  - The migration introduces provider/source tracking and external references; if naming diverges now, later sync work will be brittle.
  - Mitigation: align provider key and source semantics with the persistence model being introduced.

Follow-ups likely needed after this task:

- implement Fortnox connection/token management against `FinanceIntegrationConnections` and `FinanceIntegrationTokens`
- add sync-state handling via `FinanceIntegrationSyncStates`
- add external reference linking for Fortnox-synced entities
- add audit event persistence for provider operations
- implement end-to-end Fortnox sync/import flows using this provider foundation