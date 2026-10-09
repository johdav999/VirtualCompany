# Meeting bookings on pipeline opportunities — 2026-10-05

User finding: the converted lead has a sent meeting invitation, but its pipeline entry does not show the booking.

## Root cause and change

The lead screen reads meeting invitations. Pipeline and opportunity reads did not project those invitations, and their views had no booking component. A meeting can be created before conversion, leaving DealId null while retaining LeadId. Merely filtering invitations by DealId would still miss those bookings.

The Sales query now reads one company-scoped invitation batch and associates explicit DealId bookings with their opportunity. Lead-only bookings are associated through SourceLeadId. An explicit link to another opportunity takes precedence over the shared source lead. No schema migration or calendar mutation was needed.

The pipeline card shows booking status, title, time, timezone and additional invitation count. The opportunity displays the invitation list with distinct status, time and links to the existing booking controls and preparation workflow. Failed/cancelled bookings are not labeled scheduled; only scheduled bookings offer preparation. Links retain the company, originating Sales dashboard, pipeline and opportunity return. English and Swedish text is provided.

## Verification

- API integration: **13 passed** (`results/meeting-pipeline-api.trx`). Includes lead-only bookings before conversion, explicit opportunity links, cancellation, explicit other-opportunity exclusion and company isolation. The initial new fixture attempted cross-company references prohibited by composite foreign keys; it was corrected to valid foreign-company records and then passed.
- Web presentation/navigation/localization: **26 passed** (`results/meeting-pipeline-web.trx`). Includes no nested links in pipeline cards, no booking claim for empty data, state-specific controls, return context, recorded timezone and invalid-zone UTC fallback.
- HTTP wire contract: **1 passed** (`results/meeting-pipeline-wire.trx`). The real Web Sales client reads and decodes both API payloads in an isolated fixture.
- API and Web LocalRun builds succeeded. Existing repository nullable/analyzer warnings remain. Scoped `git diff --check` passed.
- Real original flow exercised in headless Edge against rebuilt localhost:5062 and the original configured API database. Before/after JSON and screenshots are adjacent. The desktop browser automation tool was unavailable earlier in the session; headless Edge is the documented substitute, not operation of the user's existing tab.

Verified company: `43e6a825-d1b7-429a-8608-7e668087d005`; lead: `efd8b4d2-da9d-438e-990e-d2ac719bf608`; opportunity: `015351a5-f2ec-4bf7-8b26-c6718b9e1363`.

The existing invitation `97f0c875-61e1-468f-a61b-cee129fd8c74` appears on the pipeline card and opportunity: Virtual Company demo · Test Company, Thu 1 Oct 2026, 07:49–08:49, Europe/Stockholm. All three source-lead invitations appear on the opportunity, preserving the failed invitation separately. Review booking opens the original lead and returns to the exact originating opportunity; its booking text remains unchanged. Link context checks pass. Desktop and 390px mobile images were visually inspected; mobile document width equals viewport width (390px), with no horizontal overflow. Browser page-error collection was empty.

Verification performed read-only navigation; it did not send, reschedule, retry or cancel a meeting. Preparation links were checked for destination, identity, status and return context; provider or presentation execution was not part of this change. The replacement local hosts remain running; IDs and start times are in `hosts.json`. Prior uncommitted phased work is preserved.
