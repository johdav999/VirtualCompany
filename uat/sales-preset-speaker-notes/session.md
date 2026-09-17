# Preset speaker notes — UAT session

Date: 2026-09-15. Revision: 86d34108 plus the existing working tree and this change.
Product: Virtual Company, local Blazor web application.
Route: /app/sales/presentation-presets?companyId=<active-company>.
Role: active company member editing a processed draft; narration approval uses the preset owner.
Launch: normal API/Web development startup after applying AddPresetSpeakerNotesSnapshot.
Evidence baseline: user-provided PowerPoint-tab screenshot showing read-only processed slide cards.

## Flows and acceptance evidence

1. Edit a draft slide's Speaker notes, save, and reload. Notes persist; the draft concurrency token changes.
2. Edit notes after generating audio. The prior revision becomes outdated; only the affected segment is marked needs_update, and preview is denied.
3. Prepare and approve a new script. Updated notes become narration text, while unchanged slides reuse cached audio.
4. Clear notes. Preparation falls back to slide text, not the old imported notes.
5. Edit a published version or another company's slide. The service rejects the request.
6. Save with a stale token. An error is visible and the component preserves unsaved input.

Verification substitute: real compiled Razor components driven by bUnit and HTTP transport fixtures; service/database/worker integration using isolated SQLite and synthetic speech. No live user preset was modified, no real speech was purchased, and the running application was not restarted.

## Issue ledger

| ID | Severity | Flow | Baseline | Acceptance / regression | Status |
|---|---|---|---|---|---|
| NOTES-001 | P1 | Draft authoring | Processed slide cards had no speaker-note editor | Speaker_notes_save_is_scoped_and_shows_audio_update_state; backend persistence test | Verified with automated substitute |
| NOTES-002 | P1 | Audio freshness | Edited notes needed explicit stale-audio feedback and safe playback blocking | Edited_notes_persist_and_block_old_audio_until_a_new_revision_is_approved; per-slide reuse test | Verified with automated substitute |
| NOTES-003 | P2 | Save recovery | Concurrent edit must not discard typed notes | Speaker_notes_conflict_preserves_unsaved_input; stale-token service assertion | Verified with automated substitute |
| NOTES-004 | P2 | Live visual review | New controls not yet reviewed in the running local application | Apply migration, restart API/Web, repeat PowerPoint -> edit -> Script & audio workflow at desktop/mobile widths | Not run; runtime migration/restart still required |

## Results

- Backend focused suite: 25 passed (SalesPresetNarrationTests, SalesPresentationPresetTests, SalesNarrationTests).
- Web focused suite: 19 passed (PresentationPresetComponentsTests, SalesPresentationPresetApiClientTests, SalesNarrationPreparationTests).
- API and Web compiled successfully as part of these runs; existing unrelated warnings remain.
- Migration 20260915043906_AddPresetSpeakerNotesSnapshot adds only nullable nvarchar(64) SourceNotesHash to sales_narration_segments.
- EF pending-model check passed. Migration generated and inspected, not applied to the user's database.
- Broad diff check found an existing extra EOF blank line in SalesNarrationTests.cs, outside this change.

Next review: apply the migration through the normal deployment/startup flow, restart API/Web, then verify the live card layout and audio-update workflow on a draft.

