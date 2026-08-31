# UAT issue ledger

| ID | Severity | State | Evidence | Resolution / next check |
|---|---:|---|---|---|
| AUTH-01 | P0 | Resolved in code | Baseline agent profile had no effective authority or approval evidence workspace. | Added API-backed capability matrix and safe latest-approval preview. Verify in authenticated browser. |
| AUTH-02 | P0 | Resolved in code | Approval threshold context could contain payload hashes, policy internals, and provider data. | Added an allow-list presenter that only projects named safe fields; unit test rejects secret/raw values. |
| AUTH-03 | P1 | Resolved in code | System diagnostics were not separated from daily finance evidence. | Added role-gated operator footer using the existing sandbox-admin access policy. Verify absence for non-admin roles when fixtures permit. |
| AUTH-04 | P1 | Resolved in code | Narrow capability tables risk horizontal-only access. | Added stacked card-table layout below 576 px and visible focus styles. Verify at narrow viewport. |
| AUTH-05 | P1 | Blocked by local runtime | The built API and Web processes did not bind to ports 5301/5062 within their bounded 30-second health checks. The in-app browser independently confirmed `ERR_CONNECTION_REFUSED`. Both recorded processes and the LocalDB instance started for the attempt were stopped. | Static responsive, focus, semantics, localization, and source-contract checks pass. Repeat authenticated desktop/narrow visual and keyboard verification when the repository runtime can bind locally. |
| AUTH-06 | P2 | Resolved | Localization gate found a pre-existing visible English `Accountant portfolio` shell label. | Replaced with paired English/Swedish navigation resources. |
