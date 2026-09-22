# Prompt 12 guided Microsoft 365 connection UAT

## Product profile

```yaml
product: Virtual Company
type: web
revision: working tree on 2026-09-21
launch: .\client.ps1 -Port 5062 (documented local launcher)
environment: local Development; deterministic bUnit/API fixtures; no live Microsoft tenant credentials
roles:
  - name: Company administrator
    access: deterministic authenticated integration fixture
evidence:
  reference: docs/design/references/microsoft-365-guided-connection-reference.png
  component: tests/VirtualCompany.Web.Tests/DocumentRepositorySettingsComponentTests.cs
  client: tests/VirtualCompany.Web.Tests/DocumentRepositoryApiClientTests.cs
  surface: tests/VirtualCompany.Web.Tests/DocumentRepositorySettingsSurfaceTests.cs
flows:
  - id: FLOW-012-01
    name: Resume authorization, choose SharePoint/root, select agent, review and connect
    role: Company administrator
    preconditions: Authorized opaque onboarding session and deterministic eligible source fixture
    outcome: Server-derived review is confirmed and durable provisioning reaches connected
  - id: FLOW-012-02
    name: Preserve customer-managed advanced setup and connected repository operations
    role: Company administrator
    preconditions: Existing customer-managed or connected repository
    outcome: Advanced setup remains available while health, sync, recovery and disconnect remain unchanged
```

## Reference comparison

The implementation follows the generated reference’s hierarchy: canonical Settings page, eight-step progress, two-column review/next-actions layout, read-only recommendation, empty-agent warning, deliberate confirmation, pending provisioning state, and collapsed advanced path. At widths below 680px the step labels collapse to `Step n of 8`, cards stack, review rows reflow, and action targets remain at least 44px. The design rules remain authoritative over the image.

## Evidence packet

### FLOW-012-01 — Guided connection

Revision: working tree on 2026-09-21; environment: deterministic component/API fixtures; role: company administrator; viewports: responsive CSS contract for desktop and ≤680px.

Steps:
1. Resume `/settings/document-repositories` with an opaque onboarding handle.
2. Choose SharePoint, search a site, choose a library and folder.
3. Keep read-only, select Nina, request the server review, confirm and connect.
4. Observe durable connected state and queued-import explanation.

Expected: all eight milestones are represented; no Microsoft implementation identifier appears; review and finalization use backend state; the selected agent is exact; retry cannot duplicate provider work.

Observed: the bUnit journey completed from authorized session through connected provisioning, asserted the exact agent list and absence of fixture provider IDs, and the typed-client/API suites verified the opaque endpoints and callback redirect.

Artifacts: `docs/design/references/microsoft-365-guided-connection-reference.png`; `tests/VirtualCompany.Web.Tests/DocumentRepositorySettingsComponentTests.cs`.

Result: pass using the strongest safe substitute. Native browser capture is blocked because the Computer Use runtime failed twice during initialization with `windows sandbox failed: helper_unknown_error: apply deny-read ACLs`; no window was opened and no UI action was attempted.

### FLOW-012-02 — Existing management and advanced fallback

Expected: the legacy customer-managed editor is not primary, existing connection cards retain health/recovery/disconnect behavior, and non-administrators see no management actions.

Observed: component and surface tests pass for connected health, partial-failure recovery, update conflict, disconnect confirmation, forbidden access, advanced disclosure, reference persistence and responsive rules.

Result: pass using component/surface verification; browser visual capture remains blocked by the runtime initialization failure above.

## Issue ledger

| ID | Severity | Flow | Type | Summary | Evidence | Acceptance / regression | Status |
|---|---|---|---|---|---|---|---|
| UAT-012-01 | P1 | FLOW-012-01 | defect | Technical-ID form was the primary connection path | guided component and component journey test | Primary action opens guided Microsoft 365 setup and normal flow contains no technical identifiers | verified (component/API substitute) |
| UAT-012-02 | P1 | FLOW-012-01 | defect | Web client stopped before access review and durable finalization | typed-client and callback/API tests | Review, finalize, poll, retry and cleanup use production endpoints and opaque session state | verified (component/API substitute) |
| UAT-012-03 | P2 | FLOW-012-02 | usability | Customer-managed setup competed with normal setup | settings surface test | Legacy setup is labelled advanced with its operational burden; editing remains compatible | verified (component substitute) |
| UAT-012-04 | P2 | FLOW-012-01 | verification | Desktop and narrow browser screenshots could not be captured | Computer Use initialization error above | Re-run desktop and 390px browser capture when the Windows ACL sandbox issue is resolved | blocked |
| UAT-012-05 | P1 | FLOW-012-01 | defect | Local Development could not begin authorization because the onboarding client secret had no supported path from user secrets into the encrypted platform secret store | browser comment on `/settings/document-repositories`; focused API regression and local endpoint replay | Development seeds only the explicitly configured credential reference into the encrypted local store; production remains Key Vault-backed and missing configuration still fails closed | verified; begin reached `login.microsoftonline.com` without an immediate redirect mismatch, status became `awaiting_authorization`, and cleanup succeeded |
| UAT-012-06 | P1 | FLOW-012-01 | defect | Microsoft rejected the authorization request with `AADSTS901001` because `admin_consent` is not a supported v2 `prompt` value | browser comment on Microsoft sign-in; official authorization-code documentation | Generate `prompt=consent`, preserve administrator-required delegated permissions, and request every scope exactly once | verified by focused URL contract test and live authorization-page preflight |
| UAT-012-07 | P2 | FLOW-012-01 | feedback | A transient/stale API 404 appeared as raw “Not Found / Reference” text in the wizard | browser comment on local settings page | The start step explains that setup is restarting and tells the administrator to refresh and retry | verified by source/build check |
| UAT-012-08 | P1 | FLOW-012-01 | deployment | Microsoft accepted sign-in and consent but rejected the local callback with `AADSTS50011` because `http://localhost:5301/api/document-repositories/microsoft/callback` is not registered on client `caaad960-d9ee-4ef4-a490-6b1d8cf062ba` | browser comment on Microsoft consent page; Azure CLI inspection | Register the exact callback on the owning app, or explicitly approve a replacement multi-tenant local-development app with the documented Graph permissions and a local user-secret credential; replay authorization through the callback | blocked: the authenticated Azure tenant does not own or contain the configured application, and creating a replacement app requires explicit approval |

### UAT-012-08 remediation — 2026-09-22

The user explicitly approved a replacement development application. Created `Virtual Company Document Access Local` (client ID `20c3f6dd-f8bc-442c-abfc-c639beea4fd2`) in tenant `8acf7923-17e5-492d-a8c6-756ca23599af` with audience `AzureADMultipleOrgs`, delegated `Files.ReadWrite` and `Sites.Read.All`, and application `Files.SelectedOperations.Selected`. These are configured permission requests; tenant consent and selected-resource access remain separate steps.

The Web redirect URI is exactly `http://localhost:5301/api/document-repositories/microsoft/callback`. The local API user secrets now reference the new application and its credential, expiring 2027-09-22. The API was rebuilt and restarted successfully. A live onboarding request used the new client ID, and its decoded callback matched the registration exactly. The Microsoft sign-in page returned HTTP 200 with no immediate AADSTS error; the temporary test session was cancelled.

UAT-012-08 status: configuration repaired and verified against Entra; authenticated consent and callback replay remain pending the user's sign-in. The original approval blocker in the ledger above is resolved. Initial sign-in-page HTTP success alone does not prove post-consent callback acceptance.

### UAT-012-09 — callback returns to the web host

The callback redirected to a relative settings path on API port 5301 instead of the web host on port 5062. Added deployment-owned `Microsoft365DocumentOnboarding:WebOrigin`; Development configures `http://localhost:5062`, while an empty value preserves same-origin deployments. Configured origins must be HTTPS (or HTTP loopback), with no path, credentials, query, or fragment. Success and failure redirects preserve the existing validated return path, company query, and opaque session handle.

The saved user session reported `insufficient_administrator_authority`. The replacement Entra app had null `groupMembershipClaims`; it is now configured to `DirectoryRole` so the existing administrator check can inspect directory-role claims. This does not assign roles or grant additional API permissions. Actual user authority remains subject to a fresh signed-in callback.

Verification: 12 focused onboarding tests passed, including successful callbacks on same-origin, separate HTTPS web origin, and localhost port 5062, plus failed consent returning to the web origin with company/session context intact.

### UAT-012-10 / UAT-012-11 — session binding and granted scopes

The settings page passed the literal string `Microsoft365Session` to the wizard rather than the query parameter value. This triggered a nonexistent-session lookup both on first opening setup and on callback return. Fixed the Razor expression binding. The component fixture now rejects incorrect handles; tests verify initial entry makes no status request and callback resume passes the exact opaque handle and advances to source selection. Genuine missing sessions now show an actionable restart message.

The real returned session was readable through the API and had failed with `consent_denied` from the granted-scope comparison. That comparison normalized required scopes but not returned scopes. Graph-qualified and short scopes are now compared consistently; other resource origins and missing required permissions remain rejected.

Verification: 15 web repository tests and 17 API onboarding tests passed. Both services rebuilt and restarted. Live HTTP rendering of the user's returned session now resolves the actual saved consent failure, with no raw Not Found reference; clean settings and API liveness return HTTP 200. The old failed session cannot be replayed; a fresh Microsoft sign-in is required to verify actual provider consent and proceed to folder selection. Evidence uses component/API/HTTP substitutes for the unavailable browser automation.

### UAT-012-12 — authorized source options disabled

The real authorized session returned both source kinds as unavailable. Callback validation normalized Graph-qualified scope names, but discovery's separate parser did not. Callback and all discovery checks now share the same Graph scope normalization, including source-kind availability, OneDrive discovery, SharePoint discovery, browsing, and folder selection. Refresh requests also reuse the de-duplicated setup scopes.

Regression coverage extends the discovery-to-finalization journey across short, Graph-qualified, and mixed scope formats, including a SharePoint search request and OneDrive folder selection. Provider consent and real resource grants remain required; no permission checks are bypassed.

Verified: 19 onboarding tests passed and the API rebuild succeeded. After restarting the API, the user's existing session returned both OneDrive and SharePoint with `isAvailable: true` and no unavailable reason. Live web rendering returned HTTP 200, displayed both source choices, and contained no missing-scope warning. No new sign-in or permission grant was needed for this correction.

### UAT-012-13 — OneDrive Continue did not open the source

At the source-list stage, Continue was permanently disabled even when the single OneDrive was available. Continue now invokes the same source-opening action as the row when exactly one source is present. Rows include visible Browse folders / Browse libraries text instead of relying on an icon alone. Folder selection remains explicit through Use this folder.

A read-only request against the user's actual session returned one OneDrive, zero eligible subfolders, and no next page. Empty folder browsing now explains how to create a folder in the source and offers Refresh folders. No folders or remote permissions were created by this fix.

Verification: 16 focused web repository tests passed, including the new OneDrive Continue-to-folder-to-access journey. Live provider discovery confirmed the source and empty folder result; interactive provider completion remains user-operated.

### UAT-012-14 — provisioning failure hidden behind stale empty state

The submitted OneDrive connection was persisted as pending_validation. Its provisioning operation entered reconciliation_required with invalid_credentials. A read-only app-only token check in the document tenant showed no application roles; reading the exact selected folder returned Graph 401. Delegated setup consent does not supply the required Files.SelectedOperations.Selected application consent. No broader permissions or remote cleanup were performed.

The wizard previously stopped polling after six seconds, leaving Verifying Microsoft access visible after failure. It now monitors queued/provisioning operations until an actionable status, cancels monitoring when removed, and notifies the parent on completion to reload repositories. An open setup no longer displays the contradictory No document repository connected empty-state message.

Verification: 17 focused web repository tests passed, including resumed provisioning automatically reaching connected and notifying the parent once. Tenant consent and a successful live import remain blocked: Azure CLI access to the document tenant requires interactive administrator authentication (AADSTS530035 security defaults). These checks use component/API/HTTP evidence, not a completed browser journey.

## Verification results

- `dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~DocumentRepository"`: 14 passed.
- `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~DocumentRepositoryMicrosoftOnboardingTests"`: 10 passed.
- `dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-restore`: passed; only pre-existing warnings outside this feature remained after the new nullable warning was removed.
- Live Microsoft success remains unverified because no tenant credentials or selected-resource grant were supplied.
