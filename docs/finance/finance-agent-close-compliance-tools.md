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
state, and backend-computed allowed actions. Readiness output identifies its current evidence hash and stale state;
stale readiness is labelled `authoritative_stale`. Close-template, compliance-calendar, audit-package, accountant,
and recommendation lists are capped at 100 items. Paged results return applied paging/count fields and explicit
truncation metadata; accountant grants and engagements report their counts separately. Compliance date ranges are
limited to 366 days, and provenance reports its complete distinct count while capping the returned source-ID list
at 2,000 entries.

## Authority states

The tools report these as distinct states:

- technical readiness;
- recorded manual-submission evidence;
- provider or authority acknowledgement;
- human approval;
- qualified-professional or statutory sign-off.

Manual-submission evidence without an acknowledgement is explicitly returned as not submitted or accepted. A
technically complete audit package is not statutory approval. Pending human approval is never upgraded by a
technical verification result. Audit-package completeness requires a valid verification whose package and manifest
checksums match the current package; a valid verification of an older version is reported as stale evidence.

Final lock, reopen, filing, rollover, professional approval, and statutory sign-off remain declared human-only
operations in `FinanceAgentCoverageCatalogue`. Recommendations cannot waive a blocker, sign off, lock or reopen a
period, submit a filing, authorize a protected download, or execute rollover.

## Protected packages and access scope

Audit-package tools return metadata only. They never call `AuthorizeDownloadAsync` or `DownloadAsync`, never return a
stream or token, and never renew an expired link. A protected package requires the owning, company-authorized,
one-time download flow outside the agent tool.

All object queries include the active company ID. Accountant reads use the company-scoped grant and engagement
queries; a requested grant returns only its engagements, a requested engagement returns only its owning grant, and
inconsistent grant/engagement pairs return not found. Inaccessible evidence responses retain the access-denied state
but omit the document identifier and document provenance. Existing membership, accounting, object-access, and
document-access policies remain authoritative at the called query boundary.

## Verification

`FinanceCloseComplianceAgentToolTests` covers registry ownership/action classes, bounded compliance reads,
provenance caps, manual evidence without authority acknowledgement, current-checksum package verification,
protected/expired package behavior, grant-scoped accountant activity, inaccessible document identifiers, year-end
authority separation, and negative action-class boundaries. Existing close workspace, release-readiness, compliance
obligation, audit package, accountant collaboration, and year-end suites remain the authoritative domain and
authorization proof suites.
