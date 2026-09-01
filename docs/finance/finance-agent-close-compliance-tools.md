# Finance close, compliance, audit, and year-end agent tools

Prompt 3 registers `finance-close-compliance-agent-v1`: eight read tools and four recommendation tools backed by the
existing authoritative Finance and accountant-collaboration query services. These tools coordinate evidence. They
do not acquire final accounting, filing, or professional authority.

## Tool surface

| Area | Reads | Recommendation/explanation |
| --- | --- | --- |
| Close | templates and immutable versions; instance/task graph; readiness snapshots, blockers, waivers and sign-offs; period lock history | blocker priority, owners, evidence hashes and age, safe next actions |
| Compliance | obligation calendar or one company-scoped obligation, submission evidence and acknowledgements | missing evidence and filing-preparation checklist |
| Audit package | package definitions, runs, artifact metadata, attempts, approvals and verification metadata | required-artifact and checksum completeness |
| Accountant collaboration | company-scoped grants, engagements, evidence requests, review items, sign-offs and safe activity history | included in close and audit coordination |
| Year-end | run summaries or one run with readiness, proposals, opening balances, sign-offs, subsequent events and history | prerequisite blockers and pending human approval |

Every result retains native IDs, versions, hashes, owners, due dates, materiality, evidence references, approval
state, and backend-computed allowed actions. Readiness output identifies its current evidence hash and stale state.

## Authority states

The tools report these as distinct states:

- technical readiness;
- recorded manual-submission evidence;
- provider or authority acknowledgement;
- human approval;
- qualified-professional or statutory sign-off.

Manual-submission evidence without an acknowledgement is explicitly returned as not submitted or accepted. A
technically complete audit package is not statutory approval. Pending human approval is never upgraded by a
technical verification result.

Final lock, reopen, filing, rollover, professional approval, and statutory sign-off remain declared human-only
operations in `FinanceAgentCoverageCatalogue`. Recommendations cannot waive a blocker, sign off, lock or reopen a
period, submit a filing, authorize a protected download, or execute rollover.

## Protected packages and access scope

Audit-package tools return metadata only. They never call `AuthorizeDownloadAsync` or `DownloadAsync`, never return a
stream or token, and never renew an expired link. A protected package requires the owning, company-authorized,
one-time download flow outside the agent tool.

All object queries include the active company ID. Accountant reads use the company-scoped grant and engagement
queries; a requested grant or engagement absent from that set returns not found. Existing membership, accounting,
object-access, and document-access policies remain authoritative at the called query boundary.

## Verification

`FinanceCloseComplianceAgentToolTests` covers registry ownership/action classes, manual evidence without authority
acknowledgement, protected/expired package behavior, and recommendation action mismatch. Existing close workspace,
release-readiness, compliance obligation, audit package, accountant collaboration, and year-end suites remain the
authoritative domain and authorization proof suites.
