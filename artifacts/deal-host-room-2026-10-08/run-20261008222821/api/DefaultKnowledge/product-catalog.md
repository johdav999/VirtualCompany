# Virtual Company Product Catalog

This catalog describes the first supported product areas for the Virtual Company solution. It is intended as a business-facing knowledge source for support, sales, onboarding, and product planning.

## Product Overview

Virtual Company is an operations platform for small and medium-sized businesses that combines finance workflows, sales workflows, customer support, approvals, and AI-assisted agents in one local-first business system.

The solution helps SMEs receive documents, reconcile business events, prepare accounting actions, manage sales and customers, answer support requests, and keep human review around high-risk decisions.

## Core Modules

### Finance Workspace

The finance workspace helps teams manage supplier bills, payments, reconciliation warnings, Fortnox sync actions, corrections, credit notes, and expense posting.

Primary users:

- Business owners
- Finance administrators
- Accountants
- Operations staff

Key capabilities:

- Supplier bill overview
- Approval status tracking
- Payment proposal status
- Payment export and booking status
- Fortnox draft visibility
- Reconciliation warnings
- Suggested ledger accounts
- Expense posting readiness
- Supplier account mapping repair
- Local SQL Server and Docker-compatible database restore support

Typical support questions:

- Why is a supplier bill not ready for expense posting?
- Why did Laura suggest a specific ledger account?
- How do I repair a supplier account mapping?
- Has a payment been exported or booked?
- Can I cancel or credit this supplier invoice?

### Sales Workspace

The sales workspace supports customer-facing business activity, sales pipeline visibility, timelines, email activity, and customer/company context.

Primary users:

- Sales representatives
- Account managers
- Business owners
- Customer success staff

Key capabilities:

- Customer and activity views
- Sales timelines
- Email timeline display
- Activity feed
- Sales/customer context for support and operations

Typical support questions:

- Where can I see recent customer activity?
- How do I understand a customer's sales history?
- Why is an email or activity not appearing in the timeline?

### Support Workspace

The support workspace manages inbound customer questions, case triage, reply drafting, SLA tracking, agent-assisted replies, memory review, and mailbox routing.

Primary users:

- Support agents
- Customer success staff
- Operations managers
- Business owners

Key capabilities:

- Support case inbox
- Case detail view
- Status, priority, category, owner, and view filters
- Open, escalated, approval, breached, SLA risk, and resolved-today views
- Reply draft generation
- Human approval before sending higher-risk replies
- Safety checks before draft approval or send
- Support memory review
- Mailbox routing and email thread matching
- SLA performance summary
- Learning effectiveness analytics

Typical support questions:

- How does Ben prepare a reply?
- Why was a draft blocked by safety checks?
- Why did an email become a support case?
- Why was an email ignored or marked ambiguous?
- Where can I review learned customer preferences?

### AI Agent Assistance

Virtual Company includes named AI-assisted workflows that prepare work for humans, explain suggested actions, and reduce repetitive operational effort.

Current agent concepts:

- Laura: finance-oriented review and enrichment, including supplier bill review and ledger account suggestions.
- Ben: support-oriented case analysis and reply drafting.

Key capabilities:

- Case-aware reply drafts
- Finance enrichment notes
- Source-backed suggestions
- Safety gating for sensitive operations
- Audit trails for important automated decisions

Important limitation:

AI agents should not be treated as final decision makers. They prepare and recommend actions. Human review remains required for high-risk, unclear, financial, legal, or customer-impacting decisions.

### Integrations

Virtual Company is designed to work with external systems while keeping the local setup reversible.

Current or planned integration areas:

- Fortnox for supplier invoices, drafts, and payments
- Email mailbox connections for support intake
- SQL Server Express for local database development
- Docker SQL Server for portable database development where virtualization is available
- Future connector-ready sources such as Microsoft 365, Google Workspace, Shopify, WooCommerce, HubSpot, Pipedrive, Zendesk, Freshdesk, Intercom, Stripe, and accounting systems

## Knowledge Sources Supported by the Product

The support agent and business workflows should use trusted company knowledge instead of relying on model training.

Recommended knowledge source types:

- Company profile and contact details
- Product and service catalog
- Prices and package descriptions
- Warranty, refund, delivery, cancellation, and payment policies
- FAQ and help articles
- Website pages
- Uploaded PDFs, Word files, spreadsheets, CSV files, and text files
- CRM records
- Orders, invoices, payments, subscriptions, and support history
- Approved previous support answers

## Deployment and Data Options

Virtual Company should support both local SQL Server and Docker SQL Server database paths.

Supported local development database options:

- SQL Server Express on `localhost\SQLEXPRESS`
- Docker SQL Server where virtualization and Docker Desktop are available

Database compatibility rule:

Database schema, migrations, seed data, backup/restore scripts, and local setup changes must preserve a clear path back to Docker SQL Server unless explicitly decided otherwise.

## Packaging Ideas

### Starter

For very small businesses that need basic support and finance tracking.

Included:

- Finance workspace
- Supplier bill tracking
- Support inbox
- Manual knowledge base
- Local database setup

### Operations

For SMEs that need tighter finance, support, and sales coordination.

Included:

- Everything in Starter
- Sales workspace
- Fortnox workflows
- SLA tracking
- Agent-assisted drafts
- Support memory review

### Connected Business

For SMEs that need external integrations and broader automation.

Included:

- Everything in Operations
- Mailbox routing policies
- Connector-ready knowledge sources
- Advanced audit and diagnostics
- More automation around safe low-risk support replies

## Product Principles

- Human review for risky decisions
- Source-backed answers
- Clear audit trails
- Reversible local setup
- Docker compatibility where database changes are made
- Plain-language business workflows
- No hidden auto-send behavior for customer-impacting communication
