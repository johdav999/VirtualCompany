# Prompt 3 UAT record — 2026-09-09

Scope: conferencing selection, organizer link and browser preparation, approved scheduling/change recovery, and Teams preservation. Reference: [design reference](../../design/references/browser-meeting-scheduling-reference.png), generated before changing the UI from the [stored prompt](../../design/references/browser-meeting-scheduling-reference-prompt.md).

## Environment and evidence boundary

Rendered production Razor components from bUnit fixtures were wrapped with the actual application and scoped component styles, served on an isolated loopback HTTP server and captured with headless Chrome through Playwright. Network requests were restricted to that server. This is isolated component visual verification, not an authenticated running application. The desktop computer-use runtime could not initialize because of the environment's filesystem ACL error; Playwright provided the visual fallback. The temporary HTTP server and browser were closed after capture. No customer data or invitation secrets are included.

## Results

| Flow | Evidence | Result |
| --- | --- | --- |
| Select conferencing separately from calendar | [Desktop picker](prompt3-picker-desktop.png), [mobile picker](prompt3-picker-mobile.png); picker availability and route tests | Browser option, consent notice and readiness state render; no horizontal overflow at desktop or 390px mobile width. |
| Prepare browser presentation and retrieve a link | [Preparation](prompt3-preparation-desktop.png); preparation/navigation and typed API client tests | Browser guidance and organizer copy control render. Browser preparation does not navigate to the Teams side panel; Teams fixtures retain their original route. |
| Approve, retry, reschedule and cancel | Scheduling/provider tests, authorization and tenant checks, SQL migration tests | Browser event requests disable calendar conferencing; repeated commands reuse the invitation/room, ambiguous outcomes require inspection, cancellation revokes admission, and known failed approved changes can be queued for retry. |

The generated reference guided the white-card layout, blue selection controls and readable notice placement. Visual inspection found no horizontal overflow in the three captures. The isolated preparation fixture has a textarea serialization limitation; it cannot establish interactive form editing behavior. Actual clipboard permission, authentication, a full calendar/mailbox round trip and human-room entry were not exercised. Prompt 4 owns the browser meeting page.

## Remaining live checks

Run an explicitly authorized organizer/customer flow against connected test calendar and mailbox accounts, then verify the delivered fragment link in the actual Prompt 4 room UI. Exercise clipboard denial, approval/retry feedback and refresh/reconnect in the authenticated application. Verify real LiveKit provisioning, admission revocation and disconnection under the preceding provider/runtime gates. Keep browser feature flags disabled pending these checks. No live-readiness claim is made by this record.

## Session profile and issue ledger

Product: Virtual Company web application. Build: uncommitted Prompt 3 implementation, Release test logs recorded in `prompt3-check-results.json`. Role: synthetic organizer fixture scoped to a test company. Launch/capture procedure: `artifacts/browser-scheduling-uat/capture.cjs` against isolated rendered markup and application CSS. Viewports: picker 1024x640 and 390x650; preparation 1440x2550.

| ID | Severity | Flow | Finding | Status |
| --- | --- | --- | --- | --- |
| P3-UAT-01 | P2 | Preparation visual capture | Initial fixture wrapper omitted compiled scoped styles. Included the actual scoped stylesheet bundle and recaptured. | Resolved in evidence harness; no production defect established. |
| P3-UAT-02 | P1 | Live end-to-end delivery | Connected accounts, authorized recipients and the Prompt 4 human room experience are unavailable for this check. | Live acceptance blocked; browser route remains disabled pending release gate. |
| P3-UAT-03 | P2 | Interactive form/clipboard | Static rendered-component capture cannot establish editing, clipboard permissions or authenticated refresh behavior. | Not run; requires authenticated application UAT. |
