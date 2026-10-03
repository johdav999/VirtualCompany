# P10 verification profile

Mission: make agent work understandable by durable identity, ownership, evidence and next dependency. The shared contract, repository architecture/design rules, polish-uat-loop and reference-first workflow govern this run. Baseline HEAD is `37834c7f75d4c1ddd532e1a48464887724a84978`; all prior phased changes remain uncommitted in the same checkout.

Windows PowerShell/.NET 9; real Blazor Server Web and composed authenticated API in `tests/VirtualCompany.Workspace.Uat`, disposable SQLite, background workers disabled. Existing North/South/Ledger and P01–P09 fixture families are retained. P10 adds internal work evidence only. Owner is p01-owner; dual Manager has Sales/Marketing assignments; p01-member has no assignment. No customer/provider delivery is requested.

Ports: API 5319; owner Web 5079, dual 5081, member 5080. Each host is launched directly with Start-Process/PassThru/Hidden and recorded immediately. The ordinary Web runtime failed at existing Windows DPAPI key decryption/Event Log access; exact runtime startup/cleanup used scoped escalation after that failure. Source reads, builds and tests used the workspace sandbox. Process files are historical after cleanup.

Codex in-app browser, background tabs. Capture metadata records actual viewport dimensions and URLs; screenshots are separate from illustrative references. Async loading is not accepted as a final result. UI interactions use the documented CUA browser API. Before/after source reconciliation uses read-only localhost requests against the disposable fixture. NuGet vulnerability lookup warnings reflect unavailable nuget.org metadata; they do not establish a completed vulnerability audit.

References: `docs/design/references/agent-work-board-p10-reference.png` and `agent-work-detail-p10-reference.png`, with their saved `-prompt.md` files. Provider: built-in ImageGen, no alternate provider. Existing product shell and durable source values take precedence over illustrative reference navigation, people, counts and diagrams. P11 collaboration topology is not fabricated to imitate a picture.

No structural EF change. SQLite verifies portable query behavior, not deployed SQL Server acceptance. Human release approval is Pending; earlier independent provider/native/statutory/output gates are unchanged.
