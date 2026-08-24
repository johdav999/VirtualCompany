# Accounting integrity scenario

`AccountingIntegrityScenarioTests` is the Release 0 production-shaped proof for the native accounting path. The fixture creates only company membership, source documents, evidence objects, counterparties, invoices, bills, payments, and a bank account. Accounting setup, decisions, approvals, journals, settlement postings, reconciliation, reports, close artifacts, exports, audits, and recovery evidence are produced through the registered application and domain services.

## Deterministic flow

The scenario uses a fixed clock and stable company, user, source-document, invoice, bill, payment, and period identities. It performs this sequence:

1. Complete and replay country-neutral accounting setup, including chart roles, monthly periods, and voucher series.
2. Preview, submit, approve, post, and replay one customer invoice and one supplier bill with persisted source evidence.
3. Reject a stale source version and a user from another company.
4. Inject a failure while the first payment allocation and cash journal are being saved, prove both roll back, then retry and replay partial and final incoming/outgoing allocations.
5. Import and replay a bank statement, reject an overlapping row as a duplicate, and match its settlement rows to the existing payment cash journals.
6. Post an unknown receipt to suspense, then create and replay the linked reversal and evidence-backed reclassification.
7. Verify trial balance, general ledger evidence, tax summary/review replay, AR/AP/control/bank reconciliation, profit and loss, and balance sheet.
8. Validate, close, and lock the period; request and replay the durable export; execute the production export worker; verify its content checksum; then run database and object-content recovery verification.
9. Assert the stable persisted evidence totals: nine balanced journals, four allocations, five imported bank rows, two approvals, two evidence links, one tax-review audit, paid invoice and bill states, and matching close/export/recovery checksums.

The selected policy pack is deliberately country-neutral. The tax summary is reviewed and checksummed, but the scenario asserts that it is not represented as a jurisdiction-specific statutory return.

## Running it

Fast isolated run:

```powershell
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj -c Release --filter "FullyQualifiedName~AccountingIntegrityScenarioTests&Category!=SqlServer"
```

SQL Server, using a dedicated disposable local or Docker instance:

```powershell
$env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION = '<connection string>'
./scripts/test-matrix.ps1 -Lane sqlserver
```

The SQL Server host derives a unique catalog name, applies the checked-in EF migrations, and deletes only that isolated catalog during cleanup. The same connection variable and flow work for local SQL Server and a Docker-exposed SQL Server endpoint; no LocalDB-only behavior is required.
