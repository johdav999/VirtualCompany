# P04 issue ledger

| ID | Finding / impact | Correction and evidence | State |
| --- | --- | --- | --- |
| P04-01 | Report/record links lost inner filters or exact Sales origin after reload | Shared validated Sales journey links, record returns and preset/version IDs; route tests and real contact/preset return replay | Resolved |
| P04-02 | A one-currency forecast label could conceal mixed currency inputs; live and stored rounding could diverge | Separate report totals/windows; deterministic primary legacy currency; shared per-row formula; API and CSV reconciliation | Resolved |
| P04-03 | Opening legacy forecast persisted data when no snapshot existed | Read-only transient fallback keeps the existing response shape; API asserts snapshot count unchanged | Resolved |
| P04-04 | Pending/future internal commitments could look like customer engagement or completed agent work | Pending excluded from completed updates; customer risk excludes internal commitments; real review time in history; forecast regression and Today wire tests | Resolved |
| P04-05 | Blind retries after an uncertain write or stale review could obscure the outcome | Command GUID and payload conflict; compare-and-update review; history reload required; client/component/API tests | Resolved |
| P04-06 | Browser form submission raced input updates; formatted heading displayed literal `{0}` | Input events update form immediately; separate nonformatted close-date label; semantic interaction replay waits for rendered input state before save | Resolved |
| P04-07 | Narrow currency selector overflowed its card | Flexible bounded filter labels/selects; final mobile screenshot and measured contained controls | Resolved |
| P04-08 | Existing presenting-session copy did not explain why preset mutation is locked | Explicit already-started instruction retains stop/leave recovery; preset workflow tests | Resolved |
| P04-09 | Controlled browser download event returned no saved path | Final Blob download uses the existing financial-report mechanism and rechecks scope/rows; component tests verify current CSV handoff and revoked-access denial. User saved the earlier browser export; its actual file reconciles to one SEK Proposal row, 12000 gross / 6300 expected | Resolved; exact evidence boundary below |
| P04-G1 | No controlled live calendar/email integration, presenter or published preset in browser fixture | Canonical blocked preparation is visible; retained scheduling/closing/approval tests pass. No provider-success claim | External acceptance gate |
| P04-G2 | Human review, deployed tenant and SQL Server acceptance unavailable | Separate from SQLite browser/API evidence; P05 implementation can continue | Pending independent acceptance |
| P04-G3 | Browser automation could not observe physical save completion | User confirmed saving CSV; file found in Downloads and copied to forecast-downloaded.csv. File observation time is 18:40:19 UTC, before the final Blob change. Final Blob path is verified by component handoff and module load, not by that older file | Saved export verified; final Blob physical save not independently observed |

No unresolved implementation defect is knowingly left in P04's internal journey. Provider/deployed/human release gates remain open. Browser automation's download timeout did not prove a download failure: the user saved a file, which was then inspected. Final Blob physical completion was not independently observed; its refreshed authorized CSV handoff is covered automatically.
