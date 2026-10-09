# P16 bounded UAT profile

Use the existing Workspace.Uat test host with its normal Development/SQLite provider-free profile. P16 owns company `16161616-1616-1616-1616-161616161616`; all other prior phase fixtures retain their existing test tool adapter. GET `/_uat/p16/profile` returns freshly generated agent/task/record identities. POST `/_uat/p16/dispatch` runs the actual operating dispatcher only for this company; never reuse saved fixture identities or run an unrestricted production queue for screen acceptance.

The native test adapter uses the production task/tool wrapper for this company and supplied SQLite rowversion tokens only for three research fixture entities. SQL concurrency is verified separately against an isolated owned SQL Server database. No provider call, real mail or payment is part of this profile.

Browser scenarios pending runtime recovery:

1. Scoped preview changes no owner records. Apply pauses queued real research; duplicate apply retains one receipt. A second unpaused agent remains eligible.
2. Company pause overrides a scoped resume. Refresh acknowledges durable state. Resume with current policy permits retained internal work; a revoked/expired policy does not.
3. Existing admitted or uncertain work retains its evidence and owning recovery destination. Resume does not resend.
4. Restricted members can read only their responsibility scope and cannot apply a company control. Foreign scope and stale previews fail safely.
5. Keyboard labels, disabled/busy controls, error refresh, narrow layout, focus/contrast and exact task return compare with the generated reference.

Status: Browser Blocked. Both computer-use initialization attempts failed before browser inventory or tab creation. A separately owned provider-free HTTP UAT host passed 13 source checks (`http-source-reconciliation.json`) and was stopped with command-line identity and listener cleanup (`cleanup.json`). No screenshot or browser acceptance evidence is fabricated. Saved host PID and fixture IDs are historical; use a fresh bounded host for replay.
