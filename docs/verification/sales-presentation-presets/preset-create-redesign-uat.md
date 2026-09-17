# Presentation preset creation redesign UAT

Date: 2026-09-14

## Product profile

- Product: Virtual Company Sales presentation presets
- Flow: create a reusable preset draft
- Route: `/app/sales/presentation-presets`
- Environment: fresh local Web build on port 5066
- Browser: Google Chrome headless through the DevTools protocol
- Viewports: 1440×1100 and 390×844
- Reference: `docs/design/references/sales-presentation-preset-create-reference-v2.png`

## Evidence

- `preset-create-before.png` — user-reported baseline with browser-default controls, collapsed labels, and no usable form hierarchy.
- `preset-create-redesign-desktop.png` — rebuilt modal opened through the real **New preset** action at desktop width.
- `preset-create-redesign-mobile.png` — the same live modal at mobile width.

## Issue ledger

| ID | Severity | Flow | Type | Finding | Acceptance / regression | Status |
|---|---|---|---|---|---|---|
| PRESET-UI-001 | P1 | Create preset | defect | Parent-page scoped CSS could not style markup rendered by the child editor component, leaving browser-default controls and broken alignment. | The editor owns scoped styles; labels, controls, sections, choices, and actions render consistently in both modal and detail contexts. | Verified |
| PRESET-UI-002 | P1 | Create preset | functional | New forms defaulted to `guided`, while the domain accepts only `manual` or `assisted`. | Creation defaults to `assisted`; the invalid option is absent; component regression coverage asserts both conditions. | Verified |
| PRESET-UI-003 | P2 | Create preset | usability | The form had no meaningful grouping, helper hierarchy, or explicit cancel action. | Identity, defaults, supported situations, and presenter behavior are distinct sections with helper copy and a persistent Cancel/Create footer. | Verified |
| PRESET-UI-004 | P2 | Create preset | responsive | The previous form did not provide a production mobile layout. | At 390 px the modal becomes a full-height sheet, fields and choice cards stack, and actions remain reachable in a sticky footer. | Verified |

## Verification

- The real route loaded and exposed the **New preset** entry point.
- Chrome opened the modal through that action before both screenshots were captured.
- Visual comparison confirms the design-system hierarchy, spacing, slate/blue palette, consistent 44 px controls, reserved validation space, and restrained card treatment.
- `PresentationPresetComponentsTests`: 6 passed, including creation hierarchy, valid default mode, field bounds, read-only ownership, situation cards, and cancel behavior.
- `VirtualCompany.Web` build: succeeded with 0 warnings and 0 errors when run serially.
- Windows interactive browser control was unavailable because the host ACL sandbox helper exited during initialization; the bounded DevTools browser run provided real-surface evidence without altering the user's existing browser session.

Result: all reported in-scope production-readiness defects are fixed and verified at desktop and mobile widths.
