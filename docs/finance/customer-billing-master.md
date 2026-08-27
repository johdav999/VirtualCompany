# Customer billing master operations

Release 2 customer billing profiles are company-scoped records linked one-to-one to the existing Finance customer identity. Existing customer list and edit routes remain compatible; structured billing facts are managed through the customer billing-profile endpoints.

## Safe operating model

- Identity validation reports format, user-attested, provider-sourced, or externally verified state separately. Provider data is not treated as registry verification.
- User and provider changes with no safe precedence create a visible source conflict. An accounting administrator must explicitly retain the current values or accept the incoming snapshot.
- Duplicate candidates are deterministic evidence, not merge instructions. Matching shared email alone does not create a candidate and no candidate is merged automatically.
- Merge decisions retain the source customer as a tombstone and redirect, preserve provider references, snapshot historical invoice customer facts, and re-point supported customer links in one database transaction. A merge is blocked if the source contains supplier-only relationships or would create a redirect cycle.
- `ExpectedVersion` is required for updates and decisions after initial profile creation. A conflict response means the operator must reload before retrying.

## API routes

- `GET/PUT /internal/companies/{companyId}/finance/customers/{customerId}/billing-profile`
- `GET /internal/companies/{companyId}/finance/customers/{customerId}/billing-profile/history`
- `GET /internal/companies/{companyId}/finance/customer-duplicates`
- `POST /internal/companies/{companyId}/finance/customer-duplicates/{candidateId}/decision`
- `PUT /internal/companies/{companyId}/finance/customer-billing/source-conflicts/{conflictId}`

Reads require Finance view access. Billing-profile changes, source-conflict decisions, and duplicate decisions require Accounting admin access. Audit history records profile changes and human governance decisions without logging unbounded provider payloads.

## Migration and restore

Apply the additive SQL Server migration through the normal `VirtualCompany.Persistence.Migrations` deployment path before enabling the endpoints. Local SQL Server and Docker SQL Server use the same migration history. Backup and restore remain coordinated with the existing database procedure; all new profile, history, conflict, candidate, redirect, and invoice-snapshot tables are relational database state and require no separate object-store step.
