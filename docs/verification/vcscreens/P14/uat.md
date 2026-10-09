# P14 accepted local UAT

The exact ImageGen reference prompt was saved before generating and inspecting the authority reference, using reference 09 as baseline. Production CSS/components were then checked against real rendered Work and settings. The reference is design evidence; screenshots and owner reconciliation establish actual behavior.

All twelve checks in `browser-checks.json` pass. The browser journey opens the durable P14 task, verifies four labeled disabled positions and consequence descriptions, confirms arrow keys do not change intent, opens native restrictions with the keyboard, reads five meaningful action checks, follows settings, compares the five identical action explanations, and confirms the authority region exposes no edit/grant/apply controls. Keyboard refresh after controlled fixture expiry shows the Finance owner's expiry reason, with the amount 100.0 intact. Back to work retains the original company/task identity and shows the same expiry. At 375px all positions fit without horizontal overflow. The settings hub retains its existing autonomy configuration destination. Standalone settings agent choice persists in agentId and survives native page reload.

| Screenshot | Accepted state |
| --- | --- |
| work-desktop.png | Stable loaded Work authority summary and four positions |
| work-expanded.png | Work detail with actual restrictions/checks |
| settings-desktop.png | Contextual settings and the same action explanations |
| settings-expired.png | Fresh expired-grant explanation |
| settings-mobile.png | Narrow layout and four read-only positions |
| settings-selected-reload.png | Standalone selected agent retained after reload |

The polish loop fixed the literal Razor work-kind binding, excessive catalogue prominence in Work, numeric punctuation humanization, and agent selection held only in component state. Each fix has component/parent regression checks and a rebuilt browser replay. The final desktop/mobile screenshots were visually inspected. The accepted reload screenshot is captured after hydration and agent selection, not from the intermediate checking state.

Twenty-five authenticated source checks (`reconcile.ps1`, `source-reconciliation.json`) compare current configuration/limits, work/task policy, capability owner fingerprints, Finance grant bounds/version/expiry, Work/settings actions, repeated stable hashes and expiry-driven changes. They exercise no-store, foreign-company 403, scoped Finance agent 404, invalid context 400 and absence of POST 405. Department/actor denial, controlled evaluator failure, matrix cases and no attempted execution/approval writes are covered by actual API tests. Offline/foreign typed transport refusal and cancellation are covered by Web tests. These automated fault paths are the safe substitute for deliberately breaking live provider/configuration state during browser UAT.

Accepted suites: 153 API, 108 Web and 10 typed-wire checks, zero failures/skips. Counts overlap prior-phase tests. API, UAT and Web builds pass; EF reports no pending model change. Existing analyzer/NuGet feed and model warnings are retained in logs. This is local implementation/browser verification, not human release approval, deployed tenant/provider validation, physical/native/statutory acceptance or production application of earlier migrations. Human release approval remains Pending. P15 task-policy editing/enforcement has not been implemented.
