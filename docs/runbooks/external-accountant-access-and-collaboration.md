# External accountant access and collaboration

## Security model

External accountants are company members with the `accountant` role, but that role alone grants no company access. Every company requires one explicit `accountant_company_grants` record tied to the exact membership and user. A grant begins as `pending_approval`; an owner or administrator other than the inviter must approve it. Effective dates and revocation are evaluated on every company-context resolution, so a revoked or expired deep link is denied immediately.

The general finance boundary is read-only for accountants. Collaboration mutations are further constrained by the grant: evidence requests require `CanRequestEvidence`, document identifiers are omitted when `CanViewDocuments` is false, and sign-off requires `CanSignOff`. Engagement reads are restricted to the grant ID. There is no group, firm, email-domain, or inherited company access.

Separation of duties is enforced server-side. An engagement preparer cannot sign off the same engagement, and open findings or evidence requests block sign-off. Grant activation also requires independent approval.

## Operations

- Review active, pending, expired, and revoked grants from the company accountant-collaboration API.
- Revoke a grant with a durable reason when an engagement ends or access is suspected. Do not delete the membership or collaboration records to conceal history.
- Audit events use the `accountant.*` action family. Monitor `accountant_collaboration.access_denied` and `accountant_collaboration.mutations` metrics and the `VirtualCompany.AccountantCollaboration` activity source.
- Evidence and grant notifications are queued through the company outbox with idempotency keys. Delivery failures follow normal outbox retry and dead-letter procedures.
- The portfolio query starts from active grants for the authenticated accountant. Company risk counts are computed only for those company IDs and contain aggregate counts, not cross-company source details.

## Incident response

For suspected overexposure, revoke the exact grant first, confirm a deep link returns 403, then inspect grant, engagement-history, and business-audit records. Retained notes, findings, evidence request metadata, responses, and sign-offs remain company-controlled after revocation. Never reactivate a grant by editing status directly; issue a new independently approved grant.

## Migration and rollback

Migration `20260830230000_AddExternalAccountantCollaboration` creates seven isolated tables and their indexes. Rollback drops collaboration data in dependency order. Export required audit evidence before rollback because schema rollback removes retained collaboration history.
