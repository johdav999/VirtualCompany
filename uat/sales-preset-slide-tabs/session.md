# Preset slide image and content tabs — 2026-09-15

## Product profile

Virtual Company local Blazor Web/API, current working tree. Author role: preset owner; image access: company member. Entry point: `/app/sales/presentation-presets`, select preset/version, then PowerPoint. Launch procedures remain the repository API script and `client.ps1`; existing user processes were not restarted.

Baseline: user-provided 1694×1272 screenshot showed extracted text in place of rendered slides and always-visible speaker notes.

Design reference generated with built-in ImageGen: [reference](../../docs/design/references/sales-preset-slide-tabs-reference.png), [exact prompt](../../docs/design/references/sales-preset-slide-tabs-reference-prompt.md). Applied its image-first cards, compact tab strip, and restrained status treatment. Generated imagery is reference-only; production cards use original rendered PowerPoint images.

## Issue ledger and evidence

| ID | Severity | Flow | Acceptance and regression evidence | Status |
|---|---|---|---|---|
| SLIDE-001 | P2 | View processed slides | Each slide uses its own authorized raster image; wrong preset/version and nonmember requests cannot retrieve it. API `Slide_images_include_later_slides_and_require_matching_preset_version_and_membership`. | Verified with service/component substitute |
| SLIDE-002 | P2 | Review dialogue and notes | Two tabs below image; dialogue shows only this slide's prepared script, not extracted text; missing script has clear empty state. Web `Slide_card_shows_authorized_image_and_separate_dialogue_and_notes_tabs`. | Verified with component substitute |
| SLIDE-003 | P2 | Edit notes | Switching content tabs preserves unsaved input; audio-update status retained; published notes readonly. Same component test plus existing speaker-note save/conflict tests. | Verified with component substitute |
| SLIDE-004 | P2 | Recover from image error | Failed image shows explanation and Retry image restores request. Same component test. | Verified with component substitute |
| SLIDE-005 | P3 | Visual/responsive review | Compare actual new-build desktop/mobile rendering with reference, check keyboard tab navigation and focus. Existing running processes serve an earlier build; no restart or live data changes performed. | Pending live verification |

## Verification

- Web: `dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --filter 'FullyQualifiedName~PresentationPresetComponentsTests|FullyQualifiedName~SalesNarrationPreparationTests|FullyQualifiedName~SalesPresentationPresetApiClientTests' --no-restore -v quiet -m:1 -p:UseSharedCompilation=false` — 25 passed.
- API: `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --filter 'FullyQualifiedName~SalesPresentationPresetTests|FullyQualifiedName~SalesPresetNarrationTests' --no-restore -v quiet -m:1 -p:UseSharedCompilation=false` — 15 passed.
- Both commands compiled their affected projects. Existing unrelated warnings remain. No database migration or paid narration generation required for this change.

## Next review

Restart API/Web using normal repository scripts, then open a processed draft's PowerPoint tab. Verify actual slide artwork, both tabs, unsaved notes across tab switches, and image retry. Prepare/edit narration in Script & audio and return to PowerPoint to verify refreshed dialogue. Repeat at a narrow viewport. No live visual pass is claimed here.
