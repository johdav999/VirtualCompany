# Finance formatting migration inventory

Replace direct presentation formatting with `ILocalDateTimeFormatter`, `INumberFormatter`, and `IMoneyFormatter` in these areas:

- Finance overview, cash, bills, invoices, payments, transactions, anomalies, settings, mailbox, sandbox administration, and transparency pages.
- Agent finance recommendations embedded in the Agents page.
- Direct `ToLocalTime()` calls and `ToString("g"|"d"|"N2"|"P0", CultureInfo...)` calls in Finance Razor/code-behind.

Do not replace invariant formatting used for API query strings, imports/exports, provider payloads, audit codes, idempotency identities, or database values.
