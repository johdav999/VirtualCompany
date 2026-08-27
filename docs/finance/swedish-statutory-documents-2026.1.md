# Swedish statutory document engineering specification 2026.1

Status: engineering source specification; qualified Swedish reviewer approval pending.

Policy-pack identity: `sweden-statutory-candidate` / `1.2.0`. Jurisdiction: Sweden. Currency and accounting method: SEK and invoice/accrual method. This document narrows the launch scope recorded in `swedish-domestic-vat-launch-specification-2026.1.md` and its source inventory. It does not claim government verification, statutory certification, or qualified reviewer approval.

## Supported boundary

- Native number allocation is supported for full customer invoices and customer credit notes only.
- Supplier invoices and supplier credit notes are imported or provider-issued and retain the number assigned by their source authority.
- The selected company must have a format-complete, user-attested Swedish statutory profile and policy pack `1.2.0`.
- The launch scope supports Swedish accounting currency only. Simplified invoices, foreign currency, cash-method timing, EU/non-EU documents, reverse charge, mixed use, partial recovery, imports, and every tax treatment outside the Prompt 2 package remain blocked.
- The candidate remains `IsStatutoryComplianceValidated = false` until Prompt 7 has exact reviewer evidence for the immutable definition hash.

## Required frozen facts

Every document stores seller legal identity, organisation number, registered address and seller VAT identifier when VAT registered; buyer legal name and address; buyer VAT identifier when supplied/applicable; unique number and its authority; issue, supply, accounting and due dates; currency; payment terms; explanatory text; line descriptions, positive quantities, unit prices, net amounts, VAT rates and VAT amounts; document net, VAT and gross totals; source identity/version; policy-pack identity/hash; statutory-profile identity/version; tax facts; approval references; and the canonical snapshot hash.

The issue policy rejects an empty required field, an unsupported authority/type combination, due dates before issue, accounting dates before issue in this launch scope, supply dates after issue, negative line facts, line extensions that do not match quantity times price, VAT amounts that do not match basis times rate, or document totals that do not match lines. A credit document must reference a retained original issued document of the matching customer/supplier type.

## Number control

Customer invoice and credit-note series are separate from journal voucher series. Each series is company scoped, document-type scoped, and fiscal-year bounded. A number is allocated only inside the issue transaction after policy validation. Preview never allocates. SQL Server uniqueness covers company/series/fiscal-year/number, source identity/version, and stable business key/version. Optimistic concurrency plus a serializable transaction and bounded retry protect concurrent allocation. A committed number is never decremented or reused.

An intentional unused number is recorded as an immutable `gap` allocation with a bounded operator explanation. A transaction that fails before commit leaves neither a number allocation nor a partial issued snapshot. Replaying the same business key and source version returns the original result.

## Immutability and corrections

The issued snapshot and hash have no update operation. Rendered and delivery evidence may be attached later through a separately versioned evidence operation without changing the snapshot. Corrections use a new linked customer or supplier credit document; original document and journal facts remain unchanged. Accounting preview for pack `1.2.0` is fail-closed until the source record has a matching immutable statutory-document registration.

## Provider preservation

Registration of an imported or provider-issued document requires its retained source record and exact original number, currency, and signed gross total. Registration never writes or renumbers the source invoice or bill. Fortnox and other historical source documents remain readable through their existing paths; they enter the `1.2.0` posting boundary only after an authorized operator registers the immutable provider/import snapshot.

## Operations and migration

Migration `AddSwedishStatutoryDocumentControls` adds `statutory_document_series`, `statutory_document_number_allocations`, and `issued_statutory_documents`. It does not backfill or mutate existing invoices, bills, provider references, voucher series, voucher sequences, or journals.

Apply the same EF migration history after either local SQL Server restore or Docker SQL Server restore:

```powershell
dotnet ef database update --project src/VirtualCompany.Persistence.Migrations --startup-project src/VirtualCompany.Api --context VirtualCompanyDbContext
```

Rollback is an application forward-fix: disable selection/issuance of pack `1.2.0` and retain every series, allocation, gap, snapshot, and hash. Do not drop the tables, lower `next_number`, delete a gap, renumber a source, or edit snapshot JSON. When investigating a duplicate or gap, preserve the database, correlation ID, audit records, business key, source version, and allocation history.
