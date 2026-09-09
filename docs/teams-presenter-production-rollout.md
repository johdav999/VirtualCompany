# Alex Teams presenter: production rollout and operations

## Release boundary

The production path is disabled by default. Enabling `TeamsPresenter:Enabled` is not sufficient: the server also evaluates tenant registration, exact Graph permissions, Teams policy attestation, app installation/package compatibility, company and organizer pilot allowlists, automated evidence, live tenant UAT, global/company/host concurrency, demo-company exclusion, certificate/media health, and the emergency-disable state. A rejected decision returns a stable reason code and creates no provider side effect.

The organizer remains the accountable human. Alex never represents itself as human, admits itself from a lobby, changes native meeting roles, silently starts media, or reports stage sharing as successful before Teams/provider confirmation.

## Configuration gates

Configure secrets through the approved secret references described in `docs/teams-presenter-identity-and-consent.md`; never put a secret in the rollout settings below.

| Setting | Production meaning |
|---|---|
| `ProductionEnabled` | Global production release gate. Leave `false` during pilot. |
| `PilotEnabled` | Allows only explicitly listed pilot companies and organizers. |
| `EmergencyDisabled` | Global fail-closed switch; prevents joins and media starts. |
| `PilotCompanyIds` / `PilotUserIds` | Approved non-demo companies and named organizers. |
| `AllowedTenantIds` | Microsoft Entra tenants accepted by SSO and rollout policy. |
| `MaxActiveCallsGlobal` | Cost/capacity circuit breaker across the deployment. |
| `MonthlyCostUsed` / `MonthlyCostLimit` / `CostCurrency` | Billing pipeline input and fail-closed monthly budget gate. A zero/missing limit blocks live side effects. |
| `MinimumPackageVersion` / `InstalledPackageVersion` | Compatibility range; installed must be at least the minimum and no newer than the deployed package. |
| `AppInstallationAttested` | Tenant administrator has installed the approved package. |
| `AutomatedEvidenceApproved` / `LiveUatApproved` | Explicit release evidence gates. |
| `LiveUatOwner`, `LiveUatCompletedUtc`, `LiveUatEvidenceReference` | Non-customer UAT provenance. |
| `MediaCertificateExpiresUtc` | Operator-visible certificate expiry evidence. |
| `InstallUrl` | Approved Teams app installation/deep link shown to organizers. |

The system-admin page is `/system/admin/teams-presenter?companyId={companyId}`. It shows required versus granted permissions, package state, policy/callback/media checks, certificate evidence, rollout gates, and remediation links. The downloadable package endpoint and tenant-changing actions require the platform-administrator policy.

## Install and tenant approval

1. Build or download the versioned Teams package and verify its SHA-256 using `docs/teams-presenter-app-package.md`.
2. In Teams admin center, upload and approve that exact package. Apply app setup/permission policies only to pilot users.
3. In Virtual Company, associate the company with the exact Entra tenant.
4. Run admin consent and verify that granted application permissions exactly match the required list; excess permission blocks readiness.
5. Verify calling and meeting-app policies in Microsoft 365. Record policy attestation only after checking the effective pilot-user policy.
6. Deploy Prompt 6 infrastructure, verify callback authentication/certificate expiry, and keep the media host out of drain mode.
7. Authorize the scoped first live test described below, then complete `docs/teams-presenter-live-uat-matrix.md` in an isolated non-demo tenant without customer data or raw audio.
8. Set pilot allowlists/evidence, then `PilotEnabled=true`. Keep `ProductionEnabled=false` until formal release approval.

## Salesperson walkthrough

1. Schedule a Teams meeting from the lead page. The invitation identifies Alex as AI, describes its limited access, states that audio requires consent, and says the organizer can pause or remove it.
2. Open the meeting and add/open the Alex meeting app. If Teams asks, install only the approved package/version.
3. Open Alex's private side panel and refresh state. Resolve any displayed tenant or rollout reason first.
4. Record the approved meeting-media consent and tell attendees Alex is an AI assistant.
5. Select **Request presenter to join**. If **Waiting in lobby**, open **People**, find Alex, select **Admit**, and wait for provider-confirmed **Connected** state.
6. Select **Share to meeting**, then confirm **Start sharing** in Teams. If blocked, verify the meeting-app policy and organizer role. Wait for the exact Blazor stage render acknowledgement.
7. Select **Start presenter audio** only after consent. A persistent indicator states when Alex can hear or speak.
8. Choose **Manual** (default), **Assisted**, or **Autonomous**. Assisted proposals need approval. Autonomous transitions remain in the active deck and narration waits for render acknowledgement.
9. Select **Pause presenter · Take control** at any time. It switches to manual, preempts stale narration, and keeps human commands authoritative.
10. Select **Mute / stop Alex audio** to end media. Use native Teams **Stop presenting** for the stage. Select **Remove / leave** and wait for provider confirmation.
11. **Revoke meeting consent** stops media immediately. Existing notes/transcripts remain governed by the displayed retention date and approved privacy policy.

## Support, incident response, and rollback

- For an ambiguous join/leave, reconcile once; never blindly retry. If ambiguity remains, remove Alex in Teams and escalate with correlation ID, safe provider reference, timestamps, package version, host instance, and reason code—never a token, raw callback, customer content, or audio.
- For provider/callback/media failure, stop audio, use typed/browser fallback, and follow `docs/teams-call-control-runbook.md` plus `docs/runbooks/teams-media-azure-runtime.md`.
- For suspected tenant isolation or consent failure, activate emergency disable, stop active calls, preserve sanitized evidence, and treat it as a release blocker.
- Rotate credentials/certificates using the identity and Azure runbooks, update expiry evidence, and re-run readiness.
- To uninstall: disable the company first, remove the app from Teams policies, uninstall it, revoke Entra consent, record revocation in Virtual Company, remove allowlists, and keep rollout gates off.
- Roll back to typed/browser-hosted workflows. Deck/meeting records stay company-scoped and retention/deletion continues under recorded policy.

## Privacy and evidence

Meeting audio starts only after explicit meeting consent and a separate organizer start command. Revocation terminates the bridge. Logs contain safe identifiers/reason codes, not raw audio or tokens. UAT uses synthetic content and records client, role, policy, build, date, owner, and sanitized evidence.

## Scoped first live test and marketing presenter

`TeamsPresenter:FirstUatEnabled` defaults to false. To gather the first live evidence, enable that gate for the isolated deployment alongside `PilotEnabled`; leave `ProductionEnabled=false` and `LiveUatApproved=false`. Require the usual exact tenant/company/organizer allowlists, verified tenant consent and permissions, installed compatible package, approved automated evidence, budget, healthy approved media route, and explicit meeting consent. None of these gates is bypassed.

1. Prepare a scheduled meeting and its existing approved presentation deck.
2. On the meeting preparation page, use **Meeting presenter** to explicitly select the active same-company Sales or Marketing assistant. The deck's author/plan agent remains independent of the live speaker.
3. The list contains only assistants with currently available meeting tools in the shared effective capability catalog. Review the assistant's existing tool configuration and action scopes; do not grant broad authority simply to make it selectable. The registered tools are `presentation.get_current_slide`, `presentation.search_slides`, `presentation.next`, `presentation.previous`, `presentation.goto`, `presentation.pause`, and `presentation.resume`. Read tools require read authority; mutations require execute authority and the existing meeting control policy. These tools currently use the shared sales/prospecting scope registration. For a Marketing assistant, inspect the effective catalog's chosen scope (currently prospecting when no departmental match exists) and explicitly approve only the required tool/action/scope combinations through the existing agent administration. Tools with approval-required or denied state are not exposed to the realtime model. Sales grounded Q&A is included only when its effective capability is available.
4. As platform administrator, open `/system/admin/teams-presenter?companyId=<company-id>` and use **First live test**. Enter the exact organizer user ID, meeting session ID, reason, and 1–60 minute duration. The company and tenant come from the verified company registration. Grant and revoke require the registration's current version and record audit events with the actor and scope.
5. Reopen the authorized meeting's Teams side panel. Its scoped rollout state may show `teams_presenter.first_uat_allowed` while the company-wide release readiness still requires completed live UAT. Only one active first-test meeting is allowed; an active call does not count itself against its own capacity check.
6. Request the presenter to join, admit the configured bot in Teams if necessary, share the stage, explicitly authorize audio, and perform the live matrix. The installed bot/app may still be named Alex; selecting Maya changes the assistant's identity/profile and authorized tools, not the tenant's package branding.
7. Expiry, **Revoke test**, tenant/consent disable, and emergency policy deny subsequent authorization. Active first-test media is rechecked and terminated through the existing owner-host workflow. A call retains its original grant provenance: a later grant or later release approval cannot revive that expired/revoked test call.
8. Record actual evidence separately. Neither granting, revoking, nor finishing a test sets `LiveUatApproved`. Ordinary pilot and production admission continue to require completed UAT. Disable `FirstUatEnabled` after this evidence-gathering phase.

Platform API equivalents are GET/POST `/api/platform/teams-presenter/companies/{companyId}/first-uat` and POST `.../first-uat/revoke`. Authorization body:
```json
{
  "organizerUserId": "<organizer-guid>",
  "meetingSessionId": "<meeting-guid>",
  "durationMinutes": 30,
  "reason": "Isolated marketing speech and stage acceptance test",
  "expectedVersion": 1
}
```
Use the version returned by GET; the example value is not a reusable version. Revoke sends only `expectedVersion`. Presenter GET/PUT is `/api/sales/meeting-sessions/{sessionId}/presenter` under the authenticated company context; PUT accepts `agentId` and the meeting's `expectedVersion`. The server validates membership, organizer ownership, active status, company, available tools, version, and absence of active calls.

Presenter selection never silently changes a running voice session. Runtime instructions use the selected agent's shared communication profile and role brief. Tool authority is rechecked during Teams tool execution. Typed controls, approved-deck rendering, human takeover, and stage acknowledgement remain authoritative.
