# Native customer invoice PDF and email delivery

PDFs are generated from the immutable issued statutory snapshot only. The built-in deterministic renderer uses PDF 1.7, Helvetica/WinAnsi encoding, fixed object ordering, document language metadata, page numbering, and bounded multipage line output. It has no third-party runtime dependency; reproducibility is limited to the same snapshot, renderer template version, locale, and runtime encoding implementation.

Rendering and email delivery are durable company-outbox operations. A PDF artifact is immutable for a snapshot/template/locale tuple. Email acceptance means the connected mailbox accepted submission; it does not mean the recipient opened or received it. Timeouts or ambiguous provider outcomes require operator reconciliation before resend. Configure an active Finance-purpose standard SMTP mailbox with SendMessages capability to send attachment-bearing invoice emails.

Preferred delivery uses Peppol first and the durable email outbox as a controlled fallback. Email fallback is allowed only when no external submission occurred or a provider returns definitive safe-to-fallback evidence. It is suppressed for queued, accepted, retryable, delivered, timeout, and reconciliation-required Peppol outcomes. See `customer-invoice-peppol-email-fallback-design.md` for the decision table and API contract.
