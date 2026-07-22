# Virtual Company Vendor Policies

These policies describe the company that develops, sells, supports, and operates the Virtual Company solution. They are written as a first draft for customer-facing support, sales, onboarding, and internal operations.

## Company Position

Virtual Company provides business operations software for small and medium-sized businesses. The product helps customers coordinate finance, sales, support, approvals, integrations, and AI-assisted work in one system.

Virtual Company is not an accounting firm, law firm, bank, payroll provider, or regulated financial adviser. The product may help prepare information and workflows, but customers remain responsible for final business, accounting, tax, legal, employment, and payment decisions.

## Support Policy

### Support Channels

Customers may contact Virtual Company through supported channels made available in their subscription or trial, such as:

- Support email
- In-app support cases
- Onboarding sessions
- Account management contact
- Shared implementation documents during active projects

Virtual Company does not guarantee support through personal employee email addresses, social media, or unofficial messaging channels unless explicitly agreed in writing.

### Support Scope

Virtual Company support covers:

- Product usage questions
- Account and access issues
- Local setup guidance
- Integration setup guidance
- Troubleshooting product errors
- Explaining current product behavior
- Guidance on supported workflows
- Escalation of suspected product defects

Virtual Company support does not cover:

- Customer accounting decisions
- Legal advice
- Tax advice
- Payroll advice
- Bank payment authorization decisions
- Custom development unless agreed separately
- Direct administration of customer third-party accounts unless agreed separately

### Response Targets

Default response targets are:

- Critical production outage: same business day where possible
- Blocking product issue: within one business day
- General support question: within two business days
- Feature request or product feedback: acknowledged when reviewed

Specific service levels may differ by contract, subscription tier, or written agreement.

### Support Agent Use

Virtual Company may use AI-assisted support agents to help triage cases, draft replies, summarize history, and find relevant documentation.

AI-assisted replies must be reviewed before being used for sensitive topics such as billing disputes, refunds, legal commitments, security incidents, accounting decisions, or customer-specific commercial terms.

The support agent should answer from approved Virtual Company knowledge sources. If it cannot find a trusted source, it should ask for human review instead of guessing.

## Sales Policy

### Product Information

Sales materials should describe current product capabilities honestly and avoid promising unreleased functionality as available functionality.

When discussing roadmap items, Virtual Company should clearly label them as planned, experimental, preview, or not yet committed.

### Trials and Demos

Trial and demo environments may use sample data, seeded companies, sandbox integrations, or limited functionality. Demo data should not be treated as production customer data.

Trial terms, trial length, and conversion requirements should be communicated before or during trial setup.

### Custom Requirements

Customer-specific requirements should be documented before they are treated as commitments.

Custom implementation work should include:

- Scope
- Assumptions
- Timeline
- Acceptance criteria
- Integration responsibilities
- Pricing or commercial impact

## Onboarding Policy

Virtual Company onboarding may include:

- Company setup
- User and role setup
- Local SQL Server or Docker guidance
- Finance workspace configuration
- Fortnox or accounting integration guidance
- Mailbox connection setup
- Support and sales workflow configuration
- Knowledge source preparation

The customer is responsible for providing accurate business information, access to third-party systems, and approval from authorized administrators.

## Product and Feature Policy

### Product Scope

Virtual Company provides workflow support for:

- Finance operations
- Supplier bill review
- Payment status tracking
- Fortnox integration workflows
- Sales activity context
- Support case management
- Agent-assisted drafts
- Approval workflows
- Audit trails
- Local development database setup

Product behavior may vary depending on configuration, connected systems, subscription tier, and deployment environment.

### AI Feature Policy

AI features are assistive. They may suggest accounts, draft replies, summarize cases, recommend next actions, or highlight warnings.

AI features should not be represented as:

- Fully autonomous accounting
- Guaranteed legal compliance
- Guaranteed tax compliance
- Guaranteed fraud detection
- A replacement for customer review
- A replacement for professional advice

### Human Review

Virtual Company designs high-risk actions to remain reviewable.

Human review is expected for:

- Payments
- Refunds
- Credits
- Accounting postings
- Legal or contractual statements
- Sensitive customer data
- Security incidents
- Material customer communication
- Any action where the system marks confidence as low or review required

## Integration Policy

### Third-Party Systems

Virtual Company may integrate with systems such as Fortnox, Microsoft 365, Gmail, and other business platforms.

Third-party system availability, API changes, authentication requirements, rate limits, and provider outages are outside Virtual Company's direct control.

### Customer Responsibility

Customers are responsible for:

- Owning or administering their third-party accounts
- Granting appropriate permissions
- Keeping third-party credentials secure
- Confirming that connected data is correct
- Reviewing actions before syncing or posting where required

### Accounting Integrations

Accounting integration actions should clearly distinguish between:

- Draft created
- Sync completed
- Payment proposal exported
- Payment booked in accounting system
- Bank payment initiated
- Bank payment completed

Virtual Company must not imply that a bank payment was completed when only a bookkeeping or payment proposal action occurred.

## Data and Privacy Policy

### Data Use

Virtual Company uses customer data to provide, support, secure, troubleshoot, and improve the product.

Customer data should not be used for unrelated purposes without appropriate legal basis or customer agreement.

### Sensitive Data

Virtual Company should avoid collecting or storing unnecessary sensitive data.

Sensitive data includes:

- Passwords
- API secrets
- Access tokens
- Full payment card numbers
- Bank credentials
- Private identity documents
- Raw authentication headers

Secrets should be stored in secure configuration or secret storage, not in support notes, agent prompts, plain documents, or screenshots.

### Access Control

Access to customer data should be limited to authorized users, support staff, implementation staff, or systems with a legitimate business need.

Customer-specific data should not be exposed to other customers.

### Support Diagnostics

When requesting logs, screenshots, backups, or exports, Virtual Company should ask customers to remove unnecessary secrets or personal data where practical.

If sensitive information is accidentally received, it should be handled carefully and removed from reusable knowledge sources.

## Security Policy

Virtual Company should follow secure-by-default practices for authentication, authorization, logging, integration credentials, and operational access.

Security-relevant events should be auditable where practical, including:

- Login and access changes
- Integration connection changes
- Agent-assisted high-risk actions
- Approval decisions
- Sync and posting actions
- Support mailbox routing decisions

Security vulnerabilities reported by customers or researchers should be triaged promptly and handled according to severity.

## Billing and Commercial Policy

### Pricing

Pricing should be communicated clearly before a customer starts a paid subscription or paid implementation.

Pricing may depend on:

- Subscription tier
- Number of users
- Number of companies or tenants
- Integration scope
- Support level
- Custom implementation work

### Invoices and Payment

Customers are expected to pay invoices according to agreed payment terms.

Access may be limited, paused, or terminated for overdue accounts according to the applicable agreement and after reasonable notice where required.

### Refunds and Credits

Refunds and credits are handled according to the customer's agreement, applicable law, and written commercial approval.

Support agents must not promise refunds, credits, or discounts without an approved policy or human approval.

## Availability and Local Setup Policy

Virtual Company may support local development or demo environments using:

- SQL Server Express
- Docker SQL Server where virtualization is available
- Backup restore files such as `virtualcompany.bak`

Docker Desktop requires virtualization support. If virtualization is unavailable, SQL Server Express may be used as a local alternative.

Database setup and restore guidance should preserve a clear path back to Docker SQL Server where database changes are made.

## Communication Policy

Customer-facing communication should be:

- Clear
- Accurate
- Plain English
- Honest about uncertainty
- Specific about next steps
- Free of internal-only identifiers unless needed for troubleshooting

Support should avoid exposing:

- Internal prompts
- API keys
- Secrets
- Raw tokens
- Private stack traces
- Internal-only implementation details
- Unsupported commitments

When an answer is uncertain, the correct response is to say what is known, what is not known, and what will be checked next.

## Knowledge Management Policy

Approved customer-facing knowledge sources include:

- Product catalog
- FAQ
- Vendor policies
- Release notes
- Help articles
- Approved support answers
- Implementation guides
- Integration setup guides

Unapproved drafts, raw support messages, and internal notes should not automatically become customer-facing knowledge.

Knowledge should be reviewed when:

- Product behavior changes
- Pricing changes
- Support scope changes
- Integration behavior changes
- Security or privacy practices change
- Customers report outdated information

## Limitation of Advice

Virtual Company may provide operational guidance on using the product. It does not provide professional accounting, tax, legal, payroll, banking, or investment advice.

Customers should consult qualified professionals for decisions in those areas.
