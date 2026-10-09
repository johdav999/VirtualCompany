# P30 implementation and reproduction

Baseline HEAD is `9a356c7901abc2044f6ecb6a4e8a5ebf64ceab5b`, plus existing uncommitted P11–P29. See the entry status and final source manifest; this is not a clean-commit claim.

## Production changes

`WeeklyWorkspace.razor.css` now owns the period-switch styling inside its CSS isolation scope. The active Week control is blue, focus remains visible, and six periods wrap to two rows at 390px.

`SalesManagementEvidence.razor` and its stylesheet preserve the cohort table and eight column headings in a named, focusable horizontal scroll region. The prior mobile rules hid headings and compressed the caption into a vertical column. Sources and all underlying definitions are unchanged.

Finance variance/comparison and Support quality/capacity previously called a global `downloadReport` function, although the existing downloader exports an ES module. They now import `./js/reportDownload.js`, use a fresh authorized API export, respect cancellation and dispose the module. Finance and Support show an actionable download error instead of a raw JavaScript stack. Four rendered regression cases cover the two Finance exports, fresh access denial, the two Support exports and safe JavaScript failure. Actual downloaded bytes are separately exercised in Edge.

`Release3IntegratedReviewTests` adds two native cross-owner journeys: fixed-clock CEO/department weekly reconciliation plus immutable monthly open/export; and changed scenario assumptions → frozen source → owned work → mandatory canonical review → timed native jobs/outbox → exactly one inbox delivery. It verifies that the original scenario remains unchanged. Existing R2 policy, execution-control, Today and scheduler regressions remain in the integrated filter.

The existing R2 SQL test is now resilient to later migrations: it locates `AddAgentExecutionControls`, rolls back to its predecessor, checks native work/policy preservation, reapplies the full captured migration history and verifies the pause/admission fence. The final focused SQL run passed. This corrects the test's latest-migration assumption without weakening its preservation or concurrency assertions.

## Reproduction

Run [commands.ps1](commands.ps1) from the repository root with the local SQL Server test connection supplied through the documented environment variable. Build before starting hosts. `start-hosts.ps1` requires free ports 5348 and 5108 and records only its own direct dotnet PIDs. The combined fixture contains P19 weekly history and P22–P29 departmental/planning records. Browser scripts write only into P30.

On a fresh fixture, run `verify-integrated-browser.mjs`, then `browser/P22` through `browser/P29` in order, then `verify-exports-and-polish.mjs`. Each phase script is a P30-local adaptation of its earlier acceptance script. Quarter/year scripts revise the already-seeded plans; P28 resolves the new P25/P26 revisions and creates a fresh native scenario copy so existing origin idempotency is respected. P29 selects the recorded commitment by title because the integrated company contains other legitimate scenario tasks. Hydration waits prevent acting on prerendered controls. Source/result assertions remain active.

The fixed-clock test compares exact shared metrics. Live browser reconciliation ignores only explicitly recorded per-request observation timestamps. Saved monthly CSV bytes equal the native UTF-8 BOM plus the entire retained export. Department CSVs compare every parsed cell except their named live observation timestamp cells. No numeric/source/checksum mismatch is normalized away.

Use `stop-hosts.ps1` to stop only the current recorded DLL/listener owners. A Web-only rebuild during P30 preserved the API fixture and updated the recorded Web PID. Earlier failed attempts and diagnostic screenshots are not accepted evidence; final `browser-accepted.json`, `exports-and-polish.json` and completed TRX are authoritative.

No P30 migration was needed. EF reports no changes since the last migration. The integrated SQL Server tests cover additive migration preservation, retained snapshots, concurrency and scheduling on disposable databases. They do not migrate a deployed tenant.
