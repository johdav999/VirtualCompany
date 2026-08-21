# Native ledger posting migration seam

`IAccountingPostingService` is the single governed posting boundary for new native-accounting sources. It owns whole-entry validation, authority and period checks, source-version idempotency, voucher allocation, immutable journal persistence, reversal links, and business audit evidence.

The existing `CompanyBankTransactionService` and `CompanyCashSettlementPostingService` remain unchanged in Prompt 2 so their current records and rollout behavior stay readable. Prompt 7 must adapt their source-specific inputs into `ProposedAccountingEntry` and call `IAccountingPostingService`; it must not copy validation or voucher logic into those services. Their existing source identifiers map as follows:

- bank transaction identity becomes `SourceType`, `SourceId`, and a stable source content `SourceVersion`;
- cash settlement identity becomes the same stable source tuple and action-specific `IdempotencyKey`;
- existing payment/bank/source links remain operational workflow records, while the resulting posted journal ID is the accounting truth reference;
- reconciliation, payment allocation, posting, source links, and workflow state must share one transaction when Prompt 7 performs the cutover.

Characterization coverage remains in `CashSettlementPostingServiceTests` and the bank transaction regression suite. Those tests are the preservation baseline for the Prompt 7 adapter change.
