# Virtual Company code-quality assessment

**Assessment date:** 2026-08-12  
**Baseline at completion:** commit `49899663145e8880fdc0b034d966e488de080e53` plus 9 modified and 18 untracked files, primarily the guided-work-session feature  
**Related assessment:** [Architecture assessment](architecture-assessment-2026-08-12.md)

## Executive conclusion

Virtual Company contains a substantial amount of thoughtful correctness work: nullable reference types are enabled, many commands accept cancellation, tenant predicates and authorization tests are common, important public and background workflows use transactions and idempotency keys, and the API test assembly discovers about 1,802 cases. The strongest code is code that encodes a complete business invariant in one place—for example, public website lead capture combines validation, tenant lookup by form key, a serializable transaction, duplicate detection, audit, source attribution, consent, and follow-up enrollment, with tests including a concurrent duplicate case.

The current baseline is nevertheless **not a releasable quality baseline**. The two Critical issues from the architecture review are direct code-correctness failures: caller-controlled development identity headers work outside Development, and caller-controlled support autonomy can bypass approval. The code also has a systemic audit persistence trap, incomplete distributed side-effect recovery, a new untested guided-work implementation with non-atomic idempotency records, a broken Web/contract test baseline, a High-severity vulnerable transitive package, and no authoritative secret scan.

Maintainability is mixed. Project-level modularity is good, but many implementation units are very large and state-heavy. Excluding generated migrations, the source contains approximately 272,000 lines in 1,186 C#/Razor files; 137 files exceed 500 lines and 34 exceed 1,000. Size is not itself a defect, but the largest files also have high branch density and mixed responsibilities. A representative orchestration/marketing commit changed 210 files and added 26 migrations. The current guided-work change crosses Domain, Application, Persistence, Migrations, Operations, API and Web without adding tests. Those are direct measures of change cost.

The immediate priority is correctness, not aesthetic cleanup. Restore trusted authentication and approval boundaries, make audit/idempotency/effect state atomic or reconcilable, and make the risk-based test gate green. Formatting, decomposition and consistency work should follow behind those controls.

## Baseline integrity and moving-scope note

The repository changed during the assessment. It began from `294bc5c`; while checks were running, commit `4989966` (“Created company orchestration and a marketing agent”) was created and included the architecture assessment and the previously uncommitted orchestration/marketing implementation. The final code-quality report is anchored to `4989966` plus the remaining guided-work changes.

Checks executed after the new commit include the forced API rebuild, isolated Web and Web-contract tests, and the current focused API test run. Earlier formatting, dependency and source inspection operated on substantially the same orchestration/marketing content before it was committed. Results are therefore useful current-state evidence, but this was not an immutable CI checkout. A clean CI run on the final commit remains required.

## Current component and dependency map

| Area | Code-quality role | Current evidence |
|---|---|---|
| Domain | Entities, state transitions and storage values | Dependency direction is clean. Large entity collections and a mix of enum/constants/raw strings make some state machines hard to review. |
| Application | Contracts, commands, queries, validators and ports | Keeps infrastructure dependencies mostly outward. Several contract files and validators have become large; public contracts sometimes expose control decisions that should remain trusted server context, notably support `Autonomous`. |
| Persistence | One shared EF Core context, mappings and query filters | Enables atomic cross-capability commits. The context has 263 sets and hundreds of filters, making model and migration changes broad. Query-filter bypass is widespread. |
| Platform infrastructure | Tenancy, audit, outbox, storage, AI/tool and operational primitives | Provides important reusable controls. `IAuditEventWriter.WriteAsync` has misleading persistence semantics: it adds to the DbContext but never saves. |
| Capability infrastructure | Finance, Sales, Support, Mailbox and Operations use cases | Contains most business logic. Project boundaries are good, but several services exceed 1,500–2,800 lines and combine querying, mapping, policy, orchestration and persistence. |
| API | Transport, middleware and composition | Mostly delegates to services, but `InternalFinanceController` directly injects the DbContext and contains substantial domain/query logic. Error response construction is repeated and inconsistent. |
| Web/Mobile | UI, client adapters and presentation logic | Web has meaningful component tests, currently with many failures. Several Razor/page units are extremely large. Mobile creates its own `HttpClient` and concentrates much behavior in `MainPage.xaml.cs`. |
| Tests | Unit, integration, contract, component and architecture tests | Broad intent and many risk-oriented tests. Organization is coupled: Web projects link source files physically stored under `Api.Tests`. Provider fidelity and baseline stability are weak. |

The project dependency graph itself remains a strength and is described in detail in the architecture assessment. The most important leakage within that graph is the API controller's direct persistence dependency, not an incorrect project reference.

## Clean-baseline results

No source was formatted or corrected during measurement. Restore created only generated `obj` assets for two previously unrestored test projects.

| Check | Command or method | Result |
|---|---|---|
| API Release rebuild | `dotnet build ...VirtualCompany.Api.csproj --no-restore -c Release -t:Rebuild` | **Passed**, 35 unique compiler warning sites, 0 errors. Warnings include possible null dereferences/conversions, nullable generic keys, async methods without `await`, and unreachable code. |
| Cached incremental API build | Normal Release build after rebuild | Passed with 3 API nullability warnings. This demonstrates why a forced/clean build is required for the real warning baseline. |
| Full solution Release build | Run during the architecture assessment | Core projects compiled, then MAUI failed for missing Mac Catalyst restore assets and Android AOT failures. The supported target matrix is not reproducible as a single solution build on this machine. |
| Formatting | `dotnet format whitespace ... --verify-no-changes` | **Failed:** 6,824 whitespace diagnostics across 163 files. Many orchestration/marketing files use compressed multi-statement lines. |
| Configured analyzers | `dotnet format analyzers ... --verify-no-changes` | **Failed:** 82 diagnostics, all in tests: 70 `xUnit2031`, 5 `xUnit2013`, 5 `xUnit2029`, 1 unused theory parameter and 1 blocking task operation. No central `.editorconfig`, analysis-level policy, warnings-as-errors rule or additional production analyzer package was found. |
| Focused current API tests | Architecture, auth/tenant, outbox and marketing filter | **115 passed, 1 failed.** Hosted-service topology expectation is stale after new schedulers/workers were registered. |
| API suite discovery | `dotnet test ... --list-tests` | Approximately 1,802 discoverable test-case lines. |
| Full API tests | Full Release execution attempted earlier | Did not finish within 244 seconds; no pass/fail claim is made. The command provided no useful progress signal before timeout. |
| Finance tests | Isolated Release run | 34/34 passed. |
| Mailbox infrastructure tests | Isolated Release run | 5/5 passed. |
| Platform infrastructure tests | Isolated Release run | 2/2 passed. |
| Sales source tests | Restored, built and isolated | 6/6 passed. |
| Support grounding tests | Restored, built and isolated | 5/5 passed. |
| Web contract tests | Rebuilt and isolated | **2 passed, 14 failed.** Most failures are SQLite foreign-key seeding errors; two also expose changed finance initialization behavior. |
| Web component/presentation tests | Rebuilt and isolated | **190 passed, 113 failed.** Common failures are stale query-parameter setup after `SupplyParameterFromQuery`, changed routes/content, exact timestamp assumptions and changed component behavior. |
| NuGet vulnerability audit | `dotnet list ... package --vulnerable --include-transitive` | **Failed quality gate:** `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 has a High advisory and is present transitively across API/infrastructure/test graphs. `AngleSharp` 1.1.2 has a Moderate advisory in Web tests. |
| Secret scan | Installed-tool check plus fallback working-tree pattern scan | `gitleaks`, `trufflehog` and `detect-secrets` are unavailable. The fallback found 259 generic credential-assignment candidates and no reported high-confidence private-key/GitHub/Slack/Azure-storage signature. `.env` is ignored. This is **inconclusive**, does not inspect Git history adequately, and must not be treated as a clean secret scan. |
| Coverage | Repository tooling inspection | No coverage collector configuration, threshold or risk-based coverage gate was found. No coverage percentage is claimed. |

### Compiler-warning distribution

The forced rebuild's 35 sites are primarily nullability findings. Representative locations include:

- `CampaignPlanningService.cs:280`, where `PrimaryObjectiveUnit.Equals(...)` can dereference a nullable value in the revenue objective branch.
- `CompanyFinanceReadService.AgentQueries.cs`, where nullable aggregation objects are dereferenced in several projections.
- `CompanyExecutiveCockpitDashboardService.cs`, where nullable payloads and nullable dictionary keys conflict with declared contracts.
- `FortnoxSyncService.cs`, with repeated possible-null conversions across provider response handling.
- `FinanceWorkflowTriggerService.cs:399` and `:432`, which contain unreachable `break` statements after `return`.

Some warnings reflect limitations of EF expression-flow analysis rather than executable defects, but the campaign example is a plausible runtime null dereference. Because warnings are not treated as errors, new warnings can enter without a deliberate decision.

## Critical scenario traces

### Scenario 1 — Authenticated request and cross-tenant access

For a genuine principal, the flow has good defenses: company identifiers are reconciled by middleware, persisted active membership is required, service queries commonly include `CompanyId`, tenant filters apply by default, and tracked mutations reject company mismatches when company context is active. Tests are abundant in this area: a heuristic found 158 authorization-related and 241 tenant-related test files.

The outer identity premise is broken. `DevHeader` is the default scheme in all environments, and its handler accepts caller-provided subject/email headers outside Development. Most integration tests actively use those headers and no Production-environment negative test was found. The broad tenant test suite therefore validates behavior behind an authentication mechanism that is unsafe in production.

### Scenario 2 — Support reply approval and delivery

Draft generation, safety checks, durable outbox intent, database claiming and failure classification are well structured. However, approve/send endpoints require only company membership, and the public request accepts `Autonomous`. When true, low-risk content can skip the approved state. Delivery also has a provider/database dual-write window: provider success followed by failure to save `SentUtc` permits another send.

Existing support tests exercise drafting, sending and failure behavior at service level, but no controller authorization test was found for ordinary member versus manager, and no test proves a caller cannot self-select autonomous execution. No test simulates provider success followed by database commit failure.

### Scenario 3 — Anonymous website lead submission

This is a positive reference implementation. The endpoint validates bounded fields; the service resolves a tenant using a high-entropy form key; a serializable transaction surrounds duplicate lookup and creation; company predicates are explicit despite query-filter bypass; audit/source/consent/follow-up records commit together; external submission IDs are idempotent; and a test sends two concurrent normalized-email submissions and asserts one active lead.

The remaining weakness is at the public boundary: the endpoint has no rate-limit attribute or bot-abuse control. A leaked or embedded form key can be used to generate database work and follow-up workflow load. Raw `InvalidOperationException.Message` is also returned from this anonymous endpoint.

### Scenario 4 — Marketing channel dispatch

The dispatcher validates approval, content version, destination capability and provider result, and saves a durable `dispatching` claim before publication. It classifies retryable and ambiguous outcomes. Two code-level gaps remain:

1. Provider success followed by failure of the completion save can leave the row permanently `dispatching`, outside the queued/retry query, without demonstrated stale-lease reconciliation.
2. The success/reconciliation code saves state before calling `IAuditEventWriter.WriteAsync`; the writer only adds an entity. With no subsequent save, the audit record is lost.

These are failures of transaction/state-machine implementation, not stylistic concerns.

### Scenario 5 — Guided-work edit, review and commit

The current untracked feature has strong ideas: sessions are scoped to the creating member, optimistic versions are checked, AI output is constrained by a JSON schema and server-side field validation, review tokens are random and hashed, fixed-time comparison is used, and commit wraps target mutation plus operation-result persistence in one transaction.

However, `CorrectFieldAsync`, `PrepareReviewAsync` and `CancelAsync` first save the session, then separately save the idempotency operation. A crash between those saves changes the resource but loses the response record; a retry with the same request ID can then conflict on version instead of returning the original outcome. Start has no request-level idempotency key, so a transport retry can create two sessions. Audits are written after the final save/transaction and are not persisted. No guided-work tests exist for authorization, tenant isolation, schema validation, optimistic concurrency, retry, token expiry, commit atomicity, AI failure or audit.

## Prioritized findings

### CQ-01 — Development header authentication is exploitable outside Development

**Severity:** Critical  
**Consequence:** Identity impersonation and possible tenant compromise  
**Evidence:** `OperationsModuleRegistration.cs` registers `DevHeader` as the default scheme unconditionally. `AuthInfrastructure.cs` creates a principal from `X-Dev-Auth-*` headers in non-Development environments when subject or email is supplied. The test suite relies heavily on these headers but does not test Production rejection.

**Smallest safe remediation:** Register header authentication only for explicit Development/Testing. Configure cryptographically verified production authentication and fail startup if none is available.

**Verification:** Production-host integration tests must return 401 for every development header combination, malformed/unsigned/expired tokens and wrong issuer/audience, while a valid production identity with active membership succeeds.

### CQ-02 — Public support autonomy and weak endpoint policy bypass approval

**Severity:** Critical  
**Consequence:** Unauthorized customer communication and false audit attribution  
**Evidence:** Support controller approve/send actions inherit only `CompanyMember`; `SendSupportReplyDraftRequest.Autonomous` is supplied by the client; the service uses it to skip approved status for qualifying drafts.

**Smallest safe remediation:** Remove autonomous execution from the public request contract, derive actor/mode from a trusted internal principal and tenant policy, and require explicit approve/send permissions.

**Verification:** Employee/member requests must receive 403; public payload fields cannot change execution actor; only authorized managers/internal agents can proceed; revoked or stale approval must block the dispatcher.

### CQ-03 — `IAuditEventWriter.WriteAsync` has a systemic lost-audit trap

**Severity:** High  
**Consequence:** Business changes and external effects can lack durable audit evidence  
**Evidence:** `AuditEventWriter.WriteAsync` calls `DbContext.AuditEvents.Add` and returns `Task.CompletedTask`; it never saves. There are 62 source call sites. Marketing dispatch/reconciliation and current guided-work operations call it after their final save or committed transaction, guaranteeing or strongly risking lost audit records.

**Smallest safe remediation:** Make the contract explicit as a unit-of-work operation, for example `Add`, and require callers to add audit before the same final `SaveChanges`; alternatively provide an explicit atomic command transaction abstraction. Do not make the writer save independently where state and audit must be atomic.

**Verification:** For every externally visible or controlled action, reload from a fresh DbContext and assert state plus one correctly attributed audit record. Inject save failure and assert neither local state nor audit partially commits.

### CQ-04 — External side-effect state machines do not close post-provider failure windows

**Severity:** High  
**Consequence:** Duplicate support email or externally published marketing content stranded in local `dispatching` state  
**Evidence:** Support saves sent state after provider delivery, allowing retry if that save fails. Marketing saves its claim, publishes, then saves completion; candidate selection excludes stale `dispatching` rows. Provider request headers are evidence but not universal provider-enforced idempotency.

**Smallest safe remediation:** Persist an attempt/lease before the call, transition uncertain post-call states to reconciliation-required, recover stale leases, and reconcile with deterministic provider identifiers before retry.

**Verification:** Fault-injection tests for provider-success/save-failure, timeout, process death, lease expiry and duplicate pickup must produce either one effect or a visible reconciliation item—never blind duplicate delivery or permanent invisible stranding.

### CQ-05 — The test baseline cannot currently act as a release gate

**Severity:** High  
**Consequence:** Real regressions cannot be distinguished reliably from stale fixtures and broken harness assumptions  
**Evidence:** Web contracts fail 14/16, Web tests fail 113/303, the current focused API set fails 1/116, the full API run did not complete within the assessment window, and solution Release fails at MAUI. Several contract tests now fail while seeding SQLite foreign keys; many component tests use a bUnit query-parameter setup pattern rejected by the current framework.

**Smallest safe remediation:** Triage failures by root cause rather than updating assertions blindly. First fix shared harness/schema seeding and query-navigation setup, then review changed route/content expectations. Define supported build targets and a bounded API test partition strategy.

**Verification:** A clean checkout restores, builds required targets and runs all required partitions with zero failures and machine-readable results. Re-run failed tests individually and in parallel to prove isolation. Track quarantine only with owner, reason and expiry.

### CQ-06 — Guided-work idempotency, audit and risk tests are incomplete

**Severity:** High while the feature remains a release candidate  
**Consequence:** Duplicate sessions, changed state without replayable result, lost audits, and undetected tenant/concurrency/AI-output regressions  
**Evidence:** The current 27-file change spans seven production project areas and has no test matches. Several mutations save session state before separately saving `GuidedSessionOperation`; start lacks a client request ID; audit occurs after the final save. The service is already more than 300 lines and owns tenancy, chat, AI validation, draft mutation, token lifecycle, idempotency, commit and audit.

**Smallest safe remediation:** Put state, operation result and audit in one transaction/save for each mutation; require a request ID on start; preserve unique database constraints for request IDs; add focused service and API tests before merging.

**Verification:** Repeat each command with the same ID before and after injected failure; assert identical response and one mutation. Test cross-user and cross-tenant access, stale versions, expired/wrong tokens, malformed AI patches, target commit rollback and fresh-context audit persistence.

### CQ-07 — A High-severity vulnerable SQLite native dependency ships broadly

**Severity:** High dependency finding; runtime exploitability depends on whether SQLite/native loading is reachable  
**Consequence:** Known vulnerable native code is present across the API/infrastructure graph even though production is intended to use SQL Server  
**Evidence:** NuGet audit reports `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 with High advisory `GHSA-2m69-gcr7-jv3q` in API, Infrastructure, Platform and most capability/test projects. Platform directly references EF SQLite, causing broad transitiveness. `AngleSharp` 1.1.2 has a Moderate advisory in Web tests.

**Smallest safe remediation:** Remove SQLite from production Platform dependencies if it is test-only; otherwise update the top-level dependency that resolves a fixed native package. Upgrade bUnit/AngleSharp transitives for tests. Generate a lock/SBOM and fail CI at an agreed severity.

**Verification:** Re-run the audit with direct and transitive packages; High/Critical must be zero or have a documented, time-bounded, exploitability-reviewed exception. Inspect published API artifacts to confirm unused SQLite native binaries are absent.

### CQ-08 — Anonymous write/callback endpoints lack consistent abuse and safe-error controls

**Severity:** High for abuse exposure; Medium for information disclosure  
**Consequence:** Database/workflow spam, callback probing and leakage of provider/configuration details  
**Evidence:** Website lead submission and OAuth callback controllers are `[AllowAnonymous]` without endpoint rate limiting. Website leads can create durable records and enroll follow-up work. Marketing callback, mailbox callback and calendar callback return or redirect raw exception messages in some failure paths. Many authenticated controllers also return `ex.Message`, but those often originate from intentionally user-safe domain exceptions; the type system does not distinguish safe from internal messages.

**Smallest safe remediation:** Apply distributed IP/form-key/provider rate limits and request-size limits to anonymous endpoints. Introduce typed safe problem exceptions/error codes; log internal exceptions with correlation IDs and return stable public summaries.

**Verification:** Load tests demonstrate bounded anonymous work per source/tenant key and no global tenant starvation. Tests inject provider/config/database exceptions and assert responses contain no connection strings, tokens, provider bodies, stack details or internal paths.

### CQ-09 — Compiler warnings are allowed to accumulate in correctness-sensitive code

**Severity:** Medium, with individual High defects possible  
**Consequence:** Null-reference 500s, misleading contracts and dead paths enter unnoticed  
**Evidence:** A forced API dependency rebuild passes with 35 warning sites, including plausible nullable dereferences in campaign analytics and provider/read projections, four unreachable-code warnings and four async-without-await warnings. Incremental build showed only three, demonstrating a misleading cached baseline. No warnings-as-errors policy exists.

**Smallest safe remediation:** Triage and eliminate current production warnings, then enable warnings-as-errors for first-party projects with narrowly documented exceptions. Use a fixed analysis level and CI forced/clean builds.

**Verification:** Forced Release rebuild reports zero first-party warnings. Add unit cases for null campaign objective units, missing provider fields and nullable dashboard/cache data.

### CQ-10 — Background validation and time-zone failures can be silent

**Severity:** Medium  
**Consequence:** Work repeats every minute or is skipped permanently without an operator-visible reason  
**Evidence:** `CompanyOperatingCycleScheduler` has five empty catches for `CompanyOperatingValidationException`; invalid time-zone lookup is caught with `catch { continue; }`. The same scan window is revisited repeatedly. The exception type represents actual validation gaps, not a dedicated duplicate marker.

**Smallest safe remediation:** Catch only explicit duplicate/idempotent outcomes; persist validation failures or emit rate-limited structured warnings/metrics with company and entity IDs; validate time zones when configuration is saved.

**Verification:** Invalid snapshot and time-zone tests produce a visible failure state/metric once, do not block other tenants, and do not produce unbounded log or polling churn.

### CQ-11 — Large mixed-responsibility units make changes expensive and review shallow

**Severity:** Medium  
**Consequence:** Broad regressions, merge conflicts, hard-to-isolate tests and duplicated business decisions  
**Evidence:** Largest handwritten units include `Agents.razor` (3,306 lines, branch heuristic 299), `CompanyFinanceReadService.cs` (2,862), `CompanySimulationFinanceGenerationService.cs` (2,482), `InternalFinanceController.cs` (2,459, branch heuristic 135), `BillsPage.razor.cs` (2,164), and `FortnoxSyncService.cs` (2,122). `InternalFinanceController` directly injects `VirtualCompanyDbContext`. The outbox dispatcher and approval/workflow services are also central branching hubs.

**Smallest safe remediation:** Extract by business capability/use case, not arbitrary line count. Begin with controller queries/commands behind Finance application ports; split Web pages into state coordinators and focused components; register capability-owned outbox handlers behind a small dispatcher.

**Verification:** Architecture test rejects DbContext injection in controllers; extracted services have focused tests; changing one finance endpoint or outbox topic no longer requires edits in the central controller/dispatcher; public routes/contracts remain unchanged.

### CQ-12 — Time and state representation are inconsistent and reduce deterministic changeability

**Severity:** Medium  
**Consequence:** Flaky exact-time tests, difficult simulation, status typos and duplicated presentation rules  
**Evidence:** First-party source contains about 1,073 `DateTime.UtcNow`/`DateTimeOffset.UtcNow` occurrences versus 175 `TimeProvider` references. A current telemetry test fails because two near-identical real timestamps differ by milliseconds. Raw `.Status == "..."` comparisons occur at least 94 times and raw status assignments at least 109 times, especially across UI/orchestration code.

**Smallest safe remediation:** Inject `TimeProvider` at orchestration/domain service boundaries and pass an explicit `now` into entities. Share typed status/storage values in Shared/Application contracts and centralize UI status mapping.

**Verification:** Time-sensitive tests use fake time and pass deterministically under repetition/parallelism. Compile-time types or exhaustive mappings reject unknown status values; API serialization compatibility tests preserve canonical strings.

### CQ-13 — Test organization and provider fidelity create hidden coupling

**Severity:** Medium  
**Consequence:** Tests compile in surprising assemblies, fail after unrelated package/framework changes, and do not fully model SQL Server behavior  
**Evidence:** Web and Web-contract projects link source files physically stored in `VirtualCompany.Api.Tests` using wildcard naming rules. The API test project removes those same filename patterns. Contract tests use SQLite against a SQL Server production model and currently fail foreign-key seeding after model changes. Platform carries SQLite largely for tests, contributing the vulnerable native dependency to production graphs.

**Smallest safe remediation:** Move tests to the project that owns them or a deliberate shared test-support project; avoid filename-glob ownership. Use fast SQLite/unit tests where provider-neutral, plus SQL Server container integration tests for constraints, migrations, transactions, concurrency and query semantics.

**Verification:** Each source test compiles in one obvious project; `dotnet test` partitions are independently green; database-risk tests run on SQL Server and reproduce production constraints.

### CQ-14 — Feature change size and migration granularity exceed practical review capacity

**Severity:** Medium to High deployment/change risk  
**Consequence:** Reviewers cannot meaningfully validate schema evolution, rollback and cross-module behavior in one change; migration sequencing increases deployment and recovery complexity  
**Evidence:** Commit `4989966` changed 210 files and reports 727,691 added lines, of which roughly 709,000 are migration designer/snapshot output. It adds 26 migration classes in one feature commit across Domain, Application, Persistence, Operations, Sales, API, Web and tests. All current-day migration classes inspected have non-empty `Down`, which is positive, but volume still impedes assurance.

**Smallest safe remediation:** Deliver bounded vertical outcomes with tests and migration prerequisites. If these migrations have never been applied outside disposable development databases, consolidate them into a small coherent migration set; if any are deployed/shared, never rewrite history—validate and preserve the chain.

**Verification:** Apply migrations from the last released schema and from a representative restored backup; verify data preservation, constraints and startup. Review migration SQL, lock duration and rollback/forward-fix procedure. Each future change should be independently deployable and testable.

### CQ-15 — Secret scanning is not an enforceable control

**Severity:** Medium process risk; no confirmed repository secret from this assessment  
**Consequence:** Credentials can be committed in current or historical content without a blocking signal  
**Evidence:** No dedicated scanner is installed or configured. The fallback scan is intentionally conservative and inconclusive; it cannot provide entropy detection, allowlisting, validation or adequate history coverage. An ignored local `.env` exists, which is appropriate for Git exclusion but still requires local handling discipline.

**Smallest safe remediation:** Add a dedicated scanner in pre-commit and CI, scan full Git history once, maintain a reviewed allowlist and documented rotation process, and keep production secrets in the configured secret store/environment rather than appsettings.

**Verification:** Seed a fake known test signature and prove CI blocks it; scan current tree and history cleanly; for any confirmed historical credential, revoke/rotate first and then remove history only through an explicitly coordinated process.

## Maintainability and changeability assessment

### Responsibility and coupling

Project boundaries make it possible to locate capabilities, which is valuable. Within projects, however, several classes act as local monoliths. `CompanyFinanceReadService` performs initialization checks, provider/source interpretation, dozens of queries and presentation mapping. `InternalFinanceController` adds more mapping and persistence access. The same business requirement can therefore require coordinated changes in a large service, large controller, Shared DTOs, Web API clients, Razor pages, test-link patterns and the central DbContext.

Service-location calls are common in background workers. Scoped resolution from `IServiceScopeFactory` is normal for hosted services, so the raw count is not itself a violation. The quality concern is that workers resolve several concrete services deep inside loops, making their dependency set invisible to constructors and harder to test. Prefer a scoped worker/coordinator object whose constructor exposes its dependencies; keep only scope creation in the hosted service shell.

### Duplication and state complexity

Business state is represented through a mixture of domain enums, storage-value helpers, static constants and raw strings. This is most visible in new marketing/orchestration code and Web presentation. Approval, retry and status-display conditions are consequently repeated across service and UI layers. The correct remedy is not a universal enum—wire/storage compatibility matters—but canonical typed values and exhaustive mapping at boundaries.

Error handling is similarly fragmented. Some controllers use stable problem codes, some return validation dictionaries, some return anonymous `{ message }`, and many expose `ex.Message`. This creates client complexity and makes it difficult to know which exception text is safe. A small shared API error contract and typed domain exceptions would improve both safety and changeability.

### Formatting and reviewability

The 6,824 formatter diagnostics are Low severity in isolation. Their quality impact becomes Medium where files compress complete methods, catches and state transitions onto one line. That style makes diffs and line-level review poor, contributes to 1,959 handwritten lines over 200 characters, and hides transaction ordering such as “save, then audit.” Establishing a formatter gate after the current tree is normalized will improve defect detection, but it should not displace the Critical/High fixes.

### Dead and stale paths

No `TODO`/`FIXME` markers were found in the source scan, which avoids explicit deferred work but does not prove completeness. Compiler analysis found unreachable statements, and documentation/configuration still retain PostgreSQL/SQLite paths while production intent is SQL Server. The more important stale paths are behavioral: test expectations and harness setup lag behind current routes, query binding and model constraints.

## Tests assessed by risk

| Risk area | Evidence of protection | Important gap |
|---|---|---|
| Authorization and tenant isolation | Broad integration-test presence; route/header mismatch, membership and cross-tenant behavior are commonly exercised. | No Production authentication negative test; tests normalize unsafe dev-header identity. Support approve/send roles are not covered. |
| Business invariants | Finance, approval, workflow, lead capture and orchestration have many focused tests. | Current red baseline means invariant failures are obscured; guided work has none. |
| Transactions and data integrity | Website lead concurrency/idempotency is strong; outbox and finance transaction tests exist. | SQLite harness differs from SQL Server and currently fails FKs; post-provider DB failure and audit persistence are missing. |
| Integration failure/retry/duplicate | Outbox, OAuth, provider and duplicate tests exist. | No effectively-once fault injection across provider success/database failure; stale marketing dispatch recovery absent. |
| Migrations and compatibility | Migration assembly/snapshot/startup checks exist; Docker/local restore scripts exist. | No compiled-model-versus-snapshot gate, representative backup migration pipeline, or full SQL Server migration test. Pending model warning is suppressed. |
| Critical UI workflows | 303 Web tests cover presentation and interaction states. | 113 currently fail; query-bound component setup and route expectations are stale. No guided-work UI tests. |
| Security supply chain | NuGet audit can be run manually. | High advisory remains; no CI threshold, SBOM or reliable secret scan. |

Coverage percentage would add little until the release gate is trustworthy. The next useful coverage work is targeted mutation/fault testing of authorization, approval, idempotency, audit and transaction boundaries, not chasing a repository-wide percentage.

## Architectural rule violations visible in code

1. **Server authorization and approval:** support autonomy is selected by the client and approve/send lack action-specific authorization, contrary to the rule that server-side authorization and approval boundaries are mandatory.
2. **Durable external effects:** support and marketing have durable intent but do not safely reconcile every provider-success/database-failure outcome.
3. **Audit as business evidence:** several state/effect paths call the non-saving audit writer after the final unit of work, so the required business audit is not durable.
4. **Thin controllers:** `InternalFinanceController` directly accesses EF persistence and contains domain/query behavior, contrary to transport-only controller guidance.
5. **Tenant system scope:** more than 1,300 `IgnoreQueryFilters` calls exist with no mechanical allowlist/system-scope wrapper. Many are explicitly re-scoped, but the rule depends too heavily on review.
6. **Migration release safety:** pending-model warning suppression and lack of model-equivalence validation weaken the SQL Server migration requirement.

Project reference direction and sibling capability isolation remain compliant and mechanically enforced.

## Remediation roadmap

### Immediate correctness and security fixes

1. Fix production authentication and add Production negative tests.
2. Remove public support autonomy and add explicit approve/send policies and actor derivation.
3. Correct audit transaction ordering in marketing and guided work; audit all other post-final-save call sites.
4. Add reconciliation/stale-lease behavior for post-provider failures.
5. Make every guided-work mutation, idempotency result and audit atomic; add the missing risk suite before merging.
6. Resolve the High NuGet advisory or remove SQLite from published production graphs.
7. Protect anonymous write/callback endpoints with distributed rate limits and stable safe errors.
8. Restore focused architecture/worker tests and the Web/contract test baseline to green.

### Boundary and test improvements

1. Add static rules for controller DbContext injection and direct `IgnoreQueryFilters` usage.
2. Add SQL Server container tests for migrations, constraints, concurrency and critical transactions.
3. Add fault injection for audit, outbox, provider and database boundary failures.
4. Separate test ownership from filename-linked Api.Tests sources; partition the 1,802 API tests into bounded suites.
5. Triage all compiler warnings, adopt a central `.editorconfig`/analysis policy, and fail first-party warnings.
6. Add secret scan, dependency threshold, SBOM and formatter verification to CI.
7. Adopt `TimeProvider` in stateful services and make time tests deterministic.

### Longer-term structural changes

1. Extract Finance API persistence/query behavior into capability application services.
2. Split large pages into focused components and testable state coordinators.
3. Split central outbox dispatch by registered capability-owned handlers.
4. Organize EF model configuration/table ownership by capability without forcing premature service decomposition.
5. Consolidate raw status and error contracts at application/Shared boundaries.
6. Keep feature delivery vertically bounded so one requirement does not require hundreds of files and dozens of migrations.

## Definition of an improved baseline

The repository should be considered release-verifiable when all of the following are true:

- Production rejects development headers and requires verified identity.
- Approval/side-effect permissions and trusted autonomous actors are enforced by integration tests.
- Every important state transition, idempotency record and audit event is durably atomic, or an external effect has explicit reconciliation.
- Required Release builds and all designated test partitions pass from a clean checkout.
- Web and contract tests have zero unexplained failures; database-risk tests run against SQL Server.
- First-party compiler warnings are zero and formatter/analyzer rules are versioned and enforced.
- High/Critical vulnerable dependencies are zero or have explicit time-bounded exceptions.
- A dedicated current-tree and history secret scan passes.
- Operator-visible tests demonstrate retry, duplicate, timeout, cancellation, stale lease and provider-success/database-failure behavior.
- The guided-work feature has authorization, tenant, validation, concurrency, idempotency, audit and UI tests before it is released.

## Final assessment

The codebase is not low quality in the sense of lacking engineering intent; it has many good primitives and unusually broad test investment. Its present weakness is **assurance coherence**: critical boundaries are implemented inconsistently, audit persistence has a deceptive contract, external-effect state machines stop just short of the hardest failure case, and the large test suite is not green enough to arbitrate changes. Fixing those issues will improve code quality far more than a broad rewrite or a cosmetic complexity campaign.

Keep the modular monolith. Make correctness controls mechanically unavoidable, restore a trustworthy release baseline, and then decompose the largest local hotspots incrementally along existing capability boundaries.
