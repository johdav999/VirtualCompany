# P01 product profile

Date: 2026-10-01. Product: Virtual Company, Blazor Interactive Server with the composed authenticated API.

Scope: authorized Overview shell and navigation, not P02 priorities or later role content. Follow `production-implementation.md`, `docs/architecture-rules.MD`, `docs/design.md`, and `ui-instructions.md`.

Reference baseline: `docs/design/references/role-time-agent-2026-10-01/01-ceo-today.png`, `02-sales-today.png`, `05-support-now.png`, existing `app-shell-navigation-reference.png`, and responsibility-driven Today/Monthly references. Keep the current layout, typography, modules and period controls. Finance Today and Marketing Today references belong to P06/P05 before their redesign; week/month references are not substituted for Today.

Local browser fixture: `tests/VirtualCompany.Workspace.Uat` adapts the real `TestWebApplicationFactory` API to loopback HTTP, using isolated SQLite state and disabled workers. Production Web uses its normal typed clients against that API. There are no mocked workspace responses in this browser adapter. This does not validate SQL Server, external providers, a deployed tenant, or statutory reports.

Accounts: `p01-owner` (North and South active; Restricted revoked), `p01-dual` (North Sales and Marketing), `p01-member` (North active, no responsibility). Identities are disposable development-header fixtures, not credentials. North ID `11111111-1111-1111-1111-111111111111`, South `22222222-2222-2222-2222-222222222222`, Restricted `33333333-3333-3333-3333-333333333333`. North renewal deal ID `44444444-4444-4444-4444-444444444444` has a Sales recommendation; South private deal ID `55555555-5555-5555-5555-555555555555` supports cross-company denial.

Build adapter: `dotnet build tests/VirtualCompany.Workspace.Uat/VirtualCompany.Workspace.Uat.csproj`. Run its DLL from the repository root on a free loopback port with `ASPNETCORE_URLS`. Readiness endpoint: `/_uat/health`. Run the Web DLL from its own project directory in Development with `ApiBaseUrl` pointing to the adapter and `DevelopmentAuth__Subject`, `DevelopmentAuth__Email`, `DevelopmentAuth__DisplayName` set to one fixture identity. This run used API 5319 and Web ports 5079/5080/5081. `Logging__EventLog__LogLevel__Default=None` avoids the local Event Log restriction; sandbox DPAPI required outside-sandbox Web startup with tool approval. Use `Start-Process dotnet -PassThru -WindowStyle Hidden`, record PIDs, poll readiness separately, and stop only those PIDs. Stop fixture processes before rebuilding their DLLs. Existing production/test configuration is not edited.

Flows:

| ID | Role / preconditions | Expected outcome |
| --- | --- | --- |
| F01 | Owner; North | Five authorized perspectives; Support label uses customers lens; existing module links |
| F02 | Dual responsibility | Server defaults to Sales; switch to Marketing; Finance request falls back without Finance content |
| F03 | Owner; Monthly explicit month | Module link and Overview return retain company, lens, year/month; reload/back restore view |
| F04 | Owner; two active companies | Switch to South; clear lens/month; never render late North results |
| F05 | Member / revoked company | Safe no-assignment view; unauthorized company direct link exposes no workspace |
| F06 | Owner; desktop and narrow viewport | Keyboard-operable company/responsibility/period controls; responsive drawer without clipping |

Evidence: focused TRX files, browser captures and UAT packets in this folder. Human approval remains pending.
