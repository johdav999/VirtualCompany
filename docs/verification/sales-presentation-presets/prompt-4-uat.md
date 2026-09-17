# Prompt 4 UAT — campaign presentation activities

Date: 2026-09-14

## UAT profile

- Product: Virtual Company Sales campaigns
- Route: `/app/sales/campaigns`
- Browser: Google Chrome headless
- Viewports: 1440×1100 and 390×844
- Services: local API on port 5301 and local Web client on port 5062
- Design reference: `docs/design/references/sales-campaign-presentation-activity-reference.png`

## Evidence

- `prompt-4-campaign-desktop.png` — desktop render of the live local campaign route.
- `prompt-4-campaign-mobile.png` — mobile-width render of the live local campaign route.
- The live route returned HTTP 200 and rendered the Sales campaign shell, campaign empty state, presentation-preset navigation, Alex panel, and responsive page chrome.
- The local API health endpoint was reachable. It reported `Degraded` only because the optional Sales meeting voice media route was unavailable; database migration readiness was healthy.
- `PresentationPresetComponentsTests` renders the configured campaign activity lifecycle, preset details, projected cardinality, readiness blockers, run states, failures, and remediation controls without exposing live-presentation or outbound-email actions.
- `CampaignPresentationActivityTests` covers typed configuration invariants, exact version pinning, concurrency, run retry behavior, relational mappings, deletion behavior, and idempotency constraints.
- Existing `SalesCampaignsIntegrationTests` and `CampaignInitiativeDomainTests` passed as outbound campaign regressions.

## Flow findings

1. The live development database had no campaign, so the route correctly displayed its empty state and did not expose the activity editor.
2. The configured and post-scheduling states were therefore verified through deterministic component rendering rather than mutating development data solely for visual setup.
3. The desktop interactive browser-control runtime could not attach because its Windows sandbox helper failed while applying read ACLs. Headless Chrome was used as the safe fallback for real browser rendering at both required widths.
4. No in-scope visual defect was found in the rendered shell or component markup. The activity editor's scoped CSS includes a single-column mobile layout and preserves the reference hierarchy: preset, execution configuration, projection/readiness, then run progress.

## Verification result

- Focused API/domain tests: 13 passed.
- Focused Web component tests: 4 passed.
- Existing campaign regression tests: 13 passed.
- Focused API build: succeeded with 0 warnings and 0 errors.
- Full solution build: blocked by the existing `VirtualCompany.Infrastructure.Mailbox.Tests` reference conflict between `Microsoft.Extensions.Hosting.Abstractions` 9.0 and 10.0; the Prompt 4 projects build successfully.

Result: Prompt 4 implementation is functionally verified. Interactive click-through of a populated campaign remains an environment limitation, with headless browser captures and component tests retained as the strongest safe evidence.
