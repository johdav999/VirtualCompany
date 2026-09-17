# Preset-first meeting preparation — UAT

Date: 2026-09-14. Product: Virtual Company, Blazor Web and Sales application services.
Environment: local development; company member / meeting organizer. Existing user Web host left unchanged.
Preview: dotnet run --no-build --project src/VirtualCompany.Web/VirtualCompany.Web.csproj --urls http://127.0.0.1:5066.

## Scope and ownership

The old implementation placed the preset picker beside the original session form.
The apply service also preferred saved session values over preset defaults and activated
a new deck without releasing the legacy active-deck slot.

The meeting now selects a prepared preset before a session exists. The application
service creates the session and applies the published version in one transaction.
Defaults, presenter control mode, rendered slides and baseline talking points are inherited.
Only explicit customer overrides replace preset defaults. Consent and retention remain
meeting-owned; a preset cannot grant attendee consent or reuse customer disclosure approval.
Legacy preparation is retained behind a collapsed compatibility section.

## Issue ledger

| ID | Severity | Finding | Acceptance and evidence | Result |
|---|---|---|---|---|
| PRESET-01 | P1 | Duplicate mandatory session setup | Preset picker appears without a session; legacy controls collapsed. Component tests and actual browser route. | Verified |
| PRESET-02 | P1 | Session values override reusable defaults | Relational service test asserts preset goal, audience, duration, presenter and control mode; keeps meeting consent. | Verified with SQLite |
| PRESET-03 | P1 | Applying preset alongside legacy active deck fails | Transaction releases active slot; repeat apply is idempotent; original deck retained inactive. | Verified with SQLite; SQL Server remains untested |
| PRESET-04 | P1 | Missing reusable runtime talking points | Preset baseline talking points materialized as meeting slide artifacts with preset-slide source references. | Verified with relational test |
| PRESET-05 | P2 | Raw version text and poor hierarchy | Real browser showed literal version expression; corrected to Test · v1; preset moved immediately below invitation. | Verified |
| PRESET-06 | P2 | Mobile layout | 390px viewport, scrollWidth 375px, optional and legacy sections closed, labels/controls readable. | Verified |

## Evidence and checks

- Reference: ../../design/references/sales-meeting-preset-first-reference.png.
  Built-in ImageGen; prompt saved beside reference. Used for card hierarchy,
  read-only defaults, optional overrides, responsive stacking and restrained styling.
- Desktop: meeting-preset-first-desktop.png.
- Mobile: meeting-preset-first-mobile.png.
- Actual route: /app/sales/meeting-invitations/4c3c2af2-98ba-4ea5-8045-ed49d4e7445f/prepare.
- Selected the existing published Test preset in the browser without applying it or
  changing the user's meeting, consent, provider event, or narration approval.
- Backend: 12 tests passed (SalesMeetingSessionServiceTests, SalesPresentationRunTests).
  Includes failed-presenter transaction rollback, tenant isolation, idempotence,
  old deck replacement, inherited defaults and runtime talking points.
- Web: 21 tests passed across SalesMeetingPreparationPageTests,
  PresentationRunWorkflowTests and PresentationPresetComponentsTests.
- API and Web compiled through the focused test builds. No schema changes.
- git diff --check passed (existing CRLF warnings only).

## Remaining verification

SQL Server integration environment variable VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION
is not configured. SQLite is not claimed as proof of SQL Server-specific concurrency.
The actual browser apply command was tested with a component HTTP fixture and backend
relational service tests, not against the user's saved meeting or the older running API.
Customer-specific narration approval and paid speech generation were not exercised.
Restart API and Web before testing the new invitation-level apply endpoint.
