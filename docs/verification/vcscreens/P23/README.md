# P23 Finance variance and rolling forecasts

Implemented locally on 2026-10-05 in the existing uncommitted P11–P23 checkout. Finance now explains actual-versus-plan differences through posted source records, retains audited explanations, previews explicit future assumptions, saves/reopens named native forecast versions and compares original versions. P20 monthly reviews retain the original Finance report. Native actuals, Budget/Forecast storage, financial statements, closing and payments retain their owners.

Accepted checks: **75 API** (including **6 SQL Server**), **4 Finance calculation**, **34 rendered Web**, **50 full typed wire**; API/Web/UAT builds and migration-model check pass. **Four fresh headless Edge journeys pass**, with desktop/narrow captures, keyboard focus and visual review. These counts are scoped to the accepted TRX files, not diagnostic attempts or the whole repository suite.

- [Implementation and ownership](implementation.md), [source reconciliation](source-reconciliation.md), [profile and boundaries](profile.md).
- [Machine-readable acceptance](verification.json), [source hashes](source-manifest.json), [prior-work preservation](preservation-verification.json).
- [UAT and screenshots](uat.md), [browser results](browser-accepted.json), [issue ledger](issue-ledger.md).
- [Replay commands](commands.md), [owned host cleanup](owned-cleanup.json), [same-checkout handoff](handoff.md).
- References: [variance](../../../design/references/finance-planning-p23-variance.png), [editor](../../../design/references/finance-planning-p23-editor.png), [comparison](../../../design/references/finance-planning-p23-comparison.png), [saved reference prompts](../../../design/references/finance-planning-p23-prompts.md).

Missing baselines, currency conversion, cash timing, opening balances and scenarios are explicit. User IAB initialization failed at the Windows sandbox ACL helper; accepted browser evidence uses fresh disposable headless Edge with the native Web and composed API. Provider acceptance, deployed migration, physical file saving and human/statutory approval are unverified. P24 was not implemented. All 1,275 prior-phase evidence files inventoried at entry are byte-identical; earlier implementation changes remain in this checkout. Ignored logs were not hashed at entry; final-only hashes are recorded separately and cannot establish their baseline equality. P23 commands wrote logs only in P23.
