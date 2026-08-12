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


## Prompt 7 — Persist subscription agreement intake proposals

### Title and outcome

Implement durable supplier-subscription agreement intake proposals so inbound contracts and recurring-service messages can be reviewed before they become subscriptions. This lets Laura surface likely agreements from the finance inbox without activating terms from untrusted document text.

### Current context

Prompts 1-6 delivered `SupplierSubscription`, `SupplierSubscriptionBillMatch`, `ISupplierSubscriptionService`, authorized APIs, the supplier-subscriptions workspace, and bill-detail match evidence. Mailbox scanning currently persists `EmailIngestionRun`, `EmailMessageSnapshot`, and `EmailAttachmentSnapshot` in `VirtualCompany.Domain/Entities/MailboxConnectionEntities.cs` and extracts supplier-bill candidates through `ManualInboxBillScanOrchestration`, `IDocumentExtractionService`, and `BillInformationExtractor`. `subscriptions.md` explicitly says contract ingestion is a later classifier and must create a draft proposal for human confirmation.

### Dependencies

Prompts 1-6.

### Implementation requirements

- Add a company-owned `SupplierSubscriptionIntakeProposal` entity that records the source email snapshot, source attachment snapshot, optional document ID, detected supplier evidence, proposed commercial terms, classification status, review status, confidence, evidence summary, safe failure summary, and timestamps.
- Add typed storage values for proposal classification and review states, preserving stable lowercase values such as `detected`, `needs_review`, `accepted`, `rejected`, `failed`, and `duplicate`.
- Store queryable proposed terms in relational columns: supplier name/org number, matched counterparty ID when available, agreement name, currency, expected amount, cadence, billing day, start/end dates, next expected bill date, amount tolerance, date tolerance, notice period, auto-renewal, contract reference, and description.
- Link accepted proposals to the resulting `SupplierSubscription` without deleting proposal evidence.
- Add SQL Server-compatible EF configuration, indexes for company/status/source/deduplication, DbSet registration, query filters, migration, and model snapshot update in `VirtualCompany.Persistence.Migrations` using `VirtualCompany.Api` as startup.
- Add application contracts for listing, reading, accepting, rejecting, and retrying intake proposals. Acceptance must call the existing subscription creation path or a shared domain factory so validation and audit behavior remain identical to manual creation.
- Persist business audit events for proposal detection, failure, acceptance, rejection, duplicate suppression, and retry.

### Constraints and preservation rules

- A proposal is untrusted evidence, not a subscription. It cannot activate a subscription, approve bills, create supplier bills, or execute provider actions by itself.
- Do not store full contract bodies, full email bodies, secrets, tokens, or provider payloads in the proposal. Keep bounded snippets and references to snapshots/documents.
- Preserve company-scoped ownership and verify all source snapshots, counterparties, documents, and resulting subscriptions belong to the same company.
- Keep the proposal model provider-neutral; mailbox providers and Fortnox schemas must not leak into Domain.
- Preserve local SQL Server and Docker SQL Server restore/run compatibility; use EF migrations only and no startup DDL.

### Acceptance criteria

- Given an inbound contract source, when a proposal is persisted, then the source email/attachment references, proposed terms, confidence, and evidence summary are company-scoped and queryable.
- Given the same source attachment is processed repeatedly, when deduplication runs, then no duplicate active proposal is created and the original proposal remains auditable.
- Given a proposal from company A, when company B tries to read, accept, reject, or retry it, then access is rejected without disclosing the proposal.
- Given an accepted proposal, when it creates a draft supplier subscription, then the proposal records the resulting subscription ID and cannot be accepted again.
- Given invalid proposed terms, when acceptance is attempted, then the existing subscription validation errors are returned safely and the proposal remains reviewable.

### Verification

- Add focused Domain/Persistence/Finance tests for proposal validation, company isolation, deduplication, acceptance idempotency, and audit events.
- Generate and inspect the SQL Server migration and model snapshot.
- Run `dotnet ef migrations has-pending-model-changes`.
- Build Domain, Application, Persistence, Infrastructure.Finance, and API.

### Definition of done

Durable proposal storage, contracts, EF migration, audit, tests, and Docker/local SQL Server compatibility are complete with no mock production data, startup DDL, silent failure, raw provider payload storage, or deferred in-scope TODOs.

## Prompt 8 — Classify inbox documents and extract subscription terms

### Title and outcome

Add a production classifier and extractor that detects supplier subscription agreements or recurring-service receipts from mailbox/document text and turns them into safe reviewable proposals. This gives Laura the ability to discover subscriptions from finance inbox evidence while preserving human approval.

### Current context

Prompt 7 supplies the proposal storage and commands. Existing document extraction contracts in `VirtualCompany.Application/Finance/DocumentExtractionContracts.cs` are bill-specific, and `DocumentExtractionService` currently returns normalized bill candidates. Mailbox snapshots store untrusted body and attachment text. The shared agent/AI orchestration rules require feature modules to use approved orchestration interfaces rather than calling LLM providers directly. Deterministic extraction should be preferred where possible, and AI output must be treated as advisory evidence.

### Dependencies

Prompt 7.

### Implementation requirements

- Add focused Finance application contracts for subscription document classification and term extraction, separate from bill extraction contracts.
- Implement deterministic pre-classification using document names, subjects, body text, attachment text, and Swedish/English recurring-agreement terms such as subscription, agreement, contract, renewal, notice period, monthly, quarterly, yearly, abonnemang, avtal, förnyelse, and uppsägningstid.
- Extract proposed terms from email body and attachments: supplier name, organization number/tax ID, contract reference, expected amount, currency, cadence, billing day, start date, renewal/end date, notice period, auto-renewal, and concise evidence snippets.
- Match extracted supplier identity to existing `FinanceCounterparty` suppliers by company-scoped exact/normalized name, tax ID, and email evidence. Ambiguous supplier matches must remain reviewable.
- Use the shared AI orchestration boundary only as an optional explanation or extraction assist when deterministic evidence is insufficient; require structured output, bounded inputs, guardrails, confidence, and safe failure handling.
- Classify recurring-payment receipts separately from agreements. Receipts may become match evidence for existing subscriptions or review proposals, but they must not create or activate a subscription without human review.
- Persist proposal outcomes through the Prompt 7 proposal service with deterministic idempotency keys derived from company, source snapshot/attachment, source document ID, and normalized supplier/reference evidence.
- Add safe technical logging for classification decisions, confidence, candidate counts, and failures without logging full document bodies or sensitive payloads.

### Constraints and preservation rules

- Do not modify the authoritative supplier-bill extraction behavior except through explicit shared helper abstractions where necessary.
- Do not classify a contract as a supplier bill or send contracts into bill approval queues.
- Do not let AI output directly create active subscriptions, approve bills, update Fortnox, or initiate payments.
- Keep every classification and extraction result company-scoped, deterministic where possible, bounded, retryable, and auditable.
- Preserve existing mailbox duplicate handling and attachment text hydration behavior.

### Acceptance criteria

- Given an email attachment that looks like a subscription agreement, when classification runs, then a `needs_review` proposal is created with extracted terms and source evidence.
- Given a recurring-service receipt for an existing supplier, when classification runs, then it is treated as receipt evidence and does not create an active subscription automatically.
- Given a normal one-off supplier invoice, when classification runs, then the existing bill candidate flow remains unchanged and no agreement proposal is created.
- Given ambiguous supplier identity or missing amount/cadence, when extraction runs, then the proposal is created as reviewable with clear missing-field evidence rather than failing silently.
- Given repeated processing of the same message and attachment, when classification runs again, then the same proposal is reused or marked duplicate without creating another review item.

### Verification

- Add focused Finance tests for classification, deterministic term extraction, Swedish/English terminology, supplier matching, ambiguous suppliers, receipt-vs-agreement distinction, idempotency, and safe failure output.
- Add AI orchestration boundary tests or fakes that verify structured output handling without calling external providers.
- Build Application and Infrastructure.Finance.
- Run existing bill extraction tests to prove invoices still behave as before.

### Definition of done

Subscription agreement/receipt classification and extraction are implemented as production code with safe proposal persistence, bounded evidence, tenant isolation, idempotency, audit, tests, and no direct provider/LLM bypass or mock production data.

## Prompt 9 — Integrate subscription discovery into mailbox intake and review APIs

### Title and outcome

Run subscription discovery as part of finance mailbox intake and expose review APIs so discovered agreements can be accepted, rejected, or retried by authorized finance users.

### Current context

Prompt 8 supplies classification and extraction. Manual mailbox scanning is orchestrated by `ManualInboxBillScanOrchestration`, while connected mailbox runs are represented by `ConnectedMailboxInboxScanOrchestration` and company mailbox services. Existing mailbox pages show scanned messages and detected bill links. Subscription APIs live under `InternalFinanceController.SupplierSubscriptions.cs`, and typed Web client methods live under `FinanceApiClient.SupplierSubscriptions.cs`.

### Dependencies

Prompts 7 and 8.

### Implementation requirements

- Invoke the subscription document classifier after mailbox body/attachment text is hydrated and snapshot persistence is stable, for both manual scans and connected inbox scans where the same snapshot data is available.
- Ensure discovery runs after, and independently from, supplier-bill candidate detection so a bill ingestion failure does not erase contract proposal evidence and a contract proposal failure does not block bill intake.
- Add idempotent background-safe execution with bounded retries for temporary extraction/classification failures and terminal safe failure summaries for permanent validation problems.
- Expose authorized internal routes for listing proposals, reading proposal detail, accepting a proposal into a draft supplier subscription, rejecting a proposal with a reason, retrying failed proposal extraction, and linking receipt evidence to an existing subscription when backend policy allows it.
- Use Finance view for proposal reads and Finance approval for acceptance, rejection, retry, and receipt-link mutations.
- Map validation/not-found/conflict/failure states to safe problem responses with correlation IDs.
- Add mailbox scanned-message response fields or related endpoints that indicate whether a message produced a subscription proposal, without exposing raw provider payloads or untrusted full text.
- Write audit events for intake execution and review decisions, including source message/attachment references and outcome.

### Constraints and preservation rules

- Controllers remain transport-only and must not query EF or implement extraction rules.
- Mailbox provider adapters remain responsible only for mailbox access and attachment fetches; they must not own Finance subscription behavior.
- Intake must be idempotent under repeated scans, duplicate provider messages, and duplicate attachment hashes.
- Existing mailbox bill detection, supplier bill review, Fortnox sync, approval, payment, and settlement behavior must remain compatible.
- Do not require external provider credentials for deterministic local tests.

### Acceptance criteria

- Given a connected finance mailbox scan with a contract attachment, when the scan completes, then a subscription proposal is visible for finance review and the scan result remains successful.
- Given a manual scan finds both a bill and a subscription agreement in separate attachments, when processing completes, then the bill candidate and agreement proposal are both preserved and independently reviewable.
- Given a classifier failure on one attachment, when the rest of the scan completes, then other messages are processed and the failed proposal/source has an actionable safe status.
- Given a view-only finance user, when proposal APIs are called, then reads succeed and accept/reject/retry are denied.
- Given duplicate mailbox scans, when the same source is processed again, then no duplicate accepted subscription or duplicate active proposal is created.

### Verification

- Add API integration tests for proposal authorization, validation, tenant isolation, idempotent acceptance, safe errors, and scanned-message proposal indicators.
- Add focused mailbox orchestration tests for manual and connected scan integration, duplicate source handling, independent bill/proposal outcomes, and retry/failure paths.
- Build API, Infrastructure.Mailbox, Infrastructure.Finance, and Web client contracts.
- Run relevant Finance, Mailbox, API, and contract tests.

### Definition of done

Mailbox subscription discovery and review APIs are production-ready with idempotent background-safe execution, authorization, audit, safe errors, tenant isolation, and no regression to bill intake or provider synchronization.

## Prompt 10 — Add subscription discovery review UI and source evidence

### Title and outcome

Extend the finance UI so users can review inbox-discovered subscription proposals, accept them into draft subscriptions, reject bad detections, and see the source agreement evidence on subscription detail pages.

### Current context

Prompts 7-9 supply proposal storage, classifier/extractor, mailbox integration, and APIs. The supplier-subscriptions workspace is implemented in `VirtualCompany.Web/Pages/Finance/SupplierSubscriptionsPage.razor`, with a supplier picker and typed client methods in `FinanceApiClient.SupplierSubscriptions.cs`. The finance mailbox page exists at `VirtualCompany.Web/Pages/Finance/MailboxPage.razor` and currently presents scanned messages and detected supplier bills. UI work must follow `ui-instructions.md` and `/docs/design.md`, including screenshot-first generation.

### Dependencies

Prompts 7, 8, and 9.

### Implementation requirements

- Explicitly write an image-generation prompt based on `/docs/design.md`, the existing supplier-subscriptions page, and the new discovery review requirements.
- Generate and save `/docs/design/references/supplier-subscription-discovery-reference.png` before UI code changes.
- Add typed Web client request/response contracts and methods for proposal list/detail, accept, reject, retry, and receipt-link operations.
- Extend the supplier-subscriptions workspace with a review queue for discovered agreements, showing supplier evidence, proposed terms, confidence, source, missing fields, status, and recommended action.
- Add proposal detail/review UI that lets an authorized user adjust terms, choose the supplier with the existing picker, accept into a draft subscription, reject with a plain-English reason, or retry a failed extraction.
- Show source agreement evidence on subscription detail pages: source document/email, accepted proposal, evidence snippets, extracted terms, reviewer, and accepted/rejected audit status.
- Extend the finance mailbox page so scanned messages can link to the subscription proposal when one was detected, without changing existing detected-bill links.
- Add loading, empty, error, unauthorized, validation, conflict, accepted, rejected, duplicate, and failed states using plain English and existing design tokens.

### Constraints and preservation rules

- UI must not implement extraction, matching, lifecycle, authorization, or acceptance policy. It consumes backend state and allowed actions.
- Do not show raw GUIDs, raw enums, full email bodies, provider payloads, or untrusted contract text as authoritative facts.
- Existing supplier-bill review, payment, Fortnox, and mailbox controls remain reachable and unchanged.
- The generated reference screenshot is design guidance only and must not be shipped as a UI asset.
- Keep responsive behavior within the existing finance list/detail pattern and avoid a new primary navigation destination.

### Acceptance criteria

- Given discovered agreement proposals, when the subscriptions page loads, then Laura shows a review queue with clear proposed terms, confidence, source evidence, and next actions.
- Given a proposal with missing or ambiguous supplier evidence, when reviewed, then the user can select the correct supplier before acceptance and cannot save without required fields.
- Given an accepted proposal, when the subscription detail opens, then source agreement evidence and accepted proposal history are visible.
- Given a rejected or duplicate proposal, when viewed later, then the decision and reason remain visible and no active subscription is created.
- Given a mailbox scanned message with a detected subscription proposal, when the mailbox page renders, then it links to the proposal without breaking the existing supplier-bill link.
- Given a narrow viewport, when the proposal queue and detail render, then content stacks without overlap or horizontal page overflow.

### Verification

- Add Web client and component/page tests for proposal routes, serialization, review actions, validation, loading/error/empty states, mailbox links, and source evidence presentation.
- Build Web and run focused Web tests.
- Use the repository-safe startup rules only if browser verification is required, then compare the built UI to the saved reference and refine spacing, hierarchy, and responsive behavior.

### Definition of done

The discovery review UI, typed client, mailbox links, subscription source evidence, tests, and visual reference are complete with no mock production data, raw technical identifiers, silent failures, inaccessible states, or unfinished UI paths.

## Prompt 11 — Validate subscription discovery production readiness

### Title and outcome

Complete migration, intake, security, regression, and operational validation for inbox-discovered supplier subscriptions and recurring-payment receipt evidence.

### Current context

Prompts 7-10 add proposal storage, document classification/extraction, mailbox integration, authorized APIs, and UI. Existing supplier subscription matching from Prompts 1-6 remains authoritative for linking actual supplier bills to agreements. SQL Server is the production provider and Docker SQL Server must remain an equivalent restore/run path.

### Dependencies

Prompts 7-10.

### Implementation requirements

- Run focused unit, integration, authorization, tenant-isolation, idempotency, mailbox orchestration, extraction, Web client, and component tests.
- Build the affected project graph or full solution.
- Run EF pending-model-change validation and inspect the migration/snapshot.
- Verify Docker SQL Server migration/update compatibility and document exact commands, including the PowerShell-safe dotnet-ef invocation.
- Verify contracts are not routed into supplier-bill approval queues and receipt evidence does not bypass subscription review, bill approval, Fortnox, payment, or settlement policies.
- Verify all new failures are operator-visible through proposal status, safe API problem responses, logs, and audit where appropriate.
- Update `docs/finance-supplier-subscriptions.md` and any mailbox operations documentation with discovery behavior, review workflow, failure recovery, duplicate handling, Docker/local SQL migration steps, and known external-provider test boundaries.
- Search new routes, DI registrations, DbSets, configurations, migrations, serialization contracts, localization/user-facing text, and dynamic usages for missed references.

### Constraints and preservation rules

- Do not reset or overwrite unrelated user changes.
- Do not require Docker, mailbox credentials, or live LLM/provider credentials for deterministic local tests; categorize credentialed checks as external integration tests.
- Do not weaken, skip, or delete valid existing tests to make discovery pass.
- Do not log or document secrets, full contract bodies, full email bodies, or provider payloads.

### Acceptance criteria

- Given the final model, when `dotnet ef migrations has-pending-model-changes` runs, then no pending model change remains.
- Given Docker SQL Server, when migrations are applied, then proposal and subscription tables share the same migration history and the app starts against the migrated database.
- Given repeated mailbox scans, duplicate provider messages, and duplicate attachment hashes, when discovery runs, then only one active proposal or accepted subscription is produced for the same source.
- Given a normal supplier bill, a subscription agreement, and a recurring receipt, when mailbox intake processes them, then each enters the correct review/evidence path without blocking the others.
- Given cross-company proposal, source snapshot, supplier, subscription, and bill references, when APIs and services are exercised, then no data leaks or cross-company mutation occurs.
- Given accepted proposal evidence and later matching bills, when the subscription detail and bill detail pages render, then source agreement evidence and recurring bill evidence are both visible and existing approvals remain required.

### Verification

- Record exact commands and results for builds, tests, EF validation, and Docker SQL Server migration/update checks.
- Include focused evidence that pre-existing supplier-bill, mailbox, Fortnox, approval, payment, and settlement behavior still passes or remains unchanged.
- If full-suite failures are unrelated and pre-existing, prove that with focused passing tests and report residual risk clearly.
- Review `git diff` only for intended subscription-discovery changes and document any unavoidable overlap with existing dirty files.

### Definition of done

Inbox-discovered supplier subscriptions are production-ready, migrated, documented, tested, and build-verified with human review, tenant isolation, idempotency, audit, safe errors, Docker/local SQL compatibility, no approval bypass, no provider leakage, and no deferred in-scope TODOs.
