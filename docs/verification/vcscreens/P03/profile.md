# P03 verification profile

2026-10-01; baseline `37834c7f` plus preserved P01/P02 and new P03 working-tree changes. Windows, .NET 9.0.317. No deployment, commit or release approval.

Real Blazor Web and authenticated composed API through the existing `tests/VirtualCompany.Workspace.Uat` adapter. Disposable SQLite and disabled workers; no provider/customer/payment calls. Owner and unassigned-member fixture accounts, North/South/revoked companies. North renewal is now a persisted eight-day-old Sales record so the revenue-risk journey uses actual monetary evidence. Existing P02 tasks, review, Support case and Marketing experiment remain.

Flows: F03-01 CEO Today and dated company-health report; F03-02 revenue risk → detail → personal follow-up → Work reload → Today/report; F03-03 source Sales record/reconciliation and exact return; F03-04 internal approval review; F03-05 member/revoked/foreign scope; F03-06 department filters, mobile and keyboard.

Ports 5319 API adapter, 5079 owner Web, 5080 member Web. Check ports before each host. Use direct Start-Process dotnet -PassThru -WindowStyle Hidden, record exact PIDs immediately, separate ≤30-second health check with 10–15-second per-request timeout. Prior P01/P02 verified sandbox DPAPI restriction permits approved outside-sandbox Web startup. Stop only owned recorded PIDs; reset temporary viewport and close agent tabs.

References: existing `role-time-agent-2026-10-01/01-ceo-today.png`; new saved `company-health-report-reference-prompt.md` and generated PNG; P02 evidence-detail reference reused for risk detail. Compare actual desktop/mobile screenshots and recheck any corrections.

Finance source availability and plan states are distinct. This browser fixture has unavailable cash initialization. Automated Finance contributor/serializer/presentation checks cover no budget, one recorded unapproved version, ambiguous versions, and failure. Deployed tenant, SQL Server/provider cash reconciliation, statutory correctness and human approval remain separate gates.

Final replay: API PID 55820, owner Web PID 17592, member Web PID 56600; all health checks HTTP 200. Final real task `a81867cd-f7af-4011-a2ee-6021580c9ea5`, source observed 23/09/2026 17:09 UTC, approval `88888888-8888-8888-8888-888888888888` explicitly rejected and reloaded. Browser measured 390×844 layout and used keyboard refresh/filter controls. Final logs empty of browser warnings/errors. All owned hosts stopped, fixture ports free, tabs closed and viewport reset. PID files are historical only; restart the fixture rather than treating these PIDs as current.
