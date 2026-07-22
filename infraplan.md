# VirtualCompany.Infrastructure Refactoring Plan

## Purpose

Refactor `VirtualCompany.Infrastructure` from one large compilation unit into a small composition facade plus cohesive persistence, platform, integration, and capability assemblies. The refactor must improve incremental build times and ownership clarity without changing production behavior, API contracts, database schema, migration history, tenant isolation, background processing, or local/Docker SQL Server compatibility.

This is a structural refactor, not a rewrite and not a move to microservices.

## Current State

The current project is a broad modular-monolith infrastructure assembly that contains EF Core persistence, migrations, provider adapters, capability implementations, hosted workers, authentication, tenancy, security, observability, and root dependency injection.

Repository observations at the time of this plan:

- `VirtualCompany.Infrastructure` contains more than 550 C# source files when generated build output is excluded.
- `Persistence` contains 312 C# files and approximately 9.4 MB of source.
- `Persistence/Migrations` contains 187 migration, designer, and snapshot files.
- `Persistence/Configurations` contains 88 entity configuration files.
- `Finance` contains 100 files and approximately 1.9 MB of source.
- `Companies` contains 68 files and approximately 1.4 MB of source.
- Root `DependencyInjection.cs` remains about 42 KB and contains capability-specific options, providers, workers, and registrations despite existing Finance, Sales, and Support module registration classes.
- Finance, Sales, Support, Mailbox, and Company Operations implementations all depend directly on `VirtualCompanyDbContext`.
- Some capability folders also depend directly on concrete classes in other Infrastructure namespaces. Examples include Support-to-Finance/Mailbox, Finance-to-Companies, Companies-to-Sales, and Sales-to-Mailbox.
- Focused tests still reference the complete Infrastructure project, so a small capability change can compile unrelated migrations and modules.
- The current project includes SQL Server, SQLite, PostgreSQL, Redis, MailKit, and scheduling packages in the same assembly even when a given capability does not use them.

The main performance problem is compilation invalidation breadth. The goal is not merely to move files between folders; the assembly graph must prevent an edit in one capability from recompiling unrelated persistence migrations and capability implementations.

## Required Architecture Outcome

Keep the modular monolith and current inward dependency direction:

```text
Domain
  ^
Application
  ^
Persistence <---------------- Persistence.Migrations
  ^                                  ^
Infrastructure.Platform              |
  ^                                   |
Infrastructure.Mailbox               |
  ^                                   |
Infrastructure.Finance               |
Infrastructure.Sales                 |
Infrastructure.Support               |
Infrastructure.Operations            |
  ^                                   |
Infrastructure (thin composition) ---+
  ^
Api
```

The exact number of projects is subject to measured value. Do not keep a split that adds complexity without improving ownership or incremental compilation. The intended responsibilities are:

### `VirtualCompany.Persistence`

- `VirtualCompanyDbContext` and design-neutral persistence abstractions.
- EF entity configurations and conventions.
- Relational converters and interceptors that are not provider adapters.
- Seed datasets and schema-independent initialization support.
- References only Domain, Application where genuinely required, and EF packages.
- Must not reference capability Infrastructure projects.

### `VirtualCompany.Persistence.Migrations`

- Existing SQL Server migrations, designer files, and model snapshot.
- SQL Server design-time context factory or migration tooling entry point.
- References `VirtualCompany.Persistence`.
- Is referenced by the API startup project so migrations are discoverable at runtime.
- Preserves all migration IDs, namespaces where practical, schema metadata, and `__EFMigrationsHistory` compatibility.

### `VirtualCompany.Infrastructure.Platform`

- Cross-cutting implementations with demonstrated reuse: field encryption, tenancy enforcement, authorization handlers, audit writers, technical observability, execution coordination, and bounded background execution primitives.
- Must not become a new catch-all project.
- Must not contain Finance, Sales, Support, or provider-specific business behavior.

### `VirtualCompany.Infrastructure.Mailbox`

- Standard mailbox transport, OAuth/app-password authentication strategies, provider adapters, connection lifecycle, inbound synchronization, outbound transport, and mailbox workers.
- Exposes behavior through Application contracts; capability projects must not depend on concrete mailbox classes.
- Provider credentials and token handling remain encrypted and never enter logs.

### Capability Infrastructure projects

- `VirtualCompany.Infrastructure.Finance`: Finance persistence services, Fortnox adapter, Finance policies, projections, commands, and Finance workers.
- `VirtualCompany.Infrastructure.Sales`: Sales persistence services, source adapters, analysis, automation, and Sales workers.
- `VirtualCompany.Infrastructure.Support`: Support cases, triage, grounding, drafting, safety, memory, SLA, refund coordination, delivery, and Support workers.
- `VirtualCompany.Infrastructure.Operations`: company setup, agents, shared AI orchestration implementations, tasks, workflows, approvals, cockpit, briefings, alerts, and operational workers.

Each capability owns a public module registration extension. Cross-capability behavior goes through Application contracts or durable workflow/event boundaries, not concrete Infrastructure references.

### `VirtualCompany.Infrastructure`

- A thin compatibility and composition facade.
- Registers persistence, platform, mailbox, and capability modules in a deterministic order.
- Holds no capability implementation and minimal configuration logic.
- Preserves the existing `AddVirtualCompanyInfrastructure` API until all hosts and tests have migrated.

## Refactoring Principles

1. Preserve behavior before optimizing it.
2. Make one assembly-boundary change per phase.
3. Keep every phase buildable, testable, and reversible.
4. Move types without renaming public contracts unless a rename is separately justified.
5. Introduce Application interfaces to remove concrete cross-capability dependencies before moving projects.
6. Preserve service lifetimes, registration order, `IEnumerable<T>` provider behavior, options validation, and hosted-service uniqueness.
7. Keep controllers and Blazor clients unchanged unless assembly visibility requires a transport-neutral adjustment.
8. Do not change EF model metadata during structural phases.
9. Do not squash, regenerate, reorder, or edit historical migrations merely to facilitate movement.
10. Keep local SQL Server and Docker SQL Server restore and migration flows equivalent.
11. Measure clean and incremental builds with process isolation and stable commands; do not claim improvement from anecdotal timings.
12. Do not trade runtime startup, test reliability, or operational visibility for compilation speed.

## Ordered Delivery Plan

### Phase 1: Establish baselines and architecture guards

- Capture clean and incremental build timings for Domain, Application, Infrastructure, API, and Web.
- Measure scenarios for edits in Support, Sales, Finance, a persistence configuration, and a migration.
- Record project dependency graphs and source-file counts.
- Add architecture tests that prohibit Domain/Application outward dependencies and later enforce capability boundaries.
- Add DI validation tests for duplicate singleton/hosted registrations and required module resolution.
- Define measurable success criteria before moving code.

Exit gate: repeatable baseline report and automated dependency/DI guards exist.

### Phase 2: Make root composition capability-neutral

- Move all remaining Finance options, Fortnox registrations, Finance workers, and provider selection into `AddFinanceModule`.
- Move Sales and Support options/workers into their module registrations.
- Introduce module registrations for Mailbox and Company Operations.
- Keep root composition limited to database/provider selection, shared platform setup, and ordered module calls.
- Preserve all existing lifetimes and effective registrations.

Exit gate: root composition contains no Finance, Sales, or Support implementation registrations.

### Phase 3: Remove concrete cross-capability dependencies

- Inventory every `using VirtualCompany.Infrastructure.<OtherCapability>` dependency.
- Replace legitimate cross-capability calls with narrow Application contracts.
- Route side effects requiring reliability through existing workflows/outbox mechanisms.
- Eliminate Support-to-concrete-Finance/Mailbox, Finance-to-concrete-Companies, Companies-to-concrete-Sales, and Sales-to-concrete-Mailbox dependencies.
- Add tests for behavior, tenant scope, idempotency, and approval boundaries at each new seam.

Exit gate: capability implementation folders depend on Application contracts, Domain, Persistence, and approved Platform abstractions, not another capability's concrete implementation.

### Phase 4: Extract persistence core

- Create `VirtualCompany.Persistence`.
- Move `VirtualCompanyDbContext`, entity configurations, conventions, converters, interceptors, and appropriate seed support.
- Update project references and namespaces conservatively.
- Leave migrations in the existing assembly temporarily, referencing the new persistence project.
- Verify no pending model changes and no generated migration.

Exit gate: API and tests use the extracted DbContext with identical EF model metadata.

### Phase 5: Extract the SQL Server migrations assembly

- Create `VirtualCompany.Persistence.Migrations`.
- Move all historical migrations, designer files, and the snapshot without changing their identifiers or operations.
- Configure `MigrationsAssembly` explicitly for SQL Server runtime and design-time use.
- Update migration discovery, startup validation, tooling commands, restore scripts, and migration tests.
- Validate upgrades from the current backup and an empty database under local SQL Server and Docker SQL Server.

Exit gate: changing a capability no longer recompiles historical migrations; migration discovery and restore paths remain compatible.

### Phase 6: Extract reusable platform infrastructure

- Create `VirtualCompany.Infrastructure.Platform` only for demonstrated shared concerns.
- Move security, tenancy, authorization, audit, observability, execution coordination, and shared background primitives in coherent slices.
- Keep tenant-aware implementations scoped and deterministic stateless policies singleton where appropriate.
- Add module registration and architecture tests preventing capability code from moving into Platform.

Exit gate: platform assembly has a narrow dependency surface and no capability-specific behavior.

### Phase 7: Extract mailbox and communication infrastructure

- Create `VirtualCompany.Infrastructure.Mailbox`.
- Move mailbox connection persistence services, authentication strategies, provider adapters, synchronization, outbound transport, and mailbox workers.
- Preserve startup credential refresh, encryption, polling, deduplication, retries, and provider-safe errors.
- Make Finance, Sales, and Support consume mailbox Application interfaces only.

Exit gate: mailbox tests compile without Finance/Sales/Support implementations and all inbox flows remain functional.

### Phase 8: Extract Finance infrastructure

- Create `VirtualCompany.Infrastructure.Finance`.
- Move Finance implementations and module registration.
- Keep Fortnox and Finance package dependencies in this project where possible.
- Partition tests so pure Finance changes do not build Support, Sales, Web, or migrations.
- Preserve paid expense posting policy, approvals, outbox, reconciliation, and provider idempotency.

Exit gate: a Finance implementation edit rebuilds Finance, the small composition facade, and consuming hosts/tests, not Sales, Support, or migrations.

### Phase 9: Extract Sales infrastructure

- Create `VirtualCompany.Infrastructure.Sales`.
- Move Sales persistence implementations, source providers, analysis, automation, and workers.
- Preserve customer memory contracts, campaign review, outbound approval, mailbox sending boundaries, source attribution, and tenant scope.
- Update focused Sales tests to reference only required projects.

Exit gate: Sales builds and tests independently of Finance, Support, and migrations.

### Phase 10: Extract Support infrastructure

- Create `VirtualCompany.Infrastructure.Support`.
- Move Support case, triage, grounding, reply, safety, SLA, memory, refund, analytics, and delivery implementations.
- Preserve indexed-source grounding, citation provenance, reply safety, approval, outbox delivery, polling idempotency, and knowledge-gap behavior.
- Update focused Support tests to avoid unrelated Infrastructure dependencies.

Exit gate: Support builds and tests independently of Finance and Sales implementations.

### Phase 11: Extract company operations and agent orchestration

- Create `VirtualCompany.Infrastructure.Operations`.
- Move company setup, membership, agents, shared AI orchestration implementations, tasks, workflows, approvals, cockpit, briefings, alerts, and operational workers.
- Split the broad `Companies` namespace internally by owning capability without breaking Application contracts.
- Preserve shared orchestration, workflow durability, approval rechecks, tenant authorization, and cockpit query behavior.

Exit gate: the old Infrastructure project is a thin composition facade and compatibility surface.

### Phase 12: Consolidate build, test, scripts, and documentation

- Update the solution, project references, test references, build-lock scripts, `server*.ps1`, `client.ps1`, and EF tooling documentation.
- Add fast capability-specific build/test commands and keep full validation commands.
- Re-measure clean and incremental builds against Phase 1 baselines.
- Consolidate projects that did not create a meaningful ownership or performance benefit.
- Update `docs/architecture-rules.MD`, architecture overview, local SQL Server runbook, and Docker restore instructions to the final project graph.

Exit gate: measured improvement is documented, all production paths work, and no temporary compatibility shim remains without an owner and removal condition.

## Build Performance Targets

Targets must be compared on the same machine, SDK, configuration, process count, package cache, and source state.

- A Support-only implementation edit must not compile historical migrations, Finance, or Sales.
- A Sales-only implementation edit must not compile historical migrations, Finance, or Support.
- A Finance-only implementation edit must not compile historical migrations, Sales, or Support.
- A migration edit may compile Persistence and Migrations but must not force compilation of capability implementations unless their contracts changed.
- Capability-focused test projects must reference the narrowest relevant implementation project.
- Median warm incremental build time for a capability-only edit should improve by at least 50 percent from the Phase 1 baseline.
- Full clean solution build time must not regress by more than 15 percent without a documented reason and explicit acceptance.
- API startup, migration validation, and hosted-service startup counts must remain equivalent.

## Verification Matrix

Every structural phase must run proportionate checks:

- `dotnet build` for changed projects and their direct consumers.
- Focused unit/integration tests for moved services.
- Architecture and dependency tests.
- DI container construction and hosted-service uniqueness tests.
- Tenant-isolation and authorization tests for changed boundaries.
- `dotnet ef migrations has-pending-model-changes` after persistence movement.
- Migration discovery and startup pending-migration behavior.
- Local SQL Server backup restore plus startup migration validation.
- Docker SQL Server restore plus startup migration validation.
- API smoke tests for Finance, Sales, Support, mailbox, agents, workflows, approvals, and cockpit.
- Web build and targeted browser checks when route behavior or registrations affect UI data.

## Main Risks and Mitigations

### EF model drift

Risk: moving configurations changes discovery order, namespaces, conventions, or the snapshot.

Mitigation: compare model metadata, require no pending model changes, do not generate a compensating migration, and validate representative local/Docker databases.

### Migration discovery failure

Risk: startup sees no migrations or creates a second history lineage.

Mitigation: explicitly configure the migrations assembly, preserve IDs and history table settings, and add runtime/design-time discovery tests.

### Circular project references

Risk: current concrete cross-capability dependencies prevent extraction.

Mitigation: complete Phase 3 before capability extraction; contracts point inward through Application and orchestration uses workflows/outbox where appropriate.

### Duplicate or missing DI registrations

Risk: moving module registrations changes the effective implementation or starts workers twice.

Mitigation: snapshot registration behavior, validate service lifetimes, test hosted-service counts, and move one module at a time.

### Internal visibility and test breakage

Risk: focused tests rely on internal types in the monolithic assembly.

Mitigation: use narrow public/internal contracts, targeted `InternalsVisibleTo` only where justified, and move tests to the narrowest owning project.

### Clean-build regression

Risk: too many projects add MSBuild overhead even while improving incremental builds.

Mitigation: measure after each extraction and consolidate boundaries that do not isolate meaningful change.

### Runtime behavior changes hidden as refactoring

Risk: service lifetimes, worker order, provider selection, or policy behavior changes during movement.

Mitigation: prohibit feature changes within structural prompts, maintain characterization tests, and split behavioral improvements into later work.

## Non-Goals

- No microservices, message broker migration, event sourcing, or second application stack.
- No database redesign or migration-history cleanup.
- No provider replacement.
- No API route or wire-contract redesign.
- No UI redesign.
- No broad renaming campaign.
- No removal of local SQL Server or Docker SQL Server support.
- No speculative generic repository, generic policy engine, or universal provider abstraction.

## Definition of Complete

The refactor is complete when the old Infrastructure project is a small composition facade, capability edits no longer compile unrelated migrations/modules, all architecture and DI guards pass, EF reports no structural model drift, local and Docker SQL Server restore/migration paths remain valid, all production workers and integrations start exactly once, and measured incremental build improvement meets the agreed targets.
