# P09 verification profile

Mission: integrate the five Release 1 daily journeys, fix evidenced navigation/data/presentation defects, and prepare a reviewable acceptance package. Hypothesis: shared company/responsibility/record context and owning measures remain consistent across role entry points. Architecture, design, shared implementation contract and polish-uat-loop govern the loop.

Environment: Windows/PowerShell, real Blazor Server Web, composed authenticated API in `tests/VirtualCompany.Workspace.Uat`, disposable SQLite, background workers disabled. North/South companies and P07 Ledger Company preserve all additive P01–P08 fixture families. Owner is `p01-owner`; dual Manager has Sales/Marketing assignments; restricted member is `p01-member`. Assignment never grants authorization beyond owning policy. No live customer/provider credentials or outbound channel was configured.

Ports: API 5319; owner Web 5079, dual 5081, member 5080. Exact owned PIDs/logs are recorded. Normal Web startup failed at Windows DPAPI/EventLog, so runtime startup/process cleanup required scoped escalation. Builds/tests/source reads used the ordinary workspace sandbox. No blanket termination or production settings change.

Adapter: Codex in-app browser, desktop 1280×720; explicit responsive override 390×844. JPEG screenshots and observed URLs/dimensions are in `screenshots/` and `browser-captures.json`. Full-page capture can scroll the viewport, so subsequent interaction used fresh DOM/visible links. Async loading observations are not accepted as final state. MainLayout owns the main landmark.

Risks checked: foreign company parents, restricted detail/count/export, stale version/priority, missing cash and plan/source coverage, false currency aggregates, provider retry versus confirmation, repeated navigation parent growth, shared header wrapping, action persistence and report definitions. Prior packet execution/approval/concurrency controls are retained and regression-tested.

Final scope and external boundaries are separate in `verification.json`. Fixture restart resets mutations; before restart the authenticated post-action results were retained and hashed. `cleanup.json` records host/tab/viewport cleanup; never reuse historical PIDs.
