# Foreign-currency settlement

## Accounting policy

Payment allocations are the durable settlement facts for customer invoices and supplier bills. When native accounting is configured, allocation creation and its cash journal are one serializable transaction. A posted allocation is immutable; corrections use the reversal endpoint and then a new allocation.

For each governed allocation the system retains:

- document, payment, and functional currencies and amounts
- the settlement date, effective rate, retained conversion, rate identity, and conversion residual
- the historical functional carrying amount relieved from receivables or payables
- bank, fee, write-off, realized exchange gain or loss, rounding, and remaining-balance amounts
- the settlement and reversal journal identifiers, idempotency identities, actor, timestamps, and version

Partial allocations relieve the document's historical functional carrying amount proportionally. The final allocation consumes the exact remaining functional balance, preventing accumulated rounding drift across multiple settlement dates. Incoming settlements calculate realized gain or loss as settlement value less the relieved carrying value; outgoing settlements use the opposite sign convention. Positive values post to the exchange-gain role and negative values post to exchange loss.

Fees, final write-offs, settlement discounts, realized exchange differences, and rounding differences resolve through configured accounting account roles. Missing required roles block posting; the service never substitutes an arbitrary account.

## Evidence and reconciliation

Foreign-currency documents must already have a posted accounting profile with authoritative transaction-date conversion evidence. The payment-date conversion is resolved through the exchange-rate authority with the `settlement_date` purpose. Provider-reported payments additionally require a bank-transaction payment link before they can create authoritative settlement journals.

Allocation, retained conversion, cash journal, payment-to-ledger link, and audit records commit atomically. Stable allocation and reversal idempotency keys make replay safe. Reversal posts an equal linked correction journal, preserves the original evidence, marks the allocation reversed, and reopens the affected invoice or bill balance.

Read APIs expose both document and functional settlement facts. Aging, statements, reconciliation, finance summaries, dashboards, and financial checks exclude reversed allocations and use persisted carrying amounts and write-offs. Legacy allocations remain readable with `legacy_unavailable` evidence status rather than receiving invented historical rates.

## API operations

- Create: `POST /internal/finance/{companyId}/payments/{paymentId}/allocations`
- Reverse: `POST /internal/finance/{companyId}/payments/{paymentId}/allocations/{allocationId}/reverse`
- Trace: `GET /internal/finance/{companyId}/payment-allocations/{allocationId}/trace`

Creation accepts optional `feeAmount` and `writeOffAmount`. Reversal requires a posting date, reason, and stable idempotency key. Posted allocations cannot be updated or deleted in place.

## Deployment and operations

Migration `AddForeignCurrencySettlement` adds the retained settlement and reversal columns, tenant-scoped indexes and foreign keys, concurrency version, and amount constraints. It backfills existing rows from their original allocation amount and currency, assigns version `1`, and marks their settlement evidence `legacy_unavailable`.

Telemetry records successful settlements, realized gain/loss totals, reversals, and blocked evidence or policy decisions. Operators should investigate blocked counters before retrying; retries must reuse the original idempotency key when the business request is unchanged.
