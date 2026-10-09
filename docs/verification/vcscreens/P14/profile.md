# P14 local verification profile

Accepted replay uses the ordinary repository-local synthetic UAT fixture and DevelopmentAuth subject `p01-owner`, email `p01-owner@example.com`. API 127.0.0.1:5319 and Web 127.0.0.1:5079 were bounded owned hosts; no provider was contacted. The API host seeds prior P01–P13 journeys before P14. Its profile endpoint is `/_uat/p14/profile`. Saved IDs apply only to this stopped host, not a future replay.

| Identity | Value |
| --- | --- |
| Company | 11111111-1111-1111-1111-111111111111 |
| Agent | b5c08109-516b-43cc-ad87-f810ad571215 |
| Work task | 92bfd0c2-bd08-4b76-9d3d-fd681697b463 |
| Initiative | d7cc7a8a-e246-453a-8639-e0c8df28cbf8 |
| Operating plan | c14cadfe-8a83-46f8-889f-ddc8897addf4 |
| Company intent/version | controlled_execution / 2 |
| Agent profile | level_2 / Guided |
| Finance grant | finance.daily_cash / read_monitor / version 1 |

The grant permits one read action and ten records with amount bound 100.0, UTC 00:00–23:59, freshness 60 minutes and manual_review trigger. It is test setup created/activated through the existing owning grant service. The no-effect explanation checks supply no current business evidence: initially evidence-stale, then expired after the UAT-only expiry control. That control is absent from production. Task lifecycle remains planned. Finance edit requires approval; list_transactions has unavailable integration; finance.removed_tool is configured but excluded from executable catalogue.

Desktop: default 1280×720 viewport. Responsive: 375×900. Real native browser controls and keyboard were used through CUA. Host PIDs and cleanup are recorded in `cleanup.json`; process stop of the elevated Web host required escalation after sandbox stop left its listener alive. API model ownership and prior migrations remain unchanged in P14.
