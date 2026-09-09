# Prompt 5 UAT session

## Product profile

- Surface: Blazor Server pages hosted by Virtual Company and framed by Microsoft Teams.
- Stage reference: `docs/design/references/sales-meeting-stage-reference.png`.
- Side-panel reference: `docs/design/references/sales-meeting-side-panel-reference.png`.
- Primary audience: meeting organizer privately controlling Alex; attendees see only the stage-safe slide.
- Critical safety boundary: a stage capability can read only the exact meeting/deck version and current/next image; stage DTOs cannot contain private presenter data.

## Journeys exercised

| Journey | Evidence | Result |
| --- | --- | --- |
| Missing stage grant at 994×678 | Browser AX tree and screenshot, 2026-09-04 | Pass: neutral protected state, no private data, no broken layout. |
| Narrow 360px private side panel with unavailable backend | Browser AX tree and screenshot, 2026-09-04 | Pass after fix: bounded SDK loading, truthful degraded state, no horizontal overflow. |
| Exact-aspect and accessibility contract | `TeamsMeetingSurfaceTests` | Pass: `object-fit: contain`, dynamic stored ratio, post-decode ack, ARIA live states, 44px controls. |
| Command/render sequencing and active-connection ack | `SalesMeetingPresentationConductorTests` | Pass: exact version render gates narration; unauthorized ack rejected; duplicate ack idempotent. |
| Teams capability boundary | `TeamsMeetingSurfaceTests` and package tests | Pass: SSO token is memory-only; real capability and share APIs; required context/RSC retained. |

## Findings and fixes

1. High — The Teams SDK loader could remain pending indefinitely when the CDN was unreachable in browser diagnostic mode. Fixed with a bounded five-second load failure and verified that the private panel leaves loading and shows a truthful degraded state.
2. High — A hub endpoint-level authorization requirement prevented short-lived stage-capability connections before the hub could verify them. Removed the blanket endpoint policy; the hub now admits only a verified stage capability or a resolved authenticated company membership.
3. High — Stage snapshots included the durable slide storage URL property value. The stage projection now always returns `null`; slide bytes are fetched through the short-lived scoped asset endpoint.

## Remaining external validation

A real Teams desktop/web meeting was not available in this local environment. Before production enablement, complete the tenant checklist in `docs/teams-presenter-stage-runbook.md`, including actual organizer share, RSC denial, meeting policy, SSO issuer/audience, render sizes, and reconnect evidence. `SharedStageEnabled` and `VisualMediaEnabled` remain fail-closed by default.
