# P18 — integrated Release 2 controls

P18 implements the remaining local integration fixes and completes a reviewable [Release 2 package](../release-2/README.md). Available automated verification is recorded in `verification.json`; final browser acceptance is **Blocked**, and named human release approval is **Pending**. This packet does not approve Release 2 or start P19.

Accepted verification: **257 distinct API checks** (including six isolated SQL Server checks and all 22 native P18 cases), **224 rendered Web**, **37 full wire** and **5 grounding** checks; zero failures/skips in accepted runs. Final API/UAT and rendered Web builds pass, the model has no pending change, and whitespace/preservation checks pass. All 588 entry files remain, with all 396 earlier baseline evidence files byte-unchanged. Repeated and failed diagnostic runs are excluded from these totals.

Continue in this checkout at HEAD `9a356c7901abc2044f6ecb6a4e8a5ebf64ceab5b`. P11–P18 remain uncommitted. `preservation-baseline.json` and `preservation-verification.json` identify retained earlier files and audited shared changes. P01–P17 evidence remains historical for its original scope.

- `implementation.md`: actual production changes and owning boundaries.
- `profile.md`, `uat.md`, `issue-ledger.md`: required polish/UAT scope, observed defects, fixes, rechecks and open acceptance gates.
- `verification.json`, accepted TRX files and build logs: exact counts and source timing; failed diagnostic runs are excluded.
- `renewal-source-reconciliation.json`: actual native source IDs, one decision/four entry points, revised input versions, controlled adapter confirmation and report attribution limits.
- `browser-blocker.json`: current CUA failure before tab inventory. No screenshots or keyboard acceptance are inferred from component tests.
- `source-manifest.json`, `cleanup.json`, `handoff.md`: final source hashes, process/database cleanup and same-checkout continuation.

The recording mail adapter accepts only `renewal-controlled@example.invalid`. Its confirmation and native sent record prove the bounded test workflow, not delivery through a live mail provider or receipt by a customer. Controlled AI analysis responses and a controlled collaboration runner retain actual owning-service outputs; autonomous LLM coordination and writing quality remain unverified.
