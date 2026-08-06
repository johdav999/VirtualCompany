# Supplier subscription implementation prompts

Run these prompts in order. Every prompt must follow `AGENTS.md`, `production-implementation.md`, and `/docs/architecture-rules.md`. `architecture-inst.md` is referenced by the repository instructions but is not present; use the mandatory architecture rules and record that absence rather than inventing instructions. UI prompts must also follow `ui-instructions.md` and `/docs/design.md`, including screenshot-first generation. The functional design is authoritative in `subscriptions.md`.

## Prompt 1 — Persist supplier agreements and billing-period matches

### Title and outcome

Implement the supplier-subscription domain and SQL Server schema so Virtual Company can persist an agreement, its next expected bill, and links to actual supplier bills. This creates the durable foundation without changing existing bill approvals.

### Current context

Finance entities live one-per-file in `VirtualCompany.Domain/Entities`. The shared `VirtualCompanyDbContext` and assembly-discovered configurations live in `VirtualCompany.Persistence`. SQL Server migrations and the snapshot live in `VirtualCompany.Persistence.Migrations`. `FinanceCounterparty` is the supplier master and `FinanceBill` is the existing supplier accounting document. No supplier-subscription aggregate currently exists.

### Dependencies

None.

### Implementation requirements

- Add company-owned `SupplierSubscription` and `SupplierSubscriptionBillMatch` entities with the fields, validation, lifecycle, cadence, and schedule-advancement behavior defined in `subscriptions.md`.
- Add typed status, cadence, match-status, and match-method constants or enums in the owning Domain area; preserve stable lowercase storage values.
- Configure both entities with SQL Server-compatible lengths, money precision, dates, relationships, indexes, and unique constraints.
- Add DbSets and established global company query filters.
- Add an EF Core migration and update the model snapshot through `VirtualCompany.Persistence.Migrations` using the API startup project.
- Preserve local SQL Server and Docker SQL Server restore/run compatibility; add no startup DDL.

### Constraints and preservation rules

- Domain has no Infrastructure/API/Web dependency.
- Every relationship and queryable business value is relational, not hidden in JSON.
- Existing FinanceBill, FinanceCounterparty, mailbox, approval, payment, and Fortnox behavior remains unchanged.
- Do not hard-delete agreement or match history.
- Do not weaken tenant filters or existing migrations.

### Acceptance criteria

- Given valid monthly terms, when an active agreement advances from a January 31 expectation, then the next expectation is the final valid day in February and cadence remains stable thereafter.
- Given an invalid amount, tolerance, company/supplier ID, cadence, status, or date range, when constructing or changing an agreement, then a deterministic validation error is produced.
- Given one bill, when a second current match is inserted, then persistence rejects the duplicate.
- Given two companies, when query filters are active, then neither company's subscriptions or matches are visible in the other's context.

### Verification

- Add focused Domain/Finance tests for validation and cadence transitions.
- Build Domain and Persistence.
- Create and inspect the SQL Server migration and snapshot.
- Run `dotnet ef migrations has-pending-model-changes`.

### Definition of done

Production entities, configurations, migration, snapshot, and tests are complete with no scaffolding, mock production data, startup DDL, silent failures, or deferred in-scope TODOs.

## Prompt 2 — Implement subscription commands, queries, deterministic matching, and audit

### Title and outcome

Implement the company-scoped Finance application service that manages agreements and deterministically connects real supplier bills to expected subscription periods.

### Current context

Prompt 1 supplies the aggregate and schema. Finance application contracts are grouped by capability under `VirtualCompany.Application/Finance/Contracts`. Finance implementations and registration belong to `VirtualCompany.Infrastructure.Finance`. Existing audit writing is available through `IAuditEventWriter`. The broad Finance read service should not gain unrelated subscription state-changing behavior.

### Dependencies

Prompt 1.

### Implementation requirements

- Add focused subscription DTOs, queries, commands, validation results, and `ISupplierSubscriptionService`.
- Implement company-scoped create/update/lifecycle commands and list/detail/bill-context queries.
- Implement deterministic candidate scoring, automatic confirmation, ambiguous suggestion, exception evidence, manual confirmation/rejection, and idempotent re-evaluation.
- Advance the schedule only after confirmed matching and never twice for the same period.
- Compute upcoming/due/missing/needs-review health from authoritative dates and matches.
- Write business audit events for every important lifecycle and match transition.
- Add structured safe logs for candidate evaluation and outcomes.
- Register the service once in the Finance module.

### Constraints and preservation rules

- A match is evidence only; it cannot approve, book, export, pay, or settle a bill.
- No provider schema or direct Fortnox call belongs in this service.
- Commands explicitly verify company ownership of supplier, bill, and optional contract document.
- Matching must be deterministic, idempotent, bounded, and safe under repeated execution.
- Do not introduce a generic policy engine or catch-all Finance service.

### Acceptance criteria

- Given one eligible active agreement and a bill inside amount/date tolerances, when evaluated repeatedly, then exactly one confirmed match exists and the schedule advances once.
- Given two equally eligible agreements, when a bill is evaluated, then no automatic confirmation occurs and reviewable suggestions explain the ambiguity.
- Given a bill outside tolerance, when evaluated, then the agreement records no confirmed period and exposes a plain-English exception.
- Given a cross-company supplier, bill, document, or match, when a command is attempted, then it is rejected without leaking the other record.
- Given a confirmed match, when existing bill approval state is inspected, then it is unchanged.

### Verification

- Add focused Finance tests for scoring, ambiguity, tolerances, idempotency, lifecycle, audit, and cross-company isolation.
- Run relevant Domain, Finance, and API tests.
- Build Application and Infrastructure.Finance.

### Definition of done

The production service, contracts, registration, audit, logs, and tests are complete; no AI-only decisions, direct provider writes, silent failures, or mock data remain.

## Prompt 3 — Expose authorized subscription APIs and integrate bill intake

### Title and outcome

Expose secure Finance endpoints and evaluate subscriptions when a supplier bill becomes an authoritative FinanceBill, so monthly bills join their agreement without a manual duplicate workflow.

### Current context

Prompt 2 supplies the service. Internal Finance routes live in capability partial controller files under `VirtualCompany.Api/Controllers`. Supplier bills can originate from mailbox bill-inbox registration and Fortnox synchronization. Controllers are transport-only, and provider synchronization remains adapter-owned.

### Dependencies

Prompts 1 and 2.

### Implementation requirements

- Add transport-only internal routes for list, detail, create, update, lifecycle, bill evaluation, match confirmation, and rejection.
- Require Finance view for reads and Finance approval for mutations.
- Resolve actor identity and correlation context using existing conventions.
- Map validation/not-found/conflict failures to safe, actionable problem responses.
- Invoke idempotent bill evaluation after authoritative bill creation/update in mailbox registration and Fortnox sync paths, without blocking successful bill ingestion on a recoverable matching failure.
- Log and expose matching failures as operator-visible Finance issues; do not swallow them.
- Add a bill subscription-context read route for UI use.

### Constraints and preservation rules

- Routes and headers are not authorization proof; server-side company authorization remains mandatory.
- Do not query EF or implement matching rules in controllers.
- Bill ingestion and synchronization remain idempotent.
- Do not directly execute external side effects from new request handlers.
- Existing bill/approval/provider routes and response contracts remain compatible.

### Acceptance criteria

- Given an authorized company finance user, when creating and activating an agreement through the API, then a company-scoped detail response is returned.
- Given a view-only user, when reading agreements, then access succeeds; when mutating, then access is denied.
- Given a bill registered twice, when intake evaluation runs twice, then one match and one schedule advancement exist.
- Given a matching-service failure, when bill intake succeeds, then the bill remains available and an actionable safe failure is logged/returned through the subscription health surface.
- Given company A, when requesting company B's subscription or bill context, then no data is disclosed.

### Verification

- Add API integration tests for authorization, validation, tenant isolation, idempotent intake, and safe errors.
- Add focused mailbox/Fortnox integration tests around the new post-ingestion hook.
- Build API and run relevant integration tests.

### Definition of done

Production endpoints and intake integration are complete with authentication, authorization, audit, observability, safe errors, no provider bypass, and no deferred in-scope TODOs.

## Prompt 4 — Build the Supplier subscriptions workspace

### Title and outcome

Add a production-grade Supplier subscriptions view where finance users can understand expected charges, missing bills, upcoming renewals, and agreement history, then take the appropriate lifecycle or matching action.

### Current context

Prompts 1-3 provide persisted data and APIs. Finance uses `FinanceModuleShell`, `FinanceSectionNav`, and list/detail page patterns. Supplier bills currently expose Bills and Review secondary views. Web clients use `ICompanyApiTransport` through typed Finance client partials. `ui-instructions.md` and `/docs/design.md` require screenshot-first implementation.

### Dependencies

Prompts 1, 2, and 3.

### Implementation requirements

- Explicitly write an image-generation prompt based on `/docs/design.md` and the subscription requirements.
- Generate and save `/docs/design/references/supplier-subscriptions-reference.png` before UI code changes.
- Add typed Web client contracts/methods using the established company-scoped transport.
- Add Subscriptions to the Supplier bills secondary navigation.
- Build a responsive list/detail workspace showing health, supplier, amount/cadence, next expected bill, term dates, last matched bill, exceptions, and clear next actions.
- Add grouped create/edit forms and activate/pause/resume/cancel actions with confirmation where appropriate.
- Add manual confirm/reject actions for suggested matches.
- Implement loading, empty, error, unauthorized, validation, and no-selection states without mock production data.
- Use existing tokens/components, Laura's Finance context, plain English, accessible labels, keyboard focus, and responsive stacking.

### Constraints and preservation rules

- UI never reimplements matching eligibility, authorization, or lifecycle policy.
- Do not expose raw enums, IDs, provider payloads, or internal workflow names.
- Do not add a primary navigation destination or a new visual framework.
- The generated screenshot is a reference only and is not shipped as the UI.

### Acceptance criteria

- Given no agreements, when the page loads, then the user sees a clear explanation and Add subscription action.
- Given active, missing, paused, and exception agreements, when listed, then each has a plain-English health label and useful next action.
- Given a selected agreement, when detail loads, then terms, next expected bill, matched history, and lifecycle actions are understandable without technical identifiers.
- Given a suggested bill match, when the user confirms or rejects it, then the backend result is refreshed and the action is auditable.
- Given a narrow viewport, when the page renders, then content stacks without horizontal page overflow and actions remain reachable.

### Verification

- Add Web client and component/page tests for routes, serialization, states, actions, and presentation.
- Build Web.
- Run the app using the repository-safe startup rules only if browser verification is required.
- Compare the implementation with the saved reference and refine spacing, hierarchy, cards, states, and responsiveness.

### Definition of done

The reference image, typed client, production UI, responsive/accessibility behavior, and tests are complete with no mock data, raw enum presentation, silent failure, or unfinished UI state.

## Prompt 5 — Show subscription evidence in supplier-bill detail

### Title and outcome

Show the agreement and covered period beside a supplier bill so reviewers understand recurring context without leaving the existing approval/payment flow.

### Current context

The supplier-bill page already has a production list/detail layout and staged right-panel actions. Prompt 3 exposes bill subscription context. Prompt 4 establishes the subscription presentation model and reference style.

### Dependencies

Prompts 1-4.

### Implementation requirements

- Load bill subscription context through the typed Finance client.
- Add a compact right-panel card for confirmed, suggested, exception, and no-match states when relevant.
- Show agreement name, supplier, expected period, expected/actual amount, variance, evidence, and a direct link to the agreement.
- Allow authorized users to confirm/reject a suggestion from the bill context; rely entirely on backend allowed actions.
- Place the card in the existing staged flow without displacing the current approval, Fortnox, payment, or settlement action.
- Add loading/error behavior that does not break the bill page.

### Constraints and preservation rules

- Existing supplier-bill approval and payment components remain authoritative.
- No automatic approval or provider action may result from rendering or confirming a match.
- Reuse the Prompt 4 reference/design language; no second unrelated visual style.

### Acceptance criteria

- Given a confirmed subscription bill, when detail opens, then its covered period and variance are visible and current bill actions are unchanged.
- Given an ambiguous suggestion, when an authorized user confirms it, then the subscription schedule updates once and the bill approval state does not change.
- Given no subscription context, when detail opens, then the existing page remains intact without a misleading subscription badge.
- Given the subscription API is temporarily unavailable, when detail opens, then a safe inline status appears and all unrelated bill actions remain usable.

### Verification

- Add component/page tests for each subscription-context state and regression tests for existing approval/payment actions.
- Build Web and run the focused Web tests.
- Perform a visual comparison against the subscription reference.

### Definition of done

Subscription evidence is integrated into the real supplier-bill detail flow with production API data, permissions, error handling, accessibility, and regression coverage.

## Prompt 6 — Validate production readiness and document operations

### Title and outcome

Complete migration, security, regression, and operational validation so supplier subscriptions can be safely deployed alongside existing Finance workflows.

### Current context

Prompts 1-5 deliver the vertical capability. The repository has a dirty working tree and existing Finance/mailbox/Fortnox behavior that must be preserved. SQL Server is authoritative, with equivalent local and Docker restore paths.

### Dependencies

Prompts 1-5.

### Implementation requirements

- Run focused unit, integration, authorization, tenant-isolation, idempotency, Web client, and component tests.
- Build the solution or the authoritative affected project graph.
- Run EF pending-model-change validation and inspect the migration/snapshot.
- Verify no startup DDL, provider schema leakage, direct provider write, approval bypass, or secret logging was introduced.
- Document subscription operation, matching evidence, exception recovery, lifecycle actions, and deployment/migration checks.
- Search all new routes, DI registrations, configurations, serialization contracts, docs, and dynamic usage for missed references.
- Resolve in-scope failures; do not weaken or delete valid tests.

### Constraints and preservation rules

- Do not clean, reset, or overwrite unrelated user changes.
- Do not require Docker for ordinary tests, but preserve and document the Docker SQL Server path.
- External integration tests requiring credentials must be explicitly categorized and must not block deterministic local coverage.

### Acceptance criteria

- Given the final model, when pending-model-change validation runs, then no change remains.
- Given the affected project graph, when built, then it succeeds without new warnings attributable to subscriptions.
- Given cross-company access attempts, repeated ingestion, ambiguous matches, and provider-independent failures, when tests run, then expected safe behavior is verified.
- Given an existing one-off supplier bill, when processed, then its behavior remains unchanged.
- Given an active subscription and matching monthly bill, when processed, then the agreement is linked, its schedule advances once, and all existing approvals remain required.

### Verification

- Record exact build/test/migration commands and results.
- If full-suite failures are unrelated and pre-existing, prove that with focused results and report them without masking the subscription result.
- Review `git diff` only for intended subscription changes and document any unavoidable overlap with existing dirty files.

### Definition of done

The full production capability is implemented, migrated, documented, tested, and build-verified with no scaffolding, mock production data, approval bypass, silent background failure, unhandled intermediate state, or deferred in-scope TODO.

