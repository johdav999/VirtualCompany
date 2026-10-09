# P29 visual review

Reference: `docs/design/references/briefing-cadence-p29.png`, generated with built-in ImageGen from the adjacent written brief and inspected before implementation. The reference establishes labelled controls, seven cadence rows, distinct urgent rules, blue primary actions, pale canvas, white cards, preview/freshness/audit hierarchy and absence routing.

Implemented evidence: `schedule-desktop.png`, `absence-desktop.png`, `absence-narrow.png`, `preview-desktop.png`, `delivery-desktop.png`, `preview-narrow.png`. Actual values come from the native composed test host. The existing application shell is retained; the reference is not embedded as the UI.

The first real browser capture exposed loose schedule spacing and quarterly/annual helper text wrapping into an extra row. Compact control spacing and the existing four-column schedule grid now keep each row coherent. Explicit accessible names were added to role, delegate and fallback selects. The delivered-source check exposed literal Razor version text; the numeric version expression is fixed and covered by an included component regression.

Desktop uses two context/preview cards followed by full-width schedule/work/audit cards. Narrow routing stacks its two cards and actions without horizontal overflow; narrow briefing stacks current work and audit information. Browser assertions verify keyboard focus, no page errors and no document overflow at 390 px. Native selects, time/date inputs and visible labels remain usable. Absence dates are explicitly UTC, an intentional difference from the illustrative reference to avoid ambiguous intervals around DST. Source links appear only within current authority.

Human visual approve/revise remains Pending. No user in-app browser or deployed appearance acceptance is inferred from headless Edge.
