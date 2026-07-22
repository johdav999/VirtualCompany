# VirtualCompany.Infrastructure Refactoring Prompts

These prompts implement the ordered plan in `infraplan.md`. Execute them in order. Each prompt must leave the repository in a buildable, testable state and must not combine structural movement with unrelated feature work.

## Shared instructions for every prompt

Before executing any prompt:

- Read and follow `AGENTS.md`, `production-implementation.md`, `infraplan.md`, and `docs/architecture-rules.MD`.
- `architecture-inst.md` is not present at the time these prompts were written. If it exists when a prompt is executed, read and follow it as required by `AGENTS.md`.
- Inspect the current repository again. Later prompts must use the implementation produced by earlier prompts rather than assuming this document is still an exact file inventory.
- Preserve the modular monolith, inward dependencies, API routes, wire contracts, service behavior, service lifetimes, hosted-service uniqueness, tenant isolation, authorization, approvals, outbox behavior, idempotency, retries, reconciliation, audit evidence, and safe logging.
- Do not alter database schema during structural phases. Do not generate an EF migration to hide model drift.
- Preserve local SQL Server and Docker SQL Server restore/run compatibility.
- Use `apply_patch` for manual edits, avoid unrelated formatting churn, and do not revert user changes in the dirty worktree.
- Record before/after build data when the prompt changes assembly boundaries.
- Update architecture documentation when the implemented structure changes the documented project graph.
- Implement production code only: no scaffolding, fake registrations, skipped runtime paths, silent fallbacks, or deferred in-scope TODOs.

---

## Prompt 1: Establish build baselines and enforce architecture boundaries

### 1. Title and outcome

Create a reproducible build-performance baseline and automated dependency/DI guards. This provides objective evidence for later project extraction and detects behavior-breaking registration or dependency changes immediately.

### 2. Current context

`VirtualCompany.Infrastructure` is a single large project containing Persistence, Finance, Companies, Sales, Support, Mailbox, platform services, and root composition. Persistence contains 312 C# files, including 187 migration/designer/snapshot files. Focused Finance and Sales tests currently reference the complete Infrastructure assembly. Existing architecture rules define inward dependencies and capability-owned module registrations, but there is no complete automated assembly-boundary and hosted-service uniqueness gate.

### 3. Dependencies

None.

### 4. Implementation requirements

- Add a deterministic script or test utility that measures clean and warm incremental builds using one documented SDK/configuration and process-isolated MSBuild settings.
- Measure Domain, Application, Infrastructure, API, and Web, plus edit scenarios for Finance, Sales, Support, persistence configuration, and migration files.
- Record source file counts, project dependency graph, elapsed time, and which projects compiled.
- Save the checked-in baseline report under `docs/` with machine/SDK caveats.
- Add architecture tests for prohibited outward dependencies from Domain and Application.
- Add a framework for capability dependency rules that later prompts can tighten as assemblies are introduced.
- Add DI tests that build the production service collection, validate required services, detect accidental duplicate non-collection registrations, and verify each hosted service is registered once.
- Keep benchmark artifacts out of normal source output and Git unless they are the intended report.

### 5. Constraints and preservation rules

- Do not move production source files yet.
- Do not modify service behavior or lifetimes.
- Do not make timing tests ordinary pass/fail unit tests with brittle absolute thresholds; use a report plus relative acceptance criteria.
- Do not require Docker, SQL Server, or a running API for ordinary architecture tests.
- Preserve existing build-lock behavior in `server*.ps1` and `client.ps1`.

### 6. Acceptance criteria

- Given a clean checkout with restored packages, when the baseline command runs, then it produces repeatable project-level build timing and compilation data.
- Given a forbidden Domain/Application project reference, when architecture tests run, then they fail with a clear boundary explanation.
- Given a duplicated hosted-service registration, when DI validation runs, then the test fails and identifies the duplicated worker type.
- Given the unchanged production composition, when DI validation runs, then all mandatory module services resolve successfully.

### 7. Verification

- Run the new architecture and DI tests.
- Run baseline clean and warm incremental scenarios at least three times and report median values.
- Build API and Web.
- Confirm no production files or EF model metadata changed.

### 8. Definition of done

A versioned baseline report, reusable measurement command, architecture tests, and DI/hosted-service guards are committed as production-quality engineering tooling with documentation and no runtime behavior change.

---

## Prompt 2: Make dependency injection composition capability-owned

### 1. Title and outcome

Reduce root `DependencyInjection.cs` to capability-neutral composition and make each module own its options, providers, services, and workers. This creates reliable extraction boundaries without changing runtime registrations.

### 2. Current context

Finance, Sales, and Support have `FinanceModuleRegistration`, `SalesModuleRegistration`, and `SupportModuleRegistration`, but root composition still owns Finance options, Fortnox clients/services, multiple Finance workers, provider selection, simulation-related Finance services, and mixed Company Operations registrations. Mailbox and Company Operations do not yet have complete module registration entry points. Root composition contains hundreds of registration calls.

### 3. Dependencies

Prompt 1 completed; architecture and DI guards are available.

### 4. Implementation requirements

- Inventory root registrations, options bindings, validators, HTTP clients, provider collections, workers, and factory registrations by owning capability.
- Move all Finance-owned configuration and registrations into `AddFinanceModule(IServiceCollection, IConfiguration)`.
- Keep Sales-owned configuration/workers in `AddSalesModule` and Support-owned configuration/workers in `AddSupportModule`.
- Create cohesive `AddMailboxModule`, `AddOperationsModule`, and platform registration extensions where ownership is clear.
- Leave database provider selection and truly cross-cutting composition in the root extension.
- Preserve registration order where it affects `TryAdd`, `TryAddEnumerable`, decorators, provider collections, or options post-configuration.
- Add characterization tests for effective implementation types, lifetimes, provider collection membership/order where relevant, and hosted-service counts.
- Split the root file into small composition helpers only when each helper represents a real module boundary.

### 5. Constraints and preservation rules

- Do not create new projects yet.
- Do not change public registration entry points used by API/tests.
- Do not register capability services in both root and module extensions.
- Preserve scoped DbContext-dependent services, singleton deterministic policies, and one registration per hosted service.
- Preserve provider selection and options validation behavior, including missing-credential startup behavior.

### 6. Acceptance criteria

- Given root infrastructure registration, when the container is inspected, then no Finance, Sales, or Support implementation is registered directly by root composition.
- Given the same configuration as before, when services resolve, then effective implementation types and lifetimes match the characterization baseline.
- Given hosted services, when composition is built, then every expected worker appears exactly once.
- Given provider collections, when resolved, then all intended providers are present without duplicates.

### 7. Verification

- Run architecture and DI tests from Prompt 1.
- Run module-focused Finance, Sales, Support, mailbox, workflow, and API composition tests.
- Build Infrastructure and API.
- Start the API with local SQL Server and confirm options validation and hosted-service startup.

### 8. Definition of done

Root DI is an ordered composition layer, every capability owns its registrations, all lifetimes/effective registrations are characterized, and no duplicate or temporary registrations remain.

---

## Prompt 3: Replace concrete cross-capability dependencies with Application seams

### 1. Title and outcome

Remove concrete Infrastructure-to-Infrastructure capability dependencies so capability code can move into separate assemblies without circular references or behavior loss.

### 2. Current context

Current source includes direct dependencies such as Support-to-Finance/Mailbox/Security, Finance-to-Companies, Companies-to-Sales, Sales-to-Mailbox/Security, and Mailbox-to-Companies. Most implementations also use `VirtualCompanyDbContext`. Some cross-capability actions involve customer email, refunds, workflow transitions, or outbox dispatch and therefore require policy, approval, durability, and idempotency boundaries rather than simple method forwarding.

### 3. Dependencies

Prompts 1-2 completed; module ownership and dependency guards exist.

### 4. Implementation requirements

- Generate and review a complete cross-capability dependency inventory.
- Classify each dependency as read query, command, policy decision, provider operation, workflow/outbox action, or shared platform concern.
- Introduce the narrowest required contracts in `VirtualCompany.Application` under the owning capability.
- Replace Support use of concrete Finance/Mailbox classes with Finance and Mailbox Application interfaces.
- Replace Finance use of concrete Company Operations classes with task/workflow/approval Application interfaces.
- Replace Company Operations use of concrete Sales implementations with Sales Application contracts or durable events.
- Replace Sales use of concrete Mailbox/Security implementations with Mailbox contracts and platform abstractions.
- Keep important external side effects behind the existing outbox/background execution and approval boundaries.
- Add correlation, tenant scope, stable idempotency keys, safe failure translation, and audit evidence where the extracted seam crosses a side-effect boundary.
- Tighten architecture tests to reject concrete cross-capability namespace dependencies.

### 5. Constraints and preservation rules

- Do not introduce a generic repository, service locator, universal integration API, or generic policy engine.
- Do not expose EF entities or provider payloads through new Application contracts.
- Do not move implementation projects yet.
- Preserve existing transaction boundaries and authoritative backend policies.
- Support reply sending must continue through `SupportReplyDeliveryDispatcher`; refund/finance actions must retain approval and workflow controls.

### 6. Acceptance criteria

- Given a capability implementation folder, when dependencies are analyzed, then it does not reference another capability's concrete implementation namespace.
- Given an existing cross-capability workflow, when it runs, then its externally observable result, approval requirement, audit record, and idempotency behavior are unchanged.
- Given a cross-company identifier, when a new seam is invoked, then server-side tenant checks prevent access or mutation.
- Given a provider failure, when it crosses the seam, then the caller receives a safe actionable result without secrets.

### 7. Verification

- Run architecture dependency tests.
- Run tenant-isolation and authorization tests for each new contract.
- Run Support reply/refund, Finance workflow, Sales email, mailbox, outbox, approval, and reconciliation tests.
- Build Application, Infrastructure, API, and focused capability tests.

### 8. Definition of done

All known concrete cross-capability dependencies are removed or explicitly documented as approved platform dependencies, tests cover the new seams, and future project extraction cannot create circular references.

---

## Prompt 4: Extract `VirtualCompany.Persistence`

### 1. Title and outcome

Create a dedicated persistence-core assembly containing the DbContext, EF model configuration, conventions, and schema-neutral seed support. This provides a stable dependency base for capability Infrastructure projects.

### 2. Current context

`VirtualCompanyDbContext`, 88 configuration files, converters, seed datasets, and migrations currently compile inside `VirtualCompany.Infrastructure`. Capability implementations directly consume the DbContext. Historical migrations must remain temporarily in the original assembly during this phase to keep the change bounded.

### 3. Dependencies

Prompts 1-3 completed; cross-capability concrete dependencies have been removed.

### 4. Implementation requirements

- Create `src/VirtualCompany.Persistence/VirtualCompany.Persistence.csproj` targeting .NET 9.
- Reference Domain and only the Application contracts genuinely required by persistence.
- Move `VirtualCompanyDbContext`, entity configurations, conventions, converters, persistence interceptors, and appropriate deterministic seed/model support.
- Keep provider-specific adapters and business services out of Persistence.
- Preserve namespaces where that reduces churn, unless a namespace move clearly improves ownership and all references are updated atomically.
- Update Infrastructure and test project references.
- Keep historical migrations in their current project but make them compile against the extracted DbContext.
- Add assembly dependency tests proving Persistence cannot reference capability Infrastructure projects.
- Compare EF model metadata and generated SQL-sensitive configuration before/after.

### 5. Constraints and preservation rules

- No schema change, migration generation, table/column/index rename, relationship change, converter change, default change, or query-filter change.
- Preserve SQL Server as the production provider and SQLite only for compatible tests.
- Do not move provider HTTP clients, outbox workers, or capability services into Persistence.
- Preserve local and Docker SQL Server connection behavior.

### 6. Acceptance criteria

- Given the extracted project, when Infrastructure builds, then capability services consume `VirtualCompanyDbContext` from Persistence.
- Given the pre-refactor and post-refactor models, when EF metadata is compared, then mapped tables, columns, indexes, keys, relationships, conversions, defaults, and filters are equivalent.
- Given `dotnet ef migrations has-pending-model-changes`, when run, then it reports no pending model changes.
- Given existing integration tests, when run, then tenant filters and persistence behavior remain unchanged.

### 7. Verification

- Build Persistence, Infrastructure, API, and all persistence-consuming test projects.
- Run model metadata, query-filter, tenant-isolation, and SQL Server-specific tests.
- Run `dotnet ef migrations has-pending-model-changes` using documented arguments.
- Start API against the restored local SQL Server database.

### 8. Definition of done

Persistence core is a separate stable assembly, migrations still function from their existing location, the EF model is unchanged, and no compatibility shim or duplicate configuration remains.

---

## Prompt 5: Extract the SQL Server migrations assembly

### 1. Title and outcome

Move historical EF Core migrations into `VirtualCompany.Persistence.Migrations` so ordinary capability edits no longer compile migration history while preserving every upgrade and restore path.

### 2. Current context

The current migration folder contains 187 migration, designer, and snapshot files. `VirtualCompanyDbContextFactory` currently lives with Infrastructure and configures SQL Server without an explicit migrations assembly. `DatabaseInitializationService` checks/applies pending migrations at API startup. Local and Docker restore scripts rely on the existing migration lineage.

### 3. Dependencies

Prompt 4 completed; `VirtualCompany.Persistence` owns the DbContext and model configuration.

### 4. Implementation requirements

- Create `src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj`.
- Reference Persistence and required SQL Server EF design/runtime packages.
- Move all historical migration `.cs`, `.Designer.cs`, and snapshot files without editing migration IDs or operations.
- Place or update the SQL Server design-time context factory so EF tooling discovers Persistence plus Migrations reliably.
- Configure SQL Server `MigrationsAssembly` explicitly in runtime registration and design-time tooling.
- Ensure API references the migrations assembly so startup migration discovery cannot be trimmed or omitted.
- Update `DatabaseInitializationService` tests, migration discovery tests, EF commands, local SQL runbook, restore scripts, and Docker instructions.
- Add a test asserting known first/latest migration IDs and snapshot discovery from the new assembly.
- Validate upgrade from the current `virtualcompany.bak` and from an empty database.

### 5. Constraints and preservation rules

- Never regenerate, squash, rename, reorder, or silently repair historical migrations.
- Preserve `__EFMigrationsHistory` table semantics and existing applied IDs.
- Do not introduce startup DDL, `EnsureCreated`, or a second migration history.
- Do not require Docker for ordinary unit tests, but perform and document both local and Docker SQL Server validation before completion.
- If a structural move produces a pending model change, stop and correct discovery/configuration rather than creating a migration.

### 6. Acceptance criteria

- Given an existing database with applied migration IDs, when the updated API starts, then it recognizes the same history and applies only genuinely pending migrations.
- Given an empty database, when migrations apply, then the final model is created successfully.
- Given the current backup restored locally or in Docker, when startup validation runs, then no migration-lineage error occurs.
- Given a Support/Finance/Sales source edit, when an incremental build runs, then the migrations project is not compiled.

### 7. Verification

- Run migration discovery and pending-model-change tests.
- Apply migrations to an empty local SQL Server database.
- Restore `virtualcompany.bak`, start the API, and validate representative Finance/Sales/Support reads.
- Repeat restore/startup validation in Docker SQL Server.
- Re-run Prompt 1 build scenarios and record migration compilation isolation.

### 8. Definition of done

All historical migrations and the snapshot live in the dedicated project, runtime/design-time discovery is explicit and tested, local/Docker upgrades work, and migration history is no longer part of capability compilation.

---

## Prompt 6: Extract reusable platform infrastructure

### 1. Title and outcome

Create a narrowly scoped Platform Infrastructure assembly for security, tenancy, authorization, audit, observability, and shared execution primitives used by multiple capabilities.

### 2. Current context

The existing Infrastructure project contains Auth, Authorization, Security, Tenancy, Auditing, Observability, BackgroundJobs, Context, and execution-coordination implementations. Some depend on Persistence; others are deterministic policies. Without a clear platform boundary, capability extraction would either duplicate these services or retain a large shared project.

### 3. Dependencies

Prompts 1-5 completed; Persistence and Migrations are separate and capability seams are contract-based.

### 4. Implementation requirements

- Create `VirtualCompany.Infrastructure.Platform` with explicit references to Application, Domain, Persistence, and only required packages.
- Move cross-cutting security/encryption, tenant-context enforcement, authorization handlers, business audit writers, technical exception/logging infrastructure, distributed execution coordination, and bounded background execution primitives in coherent groups.
- Add `AddPlatformInfrastructure(IServiceCollection, IConfiguration)` with preserved options validation and service lifetimes.
- Keep provider-specific and capability-specific workers out of Platform.
- Define architecture rules/tests that forbid Finance, Sales, Support, or provider behavior in Platform.
- Preserve Data Protection key handling and encrypted-field compatibility.
- Preserve Redis fallback/coordination behavior and safe observability.

### 5. Constraints and preservation rules

- Do not turn Platform into a generic manager or dumping ground.
- Do not move `VirtualCompanyDbContext` back into Platform.
- Never log tokens, passwords, ciphertext/plaintext secrets, or sensitive provider payloads.
- Preserve tenant authorization as a server-side boundary and retain global filter plus explicit context checks.

### 6. Acceptance criteria

- Given any capability project, when it needs encryption, tenancy, audit, or execution coordination, then it uses Platform/Application abstractions rather than duplicated code.
- Given existing protected data, when decrypted with the unchanged key ring, then compatibility is preserved.
- Given a cross-company operation, when authorization is evaluated, then it remains denied.
- Given Platform dependencies, when architecture tests run, then no capability implementation dependency exists.

### 7. Verification

- Run auth, authorization, tenancy, encryption, Data Protection, audit, exception, Redis/execution, and background job tests.
- Build Platform, Infrastructure composition, and API.
- Start API with existing keys/database and verify encrypted mailbox/provider records do not produce new decryption failures.

### 8. Definition of done

Platform is a cohesive reusable assembly with tested security and tenant behavior, no capability leakage, and root composition delegates all platform registration to it.

---

## Prompt 7: Extract mailbox and communication infrastructure

### 1. Title and outcome

Create a dedicated Mailbox Infrastructure assembly that owns standard email transport, provider authentication, synchronization, delivery, and mailbox workers for Finance, Sales, and Support.

### 2. Current context

Mailbox implementations currently depend on Persistence, Security, and some Company Operations services. Finance, Sales, and Support use mailbox behavior. The application supports Gmail, Microsoft 365, and standard IMAP/SMTP providers, encrypted credentials, startup refresh, inbound polling, outbound sending, and function-specific mailbox purposes.

### 3. Dependencies

Prompts 1-6 completed; Application mailbox contracts, Persistence, Platform, and module composition boundaries exist.

### 4. Implementation requirements

- Create `VirtualCompany.Infrastructure.Mailbox` referencing Application, Domain, Persistence, and Platform.
- Move mailbox connection services, OAuth state/replay protection, app-password strategies, standard provider adapters, inbound sync, outbound transport, health state, startup refresh, and mailbox hosted services.
- Move MailKit and mailbox-specific package dependencies out of the composition facade.
- Register all mailbox services through `AddMailboxInfrastructure` and ensure workers appear exactly once.
- Preserve purpose-specific Finance/Sales/Support assignment and automatic startup connection restoration.
- Preserve provider-specific host/port/TLS presets while retaining the provider-neutral IMAP/SMTP model.
- Preserve inbound deduplication cursors, outbound idempotency, retry classification, safe errors, and encrypted secrets.
- Replace any remaining Company Operations concrete dependency with an Application contract.

### 5. Constraints and preservation rules

- Do not expose credentials or provider tokens through Application contracts.
- Do not make Finance/Sales/Support reference concrete Mailbox implementations.
- Do not bypass Support's durable reply dispatcher or other outbox-backed delivery paths.
- Preserve existing Gmail/Microsoft OAuth and standard IMAP/SMTP compatibility.

### 6. Acceptance criteria

- Given a configured Gmail, Microsoft 365, or standard mailbox, when the API starts, then the connection is restored using the existing encrypted credentials/token flow.
- Given repeated inbox polling, when the same message is observed, then no duplicate domain record is created.
- Given an approved outbound message, when delivery executes, then it is sent once or enters visible retry/reconciliation state.
- Given a mailbox-only source edit, when incrementally built, then Finance, Sales, Support, and Migrations do not compile unless a shared contract changed.

### 7. Verification

- Run standard mailbox infrastructure, OAuth state, encryption, inbound deduplication, startup refresh, outbound delivery, and provider error tests.
- Run Finance inbox, Sales email ingestion, and Support mailbox integration contract tests.
- Build Mailbox, composition facade, API, and Web typed clients where affected.
- Perform local smoke tests with configured mailbox connections without exposing secrets in output.

### 8. Definition of done

Mailbox transport is independently buildable, capability modules consume only Application contracts, all provider/authentication paths remain secure, and no duplicate worker or transport registration remains.

---

## Prompt 8: Extract Finance infrastructure

### 1. Title and outcome

Move Finance implementation, Fortnox integration, Finance workers, and Finance registration into an independently buildable capability assembly.

### 2. Current context

Finance contains roughly 100 files and 1.9 MB of source, including broad read/command facades, accounting policies, supplier bill workflows, simulation, reporting, reconciliation, Fortnox, seeding, and hosted workers. `VirtualCompany.Finance.Tests` currently references the entire Infrastructure project. Paid supplier bill expense posting has an authoritative eligibility policy and sensitive actions use approval/outbox boundaries.

### 3. Dependencies

Prompts 1-7 completed; Persistence, Migrations, Platform, Mailbox, and cross-capability Application seams exist.

### 4. Implementation requirements

- Create `VirtualCompany.Infrastructure.Finance` referencing Application, Domain, Persistence, Platform, and Mailbox contracts/assembly only where implementation is required.
- Move all Finance implementation files and `FinanceModuleRegistration`.
- Move Fortnox, Finance OCR, and Finance-specific package/configuration ownership to the Finance project.
- Keep compatibility facades/partial types coherent; do not reassemble catch-all files.
- Update `VirtualCompany.Finance.Tests` and applicable API tests to reference Finance plus the narrowest dependencies.
- Preserve Finance options validation, provider selection, seed behavior, startup sync, worker uniqueness, read/command separation, and cockpit adapter behavior.
- Add assembly dependency tests preventing Finance from referencing Sales or Support implementations.

### 5. Constraints and preservation rules

- Preserve accounting logic, persisted/wire status values, EF mappings, and transaction boundaries.
- Preserve `PaidSupplierBillExpensePostingEligibility` as the single source of truth.
- Preserve approval rechecks, outbox dispatch, provider idempotency, retry/reconciliation, and audit evidence.
- Do not alter Finance API routes or Web contracts.
- No EF migration is expected; pending model changes are a failure.

### 6. Acceptance criteria

- Given a Finance source edit, when a warm incremental build runs, then migrations, Sales, and Support do not compile.
- Given Finance module registration, when the container resolves Finance interfaces/providers/workers, then effective implementations and lifetimes match the baseline.
- Given representative invoice, bill, payment, reconciliation, reporting, and Fortnox workflows, when tested, then outputs and side-effect controls are unchanged.
- Given an unauthorized company, when Finance services are called, then reads/writes remain isolated.

### 7. Verification

- Run Finance policy, persistence, API, tenant-isolation, approval, outbox, reconciliation, seed, reporting, and Fortnox contract tests.
- Build Finance, composition facade, API, and Finance tests.
- Run pending-model-change validation.
- Smoke test Finance overview, bill inbox, monthly summary, and integration settings against local SQL Server.
- Re-run Finance incremental build benchmark.

### 8. Definition of done

Finance is independently buildable and testable, root Infrastructure contains no Finance implementations/packages, all sensitive controls remain intact, and measured compilation isolation is documented.

---

## Prompt 9: Extract Sales infrastructure

### 1. Title and outcome

Move Sales persistence services, providers, automation, analysis, and workers into an independently buildable Sales Infrastructure assembly.

### 2. Current context

Sales contains lead/source services, pipeline/deal operations, campaigns, sequences, customer memory integration, conversion analytics, outbound policy/review, mailbox ingestion/sending, AI analysis/decisions, and hosted workers. Sales currently references Persistence, Mailbox, and Security implementations and focused Sales tests still reference the entire Infrastructure project.

### 3. Dependencies

Prompts 1-8 completed; Sales cross-capability calls use Application contracts and required Persistence/Platform/Mailbox assemblies are available.

### 4. Implementation requirements

- Create `VirtualCompany.Infrastructure.Sales` and move all Sales implementations plus `SalesModuleRegistration`.
- Move Sales-specific options, workers, and provider registrations into the Sales project.
- Keep outbound email behind Mailbox Application contracts and existing review/approval policy.
- Preserve source attribution, CRM/provider normalization, campaign/sequence idempotency, reply signal detection, forecast behavior, and customer memory boundaries.
- Update `VirtualCompany.SalesSource.Tests` and relevant API tests to reference only Sales and required lower-level projects.
- Add dependency tests preventing Sales from referencing Finance or Support implementations.
- Preserve cockpit/KPI behavior through Application adapters/read contracts.

### 5. Constraints and preservation rules

- Do not leak provider schemas into Domain/Application entities.
- Do not permit outbound communication to bypass human review or automation policy.
- Preserve tenant scope, stable idempotency, retry classification, and audit evidence.
- No route, wire contract, or schema change is expected.

### 6. Acceptance criteria

- Given a Sales-only edit, when a warm incremental build runs, then migrations, Finance, and Support do not compile.
- Given Sales registration, when resolved, then all providers/workers appear exactly once with baseline lifetimes.
- Given lead capture, campaign, sequence, email ingestion, deal update, conversion analytics, and forecast flows, when tested, then behavior is unchanged.
- Given cross-company data, when queried or mutated, then tenant isolation is enforced.

### 7. Verification

- Run Sales source, provider registry, lead, campaign, sequence, outbound review, email ingestion, analytics, decision, and tenant tests.
- Build Sales, composition facade, API, and Sales tests.
- Run pending-model-change validation.
- Smoke test Sales dashboard, lead/deal views, email activity, and Agent Staff pipeline projections.
- Re-run Sales incremental build benchmark.

### 8. Definition of done

Sales is independently buildable/testable, outbound controls and provider normalization remain production-safe, and unrelated capability/migration projects are not compiled for Sales-only changes.

---

## Prompt 10: Extract Support infrastructure

### 1. Title and outcome

Move Support case operations, grounding, drafting, safety, memory, SLA, refund coordination, delivery, analytics, and workers into an independently buildable Support Infrastructure assembly.

### 2. Current context

Support contains 26 focused implementation files but many currently import Mailbox, Finance, Persistence, and Security namespaces. Support must answer only from accessible processed/indexed company knowledge, preserve citations, create review/knowledge-gap states when grounding is insufficient, and send approved replies through durable delivery.

### 3. Dependencies

Prompts 1-9 completed; Finance and Mailbox are accessed through Application contracts, and Persistence/Platform boundaries are established.

### 4. Implementation requirements

- Create `VirtualCompany.Infrastructure.Support` and move all Support implementations plus `SupportModuleRegistration`.
- Preserve support case, mailbox routing, triage, context, knowledge retrieval, reply drafting, deterministic safety, tool actions, SLA, memory, refund workflow, analytics, decisions, and worker behavior.
- Keep Finance refund coordination behind the Application seam from Prompt 3.
- Keep outbound replies behind `SupportReplyDeliveryDispatcher` and Mailbox contracts.
- Update Support-focused tests to reference Support plus only required lower-level projects.
- Add dependency tests preventing Support from referencing Finance, Sales, or Mailbox concrete namespaces.
- Preserve Agent Staff support-case projections and existing routes/contracts.

### 5. Constraints and preservation rules

- Never weaken grounding thresholds, citation requirements, human review, reply safety, approval, or tenant isolation to simplify extraction.
- Repeated polling must remain idempotent for messages, cases, executions, drafts, and deliveries.
- No direct LLM provider call may bypass shared orchestration.
- No schema or API contract change is expected.

### 6. Acceptance criteria

- Given a Support-only edit, when a warm incremental build runs, then migrations, Finance, and Sales do not compile.
- Given sufficient approved knowledge, when a reply draft is generated, then it remains grounded and cites source references.
- Given insufficient knowledge or unsafe content, when drafting runs, then human review/knowledge-gap behavior remains enforced.
- Given repeated inbox scans or delivery retries, when processed, then no duplicate case/draft/message/delivery is created.

### 7. Verification

- Run Support grounding, safety, mailbox routing, triage, reply, SLA, memory, refund, approval, delivery, analytics, tenant, and idempotency tests.
- Build Support, composition facade, API, and Support tests.
- Run pending-model-change validation.
- Smoke test inbox ingestion, case details, grounded draft generation, approval, and outbound delivery.
- Re-run Support incremental build benchmark.

### 8. Definition of done

Support is independently buildable/testable, all grounding and delivery safety controls remain authoritative, and unrelated capabilities/migrations are excluded from Support-only compilation.

---

## Prompt 11: Extract company operations and shared agent orchestration

### 1. Title and outcome

Move the broad Company Operations area into a cohesive assembly covering company setup, tenancy-facing operations, agents, shared AI orchestration implementations, tasks, workflows, approvals, cockpit, briefings, alerts, and operational workers.

### 2. Current context

The `Companies` folder contains 68 files and approximately 1.4 MB of source spanning multiple operational capabilities. It includes company setup/query, memberships, agents, tool execution, shared orchestration, tasks, workflows, approvals, cockpit dashboards, Agent Staff overview, briefings, alerts, simulations, triggers, and background progression. Existing architecture rules require one shared AI orchestration subsystem and durable workflow/approval boundaries.

### 3. Dependencies

Prompts 1-10 completed; Persistence, Platform, Mailbox, Finance, Sales, and Support assemblies are available behind Application contracts.

### 4. Implementation requirements

- Create `VirtualCompany.Infrastructure.Operations`.
- Move Company Operations implementations and `AddOperationsModule` into the project.
- Organize source internally by Company Setup, Agent Management, Shared AI Orchestration, Tasks, Workflow, Approval, Cockpit, Briefing, Alert/Escalation, and Simulation/Trigger concerns.
- Preserve namespaces/public types where compatibility is valuable, but remove broad catch-all source files.
- Keep the shared AI orchestration stack single and provider calls behind approved gateways.
- Preserve tool permissions, scopes, guardrails, approval rechecks, durable workflow state, task ownership, background polling, cockpit queries, and Agent Staff aggregation.
- Update focused tests and add architecture rules preventing Operations from directly invoking capability implementations.
- Keep cross-capability reads behind Finance/Sales/Support Application adapters.

### 5. Constraints and preservation rules

- Do not split into microservices or duplicate orchestration per agent.
- Do not make chat the system of record for tasks/workflows/approvals.
- Preserve tenant authorization, workflow idempotency, outbox behavior, audit evidence, and worker uniqueness.
- Do not redesign APIs or UI.
- No schema change is expected.

### 6. Acceptance criteria

- Given Finance, Sales, or Support implementation assemblies, when Operations compiles, then it depends only on their Application contracts/adapters, not concrete classes.
- Given agent tool execution, when requested, then scope/permission/policy/approval checks remain enforced before execution.
- Given workflow/trigger/background polling retries, when repeated, then transitions and tasks remain idempotent.
- Given cockpit and Agent Staff queries, when run against current company data, then summaries, task stages, tenant scope, and routes remain correct.

### 7. Verification

- Run company setup/membership, agents, orchestration, tools, tasks, workflow, approvals, cockpit, Agent Staff, briefing, alert/escalation, simulation, trigger, tenant, idempotency, and worker tests.
- Build Operations, all capability projects, composition facade, API, and Web.
- Run pending-model-change validation.
- Browser smoke test Dashboard, Agent Staff, Agents, Workflows, Approvals, and Briefing Delivery.

### 8. Definition of done

Company Operations is cohesive and independently testable, shared orchestration remains singular and governed, and the original Infrastructure project contains only composition/compatibility code.

---

## Prompt 12: Finalize the composition facade, build paths, tests, and architecture documentation

### 1. Title and outcome

Complete the refactor by minimizing the compatibility facade, optimizing build/test entry points, validating all production paths, measuring results, and documenting the final architecture.

### 2. Current context

After Prompts 1-11, Persistence, Migrations, Platform, Mailbox, Finance, Sales, Support, and Operations should be separate projects. `VirtualCompany.Infrastructure` should be a thin composition facade, but solution references, test references, scripts, package ownership, build locks, EF commands, and documentation may still contain transitional structure.

### 3. Dependencies

Prompts 1-11 completed successfully.

### 4. Implementation requirements

- Remove obsolete source includes, project references, duplicate packages, temporary compatibility shims, and transitional registrations.
- Keep `AddVirtualCompanyInfrastructure` as a small deterministic composition facade unless all callers can be migrated without unnecessary breakage.
- Update `VirtualCompany.sln`, project references, `InternalsVisibleTo`, test references, and package ownership.
- Update `server.ps1`, `server-local-sql.ps1`, `client.ps1`, build-lock behavior, and `--no-restore` paths for the new graph.
- Add documented fast commands for each capability and retain full solution validation commands.
- Re-run the baseline matrix and publish before/after median clean/incremental results.
- Consolidate any project whose boundary does not produce meaningful ownership or measured compilation isolation.
- Audit provider packages; keep each package in the narrowest owning project and remove an unused provider only after proving no supported runtime/test path requires it.
- Update `docs/architecture-rules.MD`, architecture overview, SQL Server runbook, Docker restore guidance, and contributor instructions.
- Run end-to-end local and Docker SQL Server validation.

### 5. Constraints and preservation rules

- Do not optimize by disabling analyzers, skipping project references required at runtime, suppressing valid tests, or bypassing migrations.
- Do not weaken the shared build lock in a way that reintroduces locked DLL failures.
- Preserve startup service ordering where required, exactly-once hosted-service registration, and migration startup behavior.
- Keep the modular monolith and all production capability behavior.

### 6. Acceptance criteria

- Given a Finance, Sales, or Support implementation edit, when warm incremental build runs, then unrelated capability projects and Migrations are not compiled.
- Given baseline measurements, when compared with final results, then median capability-only incremental build improves by at least 50 percent or deviations are explicitly documented and accepted.
- Given a clean solution build, when compared with baseline, then it does not regress by more than 15 percent without explicit documented acceptance.
- Given local or Docker SQL Server restored from the current backup, when API starts, then migration history is recognized and representative Finance/Sales/Support/agent flows work.
- Given the complete service provider, when validated, then all mandatory services resolve and every hosted service is registered once.

### 7. Verification

- Run all architecture, DI, Domain, Application, Finance, Sales, Support, API, Web contract, and Web tests.
- Run full clean solution build and capability warm incremental benchmarks.
- Run pending-model-change and migration discovery checks.
- Restore and validate both local SQL Server and Docker SQL Server paths.
- Start server/client through supported scripts and perform browser smoke tests across core modules.
- Inspect logs for duplicate workers, migration warnings, dependency-load failures, and secret leakage.

### 8. Definition of done

The final project graph is documented and enforced, the old Infrastructure assembly is a thin production composition facade, build improvements are measured, all tests and database paths pass, no temporary in-scope TODO remains, and the application starts and operates normally through supported local and Docker workflows.
