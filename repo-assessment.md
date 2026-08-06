# Repository duplication, reachability, and obsolescence assessment

**Assessment date:** 2026-08-01  
**Scope:** Current working tree, including tracked and untracked files  
**Change policy:** Assessment only. No production code, configuration, assets, tests, or generated output were removed or changed.

## Executive summary

The repository contains a small set of high-confidence cleanup candidates, several worthwhile consolidation opportunities, and a large amount of deliberately transitional or in-progress code that should not be removed merely because it appears duplicated or is not linked from the primary navigation.

The strongest safe-removal candidates are generated build/publish output committed under `.artifacts/`, generated portions of `artifacts/`, and `.test-bin/`; temporary Codex diagnostic projects; two manual SQL copies of an EF migration; a duplicate root copy of `laura.png`; and redundant direct package references to MailKit in projects that do not compile against MailKit. The three artifact roots contain **1,359 tracked files and approximately 813.95 MiB** in total, although `artifacts/` also contains material that requires separation and human confirmation before that directory can be removed wholesale.

The strongest refactoring opportunities are:

- company-scoped Web API calls that bypass the documented `ICompanyApiTransport`;
- repeated API problem/error decoding in Web clients;
- duplicated provider-payment projection logic in the accounts-payable and accounts-receivable pages;
- test source files physically owned by `VirtualCompany.Api.Tests` but linked into the Web test projects; and
- package-version drift that would benefit from central package management.

There is no evidence that compatibility routes, the task command/query seam, provider-neutral/legacy finance integration code, offline stores, or active Marketing/Sales/Support documentation are safe deletions. These are documented compatibility layers, explicit extension points, or visibly active implementation areas.

The repository does **not** currently have a clean verification baseline. Non-mobile projects compiled during the solution build, but the solution build failed because the installed SDK lacks the MAUI workloads. NuGet audit metadata was also unreachable. The focused Finance suite passed 13/13; the Web suite failed 113/292; the Web contract suite failed 14/16; and the broad API suite exceeded five minutes after reporting many failures. Cleanup should therefore begin only after the existing failures are triaged or explicitly recorded as an accepted baseline.

## Repository and documentation context

### Documents followed

- `AGENTS.md` was read and treated as the repository-level instruction source.
- The requested root `architecture.md` does not exist. The repository-directed replacements, `docs/architecture-rules.md` and `docs/architecture-overview.md`, were read instead.
- The requested root `design.md` does not exist. The repository-directed `docs/design.md` was read instead.
- `docs/ui-route-inventory.md` was also used because it identifies compatibility routes that could otherwise be incorrectly classified as dead pages.

This documentation mismatch should be corrected by either adding root forwarding documents or updating contributor instructions to name the canonical files. It did not prevent this assessment because `AGENTS.md` names the canonical paths.

### Intended architecture relevant to this assessment

- The solution is a .NET 9 modular monolith. Domain and Application are inward dependencies; Persistence owns EF concerns; Platform, Mailbox, Finance, Sales, Support, and Operations own capability implementation; `VirtualCompany.Infrastructure` is a thin compatibility/composition facade.
- EF migrations are the schema authority. SQL Server is the production/local authority, while SQLite is limited to provider-compatible tests.
- Tenant-owned Web requests should flow through one `ICompanyApiTransport` implementation so tenant headers, correlation, errors, and offline behaviour remain consistent.
- Compatibility routes and facades may remain while callers migrate. Their apparent overlap is not, by itself, evidence of obsolescence.
- Design references and route inventories can document future/in-progress UI and should not be treated as shipped runtime assets.

### Working-tree caveat

The working tree contains extensive modified and untracked work. Findings are therefore about the present working tree, not necessarily the last committed product state. Untracked Marketing, campaign, design-reference, provider-admin, and finance-approval work appears active and is classified conservatively.

## Baseline verification performed

| Check | Result | Interpretation |
|---|---|---|
| `dotnet build VirtualCompany.sln --no-restore --verbosity:minimal` | Failed | Initial runs were blocked by `NU1900` because NuGet vulnerability metadata could not be reached. A rerun also established missing `maui-ios`, `maui-android`, `maui-tizen`, and `maui-maccatalyst` workloads (`NETSDK1147`). Non-mobile projects compiled. Build also emitted one nullability warning and four xUnit analyzer warnings in Web contract test sources. |
| `dotnet test VirtualCompany.sln --no-build --no-restore` | Stopped after more than eight minutes without a terminal result | Not a usable green baseline. Focused runs were used to obtain actionable results. |
| `VirtualCompany.Finance.Tests` | **Passed 13/13** | Focused Finance unit baseline is green. |
| `VirtualCompany.Web.Tests` | **Failed 113, passed 179, total 292** | Failures include stale route expectations, bUnit query-parameter setup, and missing formatter registrations. These pre-exist cleanup and must be baselined or fixed first. |
| `VirtualCompany.Web.Contract.Tests` | **Failed 14, passed 2, total 16** | Most failures are SQLite foreign-key fixture failures; other failures concern finance initialization/seeding expectations. |
| `VirtualCompany.Api.Tests` | Exceeded five-minute focused timeout after reporting many failures | Failures span finance projections, workflow idempotency, direct chat, onboarding, policy tests, extraction, and more. No cleanup attribution is possible because no cleanup was made. |
| `VirtualCompany.SalesSource.Tests` and `VirtualCompany.SupportGrounding.Tests` | Attempt did not reach a terminal result before being stopped | These projects are not in the solution and need explicit CI/build inclusion. |
| `dotnet format ... --verify-no-changes` | Did not finish within four minutes and was stopped | Formatting/static-analysis baseline remains unknown. |
| Docker SQL Server validation | Not run | Docker is unavailable on this machine. Any data cleanup or migration validation must preserve and later exercise the documented Docker SQL Server path. |

## Findings table

| ID | Group | Classification | Candidate | Confidence | Removal risk | Recommendation |
|---|---|---|---|---|---|---|
| S1 | Safe removal | Duplicate / obsolete | Tracked generated build, test, and publish output | High | Low if intentional artifacts are separated | Remove generated subtrees; add ignores |
| S2 | Safe removal | Obsolete / duplicate | Temporary `.codex_tmp*` repro projects | High | Low | Remove after preserving any useful regression case |
| S3 | Safe removal | Obsolete | Manual Sales-source migration SQL copies | High | Low–medium | Remove or archive outside runtime repo |
| S4 | Safe removal | Exact duplicate | Root `images/laura.png` | High | Low | Remove duplicate root copy |
| S5 | Safe removal | Unused dependency | Direct MailKit references in API and API tests | High | Low | Remove one project at a time and verify |
| C1 | Consolidation | Duplicate functionality | Company-scoped Web request construction | High | Medium | Migrate to `ICompanyApiTransport` |
| C2 | Consolidation | Duplicate functionality | Web API problem/error parsing | High | Medium | Extract common decoder; retain typed exceptions |
| C3 | Consolidation | Duplicate functionality | AP/AR payment-summary projection | High | Medium | Extract narrow shared presenter |
| C4 | Consolidation | Overlapping ownership | API test sources linked into Web test projects | High | Medium | Move sources to their owning test projects |
| C5 | Consolidation | Overlapping configuration | Package version management | Medium | Medium | Introduce central package management carefully |
| R1 | Retain | Uncertain/future use | Compatibility routes and route aliases | High | High | Retain and document deprecation criteria |
| R2 | Retain | Uncertain/future use | Task command/query split over legacy task service | High | High | Retain during migration; investigate end state |
| R3 | Retain | Uncertain/future use | Provider-neutral and legacy finance integration paths | High | High | Retain until migration telemetry proves safe |
| R4 | Retain | Uncertain/future use | Explicit Web offline stores | Medium | High | Retain; verify production gating |
| R5 | Retain | Uncertain/future use | Marketing/campaign/design documents and module boilerplate | High | Medium–high | Retain; fix naming/documentation separately |
| R6 | Retain | Unused by solution, active tests | SalesSource and SupportGrounding test projects | High | High | Add to solution/CI, do not remove |
| U1 | Human confirmation | Obsolete or fixture | Tracked tenant-specific `App_Data` object-store files | Medium | Medium | Confirm fixture intent, then relocate or ignore |
| U2 | Human confirmation | Uncertain | `artifacts/hansa-house` Blender/media work | Medium | High | Confirm ownership; relocate if unrelated |
| U3 | Human confirmation | Unreachable/misplaced asset | `paul.png` and `philip.png` outside Web root | High | Medium | Relocate/fix mapping, not blind deletion |
| U4 | Human confirmation | Uncertain compatibility | Runtime PostgreSQL and SQLite provider paths | Medium | High | Confirm supported deployment matrix |

## 1. Safe removal candidates

### S1 — Tracked generated build, test, and publish output

- **Path/section:** `.artifacts/**`, `.test-bin/**`, and generated portions of `artifacts/**` such as `api-*`, `web-*`, `verify/`, `capability-presenter-tests/`, and `language-*-build/`; `.gitignore` lines 1–3, 14–18, and 43–45.
- **Classification:** Duplicate and obsolete generated output.
- **Evidence:** Git tracks 463 files/259.39 MiB under `.artifacts`, 860 files/531.39 MiB under `artifacts`, and 36 files/23.17 MiB under `.test-bin`. Hash inspection found repeated `.deps.json`, runtimeconfig, static-web-assets manifests, copied appsettings, seed JSON, documentation, and native SQLite binaries across these directories. No source references were found for `.artifacts`, `.test-bin`, or `artifacts/verify`. Existing ignore rules cover `bin/`, `obj/`, DLLs, executables, PDBs, caches, and `.codex-build/`, but not these roots.
- **Intended purpose:** Local verification, publish staging, and test output.
- **Confidence:** High for the named generated subtrees; low for deleting all of `artifacts/` because it also contains Hansa House media and manual SQL files.
- **Risk of removal:** Low when only reproducible output is removed; high if `artifacts/hansa-house` or non-generated deliverables are swept up.
- **Dependencies/dynamic use:** Build/release scripts or CI packaging may refer to output paths even when source code does not. Existing Git history should be checked for release use.
- **Recommended action:** Remove generated output from version control in a dedicated commit; retain and separately classify intentional deliverables; add `.artifacts/`, `.test-bin/`, and narrowly scoped generated `artifacts/` rules to `.gitignore`.
- **Required checks:** Search scripts, CI, docs, and packaging manifests for each path; reproduce outputs from a clean clone; build/publish API and Web; run test suites; verify release packaging does not consume committed binaries.

### S2 — Temporary Codex diagnostic projects

- **Path/section:** `.codex_tmp_query/Program.cs`, `.codex_tmp_query/QueryAccounts.csproj`, `.codex_tmp_repro.cs`, `.codex_tmp_repro/Program.cs`, `.codex_tmp_repro/CodexJsonRepro.csproj`.
- **Classification:** Obsolete diagnostics and an exact duplicate.
- **Evidence:** Repository-wide searches found no references to `QueryAccounts`, `CodexJsonRepro`, or `.codex_tmp`. `.codex_tmp_repro.cs` and `.codex_tmp_repro/Program.cs` are byte-identical 815-byte repro programs.
- **Intended purpose:** One-off database/query and JSON reproduction while diagnosing earlier issues.
- **Confidence:** High.
- **Risk of removal:** Low; the only risk is losing an unpromoted regression scenario.
- **Dependencies/dynamic use:** None found in projects, solution files, scripts, configuration, routes, or docs.
- **Recommended action:** If either repro still represents a valid bug, convert it into a named automated test; otherwise remove all temporary files and ignore `.codex_tmp*/`.
- **Required checks:** Run the repro once if its bug is still open; search Git history/issues for references; confirm equivalent automated coverage before deletion.

### S3 — Manual SQL copies of an authoritative EF migration

- **Path/section:** `artifacts/sales-source-migration.sql`, `artifacts/sales-source-migration-fixed.sql`; authoritative migration `src/VirtualCompany.Persistence.Migrations/Persistence/Migrations/20260712160431_AddSalesSourceAttribution.cs` and its designer.
- **Classification:** Obsolete duplicate migration artifacts.
- **Evidence:** Both SQL files implement the same Sales-source attribution schema change represented by migration `20260712160431_AddSalesSourceAttribution`. No references to either SQL filename exist outside `artifacts/`. Architecture rules state that EF migrations are the schema authority.
- **Intended purpose:** Generated/manual SQL used while developing or repairing the migration.
- **Confidence:** High.
- **Risk of removal:** Low–medium because an undocumented operator procedure could still use one script.
- **Dependencies/dynamic use:** Potential manual DBA runbooks or external deployment steps cannot be discovered solely from repository references.
- **Recommended action:** Confirm no operator consumes them, then remove; if an auditable SQL deployment artifact is required, generate it deterministically from EF in CI and keep it outside source or in a clearly versioned release package.
- **Required checks:** Apply the EF migration to a restored local SQL Server database and the Docker SQL Server flow; run `database update`/pending-model checks; compare generated idempotent SQL with any externally required script.

### S4 — Duplicate Laura image outside the Web static root

- **Path/section:** `images/laura.png` and `src/VirtualCompany.Web/wwwroot/images/laura.png`.
- **Classification:** Exact duplicate asset.
- **Evidence:** The two files have the same content hash. Runtime references use `/images/laura.png`, which resolves from `wwwroot/images`; no source or project file references the root copy.
- **Intended purpose:** Agent avatar used by Dashboard, Navigation, Public Inquiry, and Finance views.
- **Confidence:** High.
- **Risk of removal:** Low for the root copy; the `wwwroot` copy must remain.
- **Dependencies/dynamic use:** Documentation or external tooling could refer to the root image, but no repository reference was found.
- **Recommended action:** Remove only `images/laura.png`; keep `src/VirtualCompany.Web/wwwroot/images/laura.png` as the canonical asset.
- **Required checks:** Search documentation/scripts case-insensitively; build Web; request `/images/laura.png`; visually verify affected pages.

### S5 — Redundant direct MailKit package references

- **Path/section:** `src/VirtualCompany.Api/VirtualCompany.Api.csproj:4` and `tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj:40`; actual owning reference `src/VirtualCompany.Infrastructure.Mailbox/VirtualCompany.Infrastructure.Mailbox.csproj:15`.
- **Classification:** Unused direct dependencies.
- **Evidence:** C# usage of `MailKit`/`MimeKit` occurs in `VirtualCompany.Infrastructure.Mailbox/Mailbox/MailKitMailboxTransport.cs` (for example lines 8–13 and 948–972). No use was found in API or API-test source. Both projects already reach the mailbox implementation through project references.
- **Intended purpose:** Mailbox transport is correctly owned by the Mailbox capability; the extra references likely remained from before extraction.
- **Confidence:** High.
- **Risk of removal:** Low, but transitive-copy behaviour should be checked at publish time.
- **Dependencies/dynamic use:** Runtime loading by reflection is unlikely but possible; publish output and mailbox integration tests are the authoritative checks.
- **Recommended action:** Remove the API-test direct reference first, then the API direct reference in a separate commit. Keep the Mailbox project reference.
- **Required checks:** Build API and API tests; publish API; verify MailKit/MimeKit remain in publish output through the Mailbox dependency; run mailbox transport and DI-resolution tests.

## 2. Consolidation/refactoring candidates

### C1 — Company-scoped Web requests bypass the shared transport

- **Path/section:** Canonical `src/VirtualCompany.Web/Services/CompanyApiTransport.cs:17`; manual header construction in `DashboardSummaryApiClient.cs:38`, `TodayFocusApiClient.cs:40`, `MarketingApiClient.cs:8,97`, `SalesApiClient.cs:9,152,176,205,221`, and `SupportApiClient.cs:9,200,229,245`.
- **Classification:** Duplicate functionality and overlapping abstraction.
- **Evidence:** These clients independently add `X-Company-Id` and construct/send requests even though the architecture requires tenant-owned requests to use `ICompanyApiTransport`. Finance and Agent Staff already use the shared transport.
- **Intended purpose:** Feature-specific typed API clients should own contracts and feature semantics; the transport should own tenant/correlation/header mechanics.
- **Confidence:** High.
- **Risk of removal/consolidation:** Medium. A bulk rewrite could change serialization, not-found handling, authentication, cancellation, or offline semantics.
- **Dependencies/dynamic use:** DI registrations, named `HttpClient` configuration, authorization handlers, correlation headers, offline-mode routing, and feature-specific exceptions.
- **Recommended action:** Consolidate one client at a time onto `ICompanyApiTransport`; do not merge the typed feature clients themselves.
- **Required checks:** Company-header and correlation tests; tenant-isolation tests; authorization/401/403/404/problem-details tests; cancellation; offline-mode behaviour; Sales, Support, Marketing, Dashboard, and Today Focus contract tests.

### C2 — Repeated Web API problem/error decoding

- **Path/section:** `CreateExceptionAsync` in `ActionInsightApiClient.cs:137`, `ActivityFeedApiClient.cs:198`, `AgentApiClient.cs:283`, `ApprovalApiClient.cs:116`, `AuditApiClient.cs:102`, `DashboardSummaryApiClient.cs:51`, `DirectChatApiClient.cs:109`, `ExecutiveCockpitApiClient.cs:186`, `FinanceApiClient.cs:139`, `InboxApiClient.cs:132`, `OnboardingApiClient.cs:355`, `SalesApiClient.cs:228`, `SupportApiClient.cs:260`, `TaskApiClient.cs:86`, `TodayFocusApiClient.cs:57`, and `WorkflowApiClient.cs:134`.
- **Classification:** Duplicate functionality.
- **Evidence:** Many typed clients repeat response-body reading, ProblemDetails/JSON decoding, safe-message selection, and exception construction. `ApiProblemContract`, `LocalizedProblemPresenter`, and `CompanyApiTransport` already represent pieces of the shared concern.
- **Intended purpose:** Convert backend failures into safe, localized, feature-appropriate Web errors.
- **Confidence:** High that common parsing is duplicated; medium that exception construction can be unified.
- **Risk of removal/consolidation:** Medium. Finance, Sales, and Support carry typed reasons and special state such as “not initialized” that must not be flattened.
- **Dependencies/dynamic use:** Localization, safe-error policy, reference IDs, response disposal, typed exception catches in pages, and tests that assert exact messages.
- **Recommended action:** Extract only a common response/problem decoder and reusable metadata record. Keep thin feature-specific exception factories and reason mappings.
- **Required checks:** Malformed/empty JSON, RFC problem details, validation errors, 401/403/404/409/500, correlation/reference ID preservation, localization, and existing page-level safe-message tests.

### C3 — Accounts-payable and accounts-receivable payment projection

- **Path/section:** `BillsPage.razor.cs:949–1002,1432–1469,1738` and `InvoicesPage.razor.cs:300–391,480,567` (`BuildPaymentSummary`, provider-status parsing, status normalization, and source-detail projection).
- **Classification:** Duplicate functionality with distinct domain presentation around it.
- **Evidence:** Both pages normalize provider status, calculate paid/remaining totals, detect partial/full payment, and produce source detail records using nearly parallel code. The surrounding bill approval/export actions and invoice collection semantics differ.
- **Intended purpose:** Present provider-neutral settlement information for AP and AR.
- **Confidence:** High for the shared calculation; high that the pages themselves should remain separate.
- **Risk of removal/consolidation:** Medium because small differences in fallback and labels may be intentional.
- **Dependencies/dynamic use:** Localization, currency formatting, provider-status JSON shape, credit/cancellation semantics, and page-specific view models.
- **Recommended action:** Extract a narrow, immutable payment-summary parser/projector with AP/AR adapters. Do not merge `BillsPage` and `InvoicesPage`, and do not consolidate merely to reduce line count.
- **Required checks:** No status, malformed status, paid, partially paid, unpaid, cancelled, credited, currency mismatch, negative/overpaid values, and localized status labels on both pages.

### C4 — Web test sources are physically owned by the API test directory

- **Path/section:** `tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj:41–58`; `tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj:24–48`; `tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj:23–30`.
- **Classification:** Overlapping test ownership and duplicated build wiring, not duplicate execution.
- **Evidence:** API tests explicitly remove filename-pattern groups while Web tests link 31 matching files back from the API-test directory. Web contract tests similarly link seven integration/support files. The API project exclusions prevent most double execution, but physical ownership, glob rules, project references, and analyzer/package requirements are difficult to reason about. Baseline failures are reported under namespace `VirtualCompany.Api.Tests` even when executed by `VirtualCompany.Web.Tests`.
- **Intended purpose:** Partition tests by architectural ownership without immediately moving files.
- **Confidence:** High.
- **Risk of removal/consolidation:** Medium. Moving files can change fixture paths, namespace assumptions, content copying, and project dependencies.
- **Dependencies/dynamic use:** MSBuild glob ordering, linked support files, bUnit, WebApplicationFactory, SQLite fixtures, assembly discovery, and CI test filters.
- **Recommended action:** Move Web component/page tests into `VirtualCompany.Web.Tests` and contract tests plus their owned fixtures into `VirtualCompany.Web.Contract.Tests`. Replace broad filename globs with normal default compile items. Share true test infrastructure through a small test-support project only if multiple projects genuinely need it.
- **Required checks:** Compare `dotnet test --list-tests` before/after; ensure every test appears exactly once; preserve fixture copying; run both projects; verify architecture test ownership rules.

### C5 — Package version drift and repeated package declarations

- **Path/section:** all `*.csproj`; examples include ASP.NET packages at 9.0.1, EF packages at 9.0.14, MAUI packages at 9.0.9/9.0.10, and `Azure.Identity` 1.13.2 in API versus 1.11.4 in Platform.
- **Classification:** Overlapping configuration.
- **Evidence:** No `Directory.Packages.props` exists, so shared package versions are repeated across many projects and have already drifted. Both Azure Identity references are used and are not safe removals.
- **Intended purpose:** Project-local dependency ownership.
- **Confidence:** Medium; version differences can be intentional, especially for MAUI workloads.
- **Risk of removal/consolidation:** Medium. Blind unification can break workload compatibility or transitive dependency resolution.
- **Dependencies/dynamic use:** SDK workload bands, ASP.NET shared framework, EF provider compatibility, Azure SDK transitive dependencies, lock/assets files, and deployment runtime.
- **Recommended action:** Introduce central package management for genuinely shared versions, with explicit documented exceptions. Do not change versions as part of the initial generated-file cleanup.
- **Required checks:** Restore from a clean package cache, full non-mobile build/test, MAUI build on a provisioned machine, API/Web publish, package vulnerability scan, and provider integration tests.

## 3. Likely future or in-progress functionality that should be retained

### R1 — Compatibility routes and alias pages

- **Path/section:** Razor `@page` declarations for `/tasks`, `/approvals`, `/inbox`, `/outbound-review-queue`, `/queue`, finance bill/inbox aliases, Sales prospecting/leads aliases, and Support memory/knowledge-gap routes; `docs/ui-route-inventory.md:14,43`.
- **Classification:** Uncertain/future use; compatibility surface.
- **Evidence:** The route inventory explicitly says these routes remain compatibility/detail routes. Route use may originate in persisted notifications, emails, browser bookmarks, external clients, or older application versions rather than static source references.
- **Intended purpose:** Preserve deep links while navigation and feature surfaces migrate.
- **Confidence:** High that they must currently be retained.
- **Risk of removal:** High.
- **Dependencies/dynamic use:** Persisted URLs, external consumers, notifications, tests, redirects, and serialized work items.
- **Recommended action:** Retain and document an owner, replacement route, telemetry threshold, and sunset date before deprecation.
- **Required checks:** Route telemetry, link crawler, API/client version inventory, persisted-link search, redirect tests, and explicit product approval.

### R2 — Task command/query seam over the legacy task service

- **Path/section:** `Application/Tasks/TaskContracts.cs:221–251`; `Infrastructure.Operations/Companies/CompanyTaskCommandService.cs`; registrations in `OperationsModuleRegistration.cs:220,229`.
- **Classification:** Overlapping abstraction / uncertain migration seam.
- **Evidence:** `CompanyTaskCommandService` delegates four commands to `ICompanyTaskService` and maps `TaskDetailDto` to `TaskCommandResultDto`; older internal services still inject `ICompanyTaskService`, while controllers and newer coordinators use the command interface. This is thin duplication, but usage distribution shows an active command/query migration rather than dead code.
- **Intended purpose:** Separate mutation contracts from richer legacy read/write service contracts without a flag-day migration.
- **Confidence:** High.
- **Risk of removal:** High until all callers are migrated.
- **Dependencies/dynamic use:** DI registrations, API controller signatures, agent coordination, Sales campaign scheduling, internal tools, and compatibility tests.
- **Recommended action:** Retain now. Document the migration end state; migrate remaining command callers; then decide whether `CompanyTaskService` should implement command/query interfaces directly or the adapter should remain an anti-corruption layer.
- **Required checks:** DI resolution, task CRUD/subtask/reassign integration tests, agent-tool tests, Sales scheduling tests, authorization, audit, and idempotency.

### R3 — Provider-neutral and legacy finance integration paths

- **Path/section:** Finance integration entities/services, Fortnox-specific adapters and token stores, startup/configuration compatibility paths, and Finance facade partials.
- **Classification:** Uncertain/future use and compatibility.
- **Evidence:** Architecture rules explicitly allow capability-owned partials where compatibility requires an existing Finance interface or route facade. Current UI and recent working-tree changes show an active migration to centrally configured provider applications with per-company authorizations.
- **Intended purpose:** Move from Fortnox-specific connection logic to provider-neutral integrations without breaking existing companies or tokens.
- **Confidence:** High that both paths are currently intentional.
- **Risk of removal:** High: token loss, reconnect loops, or broken supplier/invoice workflows.
- **Dependencies/dynamic use:** Encrypted token stores, OAuth callback routes, options binding, provider registry/DI, outbox work, reconciliation, and existing database rows.
- **Recommended action:** Retain; add migration-state telemetry and a documented cutover criterion. Deprecate legacy configuration only after all stored connections are migrated and rollback is tested.
- **Required checks:** Existing-token upgrade, reconnect, callback, multi-tenant isolation, supplier/invoice write, retry/idempotency, reconciliation, and local plus Docker SQL Server restore.

### R4 — Explicit Web offline stores

- **Path/section:** offline stores and `_useOfflineMode` branches across Web service clients; offline-mode selection in `VirtualCompany.Web/Program.cs`.
- **Classification:** Uncertain/future use, not proven unused.
- **Evidence:** Offline behaviour is wired through DI/configuration and appears in many typed clients. Architecture permits deterministic explicit offline mode but prohibits silently serving mock data in production.
- **Intended purpose:** Local/demo/test operation without the API.
- **Confidence:** Medium.
- **Risk of removal:** High for local/demo workflows; risk of retention is also material if production can enable it accidentally.
- **Dependencies/dynamic use:** Environment configuration, startup selection, tests, demo data, and deployment settings.
- **Recommended action:** Retain while its product purpose exists; document the configuration and enforce a production guard. If no supported scenario remains, deprecate as a separate product decision.
- **Required checks:** Environment matrix, production configuration validation, startup logs, negative test proving production cannot silently fall back, and explicit offline-mode integration tests.

### R5 — Active planning/design documents and repeated module boilerplate

- **Path/section:** `docs/marketing.md`, `docs/marketing-prompts.md`, `docs/campaign.md`, untracked `docs/campaing-prompts.md`, `docs/design/references/**`; per-module `GlobalUsings.cs`, `AssemblyInfo.cs`, simple capability `.csproj` files, and MAUI platform bootstrap files.
- **Classification:** Uncertain/future use; superficially similar but not meaningful duplicate functionality.
- **Evidence:** The working tree contains active Marketing/campaign implementation. Design references are required by the documented screenshot-first workflow. Identical module/bootstrap files preserve assembly and platform boundaries. The misspelled `campaing-prompts.md` has no references, but content and timestamps indicate planning rather than obsolescence.
- **Intended purpose:** Planned capability delivery, visual reference, module metadata, and platform entry points.
- **Confidence:** High that these should not be deleted for line-count reduction.
- **Risk of removal:** Medium–high.
- **Dependencies/dynamic use:** Human workflows, code-generation prompts, compiler conventions, reflection/assembly metadata, and platform build tooling.
- **Recommended action:** Retain. Consider renaming the misspelled document with link updates after human confirmation; keep small module/platform files independent.
- **Required checks:** Documentation link search, owner confirmation, project builds, reflection/architecture tests, and platform builds.

### R6 — Test projects omitted from the solution

- **Path/section:** `tests/VirtualCompany.SalesSource.Tests/VirtualCompany.SalesSource.Tests.csproj` and `tests/VirtualCompany.SupportGrounding.Tests/VirtualCompany.SupportGrounding.Tests.csproj`; absent from `VirtualCompany.sln`.
- **Classification:** Appears unused by the solution but is likely active/in-progress functionality.
- **Evidence:** Both are valid tracked test projects aligned with documented Sales-source and Support-grounding ownership, but `dotnet sln VirtualCompany.sln list` omits them. Their absence explains why a solution-only baseline cannot validate these capabilities.
- **Intended purpose:** Capability-owned tests that enforce module boundaries and behaviour.
- **Confidence:** High.
- **Risk of removal:** High; removal would reduce coverage for active capabilities.
- **Dependencies/dynamic use:** CI may invoke the projects directly even though the solution does not. Local solution builds currently miss them.
- **Recommended action:** Retain and add them to the solution and normal CI test matrix, unless a separate solution/filter is documented as authoritative.
- **Required checks:** Build/test each project independently; inspect CI scripts; add to solution; confirm test discovery and runtime; run architecture tests.

## 4. Uncertain items requiring human confirmation

### U1 — Tenant-specific local object-store data is tracked

- **Path/section:** `src/VirtualCompany.Api/App_Data/object-storage/companies/43e6a825d1b7429a86087e668087d005/knowledge/**`; configuration at `src/VirtualCompany.Api/appsettings.json:28` and `appsettings.Development.json:16`; implementation `LocalCompanyDocumentStorage.cs`.
- **Classification:** Possibly obsolete runtime state, possibly an undocumented demo fixture.
- **Evidence:** Five files are tracked under a concrete tenant UUID. Several duplicate canonical root knowledge documents. `App_Data/object-storage` is the configured local runtime storage location; exact tenant paths are not referenced in source. `.gitignore` does not exclude it.
- **Intended purpose:** Runtime company knowledge storage; possibly captured demo data.
- **Confidence:** Medium.
- **Risk of removal:** Medium because clean startup/demo seeding may rely on the captured state even though code references are absent.
- **Dependencies/dynamic use:** Runtime path construction, seeded tenant IDs, local demos, tests, and document ingestion.
- **Recommended action:** Confirm whether these are fixtures. If runtime state, remove from Git and ignore `src/VirtualCompany.Api/App_Data/`; if fixtures, move to a named test/demo fixture location and seed explicitly.
- **Required checks:** Clean-clone startup, company knowledge seeding, document retrieval tests, demo workflow, and tenant-isolation checks.

### U2 — Hansa House media/model work under the generic artifact root

- **Path/section:** `artifacts/hansa-house/**` (Blender files, GLB, images, and scripts).
- **Classification:** Uncertain ownership; potentially obsolete or unrelated.
- **Evidence:** No Virtual Company source references were found. The content differs from the surrounding generated .NET artifacts and appears to be a separate 3D/visual project.
- **Intended purpose:** Unknown; possibly an external visual deliverable or experiment.
- **Confidence:** Medium that it is unrelated to runtime, low that it is safe to delete.
- **Risk of removal:** High because binary/source artwork may not be reproducible.
- **Dependencies/dynamic use:** External design workflows, manual delivery, or another repository may consume it.
- **Recommended action:** Ask the owner. If unrelated, move it to its owning repository or archival storage before cleaning the generated `artifacts/` root.
- **Required checks:** Owner confirmation, Git history, issue/task links, checksums/backups, and external consumer search.

### U3 — Paul and Philip images are referenced but not Web-served

- **Path/section:** root `images/paul.png` and `images/philip.png`; references in `PublicInquiry.razor:27`, `ExecutiveCockpitDashboard.razor:474–483`, and `NavMenu.razor:395,486`; `src/VirtualCompany.Web/wwwroot/images` currently contains only `laura.png`.
- **Classification:** Unreachable/misplaced assets, not unused assets.
- **Evidence:** Web pages request `/images/paul.png` and `/images/philip.png`, but the files live outside `wwwroot` and the Web project has no content-link mapping for the root directory. This likely yields 404s. There is also identity drift: `paul.png` is displayed as Alex in at least one surface.
- **Intended purpose:** Sales and Operations agent avatars.
- **Confidence:** High that current static-file placement is wrong; medium on the correct names/identities.
- **Risk of removal:** Medium. Deleting them would preserve the broken state and lose intended artwork.
- **Dependencies/dynamic use:** Static-file middleware, avatar fallback maps, user-visible agent identity, and design references.
- **Recommended action:** Do not delete. Confirm identity mapping, move/copy canonical assets under `wwwroot/images` or explicitly link them as content, and remove obsolete root copies only after validation.
- **Required checks:** HTTP 200 for both URLs, visual checks of Navigation/Public Inquiry/Executive Cockpit, accessible alt text, and identity-name review.

### U4 — PostgreSQL and runtime SQLite provider support

- **Path/section:** `VirtualCompany.Infrastructure.Platform.csproj:18–19`, database provider selection using `UseSqlite`/`UseNpgsql`, and SQLite-specific handling in Operations.
- **Classification:** Uncertain compatibility/possible obsolete configuration.
- **Evidence:** Architecture declares SQL Server authoritative and SQLite a test provider. The Platform module still carries Npgsql and runtime provider selection, while Operations contains provider-specific exception handling. Static references prove the code is reachable when configured, but not whether those deployments remain supported.
- **Intended purpose:** Test portability and possibly legacy/runtime multi-provider support.
- **Confidence:** Medium.
- **Risk of removal:** High without a deployment inventory; removing a provider can break tests, local workflows, or existing installations.
- **Dependencies/dynamic use:** Configuration binding, EF provider services, migrations, SQL dialect differences, tests, and external deployments.
- **Recommended action:** Investigate and document the supported provider matrix. If PostgreSQL is unsupported, deprecate it with configuration validation and release notes before package/code removal. Keep SQLite test support where architecture-compatible.
- **Required checks:** Search deployment manifests and secrets/config stores; inspect CI; run SQL Server and Docker restore tests; run SQLite-compatible tests; obtain operator confirmation.

## Items examined but not recommended for consolidation

- `InvoiceReviewDetailPanel.razor` and `InvoiceReviewDetailContent.razor` share formatters and recommendation/history concepts, but they serve different interaction contexts: a compact split-view panel and a full detail page. Extracting tiny formatting helpers is reasonable; merging the components would weaken layout clarity and increase conditional complexity.
- Accounts payable (`BillsPage`) and accounts receivable (`InvoicesPage`) are different domain concepts. Only their provider-payment parsing/projection should be shared.
- Per-module `GlobalUsings`, `AssemblyInfo`, project files, and MAUI platform entry points may be textually identical, but they encode real assembly/platform boundaries.
- Provider-specific Fortnox code and provider-neutral Finance abstractions are not exact duplicates; they form an adapter boundary and migration path.

## Proposed removal order

1. **Establish and record a clean baseline.** Provision MAUI workloads on the mobile build agent or split mobile from the default non-mobile solution baseline; restore NuGet audit access; triage or explicitly baseline current Web/API/contract failures; complete `dotnet format --verify-no-changes`; run omitted SalesSource and SupportGrounding projects; run SQL Server and Docker validation where required.
2. **Add missing guard tests.** Add artifact-path/repository hygiene checks if desired; publish-content tests for MailKit; static-asset HTTP tests for agent images; and project/test-discovery checks ensuring every intended test runs exactly once.
3. **Cleanup group A — temporary diagnostics.** Convert any valuable repro to regression tests, remove `.codex_tmp*`, add ignore rule, build, and run focused tests. Keep this as one reversible commit.
4. **Cleanup group B — generated outputs.** First relocate/confirm `hansa-house` and any intentional release artifacts. Then untrack `.artifacts`, `.test-bin`, generated `artifacts/*` subtrees, and tracked log output; add narrow ignore rules; reproduce build/publish/test output from a clean clone. Do not combine this with source refactors.
5. **Cleanup group C — duplicate/manual artifacts.** Remove the root duplicate `laura.png` and confirmed-obsolete migration SQL in separate commits. Validate Web assets and SQL Server/Docker migration flow respectively.
6. **Cleanup group D — package references.** Remove unused API-test MailKit, verify; remove API MailKit, verify build/publish/mailbox integration. Assess `Web.Tests` SQLite separately, because Web contract tests demonstrably compile linked SQLite fixtures and must retain their direct dependency unless test ownership is moved.
7. **Refactor group E — test ownership.** Move Web and contract test sources to their owning projects, compare test lists, and run each suite. Do not mix this with fixes for currently failing tests.
8. **Refactor group F — Web transport/error handling.** Migrate one typed client at a time to `ICompanyApiTransport`, preserving typed error semantics and offline behaviour. Add contract and tenant-isolation tests before each migration.
9. **Refactor group G — Finance presentation.** Extract the narrow payment-summary parser/projector and validate AP/AR edge cases; leave page-specific workflows separate.
10. **Configuration group H — central versions and provider matrix.** Centralize only agreed package versions. Address PostgreSQL/runtime SQLite only after deployment-owner confirmation.
11. **Repeat reference searches after every group.** Include source, project files, DI registration, configuration/options, Razor routes, reflection/assembly scanning, serialization names, scripts, CI, documentation, persisted links, and publish manifests.
12. **Stop before uncertain deletion.** Require human confirmation for tenant `App_Data`, Hansa House content, provider support, compatibility routes, offline mode, and finance/task migration seams.

Every cleanup group should be a small, reversible commit with a before/after test-list and validation record.

## Validation plan

### Baseline and repository hygiene

- Run `dotnet restore` with NuGet audit access.
- Run a non-mobile solution build and a separately provisioned MAUI build.
- Run every test project, including SalesSource and SupportGrounding, and archive exact pass/fail counts.
- Run `dotnet format --verify-no-changes` and configured analyzers.
- Verify a clean clone contains no required binary that can only be obtained from the removed artifact folders.

### Reference and dynamic-use checks

- Use case-insensitive `rg` searches across `*.cs`, `*.razor`, `*.csproj`, `*.props`, `*.targets`, JSON/YAML, PowerShell, shell scripts, Docker/CI files, docs, and manifests.
- Inspect DI registrations (`AddScoped`, `AddSingleton`, keyed/named registrations), assembly scanning, reflection, route attributes/`@page`, JSON polymorphism/source generation, options binding, and provider registries.
- Compare `dotnet test --list-tests` before and after test moves.
- Compare API/Web publish manifests before and after dependency removal.

### Behavioural checks

- Tenant isolation and company-header/correlation propagation for every migrated Web client.
- Safe localized problem details and typed domain exceptions.
- AP/AR payment projection across paid, partial, unpaid, cancelled, credit, malformed, and missing-provider cases.
- Compatibility-route redirects/deep links and persisted notification/work-item links.
- OAuth reconnect, token migration, outbox/idempotency/reconciliation, and operator-visible errors for Finance providers.
- SQL Server migration/restore locally and in Docker before removing migration-related material.
- Static asset HTTP and screenshot checks following `docs/design.md`.

## Open questions requiring confirmation

1. Is `artifacts/hansa-house` part of Virtual Company, a deliverable for another project, or disposable experimental work?
2. Are the tenant-specific files under `src/VirtualCompany.Api/App_Data` intended demo fixtures, or accidental runtime state?
3. Are PostgreSQL deployments supported today, or should Npgsql support enter a documented deprecation cycle?
4. Is runtime SQLite supported outside tests, or should configuration reject it except in test/local harnesses?
5. What telemetry and client-version threshold permits removal of compatibility routes and Finance/task facades?
6. Is offline mode a supported product/demo mode? In which environments must it be impossible to enable?
7. Should `paul.png` represent Paul or Alex, and is `philip.png` the intended Operations identity?
8. Are SalesSource and SupportGrounding tests intentionally excluded from the solution because CI runs them separately, or is the omission accidental?
9. Should the canonical documentation names remain under `docs/`, or should root `architecture.md` and `design.md` forwarding files be added to match contributor requests?
10. Are the current Web/API/contract failures accepted known failures, or must they be fixed before any cleanup branch is opened?

## Conclusion

The repository has meaningful cleanup value, especially in committed generated output and temporary diagnostics, but most apparent architectural duplication is intentional boundary or migration code. The safest strategy is to make the baseline trustworthy first, remove only reproducible artifacts in narrowly scoped commits, then consolidate transport, error parsing, test ownership, and payment presentation with targeted behavioural tests. Compatibility, provider, offline, and active feature work should remain until ownership and deprecation criteria are explicit.
