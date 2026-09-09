# Alex Teams shared-stage runbook

For the complete organizer, administrator, privacy, incident, uninstall, and controlled-rollout procedure, also follow `docs/teams-presenter-production-rollout.md` and the release-gating matrix in `docs/teams-presenter-live-uat-matrix.md`.

## Supported route

The supported visual presentation route is the Virtual Company Blazor meeting stage:

- configuration: `/teams/meetings/configure`
- private organizer side panel: `/teams/meetings/{meetingSessionId}/side-panel`
- customer-visible shared stage: `/teams/meetings/{meetingSessionId}/stage`

The stage renders the repository-produced slide image. It does not embed PowerPoint, capture a desktop, or drive Teams through UI automation. Alex changes presentation state only through the versioned `presentation.*` backend tools. Human controls call those same tools and preempt older narration.

## Required configuration

Set `TeamsPresenter:Enabled` and `TeamsPresenter:SharedStageEnabled` only after Prompt 1–4 readiness checks pass. Configure the Entra web application ID/resource, the exact allowed tenant IDs, and HTTPS `WebOrigin`, `ConfigurationUrl`, `SidePanelUrl`, and `StageUrl`. Keep `TeamsMeetingUi:BrowserDiagnosticsEnabled` false outside a controlled development environment.

The app manifest must include `meetingSidePanel` and `meetingStage`, plus the currently required `MeetingStage.Write.Chat` resource-specific consent. The API validates Teams SSO bearer tokens against the configured application audience and exact allowed tenant issuer. Development header authentication remains a development-only diagnostic path.

## Organizer flow

1. Open the authorized Virtual Company meeting or lead experience and choose **Open private Teams presenter controls**.
2. In the installed Teams meeting side panel, confirm that the connection says **Synced** and review the current slide.
3. Choose Manual, Assisted, or Autonomous. Autonomous permits Alex to advance only at plan transitions; human input still wins.
4. Select **Share to meeting**. This is an explicit organizer/participant action. The app checks Teams stage-sharing capability and reports any Teams denial; it never simulates success.
5. Wait for **Stage render confirmed** before Alex narrates the new slide. Missing, unauthorized, slow, or undecodable slide assets keep the previous safe slide and narration paused.
6. Use Previous, Next, Go to, Pause/Resume, slide search, or **Mute / stop presenter audio** as needed. Use Teams' own **Stop presenting** control to end the shared stage.

## Security and recovery

Stage access is a short-lived Data Protection capability tied to one company, meeting session, active deck, and deck version. It expires at the configured bound or meeting retention boundary, whichever comes first. Every use rechecks feature gates, organizer membership/meeting ownership, meeting lifetime, and the active deck version. The asset endpoint serves only the current or immediately next slide and emits `no-store`, `no-referrer`, and `nosniff` headers.

The shared contract contains slide/state fields only. Notes, talking points, plan artifacts, voice state, consent, and diagnostics are private-contract data. On reconnect, both surfaces reload the authoritative snapshot before accepting further work. A stale acknowledgement or command is rejected.

## Optional visual media feasibility

`TeamsPresenter:VisualMediaEnabled` remains `false`. No approved SDK/deployment/tenant-policy/format/licensing feasibility evidence exists for bot video or VBSS in this phase. Shared-stage presentation and bot audio are independent; no desktop or arbitrary-window capture fallback is permitted. Enable this gate only after a separately reviewed feasibility record and frame/rate/termination test suite exist.

## Verification checklist

- Validate the package and confirm the two meeting contexts and `MeetingStage.Write.Chat` RSC.
- Test the installed app in Teams desktop and web with organizer, attendee, denied-presenter, restrictive-policy, reconnect, and meeting-ended cases.
- Verify common stage sizes, including the 994×678 default, and narrow side-panel widths.
- Confirm exact slide aspect ratio, complete image visibility, keyboard focus, 44px controls, screen-reader status announcements, render timeout, expired grant, deck replacement, and cross-company rejection.
- Preserve screenshots and tenant/policy identifiers in the release evidence; do not record access tokens.

## Azure web hosting and effective embedding policy

The web project is server-side Blazor on .NET 9. Publish it separately from the Windows media API and configure the HTTPS `ApiBaseUrl` to the deployed API. For Azure App Service, enable WebSockets, retain session affinity for the Interactive Server circuit, and use Always On on a suitable plan. Configure trusted HTTPS forwarding correctly; preserve the external hostname and scheme. Keep common Data Protection keys wherever API instances must validate the same stage capabilities.

The effective web response uses:
```text
Content-Security-Policy: frame-ancestors 'self' https://teams.microsoft.com https://*.teams.microsoft.com https://*.cloud.microsoft
```
Antiforgery's conflicting SAMEORIGIN header is suppressed; its request validation remains enabled. Do not add an incompatible CSP/X-Frame-Options rule in App Service, Front Door, or another proxy. Verify the headers on the actual Teams side-panel and stage responses after deployment. This is a bounded Teams embedding policy, not unrestricted framing.

The current typed web clients call the API from the Blazor server, so those requests do not need browser CORS. Keep the existing development-only localhost CORS policy scoped to Development. If future browser code directly calls a separate API origin, review that specific endpoint and allow only the configured web origin.

Local browser framing tests can establish CSP behavior and Blazor connectivity. They do not establish Teams SSO, tenant app policy, Graph media, or real meeting stage sharing. Repeat the restrictive-origin, Teams web/desktop, reconnect/WebSocket, and authenticated/capability-bound checks in the installed tenant package.

The meeting presenter is selected in meeting preparation. Controls use generic presenter wording so a Marketing assistant can use the same shared-stage workflow. The bot's installed display name remains determined by its package/tenant registration.

Publish the server web artifact with `dotnet publish src/VirtualCompany.Web/VirtualCompany.Web.csproj -c Release -o artifacts/teams-web`. Deploy the complete publish directory, including `wwwroot/VirtualCompany.Web.styles.css`; running a Production process against the source directory is not a substitute for a published deployment.
