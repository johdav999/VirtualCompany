# P01 issue ledger

| ID | Severity | Flow | Type / observed baseline | Surface / acceptance and regression | Status |
| --- | --- | --- | --- | --- | --- |
| P01-01 | P2 | F04 | Defect: shell had no active-company selector | NavMenu native select; active memberships only; reset to server-default Today; WorkspaceShellTests | Verified by final focused automatic checks (see implementation.md) |
| P01-02 | P2 | F03 | Defect: Overview/module links lost responsibility and Monthly return context | Shared route helper and circuit context; exact company/lens/month return; WorkspaceNavigationTests and Monthly component tests | Verified by automatic checks and browser packet F01/F03/F05/F06 |
| P01-03 | P1 | F04 | Defect: older awaited dashboard/navigation loads could overwrite newer scope | Cancel/version guards before field commits; delayed company response test and navigation race tests | Verified by final focused automatic checks (see implementation.md) |
| P01-04 | P2 | F05 | Defect: single allowed lens lacked visible availability explanation | Show server-derived lens/reason; do not invent access; component/API tests | Verified by automatic checks and browser packet F01/F03/F05/F06 |
| P01-05 | P2 | F03 | Defect: invalid month deep link could throw out of page load | Localized recoverable validation before typed request; invalid-link component test | Verified by final focused automatic checks (see implementation.md) |
| P01-06 | P1 | F03 | Defect: unsafe return URL paths and repeated return parameters were accepted | Reject decoded slash/backslash/control paths; replace single return parameter before fragment; route tests | Verified by final focused automatic checks (see implementation.md) |
| P01-07 | P1 | F03 | Browser defect: query subscription on departing Dashboard could canonicalize a module navigation back to Today | Dashboard loads/commits only on its own route; departing component regression test; rerun Monthly → module → reload → Overview | Verified by departing-component regression and original F03 browser replay |

| P01-08 | P2 | F04/F05 | Defect: fallback sidebar identity could show the persisted active company's name on another company route | Resolve label from current active membership or neutral Company; WorkspaceShellTests and revoked-company screenshot | Verified by automatic test and browser replay |

Later role redesign and report completion are tracked by their owning prompts in the screen/report register, not defects deferred from P01.
