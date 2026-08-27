# Swedish SIE 4B export and statutory archive

## Format baseline and review state

The implemented transfer format is SIE Type 4 export, specification 4B dated 2008-09-30. The pinned source is `docs/finance/SIE_file_format_4B_2008-09-30.pdf`, downloaded from Föreningen SIE-Gruppen at `https://sie.se/wp-content/uploads/2020/05/SIE_filformat_ver_4B_ENGLISH.pdf`. Its SHA-256 is `e3ceda54c4675a1f4970f426249dc0606acb142b82c316c2748ac7094653840f`.

The upstream sample at `tests/VirtualCompany.Finance.Tests/Fixtures/Sie4B/SIE4 Exempelfil.SE` came from the SIE Group download linked at `https://sie.se/in-english/`. Its SHA-256 is `fc66a49c9e913c818c6e564c59cd639fe4e0e608f1ccff6a00fe90db675e39c3`. Tests parse this sample independently and round-trip generated fixtures. These are engineering conformance checks, not qualified-reviewer approval. The policy pack remains `IsStatutoryComplianceValidated = false` and explicitly records reviewer validation as pending.

## Export types and behavior

- `generic_json` remains the default for backward-compatible requests.
- `generic_csv` produces an actual UTF-8 ledger CSV.
- `sie_4b` produces a PC8/code-page-437 `.se` file with deterministic metadata, company identity, financial-year boundaries, accounts and types, opening/closing/result and monthly balances, dimensions mapped to SIE cost centre/project, and ordered balanced vouchers and transactions.
- `swedish_statutory_archive` produces a deterministic ZIP containing `accounting.sie`, source and archive manifests, and a SHA-256 checksum manifest. The source manifest retains finalized VAT-package object references, financial statement snapshots, close history, complete policy-pack definitions and hashes, and source-document object references and hashes without copying those binaries.

Statutory exports require the version `1.3.0` Swedish candidate pack, a format-complete user-attested Swedish profile, SEK books, a closed and reporting-locked period, numeric account mappings, immutable voucher series/sequence identity, balanced two-decimal journals, complete effective-dated policy history, and only supported cost-centre/project dimensions. A missing or unrepresentable fact permanently fails the job with a stable capability-gap reason and publishes no artifact.

The job worker claims with a five-minute lease. An expired claim is recoverable; object writes use a stable company/job key so an unconfirmed write can be safely reconciled on bounded retry. Statutory artifacts are kept in object storage, and relational job metadata retains the export type, exact specification, input/output checksums, encoding, content length, source counts/totals, actor, correlation ID, and archive manifest.

## API and authorization

`POST /internal/companies/{companyId}/finance/accounting/exports` accepts `fiscalPeriodId`, `idempotencyKey`, `exportType`, and optional `correlationId`. The same company-scoped idempotency key returns the original job and cannot be reused for another export type. Requesting requires accounting-admin authorization. Listing and checksum-verifying download require accounting-view authorization and always reapply the current company scope.

## Migration, backup, restore, and expiry

Apply `20260825092127_AddSwedishStatutoryAccountingArchive` after `20260824143033_AddSwedishVatReturnWorkflow`. It additively extends `accounting_export_jobs`; existing jobs are backfilled as `generic_json`. Local and Docker SQL Server use the same migrations assembly and object-storage layout.

A coordinated backup must snapshot SQL and the complete object-storage root under one maintenance identifier. Retention cleanup deletes the referenced export object before clearing its relational storage key. After restore, run accounting recovery verification with object-content checks. It validates statutory-export metadata and SHA-256, finalized VAT package hashes, evidence hashes, journals, source links, audit references, and snapshots. Any missing reference or hash mismatch blocks promotion.
