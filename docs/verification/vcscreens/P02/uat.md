# P02 browser acceptance

2026-10-01; real Web and authenticated isolated composed SQLite API, fixture accounts from profile.md. Human approval pending. Reference comparison and recorded actions used the polish-uat-loop skill. No production or external side effects.

| Flow | Observed outcome | Evidence |
| --- | --- | --- |
| F02-01 Today → stale overdue manual follow-up → evidence | Five real ranked priorities, actual owner, explicit agent gap and deadline. Matching 6666… record, overdue ranking explanation, two-day source timestamp, stale and partial warnings. | detail-desktop.png; API ordering and Web evidence suites |
| F02-02 evidence → Work → explicit completion → return | Selected task 6666…; read alone leaves Planned. Completion persists Completed; button disappears. Reload was verified. Return reads fresh data, safely unlists the item; Today reports three changed ranks and one removed priority from its previous Today snapshot. | task-completed.png; today-after-action.png; typed-wire contract |
| F02-03 approval evidence → Work review → return | Actual approval 8888… targets task 7777…; source remains pending after opening. Existing Approve/Reject are deliberate commands. Task review uses actual internal-review reason instead of a payment/threshold claim. | approval-pending.png; approval decision API suite |
| F02-04 Sales evidence → North deal → reload → evidence | Source recommendation maps to persisted deal 4444…, title North renewal and 12,000 SEK; scoped evidence-return link survives reload. No recommendation approval or external send performed. | sales-record.png |
| F02-05 Marketing evidence → Experiments / 9999… | Correct section, anchor and actual experiment render with existing Start control. Missing optional daily review returns a normal empty state. No Start/publish/delivery performed. | marketing-record.png; HTTP 204 regression test |
| F02-06 Support evidence → case aaaa… | Matching P02-SUP-1 title, SLA breach, current recorded message gaps and existing response/resolution controls. Return to evidence remains scoped. No agent/delivery requested. | support-record.png |
| F02-07 invalid/foreign key and revoked company | North-scoped South deal key renders generic unavailable with no title/value/action. Revoked Restricted company renders access restriction and no source record. | foreign-key.png; restricted-company.png |
| F02-08 member without responsibility | Requested finance falls back to company; no Finance metrics/section or peer approval in Today. Direct peer approval remains readable under the existing CompanyMember policy; this is not a new grant or a protected-record denial. Decision rights are tested separately by the backend suite. | member-summary.png; member-approval-existing-access.png |
| F02-09 narrow screen and keyboard | 390×844 detail stacks in one 327px column, page scrollWidth 375; Refresh and Back activate with Enter. Responsive Today entry works. Reset override after testing. | detail-mobile.png |

The final detail matches the generated reference's visual hierarchy, spacing, side-by-side evidence/action grouping and ownership area; real coverage warnings extend its height. Browser-discovered fixes were replayed on the original flows. Source update time remains stale after refreshing until the underlying record changes. UTC fallback is visible because the composed test auth-context service omits timezone; Europe/Stockholm formatter conversion is covered automatically.

Fixture hosts stopped and agent tabs closed. Providers and deployed tenant are unverified. Finance live projection/report correctness, full module redesign, report exports and release approval remain the owning later prompts' acceptance work.
