# Connected-banking Prompt 9 UAT evidence

Evidence date: 2026-08-29 (Europe/Stockholm)  
Product profile: desktop-first authenticated Finance operations; responsive narrow layout; keyboard and visible-focus support; English and Swedish locales; provider-connected evidence only in an approved non-production environment.

## Evidence status

No owned authenticated host, seeded UAT company, or browser operator identity was supplied in this workstation session. Browser UAT therefore remains **BLOCKED / NOT RUN**. No screenshot or interaction is invented. This is a release stop under Prompt 9, even though the component/API automation is green.

Automated substitute evidence exercised the same permission and truth boundaries:

- connected-banking readiness and recovery routes: owner access, cross-company denial, AccountingAdmin recovery execution;
- bank connection, payment batch/execution, and treasury authorization classes;
- bank connection, reconciliation, payment, and treasury workspace component surfaces and API clients through the `connected-banking-failure` lane;
- service tests proving rejected and reconciliation-required payments are not represented as settled and feed gaps remain explicit.

These tests reduce regression risk; they do not satisfy authenticated browser UAT.

## Required owned-browser flows

| Locale / viewport | Flow | Required observations | Status |
| --- | --- | --- | --- |
| English / desktop | Connection setup and consent renewal | Provider/institution, expiry, ownership, mapping, scope loss, and safe recovery reason are visible; no credential material appears. | BLOCKED — host/identity absent |
| Swedish / desktop | Feed health and exact-gap recovery | Lag, coverage, gap dates, cursor-recovery action, and imported-once evidence are understandable and correctly localized. | BLOCKED — host/identity absent |
| English / desktop | Payment batch through execution review | Approval/version, selected debit account, provider identity/status, rejected/ambiguous distinctions, and no blind retry are clear. | BLOCKED — host/identity absent |
| Swedish / narrow | Daily treasury workspace | Balance source/time, stale/missing evidence, exceptions, permission explanations, and deep links remain usable without horizontal loss. | BLOCKED — host/identity absent |
| Both / keyboard | Connection, reconciliation, payment, treasury | Logical focus order, visible focus, accessible names, error recovery, and no color-only status meaning. | BLOCKED — host/identity absent |

Capture before/after screenshots for every issue fixed, record the route/company/role/locale/viewport, and preserve only non-sensitive sandbox data. Do not include bank tokens, callback state, certificates, full account numbers, private provider payloads, or webhook bodies.

## UAT issue ledger

| ID | Severity | Flow | Evidence | Status / release effect |
| --- | --- | --- | --- | --- |
| UAT-EXT-001 | High | Authenticated EN/SV browser matrix | No owned host or operator identity configured | Open; blocks go-live |
| UAT-EXT-002 | High | Real-provider ingestion/submission/acknowledgement | No provider application ID/private key configured | Open; blocks go-live |
| UAT-INT-001 | Info | Readiness and recovery API | Focused Finance 6/6 and API 1/1 tests passed | Closed; automated evidence only |

No critical defect was observed in the automated scope. The two high evidence gaps are unresolved by definition and keep the overall UAT decision at **NO-GO**.

