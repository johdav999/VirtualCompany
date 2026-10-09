# Deal meeting host-room action — 8 October 2026

## Product profile

- Product: Virtual Company, local Blazor Web and ASP.NET Core API.
- Revision: `9a356c7901abc2044f6ecb6a4e8a5ebf64ceab5b` plus the existing uncommitted checkout and this change.
- Role: existing Development Alice Admin session, VC company `43e6a825-d1b7-429a-8608-7e668087d005`.
- Launch: Debug net9.0 API and Web outputs copied into `artifacts/deal-host-room-2026-10-08/run-20261008222821`; launched with `dotnet "<snapshot>/VirtualCompany.Api.dll"` and `dotnet "<snapshot>/VirtualCompany.Web.dll"`, working in their respective source project directories. Existing local settings retained without recording secrets. API startup daily cadence remains suppressed as in the previous owned host.
- Hosts: API port 5301, PID 43732; Web port 5062, PID 3764. Exact previous DLL and listener ownership verified before replacing only API PID 29816 and Web PID 19324. See `api-host.json` and `web-host.json`.
- Evidence: headless installed Microsoft Edge through Playwright, 1725×837 desktop and 390×844 phone. Screenshots inspected visually. Native browser automation was unavailable because its launcher could not apply deny-read ACLs.
- Scope: scheduled booking actions, keyboard navigation into the existing host room, and phone layout. Existing bookings, invitation status and times are unchanged.

## Issue ledger

| ID | Severity | Flow | Type | Expected / observed before | Root cause and acceptance | Evidence | Status |
|---|---|---|---|---|---|---|---|
| DHR-001 | P2 | Deal meeting bookings | enhancement | A booked browser meeting should offer Open Host room alongside Review booking and Prepare meeting. The three original cards had no host-room action. | The booking summary omitted the persisted BrowserRoomId. Add it to the company-scoped projection and both response contracts; scheduled bookings with a nonempty saved room ID link to that room while preserving company and return context. Failed/cancelled and roomless bookings do not offer this action. No automatic join. | `before-bookings.png`, `after-bookings.png`, `after-browser.json`, focused TRX files | verified |

## Acceptance evidence

Run: bundled Node executable with `verify-browser.mjs before` for baseline and `verify-browser.mjs after` after refreshing both hosts.

1. Open the user-selected deal `015351a5-f2ec-4bf7-8b26-c6718b9e1363` through its Sales dashboard/prospects return context. Compare the three persisted booking identities, statuses and times with the baseline. Both scheduled browser bookings now show Open Host room; the failed booking does not. The links use the saved room IDs, company context, dashboard return and originating deal return. **Pass.** See `before-browser.json`, `after-browser.json`, desktop and booking screenshots.
2. Focus the first host link and press Enter. The existing room `af4dc01c-d832-4be9-a87c-4a41134a8196` opens without joining. This historical October 1 meeting displays “This meeting has ended. Book a new meeting to continue.” **Pass for navigation and explicit-join boundary.** See `host-entry.png`. Live joining, media and agent/provider behavior were not exercised.
3. Reopen the deal at 390×844. Actions wrap, buttons remain contained and usable, and the document has no horizontal overflow. **Pass.** See `after-narrow-bookings.png`.

All three browser groups passed with zero page runtime errors. Web `/` and API `/health/live` returned 200. The aggregate API readiness endpoint remains Degraded/503; host logs identify the existing optional Sales meeting voice pilot as unavailable. This packet does not claim aggregate service readiness or voice acceptance.

## Automated verification

- `SalesDealMeetingsTests`: **10 passed**, 0 failed/skipped (`host-room-web.trx`). Covers scheduled, failed, cancelled and roomless bookings, context preservation, compact pipeline markup and booking time zones.
- `SalesMeetingWireTests`: **1 passed**, 0 failed/skipped (`host-room-wire.trx`). The real API response deserializes through the Web typed client with the persisted BrowserRoomId in both pipeline and deal results.
- Focused `SalesOperationsApiIntegrationTests`: **2 passed**, 0 failed/skipped (`host-room-api.trx`). Covers preconversion/explicit booking matching, saved room IDs, company isolation and cross-tenant not-found behavior.
- These test runs built the affected Web, API and dependent projects. Existing compiler/analyzer warnings remain; no compiler errors. Scoped `git diff --check` passed.

No database schema change or external invitation send was required. English and Swedish labels are included. Updated local hosts remain running for review. Unrelated checkout changes are preserved.
