# Test company proposal

Status: Proposed design; not an implemented feature specification or a claim of verified runtime behaviour.

Date: 2026-10-02

## Objective

Provide a persistent test company for demonstrations and testing complete business flows. The company lives in the ordinary database, uses the same screens and services as an ordinary company, and supports realistic generated activity, including supplier bills received by email and customer invoices created and sent through the normal workflow.

## Recommended approach

Implement a normal database-backed company with a test-company profile and a scenario generator. Use the same company ownership, permissions, agents, approvals, accounting logic, document storage, background workflows, and application APIs as other companies.

Special behaviour is limited to generating activity, controlling external integrations, identifying the company as a test company, and preparing repeatable demonstrations. Avoid separate demo screens for ordinary business operations or alternative implementations of business logic.

Future implementation must follow `/production-implementation.md`, `/docs/architecture-rules.md`, and applicable scoped `AGENTS.md` files. UI work must follow `/docs/design.md`, including its reference-image workflow where applicable; `/ui-instructions.md` is a companion. Schema changes must follow the architecture rules for EF Core migrations. External actions must follow the workflow, approval, and outbox requirements in those rules.

## Existing foundation and gaps

The repository inspection identified these reusable foundations:

| Existing code | Relevance |
|---|---|
| `src/VirtualCompany.Domain/Entities/TenantEntities.cs` | Company already has `IsDemoTenant`, `DemoScenarioKey`, and `DemoScenarioVersion`. |
| `src/VirtualCompany.Infrastructure.Sales/Sales/DemoScenarioService.cs` | Provisions a demo through the ordinary company onboarding service and provides controlled sales scenarios. |
| `src/VirtualCompany.Infrastructure.Finance/Finance/CompanySimulationFinanceGenerationService.cs` | Generates persisted finance data, including invoice and bill entities. |
| `src/VirtualCompany.Infrastructure.Finance/Finance/CompanySimulationService.cs` | Provides existing simulation clock and progression functionality to assess for reuse. |
| `src/VirtualCompany.Infrastructure.Sales/Sales/DemoTenantExternalSideEffectPolicy.cs` | Rejects every evaluated action for a demo company. |
| `src/VirtualCompany.Infrastructure.Operations/Companies/CompanyOutboxInfrastructure.cs` | Consults the demo policy before dispatching outbox work when that policy is supplied. |
| `src/VirtualCompany.Infrastructure.Mailbox/Mailbox/ConnectedMailboxInboxScanOrchestration.cs` | Existing connected-mailbox scanning orchestration. |
| `src/VirtualCompany.Infrastructure.Finance/Finance/CustomerInvoiceDeliveryService.cs` | Existing invoice rendering and delivery orchestration through the outbox. |

The finance generator directly adds invoice and bill records. This is useful for preparing starting data, but it does not prove that email ingestion, extraction, or the normal invoice creation lifecycle works.

The existing blanket demo-action policy also needs refinement before realistic delivery is possible. Its presence in one dispatch path is not evidence that every external-action path is covered. Implementation must inspect all relevant execution boundaries.

## Ordinary company behaviour

Create the test company through normal onboarding with its own `CompanyId`, memberships, settings, agents, documents, and mailbox connection. Persist its business records in the ordinary company-scoped tables.

Show a persistent **Test company** badge and provide an owner-only **Demo & test controls** page. Continue using the existing business screens, typed API clients, services, and authoritative backend policies for day-to-day operations.

Separate the company's test identity from its external-action configuration. Reuse or evolve the existing demo marker without silently changing the behaviour of existing sales demos.

| Capability | Proposed behaviour |
|---|---|
| Screens, agents, approvals, accounting | Ordinary production workflow. |
| Receiving supplier emails | Real dedicated test mailbox. |
| Sending customer invoices | Real delivery to approved test addresses. |
| Banking and payment execution | Provider sandbox or explicitly simulated adapter. |
| Accounting integrations | Dedicated provider test organisation where available. |
| Other external actions | Explicit company-level policy. |

Enforce external-action rules in backend execution paths and recheck them when queued work executes. Permit internal workflow processing while routing external effects to the configured test destination or adapter. Do not implement this by broadly disabling demo protection.

Preserve ordinary approval requirements. A test-company flag is not authority to bypass a human approval or execute against a live financial account.

## Two data-generation modes

### Populate starting data

Prepare an interesting company quickly with fictional customers, suppliers, products, opening balances, and historical activity. Reuse existing generators where their output is appropriate and internally consistent.

Starting datasets should have a version, configurable size, explicit dates, and reproducible inputs. Any shortcut used to prepare historical state must be identified as setup, not presented as evidence that the corresponding workflow was executed.

### Generate live activity

Exercise the actual application by invoking normal application commands or introducing external inputs. Examples include emailing supplier bills, creating customer invoice drafts, and introducing test bank transactions.

The scenario generator coordinates inputs and observes results. Business services retain responsibility for validation, state transitions, approvals, posting, and delivery.

## Supplier bills through real email

The intended flow is:

1. Select a fictional supplier, amount, dates, and scenario.
2. Generate a realistic PDF bill with consistent line items, tax, totals, references, and payment terms.
3. Send it from a controlled supplier test mailbox to the company's dedicated finance email address.
4. Let the ordinary mailbox ingestion, extraction, duplicate detection, review, and approval workflows process it.
5. Track the resulting message, document, bill, and workflow records back to the scenario run.

The generator must not also insert the resulting bill directly into Finance. The receiving workflow must create it from the email.

Initial scenarios can include:

- Normal supplier bill.
- Duplicate submission of the same bill.
- Missing purchase reference.
- Unusually large amount requiring review.
- Supplier credit note.
- Overdue bill.

Observe separate milestones for sending, mailbox receipt, ingestion, extraction, review, and approval. Provider acceptance of an email is not proof of inbox receipt or successful processing.

## Customer invoices through the normal lifecycle

Create fictional customers with email addresses under the operator's control. Use the ordinary draft, approval where required, issue, PDF generation, and delivery workflow.

Suggested actions:

- Create a selected number of invoice drafts.
- Run an invoice scenario through normal approval and delivery.
- Introduce a simulated partial or full customer payment.
- Prepare an overdue-invoice scenario.

Automatic progression must respect the company's configured approval rules and report when a scenario is waiting for a person. Simulated customer payments should enter through a test bank feed or adapter so the ordinary reconciliation flow is exercised. Do not simply mark invoices as paid and claim payment reconciliation was tested.

## Demo and test controls

The owner-only controls page should offer:

| Control | Purpose |
|---|---|
| Populate company | Choose a starting dataset, version, and size. |
| Send supplier bills | Choose scenario, count, and dates. |
| Create customer invoices | Choose scenario, count, and desired workflow starting point. |
| Start activity schedule | Generate a bounded amount of activity on a configured schedule. |
| Pause generation | Stop scheduling new activity and expose any already queued or running work. |
| View scenario runs | Inspect progress, generated records, pending approvals, failures, and retries. |
| Create a fresh demo from template | Prepare a repeatable demonstration without deleting existing company history. |

Generation is explicit and scoped to an authorised test company. Scheduled runs need defined limits and observable failure states. Long-running generation and delivery belong in durable background execution.

## Repeatability, traceability, and retry behaviour

Persist each scenario run with company scope, scenario key and version, random seed, parameters, dates, initiating user, progress, and generated record references. Track email message identifiers and correlation identifiers where available.

Use stable idempotency keys so retries do not create extra invoices or send additional emails unintentionally. Model deliberate duplicate submissions as explicit scenario actions. Ambiguous provider results need reconciliation rather than blind retry.

Keep scenario provenance associated with generated records without replacing the ordinary business audit trail. Normal company screens and reporting should read the resulting business data as usual.

If simulated time is used, its scope must be explicit. Do not assume that advancing the existing finance simulation clock advances mailbox providers, all background workers, or all business policies. Historical dates and real-time scheduled activity must remain understandable to the operator.

## Restarting a demo

Prefer creating a fresh company from a versioned template for the first delivery. Preserve the previous company's history and ensure the new company's mailbox routing and connections remain isolated.

In-place reset is a later capability because it must coordinate company-owned records, documents, queued work, running workers, mailbox contents and ingestion cursors, and provider state. Restoring database data cannot undo previously sent emails or other completed external actions.

Do not reuse the narrow sales scenario reset as a full-company reset without evaluating these broader dependencies.

## Suggested delivery order

1. **Persistent test company:** ordinary onboarding and screens, test identity, company-scoped permissions, and backend external-action policy.
2. **Useful first demo:** dedicated mailbox, starting data, Send supplier bill, and Create customer invoice, with observable end-to-end progress.
3. **Repeatable activity:** durable scenario runs, bounded schedules, pause controls, exception scenarios, and test payment reconciliation.
4. **Reusable demonstrations:** versioned templates and fresh-company provisioning; assess in-place reset separately.

This is a design proposal, not a multi-prompt implementation pack. Detailed implementation prompts should be prepared against the current repository when implementation is requested.

## Acceptance and verification targets

- A test company persists across application restarts and appears in ordinary company selection for authorised members.
- Ordinary screens display its persisted data through the same APIs and services as other companies.
- Unauthorised users cannot generate activity, inspect runs, change test destinations, or access another company's records.
- An emailed PDF supplier bill is received and processed into the ordinary bill workflow without direct bill insertion by the scenario generator.
- A generated customer invoice follows ordinary validation, approval, issuance, rendering, and delivery to an approved test recipient.
- A simulated bank transaction can be reconciled through the ordinary reconciliation workflow.
- Retrying a scenario action does not create unintended duplicate business records or deliveries.
- Changing an external-action policy before dispatch prevents an already queued action from executing against a now-disallowed destination.
- Internal workflow work remains operational while disallowed external effects are blocked with an actionable reason.
- Scenario progress distinguishes queued, provider accepted, received, processed, waiting for approval, failed, and completed outcomes as applicable.
- Pausing a schedule stops new generation and clearly reports already queued or running work.
- Creating a fresh demo preserves the previous company's history and isolates the new company's data and external routing.

Implementation verification should include focused policy and scenario tests, tenant-isolation and authorisation tests, SQL Server migration and persistence checks where needed, outbox/retry tests, and browser checks of ordinary business flows. Live mailbox/provider checks remain separate from automated or adapter-based evidence. This proposal does not claim those checks have been performed.
