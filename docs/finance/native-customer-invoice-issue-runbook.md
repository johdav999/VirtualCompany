# Native customer invoice issue runbook

Issue only the approved current draft through `POST /internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId}/issue`.

The request must contain the loaded draft version and result hash, the matching native document series, an open accounting period and posting date, an active voucher series, and a stable idempotency key. The operation rechecks customer, tax, statutory, approval, document-series, accounting-period, authority, and evidence facts immediately before allocating a number.

The transaction creates the native invoice, immutable statutory snapshot, number allocation, accounting profile, and posted journal together. It returns `not_queued` for delivery; rendering and delivery are separate downstream actions. Replaying the exact idempotency key returns the original invoice and journal. A changed payload conflicts. A failed transaction leaves no visible invoice or journal and rolls back the number allocation, so this implementation has no normal number-gap path.

SQL deadlocks, lock timeouts, and unique-key concurrency races are retried within the bounded issue transaction. A terminal failure records safe audit evidence with the reason code and no sensitive invoice payload; reload the current draft before retrying after a version, approval, policy, period, or series failure.
