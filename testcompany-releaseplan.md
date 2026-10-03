# Test company: two-release plan

Date: 2026-10-02  
Status: Planned; implementation and verification are not yet complete.  
Source: [Test company proposal](testcompany.md)

## Scope and release boundary

Deliver the proposal in exactly two releases. Release 1 provides a persistent, usable demonstration company with real supplier-email ingestion and customer-invoice delivery. Release 2 adds the complete scenario catalogue, scheduled activity, payment and integration testing, and repeatable restart options.

Both releases use ordinary company records, screens, APIs, agents, accounting, permissions, approvals, and workflows. Synthetic business inputs are intentional test fixtures stored and processed through the application; they are not hard-coded UI data or substitutes for implemented services.

To cover the full scope, this plan assigns a bounded in-place reset to Release 2, alongside the preferred fresh-company restart. This makes concrete the proposal's later reset capability. Reset restores managed application state; it never claims to undo emails, payments, or other completed provider actions.

## Shared implementation requirements

- Follow `/production-implementation.md`, `/docs/architecture-rules.md`, and applicable `AGENTS.md` files, including `/src/AGENTS.md` and `/tests/AGENTS.md` when changing those areas.
- Follow `/docs/design.md` for UI work. Its mandatory reference-image workflow applies to the new controls page and significant redesigns. `/ui-instructions.md` is a companion, not an overriding design authority.
- Apply the architecture rules for EF Core migrations, backend authorisation, company isolation, workflow approvals, external side effects, and durable outbox execution.
- Keep cross-capability scenario coordination behind Application contracts, with implementation in the owning Operations capability. Finance and Mailbox retain their business behaviour and provider adapters. Do not add a second application stack or put workflow decisions in controllers or Blazor pages.
- Preserve existing sales demo behaviour, ordinary companies, existing data, and unrelated working-tree changes. Evolve existing demo markers and policies compatibly rather than globally enabling demo side effects.
- Audit generation, policy changes, retries, scheduling, provisioning, and resets. Keep scenario provenance separate from the normal business audit trail.
- A release is not accepted merely because a build passes. Record automated, SQL Server, browser, and live-provider evidence separately. Missing external credentials may block an acceptance gate but must not be reported as a successful test.

## Release 1 — Persistent company and complete manual finance flows

### 1. Outcome

An owner can create a persistent test company, populate it, send a generated supplier bill to its real mailbox, and create and deliver a customer invoice. The resulting work appears in ordinary business screens, with normal approvals and observable progress.

The release is independently useful for demonstrations and manual end-to-end testing. Reliability and external-action controls ship with the first sending capability.

### 2. Current context

Reuse the following inspected foundations, confirming their current behaviour before editing:

- `src/VirtualCompany.Domain/Entities/TenantEntities.cs`: existing demo identity and scenario metadata on Company.
- `src/VirtualCompany.Infrastructure.Sales/Sales/DemoScenarioService.cs`: provisioning through ordinary onboarding and existing sales-specific scenarios.
- `src/VirtualCompany.Infrastructure.Finance/Finance/CompanySimulationFinanceGenerationService.cs`: direct persisted finance-data generation, suitable for evaluated starting fixtures but not proof of ingestion.
- `src/VirtualCompany.Infrastructure.Sales/Sales/DemoTenantExternalSideEffectPolicy.cs` and `src/VirtualCompany.Infrastructure.Operations/Companies/CompanyOutboxInfrastructure.cs`: current blanket demo rejection and dispatch integration.
- `src/VirtualCompany.Infrastructure.Mailbox/Mailbox/ConnectedMailboxInboxScanOrchestration.cs`: connected mailbox processing.
- `src/VirtualCompany.Infrastructure.Finance/Finance/CustomerInvoiceDeliveryService.cs`: invoice rendering and delivery orchestration.

Existing capabilities require extension and verification; they do not already establish this release's acceptance criteria.

### 3. Dependencies

- Existing application authentication, company onboarding, SQL Server, document storage, workers, and invoice/mailbox configuration.
- A controlled supplier sender mailbox, a dedicated company finance mailbox, and controlled customer recipient addresses for live acceptance.
- Valid credentials and configuration for the existing extraction and agent services used by the chosen ordinary flow.
- No dependency on Release 2. When banking or accounting integration execution is unavailable in this release, expose that limitation and block the external action without blocking unrelated internal workflows.

### 4. Implementation requirements

**Company and controls**

- Provision through normal onboarding with its own company ID, memberships, settings, agents, documents, and connections. Persist business data in ordinary company-owned tables.
- Show a persistent Test company badge and add owner-only Demo & test controls, protected by backend permissions.
- Implement Populate company, Send supplier bills, Create customer invoices, View scenario runs, and Create a fresh demo from template. Use one built-in versioned template initially.
- Support configurable counts, explicit dates, seed, and dataset size. Populate fictional customers, suppliers, products, opening balances, and a coherent historical dataset. Identify historical setup shortcuts as fixture preparation.

**External-action policy**

- Separate test identity from allowed actions, mailbox destinations, and provider connection configuration. Preserve default protection for existing demo companies.
- Permit required internal workflow work and controlled email delivery. Recheck company context, policy, destination, and required approvals at execution time, including retries.
- Inventory and cover relevant direct provider calls as well as outbox dispatch. Do not rely on a frontend flag or one dispatcher alone.
- Default financial execution to blocked until a verified test adapter or sandbox is configured. Do not permit live financial account execution merely because the company is a test company.

**Manual supplier and customer flows**

- Generate a normal supplier PDF with consistent line items, tax, totals, references, and terms. Send from the controlled supplier mailbox to the company's finance mailbox.
- Let normal ingestion create the bill. Do not also insert the bill directly. Continue through extraction, duplicate checks, review, and normal approval requirements.
- Create customer invoice drafts through normal application commands, with an option to progress through normal approval, issuance, rendering, and email delivery to approved test recipients.
- Link run progress to ordinary documents, messages, bills, invoices, tasks, and approvals. Show waiting-for-approval and actionable failure states.

**Durability and first restart option**

- Persist company-scoped runs and steps, scenario version, seed, parameters, dates, initiating user, correlations, message IDs where available, and generated record references.
- Use durable workers, stable business idempotency keys, bounded retries, concurrent-claim protection, and reconciliation for ambiguous provider outcomes from this release onward.
- Distinguish queued, provider accepted, inbox received, ingested, extracted, waiting for approval, failed, and completed milestones where applicable.
- Create a fresh company from the built-in template without deleting the old one. Use fresh identifiers and isolated mailbox routing; do not copy credentials or provider bindings implicitly. Report connection setup still required before activity can run.
- Add EF Core migrations and model configuration for the required profile, run, step, provenance, and template data, reusing existing records where their semantics fit.

### 5. Constraints and preservation rules

Use the shared rules above. Normal business state transitions and accounting remain authoritative. Generated scenario output must not bypass business validation or required human approvals. Provider email acceptance is not recipient delivery evidence. Population and fresh provisioning must be resumable or fail visibly without leaving duplicate business records.

### 6. Acceptance criteria

1. Given an authorised owner, creating a test company produces an ordinary selectable company that survives restart; unauthorised and cross-company access is rejected server-side.
2. Given a seed and built-in template version, population produces consistent fictional starting data visible through ordinary screens; retrying the same operation does not duplicate it.
3. Given working test mailboxes, sending a generated supplier PDF results in a received message and an ordinary bill review/approval flow, with no direct bill insertion by the generator.
4. Given a controlled customer recipient, a generated invoice traverses normal draft, approval where required, issue, PDF, and delivery steps. Missing approval pauses progression.
5. Given a queued send whose destination is subsequently disallowed, dispatch refuses it with a recorded reason while unrelated internal workflow processing continues.
6. Given worker restart, concurrent claims, or retry, the same action does not create unintended duplicate invoices or emails; ambiguous sends enter reconciliation.
7. Given a fresh-demo request, a new isolated company is provisioned from the built-in template and the old company's data and history remain intact.

### 7. Verification

Run focused policy, authorisation, tenant-isolation, generation, workflow, idempotency, and retry tests. Verify SQL Server migrations against an existing database and persistence across restart. Exercise controls and ordinary bill/invoice screens in the browser. Complete live sender-to-company ingestion and company-to-customer delivery checks, retaining message/document evidence. Run the appropriate broader build and regressions for ordinary companies and existing sales demos.

### 8. Definition of done

All Release 1 behaviours are implemented with real authenticated APIs, durable persistence, functioning UI, actionable errors, and required verification. Publish setup instructions, evidence, known provider prerequisites, and a concrete Release 2 handoff. No placeholder controls, fake delivery results, or deferred Release 1 TODOs remain. Release 2 features are explicitly absent rather than shown as functional.

## Release 2 — Complete scenarios, scheduled activity, and repeatable resets

### 1. Outcome

An owner can repeatedly run a realistic company demonstration, introduce finance exceptions and payments, schedule bounded activity, and restart from a versioned template. The company continues using ordinary business flows throughout.

### 2. Current context

Build on Release 1's company profile, policy, controls, durable runs, two complete email/invoice flows, built-in template, and fresh-company provisioning. Reuse Finance's ordinary reconciliation, approval, correction, and integration services through Application contracts.

Evaluate `src/VirtualCompany.Infrastructure.Finance/Finance/CompanySimulationService.cs` for explicitly scoped date/progression support. Its finance simulation clock must not be treated as a universal clock for providers or all workers. Existing narrow sales reset behaviour is not a complete-company reset implementation.

### 3. Dependencies

- Accepted Release 1 and its migration history, run persistence, policy, workers, and live-flow configuration.
- Provider test organisations and credentials where available for supported accounting integrations and provider sandboxes selected for verification.
- A functional explicitly simulated bank/payment adapter as the baseline when a provider sandbox is unavailable. Its use must remain visible and must not be reported as live-provider acceptance.
- A defined managed reset scope covering company-owned data, documents, workers, mailbox ingestion state, and provider connections before enabling reset.

### 4. Implementation requirements

**Complete scenario catalogue**

- Extend supplier scenarios to deliberate duplicate submission, missing purchase reference, unusually large amount requiring review, supplier credit note, and overdue bill, alongside Release 1's normal bill.
- Extend customer scenarios to partial payment, full payment, and overdue invoice, alongside draft creation and complete invoice delivery.
- Send supplier scenarios through the actual mailbox path and use normal customer commands and reconciliation. Preserve financial consistency even when introducing a deliberate exception.
- Give intentional duplicate submissions separate scenario-step identity while preserving the original bill identity needed to exercise duplicate detection.

**Banking and accounting integrations**

- Add company-scoped test bank feeds and payment execution through a provider sandbox or an explicitly simulated adapter. Exercise payment approval, execution result handling, posting, and ordinary reconciliation without moving live funds.
- Introduce customer receipts as bank transactions, including partial and full settlements; do not directly mark invoices paid as a shortcut.
- Configure supported accounting integrations against dedicated provider test organisations where available, with execution-time binding checks. For integrations without a test environment, report the unsupported capability explicitly; do not silently substitute a live organisation.
- Keep other external actions governed by the company policy. Document and verify which supported routes have real test-provider evidence versus adapter evidence.

**Scheduled activity and time**

- Implement Start activity schedule and Pause generation using durable scheduling, selected scenarios, company timezone, cadence, counts, and finite per-run and per-period limits.
- Persist schedule state across restarts, prevent concurrent schedulers from duplicating occurrences, and use a bounded catch-up policy so downtime cannot trigger an uncontrolled burst.
- Pause prevents new generation and identifies queued and running work. Provide explicit handling for pending work rather than implying that pausing recalls completed sends.
- Make dates reproducible. Clearly distinguish fixture business dates, any explicitly supported simulated finance time, and actual mailbox/provider time; never change the host clock or imply all workers have advanced.

**Templates and observability**

- Expand the built-in template into selectable, versioned demonstration templates containing starting data, scenario configuration, seeds, dates, and size presets. Pin each run to its version and preserve reproducibility after template updates.
- Extend fresh-company provisioning to all supported templates and scenarios, preserving existing company history and isolating mailbox routing and provider bindings.
- Complete run views with step evidence, linked business records, waiting approvals, retry/reconciliation state, and visible schedule/reset progress.

**Bounded in-place reset**

- Provide an owner-authorised reset with an explicit preview of the managed data, baseline, preserved settings/memberships, mailbox handling, and irreversible external history. Require confirmation for destructive reset execution.
- Before changing data, pause generation, quiesce relevant company workers, and cancel or invalidate pending generation and external-action work. Use a persisted reset generation or equivalent execution fence so stale queued work cannot recreate prior state after reset.
- Restore the selected template baseline only inside the verified test-company scope. Coordinate related business data and documents; preserve audit and reset evidence. Use a resumable staged workflow with visible failure/recovery states rather than claiming a database transaction covers external systems.
- Isolate or reconcile old test mailbox messages and ingestion cursors so old bills are not reimported. Never delete unrelated mailbox messages. Treat each provider's external state explicitly; require a fresh test connection or fresh-company restart if that state cannot be safely reconciled.
- Detect unsupported/manual dependencies before destructive changes and explain the blocker with the fresh-company alternative. Successful reset is supported only for the documented managed configuration; it does not undo already sent messages or completed provider actions.
- Persist reset state, schedule state, template versions, and execution fencing through the required migrations.

### 5. Constraints and preservation rules

Retain all Release 1 protections and ordinary approval rules. Test datasets may be reproducible while AI extraction or provider timing varies; assert business outcomes instead of promising identical AI output. Do not disable business integrity constraints to reset data. In-place reset must never affect an ordinary company or another tenant, and must not proceed when its execution fence or dependency checks cannot be established.

### 6. Acceptance criteria

1. Each supplier scenario enters through email and produces its expected ordinary review, duplicate, credit, or overdue behaviour, with traceable evidence and no fabricated completion state.
2. Partial and full customer payments enter as test bank transactions and reconcile through the ordinary flow; overdue invoices appear in ordinary follow-up work according to configured policy.
3. Supplier payment execution requires normal approval and uses the configured test adapter or sandbox. Attempts to bind a test company to an unapproved live financial destination are rejected.
4. Supported accounting integration checks operate only against the selected dedicated test organisation; unavailable test environments produce a visible limitation.
5. Schedules persist across restarts, respect timezone and limits, avoid duplicate occurrences under concurrent workers, and obey the bounded catch-up policy.
6. Pausing prevents new generation and displays existing queued/running work accurately. It does not claim to retract completed external actions.
7. Selecting a template version and seed reproduces its defined inputs; both fresh-company restart and supported in-place reset return to that baseline without copying unsafe bindings.
8. During a supported reset, stale work cannot execute, old mailbox messages do not regenerate removed activity, audit evidence survives, and no other company is changed. Interrupted reset can resume or expose an actionable recovery state.
9. An unsupported reset dependency is detected before destructive changes, with a reason and a usable fresh-company alternative.

### 7. Verification

Add scenario-specific tests, bank-feed and reconciliation integration tests, provider-binding policy tests, schedule concurrency/restart/timezone tests, and reset isolation, fencing, interruption, document, and mailbox-reingestion tests. Verify Release 1-to-Release 2 SQL Server migration upgrades. Perform browser UAT for every scenario and both restart choices. Verify supported live test-provider paths independently of simulated adapters, rerun affected ordinary-company and sales-demo regressions, and complete the appropriate broader build.

### 8. Definition of done

All Release 2 capabilities and acceptance criteria are implemented and verified within their declared supported configurations. Deliver template/scenario documentation, connection prerequisites, schedule limits, reset scope and recovery instructions, and an evidence matrix separating automated, browser, simulated-adapter, and live-provider results. No in-scope functionality is left as scaffolding or deferred to an unnamed third release.

## Full-scope coverage

| Proposal scope | Release 1 | Release 2 |
|---|---|---|
| Persistent ordinary company, shared screens and workflows | Complete | Regression coverage |
| Test badge, owner-only controls, backend authorisation | Complete | Scheduling and reset extensions |
| Company-level external-action policy | Complete foundation and email enforcement | Test banking/accounting routes and broader verification |
| Starting data: customers, suppliers, products, balances, history | Built-in versioned dataset, size, dates, seed | Multiple versioned demonstration templates |
| Supplier PDF generation and real email ingestion | Normal bill, complete flow | Duplicate, missing reference, large amount, credit note, overdue |
| Customer draft, approval, issuance, PDF, delivery | Complete manual flow | Payment and overdue scenarios |
| Bank transactions, payment execution, reconciliation | External financial execution blocked until configured | Test feed, partial/full receipts, payment adapter or sandbox |
| Accounting integration test organisation | Policy boundary and explicit availability | Supported provider test bindings and verification |
| Durable runs, provenance, audit, retries, ambiguity handling | Complete for all Release 1 actions | Extended to new actions, schedules, resets |
| Activity schedule and pause | Not included | Complete, bounded and restart-safe |
| Run progress and ordinary-record links | Complete manual-run view | Schedule, integration, reset, and exception evidence |
| Explicit time semantics | Business dates and real provider time distinguished | Scoped progression and schedule semantics |
| Fresh demo from template | Built-in template; prior history preserved | All supported templates |
| In-place reset | Not included | Managed scope, execution fencing, recovery, external-state checks |
| Automated, SQL Server, browser, and live evidence | Required for Release 1 | Expanded full-scope evidence and regressions |
