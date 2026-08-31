# Fixed-asset subledger

The native fixed-asset register owns book-accounting facts from reviewed acquisition through disposal. It posts only through `IAccountingPostingService`; asset events retain the class version/hash, source version, run identity, idempotency identity, dimensions, and linked journal.

## Supported book behavior

- Functional-currency capitalization from reviewed source evidence.
- Straight-line book depreciation with deterministic calendar-month daily proration, including multi-month periods, and final carrying-value protection.
- Prospective improvements, custody/location/dimension transfers, impairment, disposal gain/loss, and linked reversal for depreciation, impairment, and disposal.
- Reconciliation of register cost, accumulated depreciation, and accumulated impairment to the journals created by the subledger.
- Explicit legacy migration conflicts for shallow `FinanceAsset` records. No useful life, residual value, placed-in-service date, or historical depreciation is inferred.
- Component rows retain their own cost, residual value, useful life, and placed-in-service date; the uncomponentized balance follows the asset terms.
- A bounded company-by-company maintenance worker discovers legacy conflicts. It never posts depreciation: period runs remain an explicit finance-approval operation.

Tax-register depreciation is not claimed by this feature. A reviewed statutory pack and qualified accounting review are required before any tax-register capability may be presented as supported.

## Operator workflow

1. Configure a company-scoped asset class with distinct posting-enabled cost, accumulated depreciation, expense, impairment, and disposal gain/loss accounts.
2. Register an asset from a source document or legacy reference. The initial state is `draft` and no journal is created.
3. Capitalize the retained acquisition cost, then record the placed-in-service date.
4. Review the period depreciation population in Finance → Accounting → Reports → Fixed assets. Posting is restricted to the finance-approval policy.
5. Resolve item exceptions without changing already posted items. Replaying the same run identity returns the retained run.
6. Before close, require fixed-asset reconciliation and resolve every legacy migration conflict.

Corrections never edit a posted book event. Use a linked reversal or a new prospective improvement/impairment event. Do not delete the originating evidence, retained class hash, event, run item, or journal.

Ledger creation, retained asset-state movement, book-event evidence, and audit evidence commit in one relational transaction. A retry must reproduce the retained event payload; reusing an idempotency key for different asset facts stops as a conflict.
