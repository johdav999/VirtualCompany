# Teams tenant allowlist verification - 2026-09-08

Product: Virtual Company, local API on port 5301 and Web on 5062.
Flow: platform administrator associates the Wellheld tenant from /system/admin/teams-presenter.
Runtime: .codex-build/api-runs/20260908131902806/VirtualCompany.Api.dll; Development; content root src/VirtualCompany.Api.
Evidence: user-provided screenshot and runtime api.stdout.log; restarted process logs api-allowlist.stdout.log in the same runtime directory.

| ID | Severity | Expected / observed | Acceptance | Status |
|---|---|---|---|---|
| TENANT-001 | P1 | Exact approved tenant should pass allowlist; running API retained pre-edit IOptions and rejected it. | Restart, replay association through real API, verify media-route gate replaces allowlist error; reject unrelated tenant. | Verified via live API substitute for browser replay |
| TENANT-002 | P1 | Registration needs approved media route and configured bot identity. Media route remains disabled. | Complete actual media/identity setup and approvals before association. | Blocked: deployment and identity prerequisites |

Configuration change: development AllowedTenantIds contains only ad6ca694-0db1-47e5-b045-9cdd0dd84101. No production defaults or feature gates were enabled.
After targeted restart, exact tenant returns teams_presenter.media_route_not_approved. Unlisted synthetic tenant continues to return the allowlist rejection. No tenant association, consent, or external Teams action was completed.
API readiness still reports enabled=false, mediaRoute=disabled, liveCallingReady=false. No build needed: configuration-only change; existing compiled API restarted with source content root.
Previous diagnosis that the copied runtime settings were authoritative was incorrect: launcher WorkingDirectory and startup logs establish the source content root.
