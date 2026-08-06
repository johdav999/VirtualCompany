# Virtual Company Architecture Rules

These rules describe the current refactored solution and are mandatory for backend, data, workflow, agent, integration, approval, AI orchestration, and cross-module changes. Existing production behavior wins over older planning documents.

## Solution Shape

- Build Virtual Company as a multi-tenant, web-first SaaS modular monolith.
- Use ASP.NET Core for the backend and Blazor Web App for the main user experience.
- Use .NET MAUI only for mobile companion flows such as alerts, approvals, chat, briefings, and task follow-up.
- Do not introduce microservices or a second application stack unless explicitly requested.
- Keep long-running workflows, scheduled work, inbox processing, retries, integration synchronization, and notifications in background workers.

The production project boundaries are:

- `VirtualCompany.Domain`: entities, value semantics, storage values, and deterministic domain rules.
- `VirtualCompany.Application`: use-case contracts, commands, queries, policies, and orchestration interfaces.
- `VirtualCompany.Persistence`: the shared EF Core DbContext, entity configurations, and seed resources. It does not reference infrastructure capability projects.
- `VirtualCompany.Persistence.Migrations`: the SQL Server migration history, model snapshot, and design-time DbContext factory. It references only `VirtualCompany.Persistence`.
- `VirtualCompany.Infrastructure.Platform`: tenant, security, authorization, audit, observability, background execution, and database-provider wiring.
- `VirtualCompany.Infrastructure.Mailbox`: mailbox provider adapters, connection lifecycle, polling, and mailbox-specific workers.
- `VirtualCompany.Infrastructure.Finance`: Finance implementations, provider adapters, policies, and workers.
- `VirtualCompany.Infrastructure.Sales`: Sales, Marketing, and customer-memory implementations, provider adapters, policies, and workers.
- `VirtualCompany.Infrastructure.Support`: Support implementations, grounding, delivery, policies, and workers.
- `VirtualCompany.Infrastructure.Operations`: company operations, documents, activity, communication, memory, and shared orchestration implementations.
- `VirtualCompany.Infrastructure`: a thin compatibility facade that composes the extracted infrastructure modules. It must not regain capability implementations or package ownership.
- `VirtualCompany.Api`: HTTP transport, authorization, request mapping, problem responses, and host startup.
- `VirtualCompany.Web`: Blazor UI, presenters/view models, and typed API clients.
- `VirtualCompany.Mobile`: mobile companion application.
- `VirtualCompany.Shared`: deliberately shared wire contracts and primitives only.

Dependencies must point inward. Domain must not depend on Infrastructure, API, or Web. Application must not depend on API or Web. Persistence and Platform must not depend on capability implementations. Capability projects communicate through Application contracts, Domain entities, or durable workflow/outbox messages; they must not reference sibling capability implementation projects. UI and transport layers must not own business decisions.

## Current Modules

Place functionality in the narrowest owning capability. Current business and platform modules include:

- Finance
- Sales
- Marketing
- Support
- Identity and Tenant Access
- Company Setup and Operations
- Agent Management and AI Orchestration
- Task and Workflow
- Approval
- Communication and Mailbox
- Knowledge and Memory
- Analytics and Cockpit
- Integration
- Audit and Explainability
- Background Execution

Infrastructure registrations are owned by each extracted project through `AddPlatformInfrastructure`, `AddOperationsInfrastructure`, `AddMailboxInfrastructure`, `AddFinanceInfrastructure`, `AddSalesInfrastructure`, and `AddSupportInfrastructure`. Add new services to the owning module registration. Do not re-register them in the root facade.

Root `DependencyInjection` composition only invokes module registration in dependency order. Cross-cutting implementation ownership belongs in Platform, not in the facade. The facade must not become the ownership location for capability-specific registrations.

Provider collections intentionally resolved through `IEnumerable<T>` may have multiple registrations. A non-collection service must have one intentional effective registration. DbContext-dependent services are scoped. Stateless deterministic policies may be singleton. Hosted services are registered once and create scopes for scoped work.

## Capability Organization

The refactor removed broad catch-all source files. Preserve that organization:

- Finance application contracts belong under `Application/Finance/Contracts` and are grouped by bills, invoices, payments, reporting, integrations, simulation, insights/policy, counterparties, or core concerns.
- Domain entities belong in one file per entity or closely related aggregate. Do not recreate `FinanceEntities.cs`, `SalesEntities.cs`, or `SupportEntities.cs`.
- EF configurations belong under `VirtualCompany.Persistence/Persistence/Configurations`, normally one configuration per entity. Register configurations through the DbContext assembly convention. Do not recreate `EntityConfigurations.cs`.
- Support services belong in capability-specific files such as cases, mailbox, triage, knowledge, drafting, safety, refunds, SLA, memory, analytics, and delivery. Do not recreate `SupportServices.cs`.
- Large Finance facades use capability-owned partial files where compatibility requires the existing interface or route facade. New behavior must go into the relevant capability file, not the base file.
- Internal Finance endpoints belong in the matching `InternalFinanceController.<Capability>.cs` file. Keep controller actions transport-only.
- Finance Web API endpoints belong in the matching `FinanceApiClient.<Capability>.cs` file. Shared HTTP behavior belongs in `CompanyApiTransport`.

Do not create a new catch-all file, generic manager, or utility class merely to avoid choosing an owner. Add an abstraction only when it removes meaningful duplication or defines a real boundary.

## Controllers and Application Services

- Controllers resolve authorized company/user context, validate transport input, invoke application services, and map results to HTTP responses.
- Do not place EF queries, accounting rules, workflow decisions, provider logic, or cross-capability orchestration in controllers.
- Preserve established routes, verbs, authorization policies, request/response contracts, and problem mappings unless a breaking change is explicitly approved.
- A route or header company ID is context input, not proof of access. Server-side company authorization is mandatory.
- Cross-capability coordination belongs in an application service, workflow handler, or background orchestration service.
- Read services must not perform state-changing operations. Commands must make side effects and transactional boundaries explicit.

## Multi-Tenancy and Authorization

- Every tenant-owned entity implements or follows the established company ownership convention and includes `CompanyId` or equivalent scope.
- Every tenant-owned query and command is company-scoped.
- Use global query filters where established, but do not treat them as the only authorization boundary.
- `IgnoreQueryFilters` is allowed only for deliberate system/admin/background operations that explicitly reapply company scope or intentionally process companies in bounded batches.
- Never return, mutate, enqueue, audit, or reconcile one company's data under another company's context.
- Never rely only on frontend visibility or disabled controls for authorization.
- Tests for new tenant-owned behavior must cover cross-company reads and writes.

## Database and EF Core

SQL Server is the production provider. Local SQL Server and Docker SQL Server must remain equivalent deployment paths.

- EF Core migrations are the only schema authority.
- Do not add startup `CREATE TABLE`, `ALTER TABLE`, `CREATE INDEX`, ad hoc repair DDL, or `EnsureCreated` as a production migration substitute.
- `DatabaseInitializationService` owns startup migration validation/application and post-schema seed orchestration.
- `DatabaseInitialization:ApplyMigrationsOnStartup` controls whether pending migrations are applied. When disabled, startup must fail clearly on incompatible pending migrations rather than silently changing schema.
- Schema changes require a migration and an updated model snapshot.
- Create and inspect migrations through `VirtualCompany.Persistence.Migrations`, using `VirtualCompany.Api` as the startup project. Runtime SQL Server wiring must explicitly select the `VirtualCompany.Persistence.Migrations` assembly.
- Structural file movement must produce no pending model change.
- Preserve existing migration IDs and upgrade paths. Never drop/recreate a production database as a migration workaround.
- Keep `restore-local-sql-db.ps1` and `restore-virtualcompany-db.ps1` compatible with the same backup and migration history.
- Any local SQL Server-specific setup change must include or document the equivalent Docker SQL Server path.
- Do not add PostgreSQL-specific types, SQL, or migration behavior to the active SQL Server implementation.
- Provider-specific SQL must be isolated and have an explicit SQL Server implementation. SQLite is a test provider only where the model and behavior are verified as compatible.
- Store core queryable business state in relational columns. Use JSON for flexible metadata, settings, thresholds, snapshots, or provider payloads, not as the sole store for important queryable state.
- Preserve table names, column names, indexes, relationships, conversions, and query filters during structural refactoring.

## Commands, Queries, and Policies

Use CQRS-lite boundaries:

- Commands perform state changes.
- Queries produce read models for pages, dashboards, agents, and reports.
- Optimized read projections remain separate from transactional write logic.
- Do not introduce event sourcing unless explicitly requested.

High-impact eligibility and allowed-action decisions must be authoritative in backend policy/application code. UI components consume policy decisions and must not reimplement them.

A policy decision should expose, as appropriate:

- whether the action is allowed
- a stable reason code
- a plain-English explanation
- whether approval or review is required
- the evidence used for the decision

Use domain-specific policies rather than one generic policy engine for unrelated rules. Preserve persisted status values and API wire values. Avoid raw status strings when an owning enum, typed value, or constants class exists.

Paid supplier bill expense posting uses `PaidSupplierBillExpensePostingEligibility` as its authoritative eligibility/account resolution policy. Do not duplicate that decision in Finance projections, controllers, or Blazor pages.

## Workflow and Approval

- Workflow is a first-class subsystem, not ad hoc UI state.
- Model manual, scheduled, domain-event, integration/webhook, retry, escalation, blocked, and exception transitions explicitly.
- Workflow state must explain its trigger, current step/state, related task or approval, and failure/retry state.
- Human approval requirements are enforced in backend policy/application logic, never only in prompts or UI.
- Approval-backed execution must recheck approval immediately before the external action.
- Support threshold checks, required roles/users, chains, rejection, cancellation, and expiration where the business action requires them.

## External Side Effects and Outbox

- Reliable external execute actions use durable outbox/background execution.
- Do not perform money movement, customer email, provider writes, or similarly important external effects directly in request handlers when retry or ambiguity matters.
- Stable idempotency keys derive from company, business action, target, and version. Do not use a random retry ID as the business idempotency key.
- Workers must be safe under duplicate delivery and concurrent claims.
- Retry only retryable failures with bounded backoff. Validation, authorization, and permanent provider failures must stop with an actionable operator-visible reason.
- Ambiguous provider outcomes enter reconciliation; do not mark them successful or blindly retry.
- Persist attempt state, safe failure summaries, provider references, retry state, and reconciliation state where applicable.
- Never log credentials, access tokens, refresh tokens, or sensitive provider payloads.

Approved Support reply sending is queued as `support.reply.delivery_requested` and dispatched by `SupportReplyDeliveryDispatcher`. Request handlers may create the durable request, but must not bypass the dispatcher with a direct mailbox send path.

## Agent and AI Orchestration

- Use one shared AI orchestration subsystem. Named agents are configuration, memory, tools, permissions, role/persona, objectives, KPIs, autonomy, and escalation rules.
- Do not create a separate orchestration stack per Finance, Sales, or Support agent.
- Feature modules call approved orchestration interfaces; they do not call LLM providers directly.
- Agent tools are explicit, structured, company-scoped, permissioned, and classified as read, recommend, or execute.
- Guardrails run before tool execution.
- Sensitive actions require policy checks and approval regardless of what an LLM requested.
- New agents default to conservative autonomy. Higher autonomy requires explicit configuration.

The standard orchestration sequence is:

1. Resolve tenant and user context.
2. Resolve the addressed agent or coordinator.
3. Load agent configuration, scopes, and permissions.
4. Retrieve company-scoped context and memory.
5. Build structured runtime instructions.
6. Select permitted tools.
7. Evaluate policy and approval requirements.
8. Execute only allowed tools.
9. Return a structured result.
10. Persist rationale summary, tool execution, and audit evidence where applicable.

Prompt construction includes only relevant company context, role brief, task/workflow state, policies, memory, records, and tool schemas. Do not expose hidden system instructions or retrieve outside the agent's scope.

## Support Grounding and Safety

- Support knowledge retrieval stays behind `ICompanyKnowledgeSearchService` and the Support knowledge/context abstractions.
- Only accessible, processed, and indexed company sources may ground a reply.
- Preserve source document/chunk references and citations.
- Missing or insufficient grounding creates a review requirement and knowledge gap; it must not silently become an authoritative answer.
- Deterministic reply safety remains backend policy logic.
- Automatic mailbox routing and agent execution must be idempotent per inbound message.
- Repeated polling must not duplicate cases, messages, executions, drafts, or outbound deliveries.
- Refund and outbound communication actions remain behind approval/workflow/provider boundaries.

## Integration Boundaries

- Integrations are adapters, not systems of record.
- Keep provider authentication, endpoints, payloads, and error translation inside provider adapters.
- Normalize provider data into internal contracts before it reaches domain/application behavior.
- Do not leak provider schemas into core entities.
- Use background synchronization or webhook ingestion where appropriate.
- Provider errors exposed to users must be safe and actionable.

## Web API Clients

- Register Web clients through `AddVirtualCompanyApiClients`.
- Tenant-owned requests use `ICompanyApiTransport`/`CompanyApiTransport` for company context and correlation headers.
- `Guid.Empty` is not a valid company context and must be rejected before sending.
- Endpoint knowledge stays in typed capability clients; do not create a universal stringly typed API client.
- Preserve authentication forwarding, cancellation, route templates, query names, JSON contracts, not-found behavior, and safe error mapping.
- Offline mode must be explicit and deterministic. It must not silently substitute mock production data.
- Shared transport behavior has one implementation. Do not duplicate company-header or correlation-header construction in endpoint methods.

## Knowledge, Memory, Communication, and Analytics

- Documents and chunks are separate persisted concepts with ingestion/indexing state and access scope.
- Knowledge retrieval and memory are always company-scoped and, when required, agent-scoped.
- Store bounded summaries and useful observations rather than unlimited raw conversation history.
- Chat messages are not the system of record for tasks, workflows, approvals, or finance state.
- Dashboard and cockpit UI use query/read models instead of assembling complex transactional data in components.
- User-facing explanations are plain English and do not expose enum names, storage tokens, provider internals, or raw policy implementation details.

## Audit and Observability

Business audit history and technical logging are separate concerns.

Persist business audit evidence for agent actions, tool executions, approvals, important workflow transitions, provider changes, and sensitive recommendations/actions. Capture the actor, action, target, outcome, rationale summary, data sources, correlation ID, timestamp, and useful before/after evidence.

Technical logs must support diagnosis without exposing secrets. Background failures must be recoverable and visible to operators; do not swallow exceptions or leave intermediate states unexplained.

## Test Architecture

Tests are partitioned by ownership and dependency:

- `VirtualCompany.Api.Tests`: backend unit and API/integration tests that require API/Infrastructure composition.
- `VirtualCompany.Finance.Tests`: focused Finance policy and module tests.
- `VirtualCompany.SupportGrounding.Tests`: Support grounding/safety tests without unrelated Finance/Web dependencies.
- `VirtualCompany.SalesSource.Tests`: focused Sales source/provider tests.
- `VirtualCompany.Web.Tests`: Blazor components, presentation, routes, and Web client transport tests.
- `VirtualCompany.Web.Contract.Tests`: Web/API contract integration tests.

Rules:

- Add a test to the narrowest project that can execute it.
- Pure domain or policy tests must not gain Web/API references.
- Non-Web test projects must not require Web output binaries or a running Web process.
- Tests do not depend on an already running API, Docker, SQL Server, or external provider unless explicitly categorized as external integration tests.
- Use SQLite only for provider-compatible behavior. SQL Server-specific migrations, metadata, SQL, and concurrency need SQL Server validation.
- Shared databases, ports, environment variables, clocks, or static state require explicit collection/fixture isolation.
- Do not delete, skip, or weaken a valid test to make a refactor pass. Replace obsolete assertions with coverage of the current authoritative behavior.
- New sensitive behavior requires proportionate tenant-isolation, authorization, policy, idempotency, and failure-path tests.

## Completion Checklist

Before completing an architecture-sensitive change, verify:

- Correct capability and project ownership.
- No new catch-all file or duplicate implementation.
- Tenant isolation and server-side authorization.
- Commands and queries remain separated.
- Backend policy is the source of truth for allowed actions.
- Approval is enforced before sensitive execution.
- Durable outbox/background execution is used for reliable side effects.
- Idempotency, retry classification, reconciliation, and safe failures are covered.
- Audit evidence and technical observability are appropriate.
- No direct LLM call bypasses shared orchestration.
- No provider schema leaks into domain models.
- Schema changes include an EF migration and preserve local/Docker SQL Server compatibility.
- `Program.cs` contains no schema DDL.
- `dotnet ef migrations has-pending-model-changes` is clean after structural changes.
- Tests are placed in and run from the narrowest relevant project.
- API and Web build after cross-layer changes.
