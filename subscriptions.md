# Supplier subscriptions

## Executive summary

Virtual Company will treat a supplier subscription as a long-lived agreement that explains and governs a series of supplier bills. A subscription is not a recurring bill template and it never creates accounting documents by itself. It records the commercial agreement, predicts when the next bill should arrive, links actual bills to the expected period, and highlights missing or materially different charges for review.

The first supported flow is:

1. A user registers the supplier contract and its commercial terms.
2. Virtual Company monitors the next expected billing date.
3. A monthly supplier bill enters the existing mailbox/Fortnox bill flow.
4. A deterministic matcher links the bill to the subscription when supplier, currency, amount, and date evidence agree.
5. Existing bill review, approval, Fortnox, payment, and settlement controls remain authoritative.
6. The subscription advances only when a bill is linked to the expected period. Missing, ambiguous, or materially different bills remain visible as exceptions.

This design adds continuity and contract evidence without weakening the supplier-bill approval boundary.

## Goals

- Keep supplier agreements, expected charges, actual bills, and payment outcomes connected.
- Show upcoming renewals, expected bills, missing bills, amount changes, and contract end dates in plain English.
- Match incoming bills deterministically and idempotently.
- Let an authorized finance user confirm or replace an uncertain match.
- Preserve all existing supplier-bill approval and Fortnox rules.
- Keep every query and command company-scoped and auditable.
- Remain compatible with local and Docker SQL Server migration/restore paths.

## Non-goals for the first release

- Creating synthetic supplier invoices or provider records from a schedule.
- Executing bank payments or bypassing payment approval.
- Automatically accepting price increases or changed contract terms.
- A generic contract-lifecycle or procurement platform.
- Provider-specific subscription schemas in the domain model.
- AI-only matching. AI may explain evidence later, but the authoritative match is deterministic.

## Existing solution context

The capability extends the existing Finance modular-monolith boundaries:

- `FinanceCounterparty` remains the supplier master record.
- `FinanceBill` remains the accounting document received from mailbox processing, Fortnox synchronization, or simulation.
- Bill inbox review remains responsible for extraction, validation, duplicate checks, supplier creation, and bill approval.
- The supplier-bill detail page remains responsible for payment proposal, approval, Fortnox draft management, and settlement actions.
- Fortnox remains an adapter, not the system of record for subscription terms.
- Existing approval and audit services remain authoritative for sensitive actions.

## Domain model

### SupplierSubscription

A company-owned agreement with one supplier.

| Field | Purpose |
| --- | --- |
| `Id`, `CompanyId` | Stable tenant-scoped identity. |
| `CounterpartyId` | Required supplier master record. |
| `Name` | Human-readable agreement name. |
| `ContractReference` | Optional supplier/customer contract reference. |
| `Description` | Optional bounded business description. |
| `Currency` | ISO-style three-letter currency used for matching. |
| `ExpectedAmount` | Expected gross amount per billing period. |
| `AmountTolerance` | Maximum absolute amount variance eligible for automatic matching. |
| `Cadence` | Monthly, quarterly, or yearly. |
| `BillingDay` | Preferred day of month, clamped for shorter months. |
| `StartDate`, `EndDate` | Contract validity dates. |
| `NextExpectedBillDate` | Next unfulfilled billing expectation. |
| `DateToleranceDays` | Allowed arrival-date variance for automatic matching. |
| `NoticePeriodDays` | Renewal/cancellation planning information. |
| `AutoRenews` | Whether the agreement renews unless cancelled. |
| `Status` | Draft, active, paused, cancelled, or expired. |
| `ContractDocumentId` | Optional link to the stored source contract. |
| timestamps | Creation and last-change audit support. |

The aggregate validates required identifiers, supported status/cadence values, positive expected amounts, non-negative tolerances, sensible dates, and bounded text. Activating a subscription sets a valid next expected date. Pausing/cancelling stops automatic matching without deleting history.

### SupplierSubscriptionBillMatch

An immutable-in-identity, updateable-in-decision link between one bill and one subscription period.

| Field | Purpose |
| --- | --- |
| `SubscriptionId`, `BillId`, `CompanyId` | Tenant-safe relationship. One bill can have at most one current subscription match. |
| `PeriodStart`, `PeriodEnd`, `ExpectedBillDate` | The expected period represented by the bill. |
| `ExpectedAmount`, `ActualAmount`, `AmountVariance` | Preserved matching evidence. |
| `Status` | Suggested, confirmed, rejected, or exception. |
| `MatchMethod` | Automatic or manual. |
| `ConfidenceScore` | Deterministic 0-100 score for presentation, not authorization. |
| `EvidenceSummary` | Plain-English bounded explanation. |
| actor/timestamps | Who confirmed/rejected the match and when. |

Unique indexes prevent duplicate period matches and more than one active match per bill. Repeated processing returns the existing result.

## Lifecycle and workflow

### Agreement states

- **Draft**: terms are being prepared; no matching or missing-bill alerts.
- **Active**: bills may match and the next expected date is monitored.
- **Paused**: history remains, but no automatic matching or missing-bill escalation.
- **Cancelled**: terminal for new periods; historic matches remain.
- **Expired**: end date has passed; historic matches remain.

### Expected billing period states

The period state is computed from subscription and match data instead of a separate mutable schedule table:

- **Upcoming**: next expected date is in the future.
- **Due**: within the configured date tolerance.
- **Missing bill**: past the expected date plus tolerance with no confirmed bill.
- **Matched**: a bill is linked to the period.
- **Needs review**: one or more plausible bills exist, or an amount/date exception exceeds automatic limits.

### Matching rules

Automatic matching runs for a company-scoped `FinanceBill` and considers active subscriptions only.

Hard requirements:

- the bill and subscription belong to the same company;
- the subscription supplier equals the bill counterparty;
- currency matches case-insensitively;
- the bill is not cancelled or a credit note;
- the bill is not already confirmed against another subscription.

Scoring evidence:

- supplier match: required;
- currency match: required;
- amount within tolerance: strong evidence;
- received/due date within the expected-date window: strong evidence;
- bill number or contract reference hints: supporting evidence only.

Exactly one candidate meeting the automatic threshold is confirmed. Multiple candidates become suggested matches and require a user decision. Candidates outside tolerances are retained as exceptions only when the supplier and currency match closely enough to be useful. No candidate leaves the subscription period as upcoming/due/missing.

Confirming a match advances `NextExpectedBillDate` by cadence from the matched period, not from the processing timestamp, preventing schedule drift. A retry cannot advance the schedule twice.

### Existing bill controls remain unchanged

A subscription match is evidence, not approval. It must not:

- approve a supplier bill;
- authorize creation of a supplier or bill in Fortnox;
- approve a payment proposal;
- export or register a payment;
- mark a bill paid or booked.

All of those actions continue through their existing backend policies, approvals, durable provider execution, idempotency, retry, reconciliation, and audit paths.

## Application and API design

Create a focused `ISupplierSubscriptionService` in Finance rather than adding unrelated methods to the broad Finance read facade.

Commands:

- create an agreement;
- update commercial terms;
- activate, pause, resume, or cancel;
- evaluate a bill for matching;
- confirm or reject a suggested match;
- remove an incorrect manual link only through an audited replacement operation.

Queries:

- list subscriptions with supplier, health, next expected date, and last bill;
- get subscription detail and match history;
- get a bill's subscription context;
- list due/missing/exception items.

Routes live under:

`/internal/companies/{companyId}/finance/supplier-subscriptions`

All endpoints require company context. Reads use the Finance view policy. Mutations use the Finance approval policy. Controllers only validate transport input, resolve actor context, invoke the service, and map safe responses.

## User experience

Add **Subscriptions** as a secondary Supplier bills view, not a new primary navigation destination.

The page uses the established list/detail operational layout:

- left: filterable agreement list with supplier, expected amount/cadence, next expected bill, and health badge;
- right: selected agreement summary, next action, agreement terms, bill history, and lifecycle actions;
- clear empty state with **Add subscription**;
- grouped create/edit form using plain-English labels;
- no raw enum values or identifiers;
- responsive stacking on narrower screens.

The supplier-bill detail panel shows a compact subscription context card when a confirmed or suggested match exists. It states the agreement, covered period, expected versus actual amount, and whether confirmation is needed. Existing payment and approval actions remain separate.

## Mailbox and contract intake

The first production release supports registering agreement terms through the UI and optionally linking an existing company knowledge document as the contract source. Contract ingestion is intentionally separated from bill ingestion because a contract is not an invoice and must not enter the supplier-bill approval queue.

A later classifier can propose subscription terms from an inbound contract, but it must create a draft proposal for human confirmation and use the shared AI orchestration boundary. It must not activate a subscription from an LLM result alone.

## Audit and observability

Persist business audit events for create, update, activate, pause, resume, cancel, automatic match, manual confirmation, rejection, and replacement. Include actor, company, subscription, bill when applicable, outcome, evidence summary, correlation ID, and timestamps.

Technical logs include company ID, subscription ID, bill ID, candidate count, selected outcome, and safe failure summary. Never log contract bodies, credentials, tokens, or provider payloads.

## Security and tenancy

- Every entity and query is company-scoped.
- Foreign-key relationships include tenant-consistency checks in application code.
- API routes and headers are context input, not authorization proof.
- Cross-company reads, writes, and bill links are rejected.
- Contract-document access uses the existing document access boundary.
- No hard delete endpoint is exposed; terminal records retain audit history.

## Migration and compatibility

Add SQL Server EF Core tables, foreign keys, indexes, check-compatible lengths/precision, and query filters through `VirtualCompany.Persistence.Migrations`. Update the model snapshot. Do not add startup DDL or `EnsureCreated` behavior.

The migration is provider-neutral within the supported SQL Server model and requires no change to local or Docker restore scripts. Both environments continue restoring the same backup and applying the same migration history.

## Testing strategy

- Domain tests for validation, cadence advancement, end dates, and status transitions.
- Finance service tests for deterministic scoring, idempotency, ambiguous candidates, tolerances, and schedule advancement.
- Tenant-isolation tests for reads, mutations, document links, and bill links.
- API tests for authorization, validation, not-found behavior, and safe errors.
- Web client/component tests for routes, plain-English presentation, empty/error/loading states, and actions.
- SQL Server migration validation and `has-pending-model-changes` verification.
- Regression tests showing a subscription match does not bypass existing bill or payment approvals.

## Delivery stages

1. Domain, persistence, migration, and deterministic schedule semantics.
2. Company-scoped service, matching, audit, and API.
3. Bill-ingestion integration and exception visibility.
4. Subscription workspace and bill-detail context.
5. Full validation, migration audit, and operational documentation.

