# P05 verification profile

2026-10-01; Windows/.NET 9, baseline 37834c7f plus preserved uncommitted P01–P04. Real Blazor Web and authenticated API via the existing Workspace.Uat adapter, disposable SQLite with disabled workers. Owner and unassigned-member accounts are synthetic; no customer delivery or spend is authorized by this fixture.

Flows: F05-01 Marketing Today → due campaign/content/audience; F05-02 reject/revise/reload/submit/approve exact content; F05-03 campaign delivery/spend/leads filters, included records and CSV; F05-04 retained asset/channel recovery and exact return; F05-05 restricted/company isolation; F05-06 responsive and keyboard.

Fixtures extend earlier records: campaign 05050505-0505-0505-0505-050505050505, brief ending 0506, original variant ending 0507, retry-scheduled action ending 0508. Recorded SEK budget 100, known cost 120 SEK + 9 USD, one unknown-cost touch, observed leads 4, no recorded audience segment or attribution. These gaps must remain visible. P01–P04 data are retained.

Host procedure: check ports 5319/5079/5080, build before starting hosts, run dotnet directly using Start-Process -PassThru -WindowStyle Hidden and record each exact PID, poll separately with bounded requests. Stop only owned processes before any rebuild. Browser screenshots through the computer-use browser surface; no native desktop capability. Provider publishing, production SQL Server, deployed tenant and human approval are independent acceptance gates.

Reference prompts and generated images: marketing-today-reference, marketing-campaign-review-reference, marketing-operational-report-reference under docs/design/references. Built-in ImageGen used before major UI implementation; images inspected. Weekly reference 03 remains reserved for P19.

Final replay 2026-10-02: added missing-budget campaign ending 0509 and rejected synthetic asset ending 0510. Spend/attribution daily counts are 2; after persisted content approval, content reviews/content due are 0. Final hosts 12964/31596/28864 stopped; PID files are historical. Temporary tabs closed, viewport reset. Requested mobile override 390x844 measured CSS width/scrollWidth 300/300; report table 680 is contained. New transaction checks use a retry-aware execution strategy over SQLite; this remains distinct from live SQL Server validation.
