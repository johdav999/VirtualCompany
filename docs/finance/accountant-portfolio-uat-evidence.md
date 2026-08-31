# Accountant portfolio UAT evidence

Date: 2026-08-30  
Reference: `docs/design/references/accountant-portfolio-collaboration-reference.png`

## Product profile

Primary actor: an external accountant working across several Swedish companies. Primary jobs: see deadlines and risks without cross-company leakage, open one explicitly granted engagement, record review notes/findings, request and assess evidence, and provide independent sign-off. Internal owners and finance managers create work, respond to evidence, approve or revoke grants, and retain the audit trail.

Critical security states are active/pending/revoked/expired grants, inaccessible evidence, guessed identifiers, preparer self-sign-off, open work blocking sign-off, and company context switching.

## Verification loop

The live authenticated host and seeded multi-company accountant identity were not available in this implementation session, so runtime browser interaction was not claimed. The strongest safe substitute was used:

1. Generated and visually inspected a desktop reference before UI implementation.
2. Built the Operations service, API, Web, and designer-free migration projects.
3. Added focused domain, HTTP integration, authorization, migration, navigation, and UI-surface tests covering explicit grants, portfolio and deep-link isolation, least privilege, independent approval, revocation with retained history, self-sign-off, inaccessible evidence, and saved reference assets.
4. Reviewed the responsive CSS breakpoints and semantic table, heading, label, status, alert, and loading markup against the repository design rules.

## Issue ledger

| ID | Severity | Finding | Resolution / disposition |
|---|---:|---|---|
| ACC-01 | Critical | Accountant membership could have become implicit company access. | Resolved: company-context resolution requires an effective grant matching membership and user. |
| ACC-02 | Critical | A guessed engagement ID could cross an accountant’s grant. | Resolved: every engagement read/mutation compares the engagement grant ID to the caller’s effective grant. |
| ACC-03 | High | The inviter could activate their own grant. | Resolved: domain approval rejects the inviter and requires a second owner/admin. |
| ACC-04 | High | Preparer self-sign-off and sign-off with open work. | Resolved: both conditions are rejected server-side with stable reason codes. |
| ACC-05 | High | Revoked deep links might retain access through membership role. | Resolved: the membership resolver returns no accountant context after grant revocation/expiry; records remain retained. |
| ACC-06 | Medium | Evidence attachment identifiers could reveal inaccessible documents. | Resolved: attachment writes and DTO reads require both the explicit grant capability and the document's own access policy; inaccessible IDs are omitted and the UI states that the attachment remains hidden. |
| ACC-07 | Medium | Portfolio density could collapse poorly on narrow screens. | Resolved statically: 1000 px and 600 px breakpoints collapse workspace, KPI cards, detail columns, and secondary table columns. Runtime viewport QA remains for a seeded environment. |
| ACC-08 | Medium | Migration assembly can crash Roslyn on Windows when compiling the full generated model. | Resolved for Prompt 7: the designer-free migration compiles, is listed after Prompt 6, and EF reports no model changes beyond the pending migration. |

## Remaining live checks

Run with a seeded authenticated browser identity to verify keyboard activation of company rows, focus order, 200% zoom, narrow viewport behavior, notification delivery, portfolio response time under representative volume, and retained history visibility for authorized internal users. Portfolio isolation, guessed-company denial, revoked deep-link denial/history retention, and self-sign-off rejection now also have HTTP integration coverage.
