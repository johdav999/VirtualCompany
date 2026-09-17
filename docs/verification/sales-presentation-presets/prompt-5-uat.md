# Prompt 5 UAT — ad-hoc preset use and legacy compatibility

Date: 2026-09-14

## UAT profile

- Product: Virtual Company Sales presentation presets
- Route: `/app/sales/presentation-presets`
- Browser: Google Chrome headless
- Viewports: 1440×1100 and 390×844
- Design reference: `docs/design/references/sales-presentation-ad-hoc-reference.png`

## Evidence

- `prompt-5-library-desktop.png` — current local preset-library route at desktop width.
- `prompt-5-library-mobile.png` — current local preset-library route at mobile width.
- The local route returned HTTP 200 at both widths and retained the established Sales shell, presentation navigation, page hierarchy, and responsive layout.
- `PresentationPresetComponentsTests.Ad_hoc_dialog_exposes_optional_context_and_never_offers_an_unbound_live_presenter` executes the populated **Use preset** dialog against typed HTTP clients. It verifies optional context selection, inherited defaults, preparation-only strategy, the no-context review warning, backend result rendering, and the absence of an unauthorized presenter-launch action.
- `SalesPresentationAdHocTests` verifies isolated preparation, explicit missing evidence, idempotency, tenant rejection, runtime-boundary rejection, audit creation, migration safety, and legacy no-false-deduplication behavior.

## Flow findings

1. The development database contains no published preset eligible for ad-hoc use, so the live route correctly cannot expose a populated **Use preset** action without mutating production-like data for visual setup.
2. The populated flow was therefore exercised through deterministic rendered-component interaction, while the real route was captured in Chrome at both required widths.
3. The dialog follows the reference hierarchy: immutable preset/version summary, slide preview, optional account/contact/lead/deal context, run-specific defaults and overrides, authoritative readiness, and preparation result.
4. At mobile width, the scoped layout collapses all grids and slide previews to one column, removes the desktop modal gutter, and keeps the action footer reachable.
5. The flow never fabricates a provider meeting identifier. It prepares and preserves the run, then explains that an authorized meeting must be scheduled or selected before a live presenter can open.

## Verification result

- Focused Prompt 5 API/domain/migration tests: 5 passed.
- Broader Sales presentation API regressions: 37 passed.
- Broad presentation, meeting, browser-room, narration, room, and campaign sweep: 258 passed; 7 environment-dependent SQL Server/live-provider tests skipped.
- Presentation preset Web regressions: 8 passed.
- Prompt 5 interactive component test: passed.
- API and Web project builds: passed.
- EF Core pending-model check: no pending changes.
- Full solution build: blocked by existing package-version conflicts in the Platform, Mailbox, and Finance test projects (Microsoft.Extensions/System.Text.Json 9.0 versus 10.0); the Prompt 5 production and test projects build successfully.

Result: the ad-hoc workflow and conservative legacy compatibility strategy are functionally verified. The empty development dataset limits the real-browser click-through to the library shell; the populated dialog and result state are covered by a typed-client component interaction rather than seeded mock production data.
