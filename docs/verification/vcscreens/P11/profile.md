# P11 product profile

Product: Virtual Company, local ASP.NET API plus interactive Blazor Web, with existing bounded agent execution. Baseline: `9a356c7901abc2044f6ecb6a4e8a5ebf64ceab5b` plus this uncommitted P11 patch.

Launch: build `tests/VirtualCompany.Workspace.Uat/VirtualCompany.Workspace.Uat.csproj`; start its DLL directly on `127.0.0.1:5319`. Run the built Web DLL from `src/VirtualCompany.Web` on 5079 and 5081 with `ApiBaseUrl=http://127.0.0.1:5319`, Development auth and the named fixture subject. Use `Start-Process -PassThru -WindowStyle Hidden`, save each PID immediately, then check `/_uat/health` and the Web root in a separate bounded call. `/health` on the composed API may be degraded for optional unavailable voice; the adapter's `/_uat/health` checks the actual synthetic host. Stop only recorded hosts. Current PID files are historical after cleanup.

Roles: `p01-owner` has explicit executive assignments across fixture areas; `p01-dual` has Sales/Marketing and lacks Finance/Support; `p01-member` has no responsibility assignment. These are existing dev-header fixture identities, not production credentials. North Company is `11111111-1111-1111-1111-111111111111`; South Company is separate. Source IDs change on restart; use the reconciliation script, not historical deep links.

Environment: disposable SQLite composed API with background workers disabled; no external provider. The SQL test separately uses Windows-authenticated local SQLEXPRESS, creates one uniquely named database and removes only that database on disposal. It does not use company production data.

Flows: F11-01 board/detail/collaboration; F11-02 input versions/revision/challenge/list; F11-03 owning approval/business/source returns; F11-04 keyboard/mobile; F11-05 restricted/company denial; F11-06 empty/retry/refresh. Evidence: JPEG screenshots and DOM captures via Codex in-app browser, authenticated GET reconciliation/hashes, TRX/build/migration logs. Default actual viewport is 1280×720; responsive tests use 390×844 and reset afterward.

Performance evidence is bounded read timing in `source-reconciliation.json`, not a production throughput benchmark. Model-check logs contain existing EF warnings; builds contain existing nullable/analyzer warnings and unavailable NuGet metadata (NU1900). They do not establish a fresh package vulnerability audit.
