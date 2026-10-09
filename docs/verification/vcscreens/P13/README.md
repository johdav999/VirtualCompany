# P13 business-record agent supervision

Implementation and local verification complete. P01–P12 are preserved in this same uncommitted checkout. Human release approval remains Pending. Start with `implementation.md`, `verification.json`, `uat.md`, `issue-ledger.md`, `profile.md`, `source-reconciliation.json` and `preservation.json`.

Sales proposals, Support cases, Finance invoice review and both supplier-bill record types, and Marketing campaign/content review now share contextual evidence and current-decision presentation. Native owners retain command, approval, posting, payment, publication and delivery authority. Missing evidence, stale decisions, restricted contributions and failed delivery remain explicit.

Accepted final runs: 26 API, 89 Web, nine typed wire and two isolated SQL Server checks, zero failures/skips. These overlap prior phases; do not add phase totals. API dependencies/UAT and Web builds pass; EF reports no pending model changes. Browser journeys cover all four business areas, exact contribution-version return, decisions elsewhere followed by refresh, stale proposal refusal, partial grounding, retained failed delivery, mobile layout and keyboard disclosure. The read-only reconciliation has 53 checks across 26 authenticated reads; the final supplier-bill replay has its own profile and reconciliation after the fixture host restart.

Apply `20261003171638_AddBusinessWorkAssociations` through normal deployment. Local SQL Server backfill/rollback/re-upgrade preserved tasks, approvals and P11 contributions, including intake bill links. Production tenant migration, real provider delivery, named human acceptance and prior native/physical/statutory gates remain independent and unverified. Local tests used synthetic records; no customer reply, publication, posting or money movement occurred.

Four separate ImageGen references and prompts are under `docs/design/references/business-evidence-p13-{sales,support,finance,marketing}-reference.*`. P14 can continue from the status handoff. PID files are historical after `cleanup.json`; never restart or stop a process using a saved PID without verifying ownership.
