# Session log

## 2026-09-06

- Reproduced from the user-provided preparation screenshot.
- Confirmed the invitation is scheduled and has a Google provider event.
- Confirmed there is no meeting session for the invitation.
- Confirmed the lead, converted deal, and contact do not resolve to a customer company.
- Fixed readiness so session creation is blocked before submission with an actionable company-identification message.
- Fixed conflict handling so only the dedicated concurrency problem code reloads server state.
- Added regression tests for missing-company readiness and non-concurrency conflict handling.
- Published and restarted API PID 52528 on port 5301 and Web PID 51796 on port 5062.
- Verified the live preparation endpoint returns HTTP 200 with `readinessState: blocked`, blocker `customer_company_missing`, and no allowed session action.
- Implemented explicit company linking on the deal Contact & account card.
- Added a tenant-scoped API command that synchronizes the deal, source lead, and contact, with sales activity and audit evidence.
- Verified blank-name rejection and cross-tenant protection in focused integration tests (2/2 passed).
- Verified the Web build and published local API/Web processes (API PID 46948, Web PID 52332).
- Replayed the live deal route through HTTP: status 200, Link company control present, and the deal remained unmodified until the organizer explicitly submits a company name.
