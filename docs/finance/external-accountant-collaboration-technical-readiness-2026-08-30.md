# External accountant collaboration technical readiness

Status: implemented; focused compile and static UAT complete; live seeded UAT pending.

The implementation adds a least-privilege accountant role, independently approved per-company grants, grant-bound review engagements, notes/findings, evidence requests/responses, assignments and due dates, independent sign-off, immutable review history, portfolio risk aggregates, notifications, audit events, activities/counters, an API surface, and a screenshot-led responsive web workspace.

Security decisions are enforced in server code and do not depend on hidden UI controls. The portfolio starts from the authenticated accountant’s effective grants. Company-context resolution refuses an accountant role without a matching effective grant. Engagement access is narrowed again by grant ID, evidence attachments require both the grant capability and document access policy, and revocation takes effect on subsequent context resolution while persisted history remains intact.

Deployment requires migration `20260830230000_AddExternalAccountantCollaboration`. Operators should monitor denied-access counters and outbox delivery, and should validate the acceptance scenarios in `accountant-portfolio-uat-evidence.md` with seeded identities before release approval. This document is technical evidence, not a statutory or professional accountant opinion.
