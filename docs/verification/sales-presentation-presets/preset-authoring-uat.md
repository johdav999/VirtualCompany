# Preset-owned presentation authoring — 2026-09-14

## Outcome

The preset library owns PowerPoint import/replacement, presenter and behavior defaults, meeting retention defaults, reusable script editing, explicit generation approval, cached audio preview, retry and revocation.

The canonical meeting preparation route now contains a published-preset selector and meeting launch controls. It has no presenter/goal/audience overrides, narration editor, retention form or save-as-preset action. Actual attendee consent remains a meeting-room check.

Published versions remain immutable. **Edit as new draft** retains the processed PowerPoint and copies matching reusable script text when narration is prepared. The new script revision requires fresh approval, while unchanged validated audio can reuse the company-scoped cache.

## Product profile and evidence

- Local ASP.NET/Blazor application; existing owner session and read-only Test preset.
- Isolated Web preview on port 5066; existing API on 5301 was not restarted or migrated.
- User screenshots are the before-state. Design reference: `docs/design/references/sales-preset-authoring-reference.png`, generated with ImageGen before implementation, guided by `docs/design.md`.
- Browser connector failed twice with a kernel error. Used an isolated headless Chrome/CDP session instead.
- Runtime captures: `.codex-build/uat/preset-authoring-20260914/`.
- Desktop: 1440×1100; mobile: 390×844.
- Audio screenshot uses the real rendered Blazor component with explicitly synthetic test data. No paid speech call or actual audio generation was made.

## Acceptance ledger

| ID | Severity | Finding / acceptance | Verification | Result |
| --- | --- | --- | --- | --- |
| PA-01 | P1 | Authoring belongs to a preset, not a customer meeting | SQLite service tests: preset revision has no session/deck/audience owner; no new meetings created | Pass |
| PA-02 | P1 | Meeting page exposes only preset selection and launch | Component request has every override null; live DOM has one select and no input/textarea; desktop/mobile captures | Pass |
| PA-03 | P1 | Approved reusable audio survives meeting binding without regeneration; revocation blocks playback | Synthetic speech/cache integration test through run service, meeting preview and source revocation | Pass |
| PA-04 | P1 | New draft retains source and edited generic script, but not approval | Source clone and cache-reuse integration test | Pass |
| PA-05 | P1 | Same PowerPoint may appear in successive immutable run snapshots | Reproduced unique-index collision; filtered legacy index fixes it while unique run binding remains | Pass, SQL deployment pending |
| PA-06 | P2 | Editor uses available width and renders version/status values instead of raw Razor text | Desktop editor width 831px, no document overflow; visual screenshot recheck | Pass |
| PA-07 | P2 | PowerPoint source is inspectable and has draft-only replacement | Existing processed source inspected read-only; text fallback is shown when storage supplies no public image URL | Pass; image URL fallback unchanged |
| PA-08 | P2 | Audio editing is readable and published scripts are read-only | Real component rendered with synthetic ready state; version-scoped preview URL; no paid calls | Pass using stated substitute |
| PA-09 | P1 | SQL Server fresh/upgrade migrations execute safely | Three SQL Server tests require an isolated test connection, absent in this environment | Not run |

Captures: `preset-desktop-final.png`, `meeting-desktop-final.png`, `meeting-mobile-final.png`, `preset-audio-synthetic-desktop.png`. Mobile preset check reported no document overflow; library and editor stack vertically.

## Automated validation

- API focused suite: **24 passed**, **3 SQL Server cases skipped**.
- Web focused suite: **28 passed**.
- Web build: succeeded, zero errors.
- EF `migrations has-pending-model-changes`: no model changes pending.
- Legacy narration generation, budget limits, tenant/audience authorization, corruption handling and revocation tests remain passing.
- New preset preview rechecks approval and owner authorization after storage I/O and validates stored length/hash.

## Deployment and manual acceptance

Apply the generated migrations through the repository's normal reviewed migration process, then restart API and Web:

1. `20260914202752_AddPresetOwnedNarration`
2. `20260914204956_AllowRepeatedPresetSourceSnapshots`

Neither migration was applied to the user's running database. The first migration's downgrade refuses to discard preset-owned/bound narration; the second keeps unique legacy-upload protection while allowing separate preset run snapshots. Review the SQL Server upgrade in an isolated database before production deployment.

To test after deployment:

1. Open **Presentation presets**, select a preset and choose **Edit as new draft** if it is published.
2. Use **Settings** for reusable presenter, goal, audience, duration, language, control, behavior and retention; save.
3. Use **PowerPoint** to retain or replace the source and wait for processing.
4. Use **Script & audio** to prepare and edit the script, save a revision, explicitly approve generation, refresh and preview. This approval can incur the configured speech cost.
5. Publish the draft. Open the lead's meeting preparation page, select the published version and choose **Use preset** (or **Replace presentation**).
6. Open the meeting. The run uses the preset's source, defaults and approved reusable narration; actual attendee consent is still checked live.

Existing customer-specific narration is not silently promoted into reusable approval. Existing meetings stay pinned until a replacement is explicitly selected. Historical authoring APIs and the explicit legacy route remain for compatibility.
