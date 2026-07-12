# Virtual Company FAQ

This FAQ is a first draft based on the current Virtual Company functionality. It is intended as a starter knowledge source for support and onboarding.

## General

### What is Virtual Company?

Virtual Company is an operations platform for SMEs. It brings finance, sales, support, approvals, integrations, and AI-assisted agents into one business workflow.

### Who is Virtual Company for?

Virtual Company is designed for small and medium-sized businesses that need practical support with finance operations, customer communication, sales context, and repeatable administrative workflows.

### Does Virtual Company replace employees?

No. Virtual Company is designed to assist employees. It prepares drafts, highlights warnings, suggests actions, and organizes work, but important customer, finance, legal, and payment decisions should stay reviewable by people.

### Does the AI need to be trained on each company?

Usually no. The preferred approach is to give the agent trusted company knowledge at runtime, such as product data, policies, FAQ, customer records, invoices, orders, and approved previous answers. This keeps knowledge easier to update than model training.

## Finance

### What can I do in the finance workspace?

You can review supplier bills, payment status, approval status, Fortnox draft status, reconciliation warnings, ledger account suggestions, payment proposals, and expense posting readiness.

### Why is a supplier bill not ready for expense posting?

A supplier bill may not be ready if it has unresolved reconciliation warnings, an invalid or fallback ledger account, inconsistent payment details, missing approval, missing supplier data, or a lifecycle state that does not allow posting.

### Why did Laura suggest ledger account 2000?

Laura may suggest an account based on available supplier mappings, prior data, invoice content, or fallback logic. Account `2000` is normally not an expense account, so it should be treated as a warning sign and reviewed before posting.

### How do I fix a wrong supplier account mapping?

Use the supplier account mapping repair workflow or script to map the supplier to the correct ledger account. For example, OpenAI software or subscription expenses may need an expense account such as `6540`, depending on the company's chart of accounts and accounting policy.

### Does payment booked in Fortnox mean the bank payment was sent?

Not necessarily. A Fortnox payment booking records the payment in Fortnox. It does not always mean a bank payment was initiated automatically.

### Can I cancel a paid supplier invoice?

Cancellation is normally only available for unpaid and uncredited supplier invoices when the connected provider allows it. Paid invoices usually require a correction workflow such as a credit note or refund handling.

## Fortnox

### What does Fortnox draft mean?

A Fortnox draft is a draft supplier invoice or related accounting object in Fortnox. Draft actions may only be available while the supplier invoice is still editable.

### What does payment proposal exported mean?

It means a payment proposal has been exported from the system. It does not by itself prove that the bank has completed the payment.

### Why are some Fortnox actions unavailable?

Fortnox actions depend on the supplier invoice lifecycle, payment status, approval state, provider rules, and whether the document is still editable.

## Support

### How does the support agent work?

The support flow is:

1. A support email or manual request becomes a support case.
2. The case is triaged and enriched with relevant context.
3. Ben can prepare a draft reply.
4. Safety checks review the draft.
5. A human approves or sends the response unless the case is clearly low-risk and allowed for autonomous handling.

### Does the support agent scan an inbox and answer automatically?

The intended flow supports mailbox scanning, but automatic answering is restricted. The system should route eligible messages into support cases, prepare drafts, and require review for risky or uncertain replies.

### How does the support agent know company-specific facts?

The support agent should retrieve facts from trusted knowledge sources such as the product catalog, company policies, FAQ, customer records, invoices, orders, support history, website pages, and uploaded documents. It should not guess missing facts.

### What happens if the support agent cannot find a trusted answer?

It should mark the case for review or draft a response asking for more information. It should not invent product details, prices, refund terms, payment status, or legal commitments.

### What is support memory?

Support memory stores safe, reviewable customer preferences or repeated context that may help future support. Examples include preferred language or preferred contact method. Sensitive data such as passwords, tokens, or payment card details must not be stored as support memory.

### Why was a reply draft blocked?

A draft may be blocked if it includes unsupported refund or payment promises, legal commitments, sensitive data, prompt-injection residue, missing source references for risky topics, or other safety policy violations.

### What are SLA risk and SLA breach?

SLA risk means a case is approaching its response or resolution target. SLA breach means the target has already been missed.

## Mailbox Routing

### Can any connected mailbox become a support inbox?

No. Support mailbox routing should be explicitly enabled. Existing mailboxes should not automatically become support sources just because a background worker is running.

### How are email threads matched to support cases?

The system should use safe evidence such as provider thread ID, internet message ID, in-reply-to headers, references headers, case numbers, and constrained sender/recipient/subject evidence.

### What happens when an email could match more than one case?

Ambiguous messages should be routed to manual review instead of being linked automatically.

### How does the system avoid duplicate support cases?

Mailbox ingestion should use idempotency keys based on company, mailbox, provider message identity, and internet message identity so the same message does not create duplicate cases.

## Sales

### What does the sales workspace show?

The sales workspace shows customer and sales activity context, including timelines, email activity, and customer-related business events.

### How does sales data help support?

Sales data can help the support agent and human agents understand customer history, recent activity, and account context before drafting a response.

## Local Setup

### Can Virtual Company run with SQL Server Express?

Yes. The local setup supports SQL Server Express, commonly using `localhost\SQLEXPRESS`.

### Can Virtual Company run with Docker SQL Server?

Yes, where Docker Desktop and virtualization are available. Database changes should preserve a clear path back to Docker SQL Server.

### What if Docker Desktop says virtualization support is not detected?

Docker Desktop requires virtualization support. If virtualization cannot be enabled on the PC, use local SQL Server Express instead and keep the database setup compatible so it can later be restored in Docker on another machine.

### Can I restore `virtualcompany.bak` to local SQL Server?

Yes. The backup can be restored to SQL Server Express. If SQL Server cannot read the backup file from the repo folder, copy it to the SQL Server backup folder first or use the restore script that handles this.

### Can I switch back from local SQL Server to Docker later?

Yes, as long as database schema changes, migrations, seed data, and backup/restore scripts stay Docker-compatible.

## Security and Safety

### Should customer-facing replies include internal technical details?

No. Customer-facing replies should use plain English and avoid internal enum names, workflow names, stack traces, tokens, configuration values, and implementation details.

### What data should the system avoid storing?

The system should avoid storing unnecessary sensitive data such as passwords, API keys, tokens, full payment card numbers, bank credentials, and raw authentication headers.

### Are agent actions audited?

Important agent-assisted actions should be auditable, including draft generation, safety blocks, approvals, sends, mailbox routing decisions, and memory updates.

## Troubleshooting

### The web app cannot reach the backend API. What should I check?

Check that the backend API project is running and that the web app API base URL points to the correct port. If the web app expects `http://localhost:5301`, the API must be available there or the client configuration must be updated.

### The build says the web executable is locked. What should I do?

Stop the running `VirtualCompany.Web` process and build again. This usually happens when a previous web server instance is still running and holding the executable file.

### SQL login for `sa` failed. What should I do?

Use the current `sa` password or run the restore script with Windows Authentication if your Windows user has SQL admin rights. If Mixed Mode authentication was changed, restart SQL Server before testing SQL authentication again.

### SQL Server cannot open the backup device. What should I do?

This is usually a file permission issue. SQL Server runs under its own service account and may not be able to read a backup file from the repo folder. Copy the `.bak` file to the SQL Server backup folder or use a script that does this before restore.
