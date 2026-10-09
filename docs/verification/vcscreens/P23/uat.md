# P23 local UAT

Final polished replay: `browser-run-polished.log` and `browser-accepted.json`, 2026-10-05 19:40 UTC. Fresh headless Edge used the real Web at port 5103 and the composed API/SQLite fixture adapter at 5343. Current Web assembly hash is recorded; adapter Finance/API assemblies match the production builds. The browser closed in a finally block. Owned hosts were stopped after acceptance; see owned-cleanup.json. Existing user hosts were preserved.

| Journey | Observed result |
|---|---|
| Native Finance entry → actual versus plan → source/explanation | September posted -1000.10 SEK, approved budget -1100.00, prior-year -900.05 and signed variance 99.90 (-9.08%); draft/other-budget excluded; saved explanation and exact journal/Finance return URL |
| Report → future assumption → changed preview → save/reload | Explicit November amount changes from -1250.25 to -1400.25; original -1400.25 reopens; September actual stays -1000.10; saved account/dimension/rationale wrap visibly |
| Original → new revision → comparison/export | Later -1500.25 yields -100.00 change; variance CSV fingerprint/99.90 and comparison CSV -100.00 reconcile to selected server evidence |
| Monthly Finance → save/reload → retained variance | Original snapshot identity survives reload/drilldown; incompatible live export is absent; ambiguous native budget versions remain unavailable |

All four journeys pass with no page errors. Desktop 1440×1000 and narrow 390×844 show no body overflow. Tables and existing Finance navigation retain intentional horizontal scrolling; table regions accept keyboard focus. The keyboard smoke check establishes focus availability, not a complete assistive-technology audit.

Actual final screenshots, visually inspected after the accepted replay:

- [Variance desktop](variance-desktop.png), [variance narrow](variance-mobile.png).
- [Reopened forecast desktop](forecast-desktop.png), [forecast narrow](forecast-mobile.png).
- [Comparison desktop](comparison-desktop.png), [comparison narrow](comparison-mobile.png).
- [Retained monthly Finance report](monthly-retained-variance.png).

Visual defects found during review were repaired: saved assumptions use wrapping text rather than clipped disabled controls; evidence/editor/comparison tables receive full width with stacked side panels; capture waits no longer retain loading frames. Existing global Finance navigation and controls were reused. Generated references preceded implementation and are documentation, never source amounts.

Current-access restriction, corruption, stale-preview conflicts, exact fiscal periods, effective dimensions, tenant isolation, negative decimals, missing sources and SQL duplicate/successor concurrency are additionally covered in accepted automatic checks. Those conditions are not all claimed as browser-observed. CSV response content was verified; a physical saved file was not observed. Human reviewer: Pending. No provider, deployed-tenant or statutory acceptance is implied.
