# P21 completion review - 2026-10-05

The requested prompt was already implemented in this checkout. This review inspected its native Sales report, recorded outcome history, forecast evidence, private capacity revisions, P20 reproduction, typed transport and rendered user journeys against `vcscreens-prompts.md` and the current architecture/design rules. Existing P01-P21 work was retained. No branch switch, commit, reset, stash, customer assignment or outbound action was performed.

Two failure-path findings were addressed in the existing owners:

- **P21-05, capacity validation:** positive hours per opportunity can be small enough to overflow decimal division. The capacity calculation now maps that overflow to an argument-validation failure, so the authenticated API returns 400 without saving a proposal or audit. Representable fractional assumptions retain the existing calculation.
- **P21-06, retained evidence integrity:** valid JSON may contain null forecast input lists/items or missing proposal fields, even when a proposal's checksum matches. Forecast inputs now fall back to explicitly labeled aggregate-only evidence; incomplete proposal payloads, invalid retained assumptions and mismatched currency are withheld as 422 integrity failures. Exceptions from invalid forecast dates/arithmetic also withhold the retained inputs.

`regression-baseline.trx` records all five original regression cases failing on the entry implementation: capacity overflow, two malformed forecast lists and two incomplete proposal payloads. That diagnostic run is excluded from accepted verification. The final API run also covers five additional malformed proposal cases and a valid fractional-capacity save/reopen, alongside the pre-existing Sales/P20 checks. Final results are recorded in `verification.json`.

Production changes are limited to `SalesCapacityCalculation` in Application and `SalesManagementService` in Infrastructure.Sales; regression tests belong to Api.Tests. No schema or wire-contract changes were made and no migration was added. Existing P21 history/proposal migrations and the immutable payload format remain authoritative. Previously saved valid proposals retain their original calculation and format.

The `polish-uat-loop` profile and flows in the parent packet were reused. `cua.getState()` failed before browser inventory with kernel PID 17856 and `windows sandbox failed: helper_unknown_error: apply deny-read ACLs`. No UI hosts, tabs or screenshots were created. Browser/reference comparison, keyboard and narrow-view acceptance remain blocked; rendered tests are an explicit substitute, not live acceptance. Human release approval and inherited deployed/provider/physical/statutory gates remain separate.

Commands and scope are in `commands.md`. Logs and TRX files are local verification artifacts. Earlier P21 accepted/diagnostic runs are retained in the parent packet; these results supersede them only for the files rechecked here. P22 remains outside this request.
