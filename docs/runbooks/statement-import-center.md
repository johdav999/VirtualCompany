# Statement import center runbook

## Supported inputs

The governed statement import center accepts only these explicitly supported inputs:

- CAMT.052, CAMT.053, and CAMT.054 namespaces ending in `.001.02` or `.001.08`
- PAIN.002 payment-status namespaces ending in `.001.03` or `.001.10`
- UTF-8 CSV files using a current, company-owned mapping-profile version

Other ISO 20022 namespaces stop with `statement_import_unsupported_version`. PAIN.002 files are retained and displayed as status-only evidence; they never create bank transactions or claim settlement.

## Security and evidence

- The API and service enforce a 20 MB file limit and a 100,000-row limit.
- XML parsing prohibits DTDs, external entities, and external resolvers.
- Every upload is written beneath a company-scoped object key and receives a SHA-256 checksum before parsing.
- The established `ICompanyDocumentVirusScanner` runs before parsing. A blocked file is never parsed. A scanner error stops the workflow until scanning is available.
- Source files are untrusted evidence. Never log their contents or return XML/provider details in user-facing errors.
- The UI renders preview text as normal encoded content and does not offer a CSV export. Any future CSV export must prefix fields beginning with `=`, `+`, `-`, or `@` before writing them.

## Preview and commit semantics

Preview validates the retained source file and persists row outcomes, file issues, control totals, parser/profile versions, and source identities. Preview does not create bank transactions, reconciliation results, payments, balances, or journals.

Commit is available only when no blocking issues remain. Accepted rows are sent through `IBankTransactionCommandService`/`CompanyBankTransactionService`; the import center never writes authoritative bank transactions directly. Each bounded commit chunk has a stable statement identity and checksum. Imported rows are checkpointed after each chunk.

## Interrupted import recovery

1. Open **Finance → Transactions → Statement imports**.
2. Select the job marked **Partially imported**.
3. Confirm that its checksum, selected bank account, parser/profile version, imported count, and remaining validation state match the retained evidence.
4. Choose **Resume**. Completed rows are excluded by their durable row outcome; an ambiguous local checkpoint is safely re-read through the existing statement-import idempotency boundary.
5. If a row identity now conflicts with different content, the job returns to **Needs review**. Do not alter the source identity. Review the retained evidence and explicitly skip the row only when exclusion is the correct authorized decision.

## Object-storage recovery

If the relational job exists but its object cannot be read:

1. Treat the job as blocked; do not reconstruct source XML/CSV from normalized rows.
2. Restore the exact object under the recorded company-scoped storage key from the coordinated backup.
3. Verify its SHA-256 checksum equals the job checksum before resuming.
4. If the checksum differs, retain both facts as an incident and start a new preview. Never overwrite the recorded checksum or import the replacement under the old job.

## Duplicate files and wrong-account data

A repeated company checksum is rejected as a duplicate and points to the existing job. Account identifiers are matched to the selected internal bank account using the exact external code where available or the retained masked suffix. Currency must equal the selected account currency. Account, currency, malformed-row, and control-total errors remain visible and block commit until an authorized row exclusion is recorded where row-level exclusion is applicable.

## Operational checks

- Monitor jobs left in `pending_scan`, `importing`, or `partially_imported` beyond the normal operator window.
- Investigate repeated malware-scan errors separately from parser errors.
- Compare `accepted_row_count`, `imported_row_count`, row outcomes, statement debit/credit totals, and source balances before declaring completion.
- Audit actions use the `accounting.bank_statement_preview.*`, `accounting.bank_statement_import.*`, and `accounting.bank_statement_csv_profile.*` families.
- Revoking a bank consent does not delete manual-import evidence or imported transaction identities.
