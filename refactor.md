# Virtual Company Refactoring Prompt Pack

Execute these prompts in order. Every prompt is an implementation task, not an analysis-only task. Before changing code, read and follow `AGENTS.md`, `production-implementation.md`, `docs/architecture-rules.md`, and `docs/architecture-overview.md`. Also read `architecture-inst.md` when it exists. Existing repository behavior and current implementation take precedence over older planning documents. Preserve unrelated work in the dirty worktree.

## Prompt 1: Replace API startup DDL with EF Core migrations

### Title and outcome

Remove ad hoc schema creation and alteration from API startup and make EF Core migrations the authoritative database schema history. The outcome is deterministic startup behavior and one compatible schema path for local SQL Server, Docker SQL Server, CI, backup restore, and production deployment.

### Current context

`src/VirtualCompany.Api/Program.cs` is approximately 3,400 lines and contains many `ExecuteSqlRawAsync`, `CREATE TABLE`, `ALTER TABLE`, and `CREATE INDEX` statements. The repository also contains EF migrations and `VirtualCompanyDbContextModelSnapshot`, creating two competing schema-management paths. Startup configuration includes `DatabaseInitialization:ApplyMigrationsOnStartup`. Local SQL Server and Docker restore scripts must remain usable.

### Dependencies

None. Complete this before the other prompts.

### Implementation requirements

- Inventory every startup DDL block and map it to an existing migration, a missing migration, or non-schema startup work.
- Generate or repair EF Core migrations for schema changes not represented in migration history. Do not edit generated designer files manually except when repairing demonstrably invalid generated output.
- Remove schema mutation from `Program.cs`; retain only migration invocation, migration validation, seed orchestration, and application startup concerns.
- Extract remaining database initialization orchestration into a focused infrastructure service with structured logging and clear failure behavior.
- Ensure migration execution is controlled by configuration and does not silently mutate production schemas when disabled.
- Add startup validation that reports pending/incompatible migrations clearly without exposing credentials.
- Update local SQL Server and Docker documentation/scripts when required so both use the same migrations after restore.
- Preserve seed data, existing databases, and upgrade paths; do not recreate or drop databases.

### Constraints and preservation rules

- Follow `production-implementation.md`, `docs/architecture-rules.md`, and `architecture-inst.md` when available.
- SQL Server remains the provider. Do not introduce PostgreSQL-specific behavior.
- Preserve all tenant data and existing migration IDs.
- Schema changes require a migration and must work with local SQL Server and Docker SQL Server.
- Do not place database repair logic in controllers or UI.
- Do not use `EnsureCreated` as a production migration substitute.

### Acceptance criteria

- Given a clean database, when migrations run, then the complete current schema is created without startup DDL.
- Given a database restored from `virtualcompany.bak`, when migrations run, then only pending migrations are applied and existing data remains intact.
- Given migrations-on-startup is disabled, when the API starts, then it performs no schema mutation and reports actionable incompatibility errors.
- Given Docker SQL Server and local SQL Server, when the same migration set is applied, then both reach the same EF model state.
- `Program.cs` contains no ad hoc `CREATE TABLE`, `ALTER TABLE`, or `CREATE INDEX` statements.

### Verification

- Run migration generation validation and inspect the resulting SQL.
- Test clean-database migration, restored-database upgrade, repeated/idempotent startup, and migrations-disabled startup.
- Run `dotnet ef migrations has-pending-model-changes` or the repository-equivalent validation.
- Build API and Infrastructure projects.
- Verify restore scripts for local SQL Server and Docker paths without destructive resets.

### Definition of done

Production migration handling is the single schema authority. No startup schema scaffolding, silent repair, deferred TODO, data-destructive workaround, or provider-specific divergence remains.

## Prompt 2: Partition and repair the automated test architecture

### Title and outcome

Create reliable module-focused test projects so Finance, Support, Sales, API, and Web tests can compile and run independently. The outcome is faster feedback and failures that identify the affected module instead of being blocked by unrelated source or locked web binaries.

### Current context

`tests/VirtualCompany.Api.Tests` contains unit, integration, UI component, persistence, and workflow tests together and references broad production projects. Existing syntax failures in unrelated test files can prevent focused tests from compiling. A running `VirtualCompany.Web` process can lock its DLL and block API test builds. `tests/VirtualCompany.SupportGrounding.Tests` and `tests/VirtualCompany.SalesSource.Tests` demonstrate narrower test-project boundaries.

### Dependencies

Prompt 1 must be complete so integration tests use the authoritative migration path.

### Implementation requirements

- Classify existing tests by module and test type before moving them.
- Create focused projects for Domain, Application, Infrastructure, API integration, Web component, Finance, Support, and Sales where the current tests justify them.
- Move tests without reducing coverage or changing assertions merely to make them pass.
- Minimize project references: pure policy tests must not reference Web or API; API integration tests may reference API; component tests may reference Web.
- Extract shared test fixtures into a dedicated test-support project only when they are truly cross-module.
- Repair existing test syntax and compilation failures in scope.
- Standardize SQL Server integration-test setup; use SQLite only for models verified as provider-compatible.
- Add collection/fixture controls for tests that share databases, ports, environment variables, or static state.
- Update solution files and test commands/documentation.

### Constraints and preservation rules

- Follow production and architecture instructions even though this is test code.
- Do not delete, skip, or weaken failing tests without documenting an obsolete behavior and replacing its coverage.
- Do not make production APIs public solely for testing when an internal seam or integration test is appropriate.
- Keep tenant-isolation and authorization tests in every affected module.
- Tests must not depend on a developer already running API, Web, Docker, or SQL Server unless explicitly categorized as external integration tests.

### Acceptance criteria

- Given a Support-only change, when Support tests run, then unrelated Finance/Web test compilation cannot block them.
- Given the full solution, when all normal tests run, then every test project compiles.
- Given a running Web process, when non-Web tests run, then no Web output DLL copy is required.
- Existing test coverage is retained or improved, with module ownership visible from project names.

### Verification

- Run each new test project independently and then the full non-external suite.
- Verify project reference graphs and absence of unnecessary Web/API references.
- Run tenant-isolation, authorization, migration, and support-grounding suites explicitly.
- Build the solution with build servers disabled to detect hidden coupling.

### Definition of done

The test structure is production-quality, independently executable, deterministic, and free of deferred syntax repairs, blanket skips, hidden external prerequisites, or lost coverage.

## Prompt 3: Decompose oversized Finance application and read services

### Title and outcome

Split large Finance services into capability-owned query, command, policy, and orchestration services while preserving every API contract and accounting behavior. The outcome is a Finance module that can be changed safely without loading thousands of unrelated lines into one class.

### Current context

Key pressure points include `CompanyFinanceReadService.cs` (about 4,500 lines), `CompanyFinanceBillInboxService.cs` (about 2,400 lines), `CompanySimulationFinanceGenerationService.cs` (about 2,300 lines), and related partial/helper files. These services cover bills, invoices, payments, allocations, reconciliation, statements, insights, simulation, and UI projections.

### Dependencies

Prompts 1 and 2.

### Implementation requirements

- Build a method-to-capability inventory and identify stable seams before moving code.
- Extract focused services for bills, supplier invoices, customer invoices, payments/allocations, reconciliation, reporting/statements, insights, and simulation generation as justified by existing behavior.
- Separate read-model projection from state-changing commands.
- Move eligibility and accounting decisions into named policy/domain services rather than projection methods.
- Keep cross-capability coordination in application services, not controllers.
- Replace broad constructors with focused dependencies and update DI registrations.
- Preserve endpoint DTOs, routes, audit events, approval boundaries, posting rules, Fortnox references, and tenant filters.
- Add characterization tests before moving high-risk accounting logic.
- Remove dead private methods and duplicate helpers only after coverage proves they are unused.

### Constraints and preservation rules

- Do not redesign accounting behavior during structural extraction.
- Every query and command remains company-scoped.
- Financial writes retain approvals, idempotency, audit, and reconciliation.
- Preserve local SQL Server and Docker compatibility; no schema change is expected unless justified, in which case add a migration.
- Avoid generic repository abstractions that erase meaningful Finance concepts.

### Acceptance criteria

- Given any existing Finance endpoint, when called before and after refactoring with the same data, then its status, payload, allowed actions, and side effects are equivalent.
- No extracted service owns unrelated Finance capabilities.
- State-changing operations are not implemented in read services.
- Constructor dependency counts and class sizes are materially reduced without introducing cyclic dependencies.

### Verification

- Run Finance calculation, posting, payments, reconciliation, tenant-isolation, approval, Fortnox, and API integration tests.
- Add snapshot/contract comparisons for important read DTOs.
- Build Application, Infrastructure, API, and Web.
- Review SQL generated by high-volume queries for regressions and N+1 behavior.

### Definition of done

Finance capabilities have clear ownership, behavior remains compatible, and no forwarding-only scaffolding, duplicate implementation, silent fallback, or in-scope TODO remains.

## Prompt 4: Decompose Support services into explicit capabilities

### Title and outcome

Split `SupportServices.cs` into cohesive Support capability files and services while preserving automatic mailbox routing, grounded drafting, safety review, refunds, SLA monitoring, memory, and knowledge-gap behavior.

### Current context

`src/VirtualCompany.Infrastructure/Support/SupportServices.cs` is about 2,600 lines and includes case operations, mailbox ingestion/routing, background processing, context resolution, triage, semantic knowledge retrieval, reply drafting, safety, outbound email, tools, agent orchestration, refunds, SLA, memory, knowledge gaps, and analytics. Recent behavior automatically runs the support agent for routed inbound messages using idempotency keys and requires trusted indexed knowledge for safe replies.

### Dependencies

Prompts 1 and 2. Prompt 3 is not required functionally but should be completed first in the ordered pack.

### Implementation requirements

- Move each existing public service into a capability-aligned file and namespace folder without changing contracts unnecessarily.
- Suggested ownership: Cases, Mailbox, Triage, Knowledge, ReplyDrafting, Safety, AgentExecution, Refunds, SLA, Memory, KnowledgeGaps, and Analytics.
- Keep `SupportReplySafetyRules` in the Application layer as deterministic policy logic.
- Keep semantic retrieval behind `ICompanyKnowledgeSearchService`; preserve access scopes, processed/indexed requirements, citations, and confidence thresholds.
- Preserve mailbox polling retries and per-message idempotency.
- Ensure background-worker failures remain visible and do not lose durable inbound messages.
- Keep outbound email and refund side effects behind provider/workflow boundaries.
- Update DI using a `AddSupportModule` registration extension if consistent with the repository.
- Add focused characterization tests around automatic agent runs, missing grounding, citations, and retries.

### Constraints and preservation rules

- Do not merge Support orchestration into a separate AI stack; use shared orchestration contracts.
- Preserve tenant isolation and mailbox credential boundaries.
- Do not automatically send customer email unless existing policy explicitly allows it.
- Unsupported answers must remain review-only and unsafe promises must remain blocked.
- Preserve audit events, knowledge gaps, and operator-visible failures.

### Acceptance criteria

- Given a newly routed inbound support email, when the worker processes it, then one idempotent agent execution prepares a grounded draft.
- Given no trusted indexed source, then the draft requires review and cannot be approved or sent.
- Given trusted company knowledge, then source document/chunk references remain attached and visible.
- Repeated polling does not create duplicate cases, messages, agent runs, or drafts.
- No single replacement file becomes another catch-all Support module.

### Verification

- Run `VirtualCompany.SupportGrounding.Tests` and Support integration tests.
- Test mailbox retries, tenant isolation, source access, drafting, approval/send safety, refund approval, SLA, memory, and knowledge gaps.
- Build Infrastructure, API, and Web.
- Verify service registrations resolve without cycles.

### Definition of done

Support capability ownership is explicit and all current behavior is preserved with no duplicate compatibility implementation, unsafe fallback, missing audit path, or deferred extraction.

## Prompt 5: Split broad contracts, entities, and EF configurations by capability

### Title and outcome

Organize large contract, entity, and configuration files into capability-focused files without changing persistence mappings or public wire contracts. The outcome is faster navigation and clearer ownership across Finance, Sales, Support, and Knowledge.

### Current context

Large handwritten containers include `FinanceContracts.cs`, `SalesEntities.cs`, `FinanceEntities.cs`, `SupportEntities.cs`, and `Persistence/EntityConfigurations.cs`. Generated migration designers and the EF model snapshot are large by nature and are not refactoring targets.

### Dependencies

Prompts 1 through 4 so capability boundaries and migration authority are established.

### Implementation requirements

- Inventory types and group them by bounded capability, preserving namespaces where possible to avoid consumer churn.
- Split DTOs/commands/queries/interfaces into files or folders matching their owning feature.
- Split domain entities only at type boundaries; do not create partial entities merely to distribute line count unless the repository already uses that pattern appropriately.
- Split EF configurations into one configuration per aggregate/entity group and register them through `ApplyConfigurationsFromAssembly` or the established convention.
- Keep enum/storage-value conversion close to the owning domain concept.
- Preserve JSON property names, constructor semantics, validation limits, table names, column names, indexes, relationships, and query filters.
- Do not modify generated migrations solely because files moved.
- Add architecture tests or conventions that prevent new unrelated catch-all files where practical.

### Constraints and preservation rules

- This is structural refactoring; no API or database contract change is implied.
- No migration should be produced unless an actual model change is intentional. A pending-model-change check must remain clean.
- Preserve tenant ownership and query filters for every entity.
- Avoid circular project or namespace dependencies.

### Acceptance criteria

- Existing consumers compile without wire-format changes.
- EF reports no pending model change caused by file movement.
- Each moved type has one clear capability owner.
- Generated migration and snapshot files remain generated artifacts, not manually partitioned.

### Verification

- Build all projects.
- Run serialization contract, EF model, migration compatibility, tenant-filter, and module-focused tests.
- Compare generated model metadata before and after refactoring.
- Search for duplicate type definitions and stale source-file references.

### Definition of done

Contracts, entities, and configurations are capability-organized with identical behavior and persistence metadata, without compatibility duplicates or arbitrary fragmentation.

## Prompt 6: Split Internal Finance API controllers by resource and workflow

### Title and outcome

Replace the oversized internal Finance controller with focused controllers whose routes and contracts remain backward-compatible. The outcome is thin transport code with clear authorization and application-service ownership.

### Current context

`src/VirtualCompany.Api/Controllers/InternalFinanceController.cs` is about 3,600 lines and exposes bills, invoices, payments, allocations, reconciliation, reporting, integrations, and action endpoints. It has many injected services and transport helpers. Finance behavior must remain in application/infrastructure services, not move into new controllers.

### Dependencies

Prompts 1 through 5, especially the Finance service decomposition.

### Implementation requirements

- Inventory every route, verb, authorization policy, input contract, response contract, and error mapping.
- Create focused controllers for bills/supplier invoices, customer invoices, payments/allocations, reconciliation, reporting, and integrations/actions where routes justify them.
- Preserve exact public routes unless an explicit compatibility redirect/version is added and tested.
- Extract shared transport-only concerns such as company/user resolution and problem mapping into existing filters/base helpers/middleware, without hiding business decisions.
- Ensure all controllers use server-side company authorization and never trust route/header company IDs alone.
- Remove direct EF access and business policy decisions from controllers.
- Update OpenAPI grouping and endpoint tests.
- Remove the original controller only after all routes are accounted for.

### Constraints and preservation rules

- No route, status code, DTO, or authorization regression.
- Keep controllers thin; do not recreate a large base controller.
- Preserve idempotency, approvals, audit, and provider boundaries.
- UI clients must continue to work without coordinated breaking changes.

### Acceptance criteria

- Every original endpoint appears exactly once after extraction.
- Given existing authorized and unauthorized requests, responses remain contract-compatible.
- No new controller contains cross-resource business orchestration.
- OpenAPI has no duplicate or missing operations.

### Verification

- Run route-registration, authorization, tenant-context, Finance integration, and API contract tests.
- Compare an automated inventory of routes before and after.
- Build API and Web.
- Smoke-test the highest-risk bill, payment, reconciliation, and Fortnox endpoints.

### Definition of done

The original broad controller is removed, all endpoints are covered by focused thin controllers, and no forwarding-only controller, hidden authorization gap, or deferred route remains.

## Prompt 7: Consolidate business eligibility, status, and allowed-action policies

### Title and outcome

Replace duplicated status strings, threshold checks, and allowed-action branches with named domain/application policies. The outcome is one explainable source of truth for whether Finance, Support, Sales, and agent actions are allowed.

### Current context

The repository contains repeated comparisons against values such as `pending`, `approved`, `failed`, `paid`, `needs_review`, and repeated UI/API eligibility calculations. Existing examples such as paid supplier bill expense eligibility and support reply safety show the desired direction, but duplication remains across services and presenters.

### Dependencies

Prompts 1 through 6.

### Implementation requirements

- Search for repeated status comparisons, threshold rules, allowed-action lists, and user-facing eligibility messages.
- Prioritize high-impact rules: supplier bill posting, payment execution, invoice correction, refund execution, support reply approval/send, sales outbound automation, and agent autonomy.
- Create named policy request/decision types containing `Allowed`, stable reason codes, plain-English explanation, required approval, and relevant evidence.
- Make backend policies authoritative; UI should consume decisions rather than reimplement them.
- Replace magic strings with existing typed constants/storage converters where appropriate.
- Preserve persisted storage values and API wire values.
- Add table-driven tests for every decision branch and boundary value.
- Ensure policy decisions are included in audit/tool execution records for sensitive actions.

### Constraints and preservation rules

- Do not bury enforcement in prompts or UI.
- Do not create one generic policy engine for unrelated domains.
- Keep rules deterministic unless external evidence is explicitly required.
- Preserve tenant scope, approval chains, idempotency, and user-facing language.

### Acceptance criteria

- A high-impact action has one backend policy implementation and no contradictory UI copy.
- Every denial/review outcome has a stable reason code and actionable explanation.
- Existing allowed behavior remains allowed and existing prohibited behavior remains prohibited unless a documented bug is fixed.
- Boundary and status-transition tests cover all extracted policies.

### Verification

- Run policy, authorization, approval, posting, support safety, and outbound automation tests.
- Search for superseded duplicate branches and raw status literals.
- Build all affected projects and smoke-test UI allowed-action rendering.

### Definition of done

Selected high-risk eligibility rules have authoritative, tested, explainable policies with no duplicate enforcement or silent permissive fallback.

## Prompt 8: Harden external side effects with outbox, idempotency, retries, and reconciliation

### Title and outcome

Make email, Fortnox, notification, and agent-triggered external actions consistently durable and recoverable. The outcome is that retries cannot duplicate money movement or messages, and operators can see and reconcile failures.

### Current context

The repository has outbox and background execution infrastructure, provider adapters, approval-backed Fortnox writes, mailbox sending, and idempotency in several workflows. Usage is not necessarily uniform across all direct `HttpClient`, `SendReplyAsync`, provider `ExecuteAsync`, and notification paths.

### Dependencies

Prompts 1 through 7.

### Implementation requirements

- Inventory every external side effect and classify it as read, recommend, or execute.
- Identify request-thread side effects that require durable outbox/background execution.
- Define stable idempotency keys based on tenant, business action, target, and version—not random retry IDs.
- Persist attempt status, provider reference, retry count, next attempt, safe error summary, and reconciliation state.
- Apply bounded exponential retries only to retryable failures; distinguish validation/auth/permanent failures.
- Ensure approvals are checked immediately before execution and cannot be bypassed by retries.
- Add reconciliation for ambiguous provider outcomes and operator-visible retry/cancel/reconcile actions.
- Preserve encrypted credentials and avoid logging tokens or sensitive payloads.
- Add audit events for requested, approved, executing, succeeded, failed, retrying, cancelled, and reconciled transitions.

### Constraints and preservation rules

- No external execute action occurs solely because an LLM requested it.
- At-least-once workers must be safe under duplicate delivery and concurrent claims.
- Keep provider schemas inside adapters.
- Preserve current APIs where possible and use migrations for new durable state.
- Any schema change must support local and Docker SQL Server paths.

### Acceptance criteria

- Replaying the same outbox item or command does not duplicate provider effects.
- Transient failures retry and permanent failures stop with an actionable explanation.
- Ambiguous outcomes enter reconciliation rather than being marked successful or blindly retried.
- Tenant A can never claim, inspect, or reconcile Tenant B's execution.
- Approval expiration/rejection blocks queued execution.

### Verification

- Add integration tests using deterministic provider fakes for success, timeout-before-response, timeout-after-provider-acceptance, auth failure, rate limit, duplicate delivery, and concurrent workers.
- Run Fortnox, mailbox, outbox, approval, tenant-isolation, and audit tests.
- Verify migration upgrade and Docker/local SQL compatibility.
- Review logs to confirm secrets are redacted.

### Definition of done

All in-scope side effects use durable, idempotent, approval-aware execution with retries and reconciliation; no direct unsafe path or silent failure remains.

## Prompt 9: Modularize dependency injection and startup composition

### Title and outcome

Organize API, Infrastructure, and Web registrations by module and eliminate duplicate, missing, or ambiguous registrations. The outcome is startup composition that clearly shows enabled capabilities and fails fast on invalid configuration.

### Current context

`Infrastructure/DependencyInjection.cs` and Web/API `Program.cs` files contain many registrations. Web currently has a duplicate `ActionInsightApiClient` registration. Large registration lists make service lifetimes, hosted services, provider collections, and optional implementations difficult to audit.

### Dependencies

Prompts 1 through 8 so final service boundaries are known.

### Implementation requirements

- Inventory registrations, implementations, lifetimes, decorators, hosted services, and options.
- Remove accidental duplicates and detect duplicate singleton/scoped registrations where multiplicity is not intentional.
- Create capability extensions such as `AddFinanceModule`, `AddSupportModule`, `AddKnowledgeModule`, `AddSalesModule`, and `AddCompanyOperationsModule` following existing project boundaries.
- Keep provider collections intentionally registered as `IEnumerable<T>` and document that intent in code structure, not verbose comments.
- Add options validation on startup for required URLs, credentials when enabled, intervals, batch sizes, and feature flags.
- Ensure hosted services are registered once and resolve only valid scoped dependencies through scopes.
- Keep Web API clients capability-specific while sharing one configured HTTP transport.
- Add DI resolution tests for core production and development configurations.

### Constraints and preservation rules

- Do not introduce a service locator or global static container.
- Preserve correct lifetimes; DbContext-dependent services remain scoped.
- Do not silently disable a feature because configuration is invalid.
- No UI redesign is required. If user-facing startup/configuration UI changes become necessary, follow `ui-instructions.md` and `docs/design.md` including screenshot-first requirements where applicable.

### Acceptance criteria

- Every non-collection service has one intentional effective registration.
- API and Web start with valid development configuration.
- Enabled integrations with missing required configuration fail validation with safe actionable messages.
- Hosted services and provider registries have no duplicate execution caused by registration duplication.

### Verification

- Run DI graph/resolution tests for API, Web, and background workers.
- Search for duplicate service/client registrations.
- Build and start API/Web with representative valid and invalid configurations.
- Verify Support, Finance, Sales, Knowledge, and agent workflows resolve successfully.

### Definition of done

Startup composition is modular, deterministic, validated, and free of duplicate registrations, lifetime errors, hidden optional fallbacks, or deferred cleanup.

## Prompt 10: Standardize and split large Web API clients

### Title and outcome

Extract shared HTTP transport behavior and split oversized Web API clients by capability while preserving all routes, authentication headers, company context, offline behavior, and error semantics. The outcome is smaller clients that remain easy to mock and safe for multi-tenant calls.

### Current context

`FinanceApiClient.cs` is about 2,100 lines, and other Web clients repeat company headers, JSON serialization, query construction, response handling, development authentication, and offline-mode behavior. Web `Program.cs` creates a shared `HttpClient` and applies forwarded/development headers.

### Dependencies

Prompts 1 through 9.

### Implementation requirements

- Inventory common transport behavior separately from capability endpoint methods.
- Create a shared typed transport abstraction for request creation, company-context headers, serialization, cancellation, safe error parsing, correlation IDs, and not-found handling.
- Keep endpoint clients focused: Finance bills, invoices, payments, reconciliation, reporting, integrations; equivalent splits for other clients only where size justifies them.
- Preserve exact HTTP methods, route templates, query parameter names, request JSON, response JSON, and exception behavior.
- Prevent callers from issuing a request without the resolved company context when the endpoint is tenant-owned.
- Preserve authentication forwarding and development-auth behavior without exposing tokens.
- Keep offline mode explicit and deterministic; do not silently return mock production data.
- Update DI and consumers incrementally, removing old methods after all call sites migrate.
- Add contract tests using a recording HTTP handler.

### Constraints and preservation rules

- Follow `production-implementation.md` and architecture rules.
- This is not a UI redesign. If components or user-visible errors change, follow `ui-instructions.md` and `docs/design.md` and preserve plain English.
- Do not create one universal client with stringly typed endpoint calls.
- Do not change backend routes to simplify the client.
- Maintain tenant isolation and cancellation propagation.

### Acceptance criteria

- Existing Web workflows issue equivalent requests before and after refactoring.
- Shared transport logic has one implementation, while endpoint knowledge remains in capability clients.
- Missing company context is rejected before sending tenant-owned requests.
- API errors remain actionable and do not leak raw provider or credential details.
- The original oversized client is removed or reduced to a narrow compatibility facade with a documented removal path completed in this prompt.

### Verification

- Add recording-handler tests for headers, authentication forwarding, query encoding, JSON, cancellation, 404, validation errors, unauthorized responses, and server failures.
- Run Web component tests and affected Finance/Support/Sales workflow tests.
- Build Web and API.
- Smoke-test representative bills, payments, reports, support cases, and sales pages against a local API.

### Definition of done

Web API access is capability-oriented and uses one secure shared transport implementation, with no duplicated plumbing, broken route, silent offline fallback, compatibility TODO, or untested tenant header behavior.
