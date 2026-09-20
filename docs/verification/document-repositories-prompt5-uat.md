# Document repositories Prompt 5 UAT

## Product profile

```yaml
product: Virtual Company
type: web
revision: working tree on 2026-09-19
launch: dotnet run --project src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-build --no-launch-profile --urls http://localhost:5062
environment: local Development; API intentionally unavailable for the live unavailable-state check
roles:
  - name: Company administrator
    access: deterministic component fixture for management states
  - name: Read-only member
    access: deterministic forbidden response fixture
evidence:
  reference: docs/design/references/document-repositories-settings-reference.png
  automated: tests/VirtualCompany.Web.Tests/DocumentRepositorySettingsComponentTests.cs
flows:
  - id: REPO-UI-001
    name: View a connected repository
  - id: REPO-UI-002
    name: Recover from a partial failure
  - id: REPO-UI-003
    name: Disconnect a repository
  - id: REPO-UI-004
    name: Read-only member opens settings
  - id: REPO-UI-005
    name: Backend unavailable at narrow width
```

## Evidence packets

### REPO-UI-001 — View a connected repository

Environment: bUnit component fixture; role: company administrator.

Expected: source, read-only/connection state, validation and synchronization freshness, indexed/processing/failed counts, and named agent grants are visible.

Observed: all fields render from the typed API projection; no simulated page data is used. Result: **pass**.

### REPO-UI-002 — Recover from a partial failure

Environment: bUnit component fixture; role: company administrator.

Expected: failed content is described as unavailable to agents and the recovery action requests a server-authorized full synchronization.

Observed: the attention panel invokes `StartSynchronizationAsync(..., forceFullReconciliation: true)` and preserves the failed count. Result: **pass**.

### REPO-UI-003 — Disconnect a repository

Environment: bUnit component fixture; role: company administrator.

Expected: disconnect requires explicit confirmation, explains that remote files and shared credentials remain unchanged, invokes the versioned server command, and refreshes status.

Observed: confirmation is required and the refreshed card renders `Disconnected`. Result: **pass**.

### REPO-UI-004 — Read-only member opens settings

Environment: bUnit component fixture; role: read-only member represented by the API's forbidden response.

Expected: no repository identity or management action leaks; the page explains that administrator access is required.

Observed: the permission state renders with no repository cards or management controls. Result: **pass**.

### REPO-UI-005 — Backend unavailable at narrow width

Environment: local Development Web host at `http://localhost:5062`; API port 5301 unavailable; intended viewport 390×844.

Expected: the route responds and the page reports that it cannot reach the configured backend.

Observed: the route returned HTTP 200 and contained both `Document repositories` and the backend-unavailable message. Interactive screenshot inspection was blocked because the Windows browser-control and image-view helpers failed while applying their sandbox read ACLs. A headless browser reached the route and loaded its static assets, but did not produce a usable final capture. Result: **blocked for screenshot comparison; HTTP/render behavior verified**.

## Reference comparison

The implementation follows the generated reference's hierarchy: Settings header and primary connect action, publication-boundary banner, dominant repository cards, compact health counts, explicit agent chips, attention rail, access explanation, and stacked narrow-layout actions. Responsive CSS changes the two-column layout, form grid, freshness/count panels, buttons, and confirmation row to single-column touch targets at 980 px and 680 px breakpoints. A visual pixel-level comparison remains blocked by the local browser-control ACL failure described above.

## Issue ledger

| ID | Severity | Flow | Type | Summary | Evidence | Acceptance / regression | Status |
|---|---|---|---|---|---|---|---|
| UAT-REPO-001 | P2 | REPO-UI-005 | environment | Interactive browser/image inspection cannot initialize because the Windows sandbox helper fails while applying read ACLs. | REPO-UI-005 | Re-run desktop and 390×844 captures against the Development host when the helper is available; compare spacing, clipping, hierarchy, and focus reachability with the stored reference. | blocked |

No open product defect was found in the deterministic success, partial-failure, disconnect, or permission flows.

## Runtime follow-up — 2026-09-20

### REPO-UI-006 — Open settings against migrated SQL Server

Environment: local Development Web host at `http://localhost:5062`; SQL Server in Docker; role: company administrator.

Expected: the repository list loads and renders the empty or connected state without a server-error banner.

Observed before repair: the page displayed a server-error banner with reference `d7c3165975e54aa8b3020514c72c6c7e`. The correlated API log showed SQL Server error 207 for missing column `writable_folder_item_id`. The EF model contained the nullable writable-folder boundary, but no applied migration had created the column. Result: **fail**.

Repair: added forward migration `20260920123437_RepairDocumentRepositoryWritableFolderColumn`. Startup applied it successfully and the API reached `Application started`; focused API and Web tests both passed 12/12. Interactive page replay remains unavailable because the browser-control helper still fails while applying sandbox read ACLs. Result: **verified by real SQL Server migration/startup and focused regression tests; browser replay blocked**.

Issue: `DOCREP-UAT-001` (P1, defect) is verified for the schema/runtime boundary. Acceptance evidence is the applied forward migration, successful API startup, and the two focused 12-test suites. Visual closure remains covered by blocked environment issue `UAT-REPO-001`.
## Automated verification

- Document-repository Web tests: **11 passed, 0 failed**.
- Focused API and Microsoft Graph repository tests: **22 passed, 0 failed**.
- `git diff --check`: passed (line-ending notices only).
- Solution build: blocked outside the Prompt 5 boundary by existing .NET 9/10 dependency conflicts in `VirtualCompany.Infrastructure.Platform.Tests`, `VirtualCompany.Infrastructure.Mailbox.Tests`, and `VirtualCompany.Finance.Tests` (seven `CS1705` errors). The affected Prompt 5 projects compile through the focused test runs above.
