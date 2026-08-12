# Virtual Company architecture assessment

**Assessment date:** 2026-08-12  
**Assessed baseline:** the current `main` working tree, including its substantial uncommitted marketing and company-orchestration changes  
**Method:** implementation and configuration inspection, representative scenario tracing, dependency and boundary analysis, and focused mechanical validation. Architecture documentation was treated as intent, not as proof of implementation.

## Executive conclusion

Virtual Company has a sound high-level direction: a modular monolith with inward project dependencies, explicit tenant context, extensive tenant query filters, mutation guards, durable outbox processing, approval concepts, audit records, and database-backed worker leases. Those choices fit a multi-tenant business system whose agents can initiate financial, messaging, support, and marketing actions.

The current implementation is **not production-safe as assessed**. Two release-blocking security and control failures override the otherwise good architecture:

1. The API registers a development header authentication scheme as its default in every environment. In a non-development environment, a caller who supplies identity headers can still receive an authenticated principal. This permits identity impersonation when a valid subject is known or discovered.
2. Support reply approval is not protected by a role-specific policy, and the HTTP send contract accepts a caller-controlled `Autonomous` flag. An ordinary company member can use that flag to bypass the approved-state requirement for low-risk replies and cause external customer communication under an agent identity.

There are also high risks around ambiguous external side effects, marketing audit persistence, multi-instance worker coordination, object-storage durability, and migration drift detection. These should be resolved before production scaling or enabling autonomous external actions.

The project/module boundaries are stronger than the runtime and operational boundaries. Build-time tests enforce project references and hosted-service registration, but currently do not protect the most important production properties: valid production authentication, authorization of approvals, controlled query-filter bypass, audit durability, provider-success/database-failure behavior, migration-model equivalence, or stale worker recovery.

## Scope and limitations

- The repository and deployed configuration were treated as authoritative. `docs/architecture-rules.md` was used to compare implementation with intended constraints.
- The assessment covers API, Web and Mobile hosts, domain/application/persistence layers, capability modules, authentication and tenancy, major background processing, external messaging and marketing side effects, migrations, local deployment scripts, storage, health checks, audit, metrics, and tests.
- The working tree is materially dirty. Some findings, particularly the marketing dispatcher and company operating-cycle workers, concern uncommitted code rather than the last committed release.
- No production cloud configuration, CI pipeline, infrastructure-as-code, backup schedule, restore evidence, SLOs, traffic profile, threat model, or cost data was present. Operational conclusions therefore describe repository evidence, not an independently verified production environment.
- A full API test run did not finish within four minutes. The focused architecture/security/outbox suite did finish and is reported below.

## Ranked quality goals

The ranking below reflects the business domain and implemented capabilities rather than a generic architecture ideal.

| Rank | Quality attribute | Why it matters here | Current status |
|---:|---|---|---|
| 1 | Security and tenant isolation | The system holds multiple companies' financial, mailbox, support, and knowledge data. Identity or tenant leakage is existential. | **Release-blocking** because production identity can be spoofed. Tenant data controls are otherwise substantial. |
| 2 | Side-effect correctness and approval safety | Agents can send customer messages, publish marketing content, and initiate business operations. A wrong or duplicate effect is often worse than temporary unavailability. | **Release-blocking** for support approval bypass; **weak** at provider-success/database-failure boundaries. |
| 3 | Auditability and explainability | Autonomous and approved actions need attributable, durable evidence. | **Mixed**; audit is a separate domain concept, but at least one marketing success/reconciliation path does not save its audit record. |
| 4 | Maintainability and changeability | The product spans many capabilities and has rapidly growing orchestration. Boundaries must prevent a central monolith from becoming a coordination bottleneck. | **Mixed**; project boundaries are strong, but the DbContext, some controllers, and the central outbox dispatcher are large hotspots. |
| 5 | Availability and recoverability | Background work and external integrations must survive process, network, and database failures without losing or duplicating work. | **Weak to mixed**; durable queues and restore scripts exist, but app deployment, storage recovery, stale dispatch recovery, and multi-instance coordination are incomplete. |
| 6 | Performance and scalability | Tenant growth increases query-filtered data, startup work, hosted workers, AI calls, and external traffic. | **Mixed**; asynchronous processing helps, but a single API hosts many polling workers and performs tenant-wide startup backfills. |
| 7 | Cost control | AI/tool calls and polling workers can create material variable cost. | **Weakly evidenced**; some rate-limit policies exist, but costly chat/task surfaces are not consistently protected and no operational cost telemetry was found. |

## Implemented system map

### Runtime components and responsibility boundaries

| Component | Implemented responsibility | Important dependencies and observations |
|---|---|---|
| `VirtualCompany.Api` | HTTP controllers, SignalR, middleware, authentication/authorization, health/readiness, startup initialization, composition root | References Application, the thin Infrastructure facade, migrations, and Shared. It also hosts approximately thirty background services, so API availability and worker availability share one failure/scaling unit. |
| `VirtualCompany.Web` | Blazor web host and client UI | References Shared rather than implementation projects, which preserves the intended host boundary. |
| `VirtualCompany.Mobile` | MAUI companion application | Separate client host; current Release solution build is not reproducible from the restored assets for Mac Catalyst/Android AOT. |
| `VirtualCompany.Domain` | Entities, value concepts, domain-level contracts | Has no outward project dependency. |
| `VirtualCompany.Application` | Use-case interfaces and contracts | Depends inward on Domain and Shared. |
| `VirtualCompany.Persistence` | Shared EF Core `VirtualCompanyDbContext`, mappings and data access infrastructure | Depends on Application and Domain. One shared context provides cross-capability transactions but is a large coupling and migration hotspot. |
| `VirtualCompany.Persistence.Migrations` | SQL Server migration assembly | Depends only on Persistence. Startup validates or applies migrations according to configuration. |
| `VirtualCompany.Infrastructure.Platform` | Cross-cutting tenancy, identity, audit, outbox, storage, AI/tool execution, health and operational infrastructure | Used by each capability module. It is the main platform boundary and also carries much of the system's runtime complexity. |
| Capability modules | Mailbox, Finance, Sales, Support and Operations implementations | Each depends on Application, Domain, Platform and Persistence. No implementation-level sibling references were found; architecture tests enforce this. |
| `VirtualCompany.Infrastructure` | Composition facade | References and registers all capability modules. It is thin rather than a second implementation layer. |
| SQL Server | System of record for tenants, business state, audit, queues, executions and leases | A single database and context support atomic business/outbox commits. Docker and local SQL Server restore scripts exist. |
| Local object storage | Default filesystem storage under API `App_Data` | Not a shared or independently backed-up production topology. No application container/volume topology includes it. |
| External providers | Mailbox providers, SMTP, marketing channels, AI/model services and optional Redis | Called mainly through background processing. Provider and database commits cannot be atomic, so reconciliation quality is decisive. |

### Dependency direction

The implemented project references follow the documented modular-monolith direction: Domain is innermost; Application depends on Domain/Shared; Persistence depends inward; Platform depends on Application/Domain/Persistence; capabilities depend on Platform and inward projects; the API composes through the Infrastructure facade. Repository architecture tests prohibit capability-to-capability references and namespace imports. This is one of the strongest parts of the implementation.

The boundary becomes less clear inside the API and shared persistence layer. `InternalFinanceController` directly injects `VirtualCompanyDbContext`, performs EF queries and mappings, and has a very large dependency surface. The shared DbContext exposes 263 `DbSet` properties and roughly 209 tenant query-filter registrations. The central company outbox processor also dispatches topics across many capabilities. These are maintainability risks within an otherwise valid modular monolith, not arguments for immediate microservice decomposition.

### Data ownership and transactions

- SQL Server is the authoritative store. Business records, outbox records and audit records can participate in one EF transaction.
- Many tenant-owned entities implement `ICompanyOwnedEntity`. Query filters use the active company context, while `SaveChanges` validates tracked tenant mutations and rejects a company mismatch when a company context is present.
- The mutation guard intentionally does nothing when no company context exists. Global background workers commonly operate in that mode, use `IgnoreQueryFilters`, scan across companies, and then establish or emulate tenant scope. This system path is powerful and insufficiently constrained mechanically.
- `IgnoreQueryFilters` occurs more than 1,200 times across roughly 169 source files. Many uses are valid worker or administrative scans, but the volume makes review-only enforcement unreliable.
- The support reply request path saves business state, audit and outbox work together. This is a good transaction boundary.
- External provider effects remain a distributed consistency boundary. Several paths have stable keys and reconciliation concepts, but they do not all close the provider-success/database-failure gap.

### Authentication and authorization boundaries

The API uses a persisted company membership resolver and authorization policies such as `CompanyMember` and company-manager policies. Company context middleware rejects conflicting route, query and header company identifiers. Most use cases re-scope resource queries by company ID, and tests cover many cross-tenant attempts.

However, the default authentication scheme is `DevHeader` in all environments. `AuthInfrastructure` will build a principal from `X-Dev-Auth-Subject` and related caller-supplied headers outside Development whenever a subject or email is present. `RequireAuthenticatedUser` consequently accepts an unverified identity. This defeats the outer security boundary before otherwise useful membership and tenant checks run.

Approval authorization is inconsistent. Marketing controller side-effect endpoints require a manager policy and the dispatcher rechecks approval close to publication. Support approve/send endpoints inherit only the company-member policy, while send accepts a public `Autonomous` flag that changes whether approval is required.

### Deployment, recovery and operational ownership

- `docker-compose.yml` defines SQL Server only. No API/Web container build, deployment manifest, rolling update policy, worker separation, production secrets binding, or infrastructure-as-code was found.
- Local and Docker SQL Server backup/restore scripts provide a useful development recovery path.
- No evidence was found for scheduled production backups, restore drills, RPO/RTO targets, application rollback, database forward-fix policy, or coordinated blob/database recovery.
- The API checks pending migrations at startup and can apply them in Development. It also performs tenant-wide seed/backfill work before accepting traffic, increasing startup time and deployment risk as tenant count grows.
- EF's pending-model-change warning is suppressed in platform registration. Existing tests verify that a migration assembly and snapshot exist, not that the current model matches the snapshot.
- The README describes PostgreSQL/pgvector while current configuration, compose, migrations and rules use SQL Server. This is operationally hazardous documentation drift.
- Worker ownership is concentrated in the API host. Coordination can fall back to process-local memory when Redis is absent, while readiness reports unconfigured Redis as healthy. Multiple API replicas would therefore risk duplicate scheduler/lock behavior unless every worker also uses a database claim correctly.

### Observability

Positive implementation evidence includes correlation IDs, centralized safe exception handling, separate business audit records, database/background execution records, health/readiness endpoints, structured logging calls, and `System.Diagnostics.Metrics` instruments in some flows.

No OpenTelemetry, Prometheus, Application Insights, OTLP, or other meter/trace export configuration was found. An in-process `Meter` without a configured exporter is not operator-visible telemetry. Logging uses console/debug configuration in repository settings, and no production aggregation/alert policy is defined. Optional Redis and object storage report healthy when disabled, so readiness does not express whether a selected production topology is safe. Named rate-limit policies exist, but costly direct-chat/task execution surfaces do not consistently apply them.

## Representative scenario traces

### 1. Normal workflow: approved support reply

1. `SupportController` requires company membership and company context.
2. The application generates and safety-checks a reply draft.
3. Approval updates the draft and writes audit data.
4. Send rechecks safety and, for a normal non-autonomous request, requires `Approved` status.
5. A stable key of the form `support:{company}:{case}:{draft}` is used to enqueue `support.reply.delivery_requested` in the same database save as local state and audit.
6. The outbox processor claims work using a database lease, establishes company scope, dispatches through the mailbox provider, and records success or classifies failure.
7. Ambiguous SMTP delivery is exposed as requiring reconciliation.

This is a good core shape: authorization, close approval checking, atomic durable intent, idempotent queue identity, database claiming, bounded retry and visible terminal state. It is weakened by the public autonomous bypass and by the post-provider database commit gap described below.

### 2. Unauthorized and cross-tenant attempt

For an honest authenticated identity, conflicting company IDs in route/query/header are rejected; a requested company without active persisted membership fails authorization; resource services generally include company predicates; and cross-company tracked mutations fail when company context is active. This is defense in depth.

Two paths undermine it:

- A caller can construct an authenticated principal through production `X-Dev-Auth-*` headers. If a member subject is known, the membership check then authorizes the impersonated identity.
- A legitimate ordinary member can approve/send support replies and can set `Autonomous=true` to skip approved status for qualifying content. The audit actor can consequently appear to be an agent rather than the initiating human.

### 3. Failed external integration

Support delivery exceptions are classified; retryable failures remain durable, terminal failures become visible background execution errors, and ambiguous SMTP outcomes require reconciliation. Marketing dispatch classifies authentication errors, rate limits, server errors and ambiguous network outcomes and exposes a reconciliation operation.

The main unresolved issue occurs after the provider has accepted an effect. In support, if the send succeeds but the following database save fails, `SentUtc` remains unset and the durable message can later send again. Provider headers such as a custom Gmail idempotency header or Graph client request ID do not by themselves prove provider-enforced exactly-once behavior. In marketing, a claim is saved, publication occurs, and then local success is saved; a failure at that last step leaves the action in `dispatching`, outside the queued/retry candidate query, with no demonstrated stale-claim recovery.

### 4. Retry or duplicate request

The company outbox uses stable idempotency keys, a unique database constraint, claim tokens and sent-state checks. That prevents most duplicate enqueue and concurrent-consumer cases. Application-side prechecking is not the actual concurrency guarantee; the unique constraint is.

Repeat delivery remains unsafe when the external provider succeeded but the database did not record success. The correct target is effectively-once business behavior through a durable attempt state plus provider reconciliation, not a claim of distributed exactly-once delivery.

### 5. Partial database or network failure

Before an external call, the support business/outbox transaction fails atomically, so no effect is attempted without durable intent. During or after an external call, network ambiguity is partly modeled, but a database failure after confirmed provider success is not consistently treated as ambiguous. Marketing also writes some audit entries after its last `SaveChanges`, so the state transition can commit without its audit evidence.

### 6. Deployment, rollback and recovery

Startup detects pending migrations and Development can apply them. Local and Docker restore scripts can restore SQL Server backups. However, no repository evidence defines production application deployment or rollback, database rollback/forward-fix, coordinated local-object restoration, backup scheduling, recovery testing, or RPO/RTO. Tenant-wide startup backfills lengthen the critical deployment path. Local object storage is outside database restore and is not mounted or replicated by the supplied compose topology.

## Risk findings

### A-01 — Caller-supplied development headers authenticate users in production

**Severity:** Critical / release blocker  
**Likelihood:** High if the API is externally reachable  
**Impact:** Critical

**Evidence:** `Infrastructure.Operations/Companies/OperationsModuleRegistration.cs` registers `DevHeader` as the default authentication scheme without an environment guard. `Infrastructure.Platform/Auth/AuthInfrastructure.cs` creates claims from `X-Dev-Auth-Subject`, email, name and role headers; outside Development it returns no result only when both subject and email are absent. `CompanyAuthorizationServiceCollectionExtensions.cs` uses `RequireAuthenticatedUser` outside Development, which accepts the header-created principal.

**Consequence:** Attackers can self-assert or impersonate identity. Knowing a tenant member's subject can turn this into tenant access; other authenticated onboarding or identity flows may expand the damage. Every downstream authorization decision is based on a compromised principal.

**Target state:** Production uses a cryptographically verified OIDC/JWT or secure server cookie scheme with issuer, audience, signature, expiry and appropriate MFA/session controls. Header authentication is compiled or registered only for explicit Development/Testing environments.

**Smallest safe remediation:** Guard the handler registration and default scheme by environment, configure a real production scheme, and fail startup outside approved local/test environments when no verified scheme is configured. Do not merely ignore headers later in middleware.

**Verification:** Start the API in Production configuration; spoofed `X-Dev-Auth-*` requests must return 401. Add issuer/audience/signature/expiry negative tests and a positive authenticated membership test.

### A-02 — Support approval and autonomous execution can be bypassed by an ordinary member

**Severity:** Critical / release blocker  
**Likelihood:** High for any tenant member with API access  
**Impact:** High to critical

**Evidence:** `Api/Controllers/SupportController.cs` applies the company-member policy at class level, while approve/send actions have no stronger permission. `Application/Support/SupportContracts.cs` exposes `Autonomous` on the public send request. `Infrastructure.Support/Support/SupportReplyDraftService.cs` trusts that value and skips the approved-state requirement when it is true and risk/confidence checks pass. The dispatcher also trusts the durable autonomous value.

**Consequence:** A regular user can cause customer-facing external communication without the required human approval and can make the event appear agent-initiated. This breaks authorization, approval integrity and audit attribution.

**Target state:** Approval and external send are distinct server permissions. Autonomous execution is derived only from a trusted internal execution identity and tenant policy, never from a public request field. The dispatcher rechecks current authorization/policy immediately before delivery.

**Smallest safe remediation:** Remove or ignore `Autonomous` from the public contract, require explicit support-approve/support-send manager permissions, and introduce a separate internal command path with a non-user-forgeable actor type.

**Verification:** Integration tests prove an employee/member receives 403 for approve/send, cannot self-label a request autonomous, an authorized manager can approve, and revoked/expired approval prevents dispatch.

### A-03 — Provider success followed by database failure can duplicate or strand external effects

**Severity:** High  
**Likelihood:** Medium  
**Impact:** High

**Evidence:** `SupportReplyDeliveryDispatcher` calls the provider and then saves `SentUtc` and audit. A failure of that save leaves the outbox eligible for another delivery. `MarketingChannelDispatch` saves a `dispatching` claim, publishes externally, and then saves completion. Candidate selection reads queued/retry-scheduled states, with no demonstrated stale-dispatch recovery. Provider request/message identifiers are helpful evidence but do not universally provide provider-enforced idempotency.

**Consequence:** Customers can receive duplicate support email, or marketing content can be published while local state remains stuck and unverifiable. Blind retry and permanent stranding are both possible depending on the flow.

**Target state:** Every external effect has a durable attempt/lease, deterministic provider identity where supported, an explicit ambiguous state, stale-lease recovery, and provider-specific reconciliation before retry after any possibly successful call.

**Smallest safe remediation:** Add an attempt record before the call; on post-call persistence failure or stale dispatch, transition/recover to reconciliation-required rather than blind retry. For email, reconcile by deterministic message ID/provider sent data where possible.

**Verification:** Fault-injection integration tests simulate provider success followed by database commit failure, worker crash after publish, timeout before response and duplicate worker pickup. Assert one business effect or a visible reconciliation state, never an automatic second effect.

### A-04 — Marketing dispatch and reconciliation audit records are not durably saved

**Severity:** High  
**Likelihood:** High on the current uncommitted implementation  
**Impact:** High

**Evidence:** `Infrastructure.Operations/Marketing/MarketingChannelDispatch.cs` saves the action state before calling `audit.WriteAsync`. `AuditEventWriter.WriteAsync` adds the entity to the DbContext but does not call `SaveChanges`. The reconciliation method similarly saves state, then adds audit and returns. A later loop iteration may accidentally flush one record, but the last and reconciliation records are not guaranteed to persist.

**Consequence:** External publication or operator reconciliation can occur without its required audit evidence. Behavior depends on later unit-of-work activity, which is nondeterministic and misleading.

**Target state:** State transition and audit record commit atomically in one explicit unit of work, after the external outcome has been classified.

**Smallest safe remediation:** Add audit before the same final `SaveChanges` and make the unit-of-work boundary explicit. Avoid making the audit writer save independently if that would break surrounding transaction composition.

**Verification:** Integration tests dispatch and reconcile one action, create a new DbContext, and assert the exact audit record and state are both present. Inject a save failure and assert neither local transition is partially committed.

### A-05 — Multi-instance execution safety is configuration-dependent but not enforced

**Severity:** High for horizontal deployment; Medium for a guaranteed singleton API  
**Likelihood:** Medium  
**Impact:** High

**Evidence:** The API hosts approximately thirty pollers/schedulers. Some use database claims, while coordination services can fall back to in-memory operation when Redis is unconfigured. Readiness treats unconfigured Redis as healthy. No deployment rule constrains the API to one replica or assigns singleton workers.

**Consequence:** Horizontal API scaling can duplicate scheduling, scans, agent cycles or non-database-protected work. Scaling web traffic also scales every poller, increasing database load and cost.

**Target state:** Separate worker and API scaling units, or prove every worker uses database/distributed leases. Production topology must require the selected coordination dependency and expose it in readiness.

**Smallest safe remediation:** Document and enforce a singleton host until all workers are audited; fail production readiness/startup if multi-instance mode lacks Redis/database coordination; inventory each hosted service's claim mechanism.

**Verification:** Run two API/worker instances against one database and assert each scheduled business action is created/executed once under crash and lease-expiry tests.

### A-06 — Default object storage is neither shared nor covered by the supplied recovery topology

**Severity:** High when documents/creative assets are production data  
**Likelihood:** Medium  
**Impact:** High

**Evidence:** default storage uses API-local `App_Data/object-storage`. The supplied compose file defines only SQL Server and no shared application volume or object-store service. Database restore scripts cannot restore these blobs. Readiness reports object storage as healthy when the optional integration is disabled.

**Consequence:** Host replacement, horizontal scaling or database-only recovery can lose documents or return inconsistent availability depending on which replica receives a request.

**Target state:** Production-grade shared object storage with encryption, tenant-aware keys, lifecycle/retention, versioning where appropriate, backup/replication, and recovery coordinated with database references.

**Smallest safe remediation:** Make local storage Development-only and fail non-development startup unless a durable provider is configured. Define a blob backup/restore procedure and consistency check.

**Verification:** Store and retrieve from two app instances, restore database plus blobs to a clean environment, verify tenant isolation and missing/orphan reconciliation.

### A-07 — Migration drift can be hidden

**Severity:** High release risk  
**Likelihood:** Medium  
**Impact:** High

**Evidence:** `Infrastructure.Platform/PlatformModuleRegistration.cs` suppresses EF's pending-model-changes warning. Existing architecture tests check migration assembly/snapshot presence but not equality between the compiled model and migration snapshot. The current working tree contains many migration/model changes.

**Consequence:** A build can ship with model changes that lack a migration, causing startup or runtime schema failures and making rollback/recovery less predictable.

**Target state:** CI fails on pending model changes and validates migrations against a clean SQL Server instance and a restored representative backup.

**Smallest safe remediation:** Stop globally suppressing the warning in validation/production paths and add EF's pending-model-change check to CI/test tooling.

**Verification:** A deliberate unmigrated property change must fail the validation job; applying all migrations to empty and restored databases must pass startup and smoke tests.

### A-08 — System-wide query-filter bypass is too broad for review-only control

**Severity:** Medium, potentially High when a worker contains a missed tenant predicate  
**Likelihood:** Medium  
**Impact:** Critical for an actual leak or mutation

**Evidence:** More than 1,200 `IgnoreQueryFilters` calls appear across roughly 169 files. Many are intentional global scans. The mutation guard returns when no company context exists, which is also how global workers operate. Current architecture tests do not inventory or constrain filter bypass.

**Consequence:** A missed re-scope can read or mutate another tenant's data without query-filter or mutation-guard protection. The large number of call sites makes assurance expensive.

**Target state:** Filter bypass is centralized in an explicit system-scope abstraction that batches by company and establishes tenant context before processing. Exceptional bypasses require an allowlisted justification and tests.

**Smallest safe remediation:** Add a static architecture test that rejects new direct `IgnoreQueryFilters` calls outside approved infrastructure wrappers, then migrate the highest-risk mutation workers first.

**Verification:** Multi-tenant tests seed colliding IDs/data and run every global worker; assert only the selected tenant is changed and audit contains the tenant and system actor.

### A-09 — Deployment and recovery are development-oriented rather than production-defined

**Severity:** Medium to High depending on current production operations  
**Likelihood:** High that operator behavior is inconsistent  
**Impact:** High

**Evidence:** SQL Server compose and restore scripts exist, but no API/Web images, CI workflow, deployment manifests, rollout/rollback procedure, backup schedule, restore drill, RPO/RTO, or coordinated blob recovery was found. Startup performs tenant-wide initialization/backfill before serving. README deployment instructions still describe PostgreSQL/pgvector.

**Consequence:** Releases and incidents depend on tribal knowledge; startup time grows with tenant count; rollback can conflict with forward-only data migrations; operators may start the wrong database topology.

**Target state:** A versioned production topology, automated build/test/migration gates, forward-compatible expand/migrate/contract deployment, backup and restore automation, and documented/tested recovery objectives.

**Smallest safe remediation:** Correct the README, document the actual topology and singleton/multi-instance assumptions, move data backfills to resumable versioned jobs, and add one clean-environment deployment/restore pipeline.

**Verification:** Deploy and restore into a clean environment from automation, roll an app version backward across an expand-compatible schema, and record achieved RPO/RTO.

### A-10 — Operator telemetry and cost/abuse controls are incomplete

**Severity:** Medium  
**Likelihood:** High  
**Impact:** Medium to High

**Evidence:** Meters are created but no exporter/provider was found. Optional dependencies can be healthy while disabled. Repository logging defines no production sink/alerts. Named rate-limit policies exist, but direct chat and task/agent execution surfaces do not consistently apply them.

**Consequence:** Queue lag, retries, stuck dispatches, tenant hot spots, model cost and authorization probing may not be detected promptly. A single tenant or compromised identity can drive expensive work.

**Target state:** Export traces/metrics/logs with tenant-safe dimensions; define SLOs and alerts for queue age, terminal failures, reconciliation, worker leases, provider latency/errors, AI token/cost budgets and authorization failures. Apply distributed per-user/per-tenant limits to costly surfaces.

**Smallest safe remediation:** Configure one supported telemetry pipeline, add outbox/dispatch/backlog dashboards and alerts, and apply existing named policies to high-cost endpoints while designing a distributed limiter for multiple replicas.

**Verification:** Induce a failed dispatch and backlog, confirm an operator-visible alert and correlation trace; load-test per-tenant limits and ensure one tenant cannot throttle all others.

### A-11 — Internal hotspots weaken otherwise good module boundaries

**Severity:** Medium  
**Likelihood:** High as features grow  
**Impact:** Medium

**Evidence:** `InternalFinanceController` directly uses the DbContext and contains large query/mapping workflows. The shared DbContext has 263 sets and hundreds of mapping/filter declarations. The company outbox processor is a large topic-level coordination hub across capabilities.

**Consequence:** Changes require broad knowledge, tests become harder to isolate, controller/persistence contracts couple, and central files accumulate conflicts and regression risk.

**Target state:** Controllers remain transport-only; capability application services own queries and commands; persistence configuration is organized by module; outbox topics are registered as capability-owned handlers behind a small platform dispatcher.

**Smallest safe remediation:** Move one high-change finance endpoint at a time behind an application service without changing routes. Split EF configuration into module-specific classes and replace central topic branching incrementally with registered handlers.

**Verification:** Architecture tests forbid API references to `VirtualCompanyDbContext`, capability tests exercise the extracted services, and handler registration tests prove exactly one owner per topic.

### A-12 — Mechanical validation is useful but not currently release-complete

**Severity:** Medium  
**Likelihood:** High on the current working tree  
**Impact:** Medium

**Evidence:** Focused tests passed 65 of 66. `DependencyInjectionArchitectureTests.Every_hosted_service_is_registered_once` failed because the expected topology does not include newly added hosted services. The API Release build passed cleanly. The solution Release build reached the client projects but failed on missing Mac Catalyst restore assets and Android AOT errors, with numerous client warnings. A full API test run exceeded four minutes without producing a result. Debug build output was locked by an existing .NET host process.

**Consequence:** The current tree is not a verified reproducible release baseline. New workers can enter the host without their architectural ownership being intentionally accepted, and cross-platform client build health is unclear.

**Target state:** Deterministic restore/build/test pipelines by supported target, bounded tests with progress/results, and architecture contracts that are updated only alongside an explicit worker-topology decision.

**Smallest safe remediation:** Review the new hosted services, update the expected topology only for intentional registrations, isolate the hanging/slow test group, and define which MAUI targets are release-required on each build agent.

**Verification:** Clean restore followed by required Release builds; focused and full test suites produce machine-readable results within stated time budgets; no test uses a pre-existing developer host.

## Architectural strengths worth preserving

- Project dependency direction and capability isolation are explicit and mechanically tested.
- A modular monolith is appropriate for the current cross-capability transaction needs; there is no evidence that microservices would reduce present risk.
- Tenant context, persisted membership, query filters, company predicates and mutation validation provide layered protection for normal request paths.
- Important support work saves durable intent through an outbox in the same database unit of work as business state.
- Outbox claims use database state rather than process-local locks, with stable keys, retries, terminal state and background execution records.
- Several dispatchers recheck approval/policy close to an external effect and classify ambiguous provider outcomes.
- Business audit is modeled separately from diagnostic logs.
- SQL Server migration/startup validation and both local/Docker restore scripts provide a solid development foundation.
- Safe exception responses and correlation IDs are present.

## Mechanical controls to add

In priority order:

1. A Production-environment authentication test that rejects every development identity header and requires a verified token/session.
2. Authorization tests for every approval and external-effect endpoint, including employee/member/manager/system actors and revoked approval.
3. Contract tests proving public requests cannot select an autonomous actor or execution mode.
4. Fault-injection tests for provider success followed by database failure, process death, timeout, lease expiry and reconciliation.
5. An audit durability test that reloads committed state and corresponding audit from a fresh DbContext.
6. A static boundary test for direct `IgnoreQueryFilters`, with a small reviewed allowlist or approved wrapper.
7. Multi-tenant worker tests and a two-instance execution test.
8. A rule preventing API controllers from injecting the persistence DbContext.
9. EF pending-model-change and clean SQL Server migration validation.
10. External integration contract tests for provider identifiers, retry classification and reconciliation.
11. Deployment smoke tests for readiness requirements, database restore and blob restore.
12. Telemetry tests or operational checks proving queue age, failures and reconciliation states are exported and alertable.

## Remediation sequence

### P0 — before any production or autonomous external-effect release

1. Replace/guard development header authentication and add Production negative tests.
2. Remove the public support `Autonomous` control, add explicit support approval/send permissions, and correct audit actor derivation.
3. Fix marketing audit transaction boundaries.
4. Put support and marketing post-provider failures into durable reconciliation, with stale-dispatch recovery and fault-injection tests.
5. Review the newly added hosted-service topology and restore the focused architecture suite to green.

### P1 — before horizontal scaling or production data reliance

1. Enforce distributed/database coordination or a singleton topology; make required dependencies fail readiness when absent.
2. Move production blobs to shared durable storage and prove database-plus-blob recovery.
3. Reinstate migration drift detection and validate against clean/restored SQL Server.
4. Correct deployment documentation; define automated deployment, expand/contract migrations, backup, rollback/forward-fix and RPO/RTO.
5. Export telemetry and add alerts; rate-limit costly endpoints by tenant and user.
6. Centralize and test system-scope query-filter bypass.

### P2 — controlled maintainability improvements

1. Extract persistence work from API controllers into capability application services.
2. Organize DbContext configuration and table ownership by capability while retaining one context where atomic transactions are valuable.
3. Replace central topic switches with capability-owned outbox handlers.
4. Move startup backfills into resumable, observable versioned jobs.
5. Establish deterministic full API and supported-client build/test pipelines.

## Validation performed

| Check | Result |
|---|---|
| `dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj --no-restore -c Release` | Passed with 0 warnings and 0 errors. |
| Focused API tests covering architecture, authorization/tenancy, outbox, startup migration and new orchestration/marketing areas | 66 total: 65 passed, 1 failed. The failure is the stale expected hosted-service topology. |
| Full `VirtualCompany.Api.Tests` Release run | Did not complete within 244 seconds; no pass/fail conclusion is claimed. |
| Full solution Debug build | Blocked by an existing .NET host locking API Debug outputs; that process was not stopped. |
| Full solution Release build | Core projects/tests compiled, then failed for MAUI Mac Catalyst restore assets and Android AOT; client build warnings also remain. |
| Project/namespace dependency inspection | Matches intended modular-monolith direction; no sibling capability implementation dependency was found. |
| Configuration and deployment inspection | SQL Server is implemented; repository README still describes PostgreSQL. Only the database has a supplied compose topology. |

## Decision summary

The architecture should remain a modular monolith. The appropriate next move is not decomposition into services; it is to harden identity, approval authorization, distributed side-effect state machines, tenant-system scopes, operational storage and production verification. Once the P0 controls are implemented and mechanically enforced, the existing modular structure and durable processing foundation can support the product well. Until then, the current tree should be treated as an integration/development baseline rather than a production release candidate.
