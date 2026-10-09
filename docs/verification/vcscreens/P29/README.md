# P29 — Briefings at the right time and to the right person

Local implementation in the existing checkout, starting from `9a356c7901abc2044f6ecb6a4e8a5ebf64ceab5b` plus uncommitted P11–P28. Prior evidence is inventoried in `prior-artifact-baseline.json`. P29 does not approve Release 3.

## Delivered behavior

`/briefing-preferences?companyId=` retains its route and adds Schedule, Preview and Absence routing. `/briefings?companyId=&deliveryId=` opens a delivered briefing with current authorized content. Seven editable schedules cover morning, end of day, shift handover, weekly, monthly, quarterly and annual checkpoints. Editable CEO/Sales/Marketing/Finance/Support defaults select authorized work areas; they confer no authority. The existing in-app switch remains authoritative and can be edited in the new screen.

Working days, overnight work intervals, timezone, quiet periods, same-time grouping, unchanged-content suppression and urgent escalation rules persist on the existing delivery preference. Absence intervals use explicitly labelled UTC inputs. An eligible delegate receives work during absence, otherwise the eligible accountable fallback receives it. If both become unavailable, the audit records failure and a safe routing escalation goes to the active accountable owner. Delegate absence is rechecked. No role, responsibility, source grant, approval or execution policy is changed.

## Native ownership

Operations owns `BriefingCadenceService`. The existing `CompanyBriefingService.GenerateDueAsync`, `BriefingSchedulerCoordinator`, update-job producer/claiming runner and company outbox carry the new recipient deliveries. Preferences with no cadence settings keep the existing daily/weekly behavior. Opted-in recipients stop receiving the old broadcast briefing notifications.

`CompanyTodayWorkspaceLensResolver` factors its existing persisted membership/responsibility rules into a recipient read path. `CompanyWorkScope.Tasks` remains the authority for work visibility. Finance permissions and effective accountant grants are checked using their existing owners. Delegation never impersonates the scheduler principal. Preview intersects requester and proposed recipient visibility. Generation, dispatch and opening recheck current membership and work scope.

Briefings include up to 100 recently updated authorized native tasks, including role commitments, blocked/overdue work and P28 retained-source links. They are a bounded work digest, not a second business report. Retained snapshot content is never copied into notifications. Source links pass through the existing source owner on open. The delivery ledger retains hashes/routing/outcomes; notification text contains a safe generic link. A permission change therefore cannot expose a stale snapshot through notification previews.

## Calendar and recovery

Local slots are stable across job retry and the repeated autumn DST hour. Spring gaps move to the first valid permitted minute. Monthly day 29–31 clamps to the month end. Quiet/closed slots defer to the next permitted local minute; the scheduler catches up only within the current local day and an open working window. A worker delayed beyond its delivery window suppresses the old slot with an explicit reason; the next permitted schedule remains active. Poll precision follows the existing scheduler polling interval.

Routine cadences at the same local minute share one delivery when grouping is enabled. Deduplication compares actual authorized item content, not the current clock/freshness label. Urgent blocked/failed/overdue work has separate keys and quiet-hours policy. Quarterly/annual review reminders remain meaningful even when task content is unchanged. Suppression is checked again immediately before dispatch.

The durable delivery ledger is unique by company, owner and local slot. A pending ledger without a job is repaired by the next native scheduler pass. Existing conditional job/outbox claims handle concurrency. Native notification deduplication and lookup reconcile acknowledgment ambiguity before retry. Delivery attempts, sent/suppressed/failed/uncertain/retry state and safe reasons are exposed in the audit; the existing job/outbox retains retry and failure evidence. Final generation failures also update the delivery ledger.

Migration `20261006180516_AddBriefingCadenceDelivery` adds nullable preference settings and the relational tenant-filtered delivery ledger. Existing tables/data are retained. SQL Server migration roundtrip and concurrent worker checks use isolated test databases.

## Verification and acceptance boundaries

Final local checks pass: 60 distinct API cases (including two SQL Server cases), 60 Web cases (including eight explicitly compiled P29 component cases), two typed wire cases and the full fresh Edge browser journey. Native/Web/UAT builds, EF pending-model check and final diff check pass. The browser demonstrates one controlled delegated workspace notification after repeated job/outbox execution, then current Work and retained-source reopening. All 2,526 baseline evidence/reference hashes are unchanged; owned ports are free after cleanup.

Morning/end-of-day/shift rows run only on configured working days, preventing weekend daily digests accumulating on Monday. Periodic closed checkpoints defer. Retained-source links are author-only within current native review authority; delegates receive independently authorized work and do not inherit the creator's private source link.

See `verification.json`, accepted case files, `commands.md`, build/test logs and `browser-accepted.json`. The real native services and dispatcher run with persisted controlled test recipients. Failure tests wrap the native notification dispatcher to simulate a temporary failure or a lost acknowledgment; they do not contact a provider.

CUA initialization failed with a trusted Node kernel/helper failure. The strongest safe browser substitute is fresh headless Microsoft Edge against native Web and the composed API test host. This is distinct from the user's in-app browser, deployed tenant and provider acceptance. The available briefing channel is the existing workspace inbox. Mobile is not activated; email/Teams delivery is not claimed. Live external delivery requires a configured channel, credentials and controlled recipient. Production migration and human Release 3 approval remain separate gates.

Built-in ImageGen generated the reference board from the written brief at `docs/design/references/briefing-cadence-p29-prompts.md`; selected and inspected output is `docs/design/references/briefing-cadence-p29.png`. Reference values are illustrative. Product samples come from tests/UAT only.
