# Test company Release 1 — implementation prompts

Date: 2026-10-02  
Status: Ready for implementation; no implementation or acceptance is asserted by this document.

## Purpose and use

Implement Release 1 of [testcompany-releaseplan.md](testcompany-releaseplan.md), using [testcompany.md](testcompany.md) as supporting design context. Execute P01–P08 in order. Each prompt includes the shared execution contract below by reference and can be assigned in a new chat with this document and the same checkout.

When asked to implement the full pack, continue through the ordered prompts without stopping at intermediate build or test checkpoints. Preserve unrelated changes. A new chat cannot recover uncommitted work from conversation history; record the actual checkout, branch, files, and outstanding work in the handoff.

| Prompt | Independently delivered outcome | Dependencies |
|---|---|---|
| P01 | Persistent, authorised test-company creation with external actions disabled | Existing onboarding and database |
| P02 | Company-specific external-action policy and executable boundary enforcement | P01 |
| P03 | Durable, reproducible population with real persisted run progress | P01–P02 |
| P04 | Owner controls, test badge, population UI, and run inspection | P01–P03 |
| P05 | Generated supplier PDF sent to the company and processed by ordinary ingestion | P01–P04; test sender and receiving mailbox |
| P06 | Customer draft generation and ordinary approval-to-email lifecycle | P01–P05; controlled recipient and finance sender |
| P07 | Fresh demonstration company from the built-in template | P01–P06 |
| P08 | Verified integrated release, defect fixes, operating guide, and Release 2 handoff | P01–P07; configured live acceptance environment |

## Shared execution contract — applies to every prompt

1. Read and follow `/production-implementation.md`, `/AGENTS.md`, `/docs/architecture-rules.md`, and applicable scoped guidance, including `/src/AGENTS.md`, `/tests/AGENTS.md`, and `/src/VirtualCompany.Web/AGENTS.md` where relevant. Follow `/docs/AGENTS.md` for implementation documentation and follow-up prompts. Current implementation and authoritative rules win over older planning files.
2. UI implementation must follow `/docs/design.md`; `/ui-instructions.md` is a companion. Complete the mandatory reference-image workflow before building a new or significantly redesigned surface. Store the reference and prompt under `/docs/design/references/`. Reuse an applicable reference for incremental controls rather than generating unrelated page designs. Invoke the installed `$polish-uat-loop` skill for hands-on UI review, UAT, and resulting fixes as required by `/src/AGENTS.md`.
3. Use real authenticated endpoints, persisted application data, ordinary business services, and durable workflows. Fictional test-company data is intentional scenario input; hard-coded UI results, fake provider success, and mock production implementations are not acceptable.
4. Put cross-capability orchestration behind Application contracts with owning Operations implementation. Keep Finance, Mailbox, Sales, Platform, Domain, Persistence, API, and Web responsibilities consistent with the architecture rules. Do not add sibling Infrastructure project dependencies or a generic cross-domain manager.
5. For schema work, follow the `Database and EF Core` rules: EF migrations and the model snapshot are authoritative; validate the SQL Server upgrade path and existing data. Reuse suitable entities without forcing general test-company behaviour into sales-only scenario semantics. Queries must not mutate state.
6. For email and other provider effects, follow the `Workflow and Approval` and `External Side Effects and Outbox` rules. Validate company ownership, permitted destination/connection, and required approvals at execution and retry time. Unknown outcomes require reconciliation, not a fabricated success or blind resend.
7. Run focused tests first and broader checks only when appropriate. Follow the authoritative `Test Architecture` rules for test ownership. Existing test paths below are starting points, not permission to put new tests in the wrong project.
8. Maintain `/docs/verification/testcompany-r1/status.md` from P01 onward, with per-prompt scope, commands and results, changed files, migration IDs, evidence paths, external blockers, and the next concrete step. Keep secrets and raw mailbox credentials out of evidence. Record a prompt as implemented separately from runtime/provider acceptance.
9. Every prompt's definition of done includes real implementation, meaningful tests, current status documentation, actionable errors, and no scaffolding, silent failures, unhandled intermediate states, or deferred in-scope TODOs. If an external service is unavailable, finish unaffected implementation and state the exact acceptance gate still open.

Release 2 remains out of scope: recurring generation schedules, general exception catalogues, bank/payment adapters, accounting-provider execution, multiple selectable templates, and in-place reset. Release 1 must show financial/provider limitations truthfully and block unsupported execution. It must not expose placeholder Release 2 controls.

## P01 — Create a persistent, isolated test company

### 1. Title and outcome

Implement authorised test-company creation through ordinary onboarding. An authenticated eligible user can create a real company that they own, select it through ordinary company selection, and resume it after application restart. External actions start disabled.

### 2. Current context

Inspect `src/VirtualCompany.Domain/Entities/TenantEntities.cs`, `src/VirtualCompany.Infrastructure.Operations/Companies/CompanyOnboardingService.cs`, and `src/VirtualCompany.Infrastructure.Sales/Sales/DemoScenarioService.cs`. Company already has demo metadata, onboarding assigns owner membership, and the sales demo service uses ordinary company creation. Existing `/api/demo-scenarios` endpoints in `src/VirtualCompany.Api/Controllers/DemoScenariosController.cs` are sales-oriented and must remain compatible.

Relevant regression starting points are `tests/VirtualCompany.Api.Tests/CompanyOnboardingIntegrationTests.cs`, `DemoScenarioServiceTests.cs`, and `DemoScenarioMigrationTests.cs` in that project. Trace ordinary company selection and membership authorisation before exposing new routes.

### 3. Dependencies

Existing authentication, onboarding, SQL Server, and company selection. No earlier prompt or external mailbox credential is required. Apply the shared execution contract in this document.

### 4. Implementation requirements

- Persist a test-company profile tied to an ordinary company. Keep test identity distinct from a sales scenario key; define compatibility with existing `IsDemoTenant` records without reclassifying ordinary or legacy companies implicitly.
- Implement authenticated create and company-scoped profile/readiness endpoints through Application contracts and owning services. Resolve the acting user server-side. Creation follows existing company-creation eligibility; subsequent test controls require active owner membership.
- Use normal onboarding for settings, agents, memberships, and documents. Make the protective test identity effective before background work can execute; close any create-then-mark interval that could permit external actions.
- Make creation idempotent and recoverable under retry or partial failure. Persist enough operation state and result identity to recover the same company after a lost response.
- Until P02 explicitly enables a permitted action, enforce external-effects-disabled behaviour for new test companies. A new profile must not create a bypass around existing demo protections.
- Return test identity in the appropriate ordinary company read contract for the later badge; no new controls page is required in this prompt.
- Add required schema, configuration, migration, audit events, problem responses, and status documentation. Never convert an existing company merely to satisfy a create request.

### 5. Constraints and preservation rules

Follow the shared contract. Preserve normal onboarding and existing sales demo routes and policy defaults. A submitted company ID or owner ID is not proof of access. Schema changes must preserve existing rows and migration history. Initial protection cannot depend on frontend visibility or on P02 being installed later.

### 6. Acceptance criteria

- Creating a company as an eligible user produces ordinary persisted company records and an active owner membership; selection still works after restart.
- Repeating the same creation request returns the same operation/company; a conflicting reuse of its idempotency key is rejected.
- Non-owners cannot read private test controls or mutate the profile; cross-company identifiers do not reveal or modify records.
- Workers cannot send external effects during provisioning or before explicit policy configuration.
- Ordinary companies and existing sales demos retain their previous behaviour.

### 7. Verification

Add focused creation, membership, retry/partial-failure, cross-company, and initial worker-protection tests. Run relevant onboarding and sales demo regressions. Apply and inspect the migration on SQL Server containing existing companies, then verify persistence across restart. Build affected projects; reserve new UI UAT for P04.

### 8. Definition of done

The create/read vertical slice is callable and persistent, with enforced initial protection and verified ordinary company selection. Meet the shared definition of done and record exact endpoint, contract, and migration names for P02.

## P02 — Enforce test-company external-action policy

### 1. Title and outcome

Implement owner-configurable, backend-enforced permitted actions and test destinations. Allow required internal workflows while ensuring external actions can execute only against explicitly permitted connections and recipients.

### 2. Current context

`src/VirtualCompany.Infrastructure.Sales/Sales/DemoTenantExternalSideEffectPolicy.cs` currently accepts company ID and action type and rejects every evaluated action for a demo tenant. `src/VirtualCompany.Infrastructure.Operations/Companies/CompanyOutboxInfrastructure.cs` consults that policy in dispatch. This alone neither distinguishes destination context nor establishes coverage of direct provider calls. Inspect `CustomerInvoiceDeliveryService.cs` in Infrastructure.Finance and actual mailbox transports, approval checks, worker registrations, and alternative delivery routes.

### 3. Dependencies

P01, its profile and migration. Live credentials are not required to implement and automatically test enforcement. Real mailbox connections will be configured for P05/P06. Apply the shared contract.

### 4. Implementation requirements

- Implement authenticated owner read/update endpoints for permitted action categories, approved recipient addresses, and approved connection references, with concurrency handling and audit evidence.
- Use existing secret storage and connection lifecycle; do not store credentials in the profile, run parameters, fixtures, or logs.
- Inventory internal workflow topics and relevant email, financial, accounting, electronic-delivery, and agent-triggered provider paths. Record each enforcement boundary in the status evidence; fix uncovered paths in scope.
- Distinguish internal work from external effects using authoritative action context. Verify actual recipients, all envelope destinations where applicable, connection ownership, and resolved provider target immediately before execution. A permitted topic alone is insufficient.
- Allow only expressly configured test email routes; keep unsupported financial and accounting execution blocked in Release 1 with stable reason codes and visible readiness state. Prevent alternate delivery/fallback routes from bypassing email restrictions.
- Re-evaluate policy on dispatch, retry, and changed bindings. Fail closed for missing company/profile/target context; preserve legacy sales demo defaults and ordinary company behaviour.
- Add required schema changes and focused integration into workers/providers; enforce required approvals alongside test policy, never instead of them.

### 5. Constraints and preservation rules

Follow the shared contract, particularly external-side-effect and approval rules. Do not remove the blanket check globally or permit all outbox work because some topics are internal. Keep capability ownership intact when evolving the sales-owned policy contract.

### 6. Acceptance criteria

- A permitted internal workflow executes while a disallowed external action records an actionable refusal.
- An allowed test email target passes policy, but changing the target, connection ownership, or permission before dispatch prevents the send.
- Alternate provider/fallback paths and agent-triggered sends cannot escape the same destination constraints.
- Policy changes require current owner authority; foreign connection references and cross-company reads/writes are rejected.
- Unsupported financial execution stays blocked; existing ordinary and legacy demo policies remain compatible.

### 7. Verification

Test the action/destination matrix, queued-policy changes, fallback routes, missing context, approval rechecks, owner removal, and cross-company connection references. Exercise real dispatcher/provider boundaries with deterministic test transports, not only isolated policy unit tests. Verify SQL Server migration changes if any and run the affected demo, mailbox, invoice-delivery, and outbox regressions.

### 8. Definition of done

Policy configuration and enforcement work through real APIs and actual execution boundaries. Publish the boundary inventory and tests. Meet the shared definition of done; P05/P06 must be able to use these protections without redesigning them.

## P03 — Populate a company through durable, reproducible runs

### 1. Title and outcome

Implement a complete Populate company API backed by one built-in versioned template and durable run execution. An owner can prepare meaningful starting data and inspect actual persisted progress and results.

### 2. Current context

Inspect `src/VirtualCompany.Infrastructure.Finance/Finance/CompanySimulationFinanceGenerationService.cs`, `CompanySimulationService.cs` in the same folder, and Persistence's `DeterministicFinanceSeedDatasetGenerator.cs`. Existing finance generation directly creates business entities; evaluate consistency and reuse for starting fixtures only. Existing `DemoScenarioRun` and `DemoScenarioCommandExecution` entities have sales semantics that may not fit a general run.

Review `tests/VirtualCompany.Api.Tests/CompanySimulationFinanceGenerationTests.cs`, `CompanySimulationFinanceDeterminismTests.cs`, and `CompanySimulationFinanceWorkflowEndToEndTests.cs`, plus the existing company outbox model and worker registration.

### 3. Dependencies

P01–P02 and their migrations. Existing document storage and company/finance setup services. No external email credentials required. Apply the shared contract.

### 4. Implementation requirements

- Define one immutable built-in template version with coherent fictional customers, suppliers, products, opening balances, and historical activity. Configure seed, dataset size, bounded counts, and explicit business dates.
- Include or establish ordinary configuration required by subsequent invoice workflows: relevant accounting setup, periods, numbering, billing profiles, and test identity data. Do not bypass validation or invent valid real-world registrations to force readiness; expose setup requirements where needed.
- Implement populate, list/get runs, and step/provenance reads through authenticated company-scoped contracts and transport-only controllers. A manual retry/reconcile action, where supported, must enforce authorisation and action eligibility.
- Persist run/step state, template/scenario version, parameters, actor, correlation, business IDs, attempts, timestamps, failures, and waiting conditions. Schema and indexes must support company-scoped queries and concurrent claims.
- Execute the real population job using durable background work. Add stable business idempotency, partial-failure recovery, atomic local commit/enqueue boundaries, and bounded retry. Reuse existing worker infrastructure rather than creating an unused execution framework.
- Repeating an operation returns its existing result; changed parameters under the same key conflict. Explicit additional population must be deliberate and must not duplicate opening balances or corrupt the baseline.
- Identify historical fixture preparation in provenance. Do not emit accidental provider sends, mark live workflows as tested, or claim that finance simulation time controls external systems.
- Provide reusable run-step handling for later invoice scenarios without implementing Release 2 scheduling.

### 5. Constraints and preservation rules

Follow the shared contract. Generated data must be stored in ordinary business tables and satisfy business consistency rules. Preserve manually entered records; population is not a hidden reset. Backend run reads must not generate data or start work.

### 6. Acceptance criteria

- A population request produces actual customers, suppliers, products, balances, and history visible through ordinary reads.
- The same version, seed, size, and dates reproduce defined business inputs; identities remain isolated between companies.
- Restarting or concurrently claiming a partially completed run does not duplicate baseline records or balances.
- Invalid parameters and incompatible existing baselines fail with an actionable explanation before unsafe mutation.
- Run reads link to actual records, show failures truthfully, and reject cross-company access.

### 7. Verification

Test fixture consistency, reproducibility, baseline protection, stable identities, concurrency, crash/retry boundaries, and cross-company run/provenance access. Validate database transactions and migrations on SQL Server. Run affected seed/simulation regressions and inspect generated data through existing business read services.

### 8. Definition of done

Population is a functioning durable workflow, not just schema or a runner abstraction. Meet the shared definition of done and record the template contract, supported size/date limits, actual setup prerequisites, and worker/run APIs for P04–P07.

## P04 — Expose test-company controls and real run progress

### 1. Title and outcome

Implement a usable owner-only Demo & test controls page, test-company badge, provisioning entry point, population form, policy setup, and run inspection using the completed backend.

### 2. Current context

Use P01–P03's endpoints and contracts. Inspect ordinary company navigation/selection, `src/VirtualCompany.Web/Services/DemoScenarioApiClient.cs`, existing finance API clients, and `CompanyApiTransport`. The existing sales demo control surface is not a replacement for company-wide controls. Relevant starting tests include `tests/VirtualCompany.Web.Tests/DemoScenarioApiClientTests.cs` and `MailboxProviderSetupSurfaceTests.cs`.

### 3. Dependencies

P01–P03, a running API/Web environment, and a populated test company for UI verification. Apply the shared contract and `/src/VirtualCompany.Web/AGENTS.md` host guidance.

### 4. Implementation requirements

- Complete the mandatory `/docs/design.md` reference-image workflow before implementing the new page. Include creation, setup/readiness, population, run list/detail, and empty/error/waiting states in the reference requirements.
- Add an eligible-user entry point to create a test company and select its ordinary workspace after successful creation. Show persistent test identity in the shell/company selection without changing ordinary business screens into demo-only screens.
- Add typed API clients and an owner-only controls route with functional profile/policy configuration, connection setup links, population inputs, and persisted run progress with links to ordinary records.
- Handle validation, conflicting requests, loading, refresh/reconnect, partial failures, waiting approvals, permission loss, and cross-company navigation. Use backend readiness and action decisions rather than duplicating policy rules in the UI.
- Show existing sender/recipient readiness and blocked financial capabilities plainly. Do not expose unimplemented Send supplier bills, Create customer invoices, fresh-demo, or Release 2 actions as functional controls; later prompts add their working UI.
- Follow existing localisation and accessibility patterns. Make links preserve or establish the correct company context.
- Use the required UAT skill to verify and correct the actual user flow, capturing evidence and status.

### 5. Constraints and preservation rules

Follow the shared contract. Owner-only UI is convenience, not the authorisation boundary. Avoid new generic demo dashboards or fake status values. Preserve existing shell navigation and legacy sales controls.

### 6. Acceptance criteria

- An eligible user creates and selects a test company in the UI and sees its persistent badge.
- An owner configures permitted destinations, populates data, refreshes the browser, and sees the same real run and business records.
- A non-owner cannot open or invoke private test controls; direct URL/API attempts receive the appropriate denial.
- Run failures and missing connections have actionable states; record links resolve inside the correct company.
- Ordinary company screens and legacy demos remain usable.

### 7. Verification

Add focused client/component and API-contract checks for owner visibility, errors, concurrency, company context, and actual run states. Run browser UAT for creation, policy setup, population, run inspection, and navigation; compare the result to the reference and fix defects. Record screenshot evidence and any unavailable native/provider checks separately.

### 8. Definition of done

The page is integrated, accessible, localised under repository conventions, and demonstrably backed by real endpoints. Meet the shared definition of done with reference images, browser evidence, and resolved in-scope UI findings.

## P05 — Send a supplier bill and observe ordinary ingestion

### 1. Title and outcome

Implement Send supplier bills from the controls page: generate realistic PDFs, send them from a controlled supplier identity, and observe ordinary mailbox ingestion through bill review and approval.

### 2. Current context

Use P02 policy, P03 run execution, and P04 controls. Inspect `src/VirtualCompany.Infrastructure.Mailbox/Mailbox/ConnectedMailboxInboxScanOrchestration.cs`, `ManualInboxBillScanOrchestration.cs`, and `StandardMailboxInboundSyncBackgroundService.cs`, plus the existing PDF/document storage and Finance bill registration/approval path. Review `tests/VirtualCompany.Infrastructure.Mailbox.Tests/StandardMailboxInboundSyncBackgroundServiceTests.cs` and applicable API mailbox tests.

### 3. Dependencies

P01–P04 and migrations. Controlled supplier sender, dedicated company receiving mailbox, working document storage, and extraction-service credentials for live acceptance. Missing credentials block live evidence, not implementation and deterministic integration tests. Apply the shared contract.

### 4. Implementation requirements

- Implement the normal-bill scenario with bounded count, fictional supplier, explicit dates, seed, currency, and consistent line items/tax/totals/payment terms. Reuse a suitable document renderer or add a capability-owned one and persist the resulting PDFs.
- Configure the controlled supplier sender through secure existing connection mechanisms or a narrowly scoped adapter. Do not mutate or replace the company's own finance sender to impersonate a supplier.
- Add an owner-authorised command and real controls form. Enqueue sends through durable execution; validate source identity, receiving mailbox ownership, destination policy, and required permissions before each send/retry.
- Feed the resulting real email into normal ingestion. Never insert a Finance bill as an alternative when extraction or ingestion fails.
- Correlate outgoing operation, provider message, received message, attachment/document, extracted data, bill, task, and approval using durable identifiers. Correlation metadata must not tell extraction to bypass normal validation or duplicate detection.
- Observe actual milestones and expose waiting/failure states and timeouts without inventing missing evidence. If provider receipt lookup is unavailable, keep submission ambiguity visible for reconciliation; do not automatically resend.
- Use normal approval work and resume observation after user action. Scenario success criteria must be explicit: sending alone is not successful bill processing.
- Add the new form/run-detail states to P04's design reference scope where required, then verify using the UAT skill.

### 5. Constraints and preservation rules

Follow the shared contract, specifically email side effects and approval rules. Release 1 contains only the normal supplier-bill scenario; deliberate duplicates and other exception scenarios belong to Release 2. A retry must not become an accidental duplicate-bill test. PDFs are real scenario inputs, not images of the application UI.

### 6. Acceptance criteria

- From the UI, sending a generated PDF produces an actual received message and an ordinary bill with linked review/approval work.
- PDF totals and stored scenario inputs are consistent; the bill is created solely by ordinary ingestion.
- Policy revocation before dispatch blocks the send; another company's sender or receiving mailbox is rejected.
- Lost responses, restart, and concurrent workers do not blindly resend; ambiguous outcomes are observable and reconcilable.
- Approval remains pending until the normal authorised action occurs, and the run reports each verified milestone accurately.

### 7. Verification

Test PDF content/totals, routing policy, send idempotency, ambiguity handling, real ingestion orchestration with test transport inputs, provenance, and tenant isolation. Render representative PDFs for visual inspection. Execute live sender-to-inbox-to-bill UAT with controlled mailboxes and retain sanitised evidence; distinguish test-transport coverage from actual receipt/extraction evidence. Run affected mailbox, Finance bill, and controls tests.

### 8. Definition of done

The complete supplier flow is implemented with a functioning UI and evidence-backed progress. Meet the shared definition of done. Document exact sender/receiver setup, remaining live-service gates if any, and recovery steps for rejected or ambiguous sends.

## P06 — Generate customer invoices through the ordinary lifecycle

### 1. Title and outcome

Implement Create customer invoices with draft-only and progress-through-delivery options. Owners can demonstrate normal validation, approval, issuance, rendering, and delivery using fictional customers and approved recipient addresses.

### 2. Current context

Inspect `src/VirtualCompany.Application/Finance/Contracts/CustomerInvoiceDraftContracts.cs`, `src/VirtualCompany.Infrastructure.Finance/Finance/CustomerInvoiceDraftService.cs`, and `CustomerInvoiceDeliveryService.cs`. Draft readiness already exposes statutory profile, period, numbering, calculation, and approval blockers. Existing transport surfaces include `src/VirtualCompany.Api/Controllers/InternalFinanceController.CustomerInvoiceDrafts.cs` and `CustomerInvoiceDeliveryController.cs`; preserve their policies and routes.

Relevant tests include `tests/VirtualCompany.Finance.Tests/CustomerInvoiceDraftTests.cs`, the API draft/delivery surface tests, and Web finance draft/delivery client tests.

### 3. Dependencies

P01–P05, the populated company/billing configuration, a finance mailbox capable of sending invoice attachments, and controlled recipients for live acceptance. Reuse P03 run machinery and P02 destination policy. Apply the shared contract.

### 4. Implementation requirements

- Add owner-authorised commands and controls with bounded count, customer, lines, dates, seed, and intended stopping point: drafts or delivery through the normal lifecycle.
- Use ordinary draft calculation/readiness/issue commands and delivery services. Generate consistent customer billing fixtures and approved destination mappings, exposing setup blockers instead of forcing fake statutory readiness.
- Reuse stable action keys for draft creation, approval requests, issue, render, and delivery. Persist actual business IDs and resume safely after restart without duplicating invoice numbers or sends.
- Stop for required approval and continue only when the normal approval is valid for the current invoice version. Handle rejection, stale approval, changed lines, and permission loss through ordinary policy decisions.
- Use the issued document and normal PDF renderer; bind delivery to the correct artifact/snapshot. Recheck actual envelope targets and approved connection immediately before external execution.
- Prevent preferred-delivery or electronic fallback from reaching a provider excluded by Release 1 policy. Expose provider acceptance separately from recipient delivery evidence.
- Link runs to ordinary invoice screens and approval work. Extend the controls reference where needed and verify the actual flow with the UAT skill.

### 5. Constraints and preservation rules

Follow the shared contract. Do not directly insert issued invoices, mark them paid, or reuse historical seeding as proof of creation. Customer receipts, overdue scenarios, banking, and accounting-provider execution are Release 2 work.

### 6. Acceptance criteria

- Draft-only generation creates the requested valid drafts without issuing or sending them.
- The delivery option traverses ordinary calculation, required approval, issuance, PDF, and permitted email delivery with actual linked records.
- A pending/rejected/stale approval prevents issuance or sending as required by normal rules; changing invoice data does not reuse an invalid approval.
- Restart or concurrent workers do not create duplicate drafts, numbers, approval requests, or sends.
- Unapproved or foreign destinations and alternate provider paths are blocked; controlled recipient receipt is evidenced separately from send acceptance.

### 7. Verification

Add lifecycle integration tests for draft-only, readiness failures, approval changes, versioned artifacts, concurrency/retry, tenant isolation, and delivery policy. Run affected draft, accounting, rendering, delivery, and Web tests. Perform live browser UAT through normal invoice and approval screens and verify the received invoice PDF at a controlled recipient. Record any live evidence gap explicitly.

### 8. Definition of done

Both draft-only and complete lifecycle modes work through real services and screens. Meet the shared definition of done, with setup instructions and evidence distinguishing issuance, submission, and recipient receipt.

## P07 — Create a fresh demonstration company from the template

### 1. Title and outcome

Implement Create a fresh demo from template so an owner can prepare another clean demonstration while retaining the previous company's records, audit trail, and completed external history.

### 2. Current context

Compose P01's idempotent provisioning and P03's built-in versioned population through ordinary Application contracts. Use P04's controls and run views. Existing sales demo reset endpoints in `DemoScenariosController.cs` are narrow scenario operations, not full-company reset tools, and must not be reused to delete company history.

### 3. Dependencies

P01–P06, existing company-creation eligibility, and the built-in template. New connection credentials are required only when enabling the fresh company's live activity. Apply the shared contract.

### 4. Implementation requirements

- Implement an owner-authorised fresh-demo command with selected built-in template version, seed, size, dates, and new name. Create a new company and populate it through the established durable services.
- Persist operation-to-new-company mapping before dependent work can be repeated. Recover the same company after partial failure or a lost response rather than provisioning another one.
- Authorise access to the source request and resulting company explicitly across the provisioning boundary. Do not leak the resulting company's private state through source-company-only authorisation.
- Generate fresh company/record identities; preserve the old company, its connections, history, documents, and runs. Do not copy secrets, memberships, approvals, mailbox cursors, external IDs, or provider bindings implicitly.
- Keep the fresh company's external actions disabled until its own permitted test destinations and connection readiness are established. Prevent reused physical receiving-mailbox routing from causing the same messages to be ingested for both companies; require isolated routing or explicit safe setup.
- Add working controls, progress, completion navigation, and connection setup links. Explain that this creates another company and does not retract previous emails. Follow the design workflow for significant UI changes and verify with the UAT skill.
- Audit the initiating request, resulting company, version, and recovery state. Add persistence/migrations only where needed beyond P01/P03.

### 5. Constraints and preservation rules

Follow the shared contract. This is a fresh-company operation, not a clone of live provider state or an in-place reset. Preserve existing company switching and normal onboarding eligibility.

### 6. Acceptance criteria

- The UI operation creates a new selectable company with the requested reproducible baseline and fresh isolated IDs.
- The previous company's records and history remain unchanged; no credentials or executable bindings are copied.
- Repeating the same request or resuming a failed population returns the same new company and does not duplicate baseline data.
- The new company's readiness accurately requires its own connection setup, and conflicting mailbox routing cannot cause cross-company ingestion.
- Non-owner, forged source/result company, and cross-company operation reads are denied.

### 7. Verification

Test provisioning/population crash boundaries, stable result identity, source/result authorisation, no secret/binding transfer, and mailbox isolation. Verify relevant migrations on SQL Server. Use browser UAT to create, select, inspect, and populate the fresh company while checking that the original remains intact; rerun affected onboarding and company-selection tests.

### 8. Definition of done

Fresh provisioning is a working recovery-safe UI/API flow with isolated readiness and preserved prior history. Meet the shared definition of done and document setup steps for enabling the new company's mailbox without copying unsafe state.

## P08 — Complete integrated acceptance and Release 2 handoff

### 1. Title and outcome

Deliver a verified, operable Release 1 by exercising the integrated flows, fixing in-scope defects, and publishing setup/recovery guidance and a precise Release 2 handoff. This is a repair-and-acceptance prompt, not an analysis-only checkpoint.

### 2. Current context

P01–P07 should provide every Release 1 capability. Read their actual status, migrations, routes, tests, and evidence under `/docs/verification/testcompany-r1/`; do not assume implementation means acceptance. Use Release 1 criteria in `/testcompany-releaseplan.md` and the coverage map below as the scope authority.

### 3. Dependencies

P01–P07, SQL Server upgrade/restart environment, runnable API/Web/workers, document storage, controlled supplier/finance/customer mailboxes, and extraction/agent configuration. Apply the shared contract and invoke `$polish-uat-loop` for hands-on acceptance and fixes.

### 4. Implementation requirements

- Exercise creation, selection, identity badge, configuration, population, run inspection, supplier email ingestion, ordinary bill approval, customer draft-only and approval-to-delivery, and fresh-demo creation through the real user surfaces.
- Capture a prioritised defect ledger with reproduction steps, expected/actual results, affected company/run, and evidence. Implement all in-scope fixes, add meaningful regressions, and rerun the failing flow.
- Verify policy revocation between enqueue and dispatch, foreign-company IDs/connections, ordinary company regression, legacy sales demo compatibility, restart recovery, concurrent claims, and ambiguous send recovery. Use controlled test environments; do not create uncontrolled real-world effects to exercise failures.
- Verify upgrade from the pre-feature SQL Server schema through all Release 1 migrations with existing company data preserved. Check pending model changes and fresh/restart behaviour under repository database rules.
- Produce an acceptance matrix linking each Release 1 criterion to automated, SQL Server, browser, and live-provider evidence. Include received PDFs and sanitised correlation references where appropriate. Mark unavailable services and acceptance gaps plainly.
- Create `/docs/testcompany-r1-operations.md` covering prerequisites, secure sender/receiver/recipient setup, supported template parameters, approval setup, actual time semantics, action policy, generation limits, run recovery/reconciliation, and fresh-company routing.
- Create `/docs/verification/testcompany-r1/release2-handoff.md` naming checkout/branch, relevant uncommitted files, actual contracts/services/migrations, verified behaviours, exact gaps, and dependencies for Release 2. Preserve the planned Release 2 scope rather than implementing it opportunistically.
- Update status with a defensible release decision. A missing live credential is an explicit open gate, not successful delivery evidence; continue all work that does not depend on it.

### 5. Constraints and preservation rules

Follow the shared contract and authorised external-action boundaries. Do not reset/delete shared data or copy production mailbox credentials into tests. Do not claim deployment, live provider acceptance, or statutory approval from build/test results. Do not claim fully accepted Release 1 while required gates remain open.

### 6. Acceptance criteria

- Every Release 1 acceptance criterion has traceable evidence or a clearly stated external blocker; no in-scope implementation defect is silently deferred.
- Normal supplier and customer flows complete in the configured live test environment with receipt evidence distinct from provider submission.
- Required approvals, company isolation, execution-time policy changes, restart/retry safety, and old-company preservation survive integrated testing.
- The controls accurately show failures and readiness and contain no functional-looking Release 2 placeholders.
- Another engineer can configure and operate Release 1 and resume Release 2 using repository documentation without conversation history.

### 7. Verification

Run the narrowest affected tests after each defect fix, then one justified broader build/regression pass covering the final code. Complete SQL Server migration/restart verification and browser/provider UAT, following host lifecycle instructions and the UAT skill. Record command results and evidence rather than rerunning unchanged matrices without cause.

### 8. Definition of done

Release 1 is implemented, documented, and accepted only when its required gates pass. If externally blocked, deliver the complete unaffected implementation and an exact open-gate report without claiming acceptance. The operations guide, evidence matrix, defect disposition, and Release 2 handoff are present, coherent, and reference actual implementation names.

## Release 1 coverage and acceptance ownership

| Requirement | Implementation owner | Integrated acceptance |
|---|---|---|
| Persistent ordinary company, onboarding, ownership, selection | P01; UI P04 | P08 |
| Test identity, profile, badge, owner controls | P01/P04 | P08 |
| Internal workflow vs external-effect policy; execute-time recipient/connection checks | P02; provider integration P05/P06 | P08 |
| Unsupported financial/accounting execution blocked truthfully | P02/P04 | P08 |
| Built-in versioned template, customers, suppliers, products, balances, history | P03; UI P04 | P08 |
| Seed, size/count limits, explicit dates, reproducible inputs | P03/P05/P06 | P08 |
| Durable runs, steps, provenance, concurrency, retries, ambiguity | P03 foundation; concrete sends P05/P06 | P08 |
| Real supplier PDF, controlled sender, receipt, extraction, ordinary bill approval | P05 | P08 |
| Customer draft-only and ordinary approval/issue/PDF/email | P06 | P08 |
| Actual record links, progress, missing prerequisites, failures | P04–P07 | P08 |
| Fresh company, preserved old history, safe new connection setup | P07 | P08 |
| EF migrations, SQL Server upgrade, existing-data preservation | Each schema-owning prompt | P08 |
| Authorisation, tenant isolation, ordinary/legacy-demo regressions | Each behaviour-owning prompt | P08 |
| Reference images, localisation, browser evidence, UAT fixes | P04–P08 | P08 |
| Operations guide, evidence matrix, Release 2 handoff | Ongoing status; final P08 | P08 |
