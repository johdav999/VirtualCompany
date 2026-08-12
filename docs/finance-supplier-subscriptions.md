# Supplier Subscriptions Operations

Supplier subscriptions track recurring supplier agreements and the evidence that links real supplier bills to the expected billing period. A subscription match never approves, books, exports, pays, settles, or changes an existing supplier bill approval state.

## Runtime Behavior

- Agreements are company-scoped and tied to a supplier counterparty.
- Only active agreements are evaluated automatically.
- Matching requires the same company, supplier, currency, positive bill amount, and a non-cancelled supplier bill.
- One eligible candidate inside amount and date tolerance is confirmed automatically.
- Multiple eligible candidates are kept as suggestions and require finance approval to confirm or reject.
- Outside-tolerance candidates can be retained as review exceptions.
- A confirmed match advances the next expected bill date once for that period.


## Inbox Agreement Discovery

- Finance mailbox scans now run supplier-subscription discovery after message and attachment snapshots are persisted.
- Agreement-like emails or attachments create `SupplierSubscriptionIntakeProposal` review items; they do not create active subscriptions, supplier bills, Fortnox records, payment proposals, or settlements.
- The classifier separates likely agreements from recurring-payment receipts and ordinary supplier invoices. Receipts are evidence only and do not bypass human subscription review. When finance policy allows it, an authorized user can link a receipt bill to an existing subscription as reviewable `receipt_evidence`; it remains suggested until confirmed and does not approve, pay, export, settle, or advance the subscription schedule by itself.
- Proposal source evidence is bounded to safe metadata and snippets: source email/attachment references, extracted supplier evidence, proposed terms, confidence, and a safe failure summary when extraction cannot complete.
- Duplicate scans reuse the same source fingerprint, so repeated mailbox scans do not create duplicate active proposals or accepted subscriptions for the same agreement source.
- Proposal failures are operator-visible through proposal status, API problem responses, logs, and audit events; bill intake continues independently when subscription discovery fails.
## Operator Recovery

- Use Finance > Supplier subscriptions to review missing bills, upcoming bills, paused agreements, and suggested matches.
- Use the Inbox discovery queue on Finance > Supplier subscriptions to review agreement proposals, correct supplier/terms, accept them into draft subscriptions, retry extraction, or reject bad detections.
- Accepted proposal evidence appears on the subscription detail page as source agreement evidence, including source message/attachment, evidence summary, and review decision.
- Use Finance > Mailbox to open scanned messages; messages that produced agreement proposals link back to the subscription review queue without changing existing detected-bill links.
- Use the supplier bill detail subscription card to confirm or reject suggested evidence from the bill workflow, including manually linked receipt evidence.
- If automatic evaluation fails during Fortnox supplier invoice sync, the bill remains synced and usable. The failure is logged with company and bill identifiers, and the bill can be evaluated again from the subscription API/UI.
- Existing supplier bill approval, Fortnox registration, payment proposal, and settlement controls remain authoritative.

## Deployment And Migration

SQL Server remains the production database provider. Do not add startup DDL for this feature.

Apply migrations locally or against Docker SQL Server with:

```powershell
& "$env:USERPROFILE\.dotnet\tools\dotnet-ef.exe" database update --project src\VirtualCompany.Persistence.Migrations\VirtualCompany.Persistence.Migrations.csproj --startup-project src\VirtualCompany.Api\VirtualCompany.Api.csproj --context VirtualCompanyDbContext
```

Validate the model before deployment with:

```powershell
& "$env:USERPROFILE\.dotnet\tools\dotnet-ef.exe" migrations has-pending-model-changes --project src\VirtualCompany.Persistence.Migrations\VirtualCompany.Persistence.Migrations.csproj --startup-project src\VirtualCompany.Api\VirtualCompany.Api.csproj --context VirtualCompanyDbContext
```

The Docker restore/run path is preserved because the feature uses EF migrations only, relational SQL Server-compatible tables/indexes, and no provider-specific startup schema changes.

For Docker SQL Server, start the repository database container first, then run the same PowerShell-safe `database update` command from the repository root. The EF migration history is shared between local SQL Server and Docker SQL Server; do not maintain separate SQLite-only schema changes.

External mailbox provider and live AI/LLM checks require credentials and are treated as external integration checks. Deterministic classifier, proposal, tenant-isolation, and idempotency behavior is covered by local tests.

## Verification Checklist

- Build `VirtualCompany.Api`, `VirtualCompany.Infrastructure.Finance`, and `VirtualCompany.Web`.
- Run `VirtualCompany.Finance.Tests` for deterministic matching, cadence, idempotency, and company isolation coverage.
- Confirm EF reports no pending model changes.
- Confirm Docker SQL Server has applied `20260806172540_AddSupplierSubscriptions`, `20260806174624_AllowMultipleSupplierSubscriptionSuggestions`, and `20260806184234_AddSupplierSubscriptionIntakeProposals`.
