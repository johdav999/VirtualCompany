# Finance agent P2 release gate

P2 uses the effective Finance coverage catalogue as a truthful product contract and a revision-bound release
manifest as its evidence boundary. Coverage metadata and UI labels do not grant authority; P0 actor authorization,
current policy, approval, and execution checks remain authoritative.

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/verify-finance-agent-p2.ps1 `
  -P0ManifestPath <current-p0-manifest.json> `
  -P1ManifestPath <current-p1-manifest.json> `
  -NoRestore
```

The verifier writes JSON and Markdown beneath `artifacts/finance-agent-p2/<UTC timestamp>/`. It records the Git
revision, dirty state, working-tree checksum, catalogue and design-reference checksums, exact commands, TRX counts,
durations, evidence paths, unresolved approvals, release stops, and a checksum over the manifest core.

The mandatory checkpoints are:

- catalogue completeness and ownership plus read/recommend/execute tool, orchestration, and safety tests;
- authorized effective-coverage API, EN/SV UI, contextual workbench links, responsive and accessibility contracts;
- full Release build, hermetic matrix, clean EF model, and disposable SQL Server lanes;
- supported small-profile accounting capacity and production-shaped report/audit-package evidence; medium results are
  retained as unsupported candidates and cannot replace small proof;
- coordinated SQL/object recovery with matching checksums, source links, and audit-package manifests;
- authenticated EN/SV desktop and narrow UAT for ledger, close, compliance, advanced accounting, draft, approval,
  accessibility, and recovery flows;
- current-checksum P0 and P1 `go` manifests;
- deterministic Swedish accounting technical verification, with qualified human approval and external provider-scope
  approval recorded separately.

`-FocusedOnly` is for development evidence. It still writes a manifest, but every omitted release lane is recorded as
`not_run` and the decision remains `no_go`. Missing SQL Server, browser, recovery, provider, or human prerequisites
are never converted into passing results. Tool count is never represented as percentage product completion, and a
failed safety checkpoint cannot be averaged against passing checks.

Any source, policy, prompt, tool, configuration, migration, localization, UAT, or evidence change invalidates a
previous manifest and requires a new run for the exact working-tree checksum. This gate is engineering evidence; it
is not statutory approval or a signed professional opinion.
