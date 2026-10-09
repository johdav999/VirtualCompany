# P11 implementation

Outcome: the P10 owning work identity now opens a real collaboration evidence view. Flow and chronological list use the same durable receipts and handoffs. Selecting an artifact shows its retained output, business rationale, recorded review/challenge, exact input versions, source worker and revision history. Contributor completion never marks a pending parent decision complete.

## Ownership and persistence

Existing Operations `MultiAgentCoordinator` remains the execution owner; `OperatingWorkDispatcher` passes the existing collaborator pattern/role. New immutable `CollaborationContribution` and `CollaborationArtifactHandoff` entities record company, existing parent/source tasks, existing plan and agent IDs, sequence/version, business output, rationale and actual review outcome. Restrict foreign keys, company query filters and unique parent/sequence/version indexes protect identity. No second company objective, task lifecycle, business workflow or provider executor was created.

Migration `20261003140818_AddCollaborationContributionEvidence` adds the receipts, handoffs and company/correlation execution lease. It imports safely linked legacy task-output contributions as one retained version and repairs valid same-company source-task ancestry. It explicitly labels imports: earlier versions, dependency relationships and reviews cannot be reconstructed. Unknown history is not invented. SQL Server upgrade, uniqueness, downgrade/upgrade and legacy import were verified in a unique disposable database; retained root work survives. Existing company databases were not migrated by this run.

Retries use the same company/correlation/material fingerprint, parent, plan, source tasks and stable steps. Completed/review-pending receipts are reused. Failed/blocked workers can append a new version to the same logical contribution. Sequential inputs are exact retained versions; failed or review-pending input blocks the handoff. The persisted lease rejects overlapping execution and material plan changes, expires after the bounded runtime and cannot be released by an older token. Independent/parallel contributions retain the existing bounded serial worker execution; this UI does not claim concurrent provider execution. A legacy execution lacking a verifiable saved plan needs a new execution identity rather than an inferred retry plan.

The coordinator now preserves blocked/review states instead of converting every nonfailed task into a completed step. Review-pending contributions terminate as `human_review_required`; the collaboration parent waits for approval and the operating dispatcher uses the existing decision boundary. Viewing evidence never resumes work, grants permission or submits approval.

## Authorized projection and navigation

Application `ICollaborationEvidenceQueryService` is implemented in Operations and exposed through authenticated `GET /api/companies/{c}/agent-work/{kind}/{id}/collaboration`. The existing P10 work query and responsibility/membership resolver authorize the owning identity first. Typed receipt projection checks source tasks, parent tasks, agents and transitive input ancestry. Hidden inputs also hide derived output, rationale, IDs and counts. The retained Work task payload and P10 dependency paths use the same guard to prevent alternate entry leaks. Delegation checks every worker and source before creating a lease or work.

Projection windows are 500 versions and 2,000 handoffs, with explicit partial diagnostics. Exceeding the dependency window withholds derived evidence. Reads write `collaboration.evidence_read`; projection failures are logged without payloads and counted by the collaboration meter. Worker failures show safe business recovery text instead of internal exception details.

Web `AgentCollaboration` and the typed `AgentWorkApiClient` expose loading, unavailable/retry, empty, restricted and partial states. Wire validation rejects mismatched company/identity and broken endpoints. Selected `artifactId` and `view=list` survive reload; validated board/record/Overview context travels through Work, Sales and source worker returns. The inherited approval route was corrected to Work's canonical `itemId`, verified in the actual approval panel.

## Preservation and acceptance

P01–P09 production modules, reports, approvals/outbox and phase evidence remain intact. P10 board/card/per-state paging code is unchanged. Shared changes are limited to authorized evidence protection, correct review lifecycle and canonical approval selection. The test-only UAT adapter adds P11 seed data to existing fixtures; production pages have no canned contribution data.

Read [verification.json](verification.json), [uat.md](uat.md) and [issue-ledger.md](issue-ledger.md). Tests verify independent/sequential work, failed handoffs, retry identity, versions, review blocking, lease overlap/expiry, material mismatch, scoped delegation, derived-content protection, company denial, API/Web transport and migration/backfill. Local browser journeys reconcile six versions/four logical contributions/three edges to one awaiting-approval outcome. No in-scope implementation TODO remains. Human Release 2 acceptance remains pending.
