# Prompt 4 UAT — human browser meetings

Revision: working tree based on `77ec3c8d`, 9 September 2026. Product: Virtual Company Blazor Web App. Roles: organizer and room-scoped guest. Scope: invitation/prejoin, admission, human media controls, reconnect/removal, leave/end, mobile and keyboard use.

## Design and evidence boundaries

The [reference prompt](../../design/references/browser-human-room-reference-prompt.md) was written before generation. The built-in image generator produced the [reference](../../design/references/browser-human-room-reference.png) before UI implementation. White cards, the minimal guest header, blue primary controls, dark participant tiles, separate private host panel, and stacked mobile layout follow that reference and `docs/design.md`. Fictional email addresses in the generated reference were not introduced into the application.

Actual local Web application UAT used a tracked `dotnet` host on `127.0.0.1:5394`, the existing local API base address, and headless Chrome and Edge. Only synthetic browser devices were permitted; no real microphone, customer invitation, LiveKit project, Teams call or mailbox was used. The tracked Web processes (72660 and 67416, the first replaced after a verified fix) were stopped after verification. The existing API process was not changed.

Guest checks in the running app covered missing invitations, opening a same-page fragment link, immediate fragment removal, permission denial with actionable help, granting synthetic microphone/camera access, a speaker-test gesture, keyboard focus, mobile overflow, and stopping local tracks. Both browsers reported no unhandled page errors. Speaker audibility to a human and real provider autoplay remain unverified.

Organizer/admission and admitted-room visuals use production Razor rendered by bUnit with isolated test contracts and the production JS tile renderer with synthetic participant fixtures. These verify component behavior and layout, not an authenticated host/provider round trip or a live call. Seven desktop/mobile captures have no horizontal overflow; representative captures were inspected against the reference. The fixture wrapper uses the actual application, Bootstrap and scoped styles, UTF-8, and blocks non-local browser network requests.

## Evidence packets

| Flow | Steps and observed outcome | Evidence |
| --- | --- | --- |
| Guest entry | Open room without a capability; open invitation on that same page. Missing-link guidance is visible; same-page invitation enables prejoin and removes the fragment. No internal navigation renders. | [Chrome results](prompt4-live-browser-checks.json), [Edge results](prompt4-live-edge-browser-checks.json), bootstrap JS regression |
| Devices | Deny access, then grant synthetic devices and retry. Actionable permission help appears; audio/video preview tracks become live; explicit disconnect ends both tracks. | [Chrome denial](prompt4-live-guest-denied-mobile.png), [Chrome preview](prompt4-live-guest-preview-mobile.png), [Edge preview](prompt4-live-edge-guest-preview-mobile.png) |
| Host admission | Render a room with a waiting guest; select Admit. Typed request carries current version and command ID; refreshed participant exposes Remove. Denied guest fixtures receive no media connection and clear the saved credential. | Web tests; [host lobby](prompt4-host-lobby-1440.png) |
| Connected layout | Render the admitted component and two synthetic participants through the real tile renderer. Desktop has a separate host panel; mobile stacks cards; no overflow. | [desktop room](prompt4-host-live-1440.png), [mobile room](prompt4-host-live-390.png), [browser checks](prompt4-browser-checks.json) |
| Media lifecycle | Controlled SDK/device doubles exercise duplicate events, late grants/connects, control loss, track cleanup, same-identity tab locks, muted camera changes and cancelled sharing. | `tests/VirtualCompany.Web.Tests/js/sales-human-room.test.mjs` |

## Issue ledger

| ID | Severity | Finding | Acceptance / result |
| --- | --- | --- | --- |
| ROOM-001 | P1 | Human room entry, admission and private/public surfaces were missing | Implemented; Web contract/component and API authorization tests pass. Live multi-human acceptance remains gated. |
| ROOM-002 | P1 | Media cleanup/reconnect required bounded ownership | JS tests verify no duplicate attachment and cleanup of late/disposed tracks. Real guest-page preview cleanup passes in Chrome/Edge. |
| ROOM-003 | P1 | Control or admission loss must not leave unbounded local publication | Server status rechecks every three seconds; independent JS watchdog stops tracks after 15 seconds without a successful control heartbeat. Unauthorized/expired responses stop media immediately on receipt. Deterministic tests pass; real remote removal remains unrun. |
| ROOM-004 | P2 | Oversized empty preview and hidden prejoin device selectors | Bounded preview height and initially expanded device settings implemented; desktop/mobile recaptures pass overflow checks. |
| ROOM-005 | P1 | Opening an invitation on the current room page only changed the hash | Reproduced in the running app. Added early hash-change consumption and component notification; original Chrome flow and Edge equivalent pass. JS regression retained. |
| ROOM-006 | P1 | Six polling participants would consume the entire mutation rate budget | Read-only polling has a separate bounded policy; API regression verifies polling cannot consume the 120/min command allowance. |
| ROOM-LIVE | P1 | Three-human provider acceptance | Blocked: an authorized LiveKit project, permitted participants and intended runtime are absent. |

## Reproduction

Run from the repository root. Use the existing .NET test projects with `DOTNET_PROCESSOR_COUNT=2`, `-m:1` and `-p:UseSharedCompilation=false` if the local compiler needs its documented bounded configuration. Set `VC_BROWSER_UAT_DIRECTORY` to the absolute `artifacts/browser-human-room-uat` directory when running `SalesHumanRoomTests`; it exports sanitized markup.

- JS: `node --test tests/VirtualCompany.Web.Tests/js/sales-human-room.test.mjs`.
- Component visuals: make Playwright available through the installed runtime or `NODE_PATH`, then run `node tests/scripts/Capture-BrowserHumanRoom.cjs` after the Debug Web test build. It starts/closes its own isolated loopback fixture server and browser.
- Actual guest page: check port 5394 first, start one tracked local Web host following `src/VirtualCompany.Web/AGENTS.md`, then run `node tests/scripts/Verify-BrowserHumanRoom.cjs`. The test uses synthetic devices, never connects to LiveKit, and does not redeem an invitation. Stop only the recorded Web PID afterwards.
- Set `VC_UAT_BROWSER_EXECUTABLE` to an installed Edge executable to repeat the same verification there. Default is installed Chrome.

## Remaining live acceptance

Still NOT RUN: a host plus two guests hearing/seeing each other, actual screen-share delivery, Cloud token refresh/revocation and provider disconnection, provider outage/reconnect, Safari/iOS, a corporate-network TURN path, real device/speaker/echo combinations, and the preceding 60-minute/Azure feasibility gates. These need a permitted LiveKit test environment and participants. No benchmark, rollout approval or production-readiness claim follows from synthetic-device or component evidence.
