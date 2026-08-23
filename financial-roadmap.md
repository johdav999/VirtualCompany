# Virtual Company Financial Product Roadmap

Status: repository-grounded plan as of 2026-08-23.

## Product outcome

Virtual Company should become a production-grade accounting application for small and medium-sized businesses, with a governed native ledger as the accounting source of truth, complete receivables and payables workflows, bank and payment connectivity, statutory country packs, period close, reporting, migration, audit evidence, and a finance agent that accelerates work without bypassing accounting policy or human approval.

This roadmap does not restart the finance product. It builds on the substantial accounting foundation already present in the repository.

## Planning assumptions

- The internal ledger remains the primary long-term product, while external accounting systems remain supported through adapters and controlled provider switching.
- Sweden is the recommended first statutory market because the repository already contains Fortnox integration, Swedish payment-field extraction, English/Swedish UI resources, and Sweden-oriented operating context. The jurisdiction must still be confirmed before Release 1 starts.
- The initial commercial target is an owner-managed or finance-team-led SME, not a multinational enterprise.
- External payments, submissions, invoice delivery, and provider writes remain approval-bound, idempotent, outbox/background executed, reconcilable, and auditable.
- Every release must preserve company isolation, immutable posted accounting, source/version identity, evidence links, local and Docker SQL Server compatibility, and safe provider recovery.
- Estimates are intentionally omitted until team size, target jurisdiction, certification approach, and external providers are selected. The sequence expresses dependencies, not calendar commitments.

## What is implemented now

| Area | Repository-backed implementation | Remaining product gap |
|---|---|---|
| Accounting foundation | Company-scoped accounting configuration, policy-pack selection, authority periods, chart setup, fiscal years, voucher series and sequences, immutable journals, evidence links, reversals, source/version idempotency, and a governed posting service. See [AccountingLedgerContracts.cs](src/VirtualCompany.Application/Finance/Contracts/AccountingLedgerContracts.cs), [AccountingPostingService.cs](src/VirtualCompany.Infrastructure.Finance/Finance/AccountingPostingService.cs), and [AccountingAdministrationService.cs](src/VirtualCompany.Infrastructure.Finance/Finance/AccountingAdministrationService.cs). | The only registered packs are country-neutral. They explicitly do not claim statutory compliance. |
| Journal operations | Journal list/detail, manual journal drafts, preview, approval, posting, adjusting journals, evidence, corrections, and audit history. The Web includes journal and manual-journal workbenches. | Add recurring journals, accrual schedules, allocation templates, stronger account lifecycle/reportability controls, and accountant productivity features. |
| Accounts receivable | Customer/invoice read models, review workflow, accounting preview and approval, native posting, credit notes, open-item reconciliation, payment allocations, overdue recommendations, and Fortnox invoice actions. | Complete native invoice origination, numbering, rendering, delivery, recurring billing, reminders, e-invoicing, and customer statements. |
| Accounts payable | Bill inbox, PDF/DOCX/email extraction, optional OCR, duplicate detection, supplier review, coding, approval automation, native supplier-bill accounting, credit notes, subscriptions, payment proposals, source attachments, and Fortnox supplier-invoice actions. | Add purchase orders and receipts, three-way matching, employee expenses/cards, native payment batches, remittance, and stronger supplier controls. |
| Cash and reconciliation | Bank accounts and statement imports, bank transactions, payment links and allocations, reconciliation suggestions and scoring, partial/unmatched/suspense/conflict states, governed suspense posting and correction, and cash-position analytics. | Add production bank feeds, payment initiation, transfer workflows, richer rules, bank-feed operations, and treasury controls. |
| Reporting and close | General ledger, trial balance, profit and loss, balance sheet, tax summary, control-account reconciliation, statement snapshots and drill-down, close validation, lock/reopen history, and background exports. See [AccountingReportingContracts.cs](src/VirtualCompany.Application/Finance/Contracts/AccountingReportingContracts.cs) and [AccountingReportsPage.razor](src/VirtualCompany.Web/Pages/Finance/AccountingReportsPage.razor). | Current tax output and export are country-neutral, not statutory returns. Cash-flow, equity, comparative, aged-ledger, and audit-pack reporting need product completion. |
| Planning and insight | Budget CRUD, stored forecasts, actual-versus-budget/forecast variance, cash projections, dashboards, anomaly detection, finance insights, and agent decision support. | Planning is API/data oriented rather than a complete planning product. It needs a first-class UI, versions, approvals, scenarios, drivers, and forecast workflows. |
| Integrations | Fortnox OAuth, token storage, read synchronization, external references, approved write commands, retry/reconciliation handling, operator diagnostics, and provider management. | Fortnox is the only production accounting provider. Bank, payment, e-invoice, payroll, commerce, and additional accounting adapters are absent or incomplete. |
| Provider migration | The full switch lifecycle is implemented: assessment, capability inventory, staging, mappings, rehearsal, preparation, target transfer, freeze, activation, monitoring, corrective cutover, agent tools, and guided UI. See [financial-migration-prompts.md](financial-migration-prompts.md) and [accounting-operations-runbook.md](docs/finance/accounting-operations-runbook.md). | Add adapters only as new providers are introduced; the orchestration foundation should be reused rather than redesigned. |
| Governance and operations | Finance permissions, tenant-scoped APIs, approval workflows, policy checks, audit events, telemetry, leased background work, bounded retries, recovery verification, and SQL/object restore runbooks. | Repository-wide test health remains a release gate. The latest checked-in monitoring evidence records 205 unrelated API failures and 14 Web contract failures despite focused finance suites passing. |
| User experience | Finance overview, cash position, transactions, payments, bills, bill inbox, subscriptions, invoices, reviews, counterparties, balances, monthly summary, anomalies, accounting setup/accounts/periods/journals/reconciliation/reports/connections, settings, and migration workspace. | Workflows need consolidation into coherent daily accounting queues, responsive polish, accessibility verification, and authenticated browser evidence with realistic production-shaped data. |

## Maturity gates

| Gate | Releases | Meaning |
|---|---|---|
| Native accounting core GA | Release 0 | Existing internal-ledger functionality is stable, operable, recoverable, and safe to enable for production tenants. |
| Sweden-ready SME accounting | Releases 1–4 | A Swedish SME can invoice, process bills, pay and collect money, reconcile banks, produce reviewed statutory outputs, and close periods without relying on Fortnox as the accounting authority. |
| Full-fledged accounting application | Releases 5–7 | The product supports advanced ledger operations, fixed assets, planning, close management, accountant workflows, and audit-ready reporting. |
| Extensible finance platform | Releases 8–9 | Multiple providers and business systems can connect, and larger multi-entity customers can operate with controlled automation. |

## Release sequence

| Release | Name | Primary outcome | Depends on |
|---|---|---|---|
| 0 | Accounting Core GA | Make the implemented native ledger production-releasable. | Current repository |
| 1 | Sweden Statutory Foundation | Make native books legally and operationally usable in the first market. | Release 0 |
| 2 | Native Receivables | Run the complete customer-to-cash accounting lifecycle. | Releases 0–1 |
| 3 | Native Payables and Spend | Run the complete supplier-to-payment accounting lifecycle. | Releases 0–1 |
| 4 | Connected Banking and Treasury | Automate bank ingestion, matching, approved payments, and cash control. | Releases 2–3 |
| 5 | Advanced Ledger and Subledgers | Support multi-currency, schedules, dimensions, and fixed assets. | Releases 1–4 |
| 6 | Close, Compliance, and Accountant Workspace | Make month/year-end and external review controlled and efficient. | Release 5 |
| 7 | Planning and Finance Intelligence | Turn actual accounting into forward-looking management. | Releases 4–6 |
| 8 | Finance Ecosystem | Add providers, public integration contracts, and operational interoperability. | Releases 2–7 |
| 9 | Multi-Entity and Governed Autonomy | Support groups, firms, and higher-volume controlled automation. | Releases 6–8 |

## Release 0 — Accounting Core GA

### Outcome

The existing internal ledger can be enabled for production companies with credible release, support, recovery, and data-quality controls.

### Reuse

- Native posting and immutable journal model.
- Accounting setup, authority, accounts, periods, reports, close, bank reconciliation, approvals, and audit.
- Historical migration and provider-switch operations.
- Existing SQL Server migration and coordinated object-recovery procedures.

### Scope

- Resolve or explicitly quarantine every repository-wide API and Web contract failure; accounting release gates may not depend only on focused filters.
- Add one deterministic end-to-end production scenario: setup → customer invoice → supplier bill → approval → posting → payments → allocations → bank reconciliation → tax review → period close → export → recovery verification.
- Remove production reliance on simulation/mock finance data and make source mode visible on every relevant screen and export.
- Verify all background workers for duplicate delivery, expired leases, cancellation, bounded retry, poison work, and operator-visible failure.
- Establish production service-level objectives for posting, read models, exports, synchronization, and reconciliation queues.
- Add query/performance budgets, indexes, pagination, retention rules, archive handling, and observability dashboards.
- Complete authenticated browser verification for every accounting route in English and Swedish, including keyboard navigation, screen-reader labels, empty/error/loading states, and narrow layouts.
- Automate fresh install, representative upgrade, backup, local SQL restore, Docker restore, object checksum, and disaster-recovery evidence in CI or a controlled release pipeline.

### Exit criteria

- Full solution build and agreed production test matrix pass with no unexplained finance/accounting failures.
- EF reports no pending model changes and both local and Docker SQL Server paths apply all migrations.
- The end-to-end accounting scenario balances and can be restored with identical journal, source, evidence, and snapshot checksums.
- Cross-company, employee/admin, stale-version, duplicate-command, provider-timeout, and worker-restart tests pass.
- Operations can identify, retry, reconcile, or safely stop every accounting background failure without direct data editing.

## Release 1 — Sweden Statutory Foundation

### Outcome

Virtual Company moves from country-neutral bookkeeping to an independently reviewed Swedish accounting policy pack and statutory workflow.

### Reuse

- Versioned policy-pack model and pack selection history.
- Account-role resolution, tax facts on journals, close locks, evidence retention, exports, and compliance notices.
- Fortnox mapping and migration evidence as comparison fixtures, not as statutory authority.

### Scope

- Implement a versioned Swedish policy pack with a reviewed BAS chart template, account roles, tax treatments, posting rules, invoice requirements, retention policy, and effective-date upgrades.
- Model Swedish VAT codes and effective dates, domestic/EU/non-EU sales and purchases, reverse charge, imports, corrections, rounding, and blocked/partially deductible input VAT where applicable.
- Produce an evidence-backed VAT return workspace with source drill-down, review, approval, lock, correction flow, and submission/export adapter boundary.
- Implement a reviewed SIE export and any additional statutory export selected for launch.
- Enforce legal invoice fields, sequential numbering, credit-note references, bookkeeping dates, fiscal-year rules, retention, and correction-by-reversal.
- Add organisation identity, VAT registration, legal address, payment identity, and statutory configuration to company setup.
- Establish a formal pack-validation process: versioned fixtures, accountant review evidence, golden reports, migration/upgrade tests, and explicit unsupported cases.

### Exit criteria

- A qualified Swedish accounting reviewer signs off the policy-pack version and golden fixtures.
- Representative sales, purchases, credits, payments, adjustments, period close, and VAT scenarios produce expected journals and return boxes.
- Statutory outputs reconcile exactly to the locked ledger and retain pack/version/checksum evidence.
- Policy changes affect only future-effective activity; historical journals and reports preserve the original pack provenance.
- Unsupported tax scenarios stop with plain operator guidance rather than falling back to invented treatment.

## Release 2 — Native Receivables

### Outcome

A business can manage the complete customer-to-cash cycle natively, from customer setup and invoice drafting through delivery, collection, settlement, and accounting.

### Reuse

- Existing counterparties, invoice records, review workflow, customer-invoice accounting, approvals, credit notes, payments, allocations, reminders/recommendations, and Fortnox write boundary.
- Existing document storage, audit, outbox, notifications, and email integration.

### Scope

- Complete customer master data with tax identity, addresses, language, currency, payment terms, delivery preferences, credit limits, and duplicate handling.
- Add invoice drafts and line editing, product/service lines, tax calculation, discounts, rounding, dimensions, attachments, preview, approval, and immutable issue/bookkeeping transitions.
- Allocate native invoice and credit-note numbers transactionally from configurable series.
- Generate accessible PDF invoices and deliver them through outbox-backed email; add delivery status, retry, bounce/failure visibility, and retained rendered evidence.
- Add recurring invoice schedules, proration rules, pause/resume, preview, approval thresholds, and safe generation idempotency.
- Add customer credit notes, cancellation where legally allowed, refunds, write-offs, bad-debt workflow, and correction links.
- Add aged receivables, customer statements, reminder schedules, dunning fees/interest policy, disputes, promises to pay, and Laura-assisted collection queues.
- Add e-invoice/Peppol behind a provider adapter when selected; do not couple e-invoice transport to the ledger service.
- Complete foreign-currency invoice handling only to the extent enabled by Release 5; until then, reject unsupported accounting combinations explicitly.

### Exit criteria

- An authorized user can create, approve, issue, send, post, credit, collect, allocate, and reconcile a native invoice without Fortnox.
- Duplicate issue/delivery/payment events are idempotent and externally visible failures reconcile safely.
- AR control-account reconciliation equals the open-item ledger for all supported lifecycle states.
- Invoice PDFs, delivery evidence, approvals, journal links, and customer communications are available from one audit trail.

## Release 3 — Native Payables and Spend

### Outcome

A finance team can control the supplier-to-payment cycle natively, including commitments, bill intake, approval, posting, payment preparation, and supplier reconciliation.

### Reuse

- Bill inbox, extraction/OCR, source documents, duplicate checks, supplier subscriptions, accounting coding, approvals, credit notes, payment proposals, and native supplier-bill posting.
- Existing approval chains, tasks, finance agent decisions, and Fortnox provider actions.

### Scope

- Complete supplier master data with organization/tax identity, payment details, currencies, payment terms, withholding flags where relevant, approval policy, and verified change history.
- Add purchase requisitions, purchase orders, approvals, receipts, cancellations, and commitment accounting where selected.
- Add two-way and three-way match, tolerance policies, partial receipt/invoice handling, price/quantity variance, exceptions, and evidence drill-down.
- Strengthen bill intake with vendor validation, duplicate confidence, tax validation, coding templates, dimension defaults, recurring-bill matching, and safe low-confidence queues.
- Add employee expense claims, receipt capture, mileage/per-diem extension points, corporate-card feeds, approval, reimbursement, and accounting.
- Create native payment batches with proposal, approval, due-date optimization, payment-file/provider export, acknowledgement, rejection, cancellation, and remittance advice.
- Support supplier credits, refunds, advances, overpayments, write-offs, payment holds, disputed bills, and supplier statements.
- Ensure payment-detail changes require strong authorization, audit history, out-of-band verification policy, and reapproval of affected batches.

### Exit criteria

- A bill can enter by upload/email, be extracted, matched or coded, approved, posted, paid, allocated, and reconciled without external-accounting authority.
- AP control-account reconciliation equals supported open supplier items and payment allocations.
- No payment instruction is emitted without current approval, stable beneficiary evidence, idempotency, and an operator-visible acknowledgement state.
- PO, expense, subscription, and non-PO bill paths preserve distinct source identity and audit evidence.

## Release 4 — Connected Banking and Treasury

### Outcome

Bank data and approved money movement flow reliably into the accounting product, while cash position and reconciliation become daily operational workflows.

### Reuse

- Bank accounts, statement import, transactions, reconciliation suggestions, partial allocations, suspense/corrections, payment links, and cash analytics.
- Integration registry, token handling, external references, write approvals, leased workers, and reconciliation patterns.

### Scope

- Add an open-banking/bank-feed adapter contract and at least one production Swedish bank or aggregator integration.
- Support scheduled and manual synchronization, stable provider identity, pagination/cursors, consent renewal, rate limits, duplicate detection, and missing-range recovery.
- Add ISO 20022 statement/import support where required, alongside bounded CSV/manual imports with mapping profiles and previews.
- Add approved payment initiation or bank-file submission, status polling, rejection handling, cancellation boundaries, and settlement reconciliation.
- Add bank transfers, account-to-account movements, fees, interest, card settlement, payout batches, and cash-account ownership controls.
- Expand matching rules to learned but explainable counterparty/reference patterns, split/partial matches, one-to-many/many-to-one settlement, and rule-version evidence.
- Add daily cash operations: feed health, unreconciled aging, expected inflows/outflows, liquidity thresholds, short-horizon forecast, and finance-owner tasks.

### Exit criteria

- At least one production bank path can ingest transactions continuously and demonstrate gap-free recovery after an interrupted sync.
- Approved payment instructions have an end-to-end state from proposal through bank acknowledgement and ledger reconciliation.
- Every bank row is imported once, remains traceable to its raw source, and ends matched, explicitly unmatched, in suspense, or in a visible conflict.
- Cash balances reconcile to imported bank evidence for every connected account and currency.

## Release 5 — Advanced Ledger and Subledgers

### Outcome

The native ledger supports the accounting depth expected from a mature SME product rather than only basic transaction posting.

### Reuse

- `IAccountingPostingService` as the only posting boundary.
- Existing account roles, tax facts, authority periods, immutable journals, reversals, evidence, source identity, and financial-statement mappings.

### Scope

- Implement true multi-currency accounting: document currency, functional currency, exchange-rate sources, rate dates, rounding, realized gains/losses, period-end revaluation, and reproducible historical rates.
- Promote cost centers/projects into governed accounting dimensions with effective dates, required-dimension policies, combinations, validation, allocation, and reporting filters.
- Add recurring journals, accruals, deferrals/prepayments, allocation templates, automatic reversals, and schedule-generated entries with approval and idempotency.
- Build a fixed-asset subledger: asset classes, capitalization, components, useful life, depreciation methods, impairment, transfer, disposal, gain/loss, register reports, and ledger reconciliation.
- Add inventory/COGS accounting only if the selected SME segment requires it; otherwise define a stable commerce/inventory integration boundary and keep inventory quantity outside accounting.
- Complete account lifecycle/reportability flags, effective-dated renames/mappings, posting restrictions, retained history, merge prevention, and replacement-account workflows.
- Add configurable document and voucher series by transaction type, location, fiscal year, and jurisdictional rule.

### Exit criteria

- Multi-currency open items, settlements, revaluation, and reporting reconcile by currency and functional amount.
- Dimension totals drill down to the same immutable journal lines as the general ledger.
- Accrual, prepayment, recurring, and depreciation schedules can be regenerated safely without duplicate journals.
- Every subledger has a deterministic control-account reconciliation and a blocking close check.

## Release 6 — Close, Compliance, and Accountant Workspace

### Outcome

Month-end, year-end, statutory review, and external accountant collaboration become controlled workflows rather than a collection of separate screens.

### Reuse

- Period close validation, reporting locks, snapshots, tax review, control reconciliations, audit events, approvals, tasks, exports, and recovery verification.

### Scope

- Create a close cockpit with reusable templates, owners, due dates, dependencies, sign-offs, evidence requests, status history, and company-specific materiality thresholds.
- Add close tasks for bank, AR, AP, tax, suspense, payroll import, assets, accruals, deferred items, interperiod checks, provider backlog, and statement review.
- Add cash-flow statement, statement of changes in equity, comparative periods, rolling twelve months, aged AR/AP, fixed-asset register, journal register, tax detail, and dimension reports.
- Add custom report layouts and account-group mappings without mutating historical journal classification; version report definitions and snapshots.
- Produce a downloadable audit package with trial balance, general ledger, statements, tax evidence, reconciliations, approvals, significant journals, source documents, policy-pack provenance, and checksums.
- Add an accountant role and collaboration workspace with scoped client access, review notes, evidence requests, prepared-by/reviewed-by separation, and immutable sign-off history.
- Add formal year-end rollover, retained earnings transfer, opening-balance verification, reopening policy, subsequent-event notes, and forward correction workflow.

### Exit criteria

- A month can be closed from one workspace with every blocking condition backed by source evidence and a responsible owner.
- Closed reports reproduce from their snapshot and checksum; later corrections follow explicit reopen or future-period correction policy.
- An external accountant can trace every reported balance to journals, source records, documents, approvals, and policy-pack version without database access.
- Year-end rollover produces verified opening balances and does not mutate the closed prior year.

## Release 7 — Planning and Finance Intelligence

### Outcome

Virtual Company connects governed accounting actuals to budgeting, forecasting, variance management, cash planning, and finance-agent assistance.

### Reuse

- Existing budgets, forecasts, variance queries, cash projections, revenue forecast snapshots, anomaly detection, finance insights, tasks, approvals, and Laura's governed tool boundary.

### Scope

- Build a planning workspace for annual budgets, monthly phasing, versions, dimensions, drivers, comments, owners, submission, approval, locking, and copy/roll-forward.
- Add rolling forecasts, scenarios, assumptions, sensitivities, forecast-versus-budget-versus-actual comparison, and retained model/version evidence.
- Add 13-week cash forecasting with receivables, payables, subscriptions, payroll/other imported commitments, payment timing, scenarios, and confidence/source indicators.
- Add management reporting packs with narrative, KPIs, trends, variance thresholds, drill-down, scheduled generation, and approval before external delivery.
- Let Laura prepare reconciliations, propose coding, explain variances, draft collection/payment priorities, assemble close evidence, and answer grounded finance questions.
- Keep AI outputs in recommend/prepare mode by default. Posting, payments, tax submission, period close, policy changes, provider activation, and material write-offs remain policy- and approval-controlled.
- Measure recommendation acceptance, override reasons, false positives, time saved, unresolved exceptions, and control breaches without storing hidden reasoning.

### Exit criteria

- Finance users can create, approve, lock, revise, and compare a full budget and forecast from the Web UI.
- Every planning number identifies its version, driver or source, owner, currency, period, and dimensions.
- Laura's recommendations are reproducible from cited records, stay tenant-scoped, and cannot bypass posting or external-action boundaries.
- Forecast quality and automation outcomes are monitored with explicit business metrics.

## Release 8 — Finance Ecosystem

### Outcome

The accounting app connects cleanly to the systems SMEs already use and can migrate between providers without bespoke orchestration.

### Reuse

- Finance integration registry, OAuth/token storage, external references, provider capabilities, provider-switch lifecycle, outbox execution, write approvals, reconciliation, and monitoring.

### Scope

- Add a second accounting provider end to end to prove the adapter architecture beyond Fortnox.
- Add production adapters for e-invoicing, payment/banking, payroll journals, expense/card systems, and one commerce/POS platform selected from customer demand.
- Publish versioned import/export schemas and a tenant-scoped public finance API for counterparties, documents, journals, payments, dimensions, attachments, and reporting reads.
- Add signed webhooks with replay protection, delivery logs, idempotency, versioning, subscription management, and safe tenant resolution.
- Add a self-service import center for CSV/SIE/provider files with preview, mappings, validation, dry run, resumable execution, reconciliation, and rollback-before-activation rules.
- Extend provider-switch adapters for every supported provider while keeping the existing assessment, staging, rehearsal, cutover, and monitoring workflow unchanged.
- Add integration certification fixtures, sandboxes, contract tests, rate-limit simulations, provider status pages, and operator runbooks.

### Exit criteria

- At least two accounting providers and the selected bank/payment/e-invoice paths pass the same capability, idempotency, authorization, reconciliation, and recovery contract suites.
- External applications can integrate without direct database access or provider-specific logic in controllers/UI.
- Migration rehearsals reconcile counts, totals, open items, tax, currency, documents, and evidence before activation.
- Integration failures never become silent accounting success and always expose a safe operator action.

## Release 9 — Multi-Entity and Governed Autonomy

### Outcome

Virtual Company can serve accounting firms and company groups while safely automating high-volume routine work.

### Reuse

- Company isolation, memberships, accounting authority, dimensions, policy packs, approvals, audit, provider switching, accountant workspace, and agent orchestration.

### Scope

- Add group structures without weakening tenant boundaries: explicit group access, company membership, scoped cross-company reads, and auditable context switching.
- Add intercompany counterparties, mirrored transactions, matching, settlement, elimination proposals, and imbalance workflows.
- Add consolidation ledgers, ownership/effective dates, currency translation, eliminations, consolidation adjustments, group reports, and subsidiary-to-group drill-down.
- Add multi-book reporting only when a real requirement exists; preserve one governed posting boundary per book and explicit bridge entries.
- Add accounting-firm portfolio views for deadlines, VAT/close status, unreconciled items, failed integrations, approvals, and client evidence requests.
- Introduce policy-bounded automation levels for high-confidence coding, matching, recurring postings, reminder delivery, and close-task preparation.
- Require sampled review, confidence thresholds, monetary limits, segregation of duties, automatic rollback-by-reversal, drift monitoring, and a global stop control for autonomous routines.

### Exit criteria

- Cross-company access is explicit, least-privilege, and proven not to bypass company-level authorization.
- Consolidated statements reproduce from retained subsidiary snapshots, translation rates, eliminations, and adjustment journals.
- Autonomous actions remain within configured authority and produce the same approval, journal, evidence, audit, and reconciliation records as human actions.
- Operators can pause automation globally or by company/workflow without corrupting in-flight accounting work.

## Cross-release architecture rules

Every release must:

- Follow [architecture-rules.md](docs/architecture-rules.md); keep Domain and Application free of infrastructure dependencies and keep controllers thin.
- Route every native posting through `IAccountingPostingService`; never duplicate voucher allocation, authority, period, balancing, or idempotency logic in source workflows.
- Keep external systems behind application-owned contracts and infrastructure adapters.
- Use durable outbox/background execution for external side effects, with idempotency, bounded retry, acknowledgement tracking, reconciliation, and operator-visible failure.
- Preserve tenant keys on entities, relationships, indexes, worker claims, audit records, caches, exports, and object-storage references.
- Treat posted journals as immutable; use reversals, corrections, effective dates, and versioned mappings instead of destructive edits.
- Require current approvals for money movement, statutory submission, material write-offs, period close/reopen, policy-pack changes, and authority/provider changes.
- Store safe rationale summaries and source references, never model chain-of-thought or secrets.
- Add EF migrations where needed and prove both local SQL Server and Docker restore/run compatibility.
- Follow [design.md](docs/design.md) and its screenshot-first workflow for every new or materially changed accounting UI.
- Include unit, integration, authorization, tenant-isolation, migration, provider-contract, concurrency, retry, recovery, build, and browser checks in proportion to risk.

## Product metrics by maturity gate

| Area | Core GA target | Full-fledged target |
|---|---|---|
| Ledger integrity | Zero unbalanced posted journals; zero duplicate source/version or voucher identities. | Same, including all subledgers, currencies, dimensions, and imports. |
| Close | Every blocker is visible and actionable. | Routine monthly close duration and late-task rate measured and improving. |
| Reconciliation | All bank rows have an explicit state. | High-confidence automatic match rate measured with low override/error rate. |
| AR/AP | Open-item totals reconcile to control accounts. | Invoice-to-cash and bill-to-payment cycle time, overdue value, and exception rate tracked. |
| Integrations | No silent external outcome; failures reconcile. | Provider availability, sync lag, webhook delivery, and recovery SLOs met. |
| Compliance | Country-neutral limitations are explicit. | Reviewed jurisdiction pack and statutory outputs pass golden fixtures and accountant sign-off. |
| Automation | Recommendations cannot bypass policy. | Automation acceptance, override, exception, and control-breach rates are monitored. |
| Operations | Backups and recovery are repeatable. | Recovery point/time objectives are proven in scheduled rehearsals. |

## Immediate next decisions

Before converting Release 0 and Release 1 into implementation prompts, decide:

1. Confirm Sweden as the first statutory jurisdiction and appoint the qualified accounting reviewer.
2. Confirm the primary customer segment: Swedish microbusiness, SME with a finance team, or accounting firm.
3. Decide whether native Virtual Company accounting or Fortnox is the launch-default authority for new companies.
4. Select the first bank/open-banking, payment, e-invoice, and statutory-submission providers.
5. Define the production test-health policy: repair all failures or formally quarantine proven unrelated tests with owners and deadlines.
6. Define release SLOs, retention periods, recovery objectives, and supported data volumes.

The recommended next delivery unit is Release 0. It converts the already broad implementation into a trustworthy production baseline and prevents statutory, banking, and workflow work from accumulating on top of unresolved release-health debt.
