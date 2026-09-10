# Prompt 6 narration UAT

Product: Virtual Company, Blazor Sales meeting preparation. Role: organizer.
Environment: isolated component/API/SQL fixtures and real shared speech with fictional scripts.
Reference: [generated design](../../design/references/sales-narration-reference.png), [written prompt](../../design/references/sales-narration-reference-prompt.md).
The imagegen built-in generated the reference before UI implementation.

## Flows and evidence

1. Prepare/review a script: generated production-component markup, explicit source evidence and editable customer-safe wording. bUnit verifies approval is disabled without review and edited text cannot approve a previously saved version.
2. Approval/generation/retry: relational service tests verify isolation, concurrency, revocation, content rejection, cost bounds, unknown outcomes and retry accounting.
3. Authorized preview: real English/Swedish shared gateway generation through durable worker and real local document storage. Both first-slide previews were returned by the production release-checking service; no new generation occurred on reuse. Returned transcripts matched their approved text. This is automated correspondence validation, not an independent human listening assessment.
4. Presentation review UI: Chrome at 1440 and 390 pixels, using actual rendered components and existing compiled scoped styles. A minimal shell fixture supplies surrounding layout. No horizontal overflow; script fields contain text. Browser audio decoded and progressed through real generated WAV bytes.

Screenshots: [draft desktop](prompt6-draft-1440.png), [draft mobile](prompt6-draft-390.png), [ready desktop](prompt6-ready-1440.png), [ready mobile](prompt6-ready-390.png). The built cards, spacing, palette, evidence/script columns and next-action rail were compared to the reference; mobile stacks these controls.

## Issue ledger

| ID | Severity | Finding | Fix / regression | Status |
| --- | --- | --- | --- | --- |
| NAR-01 | P1 | Static textarea rendering did not show the saved script. | Render script as textarea content; browser capture asserts nonempty input value; approval checks reject unsaved edits. | Verified |
| NAR-02 | P2 | Test HTML lacked UTF-8 declaration. | Add charset; recapture desktop/mobile and inspect punctuation. | Verified |
| NAR-03 | P1 | Missing generation configuration could leave approved work waiting without clear readiness. | Disable generation readiness and reject approval with actionable configuration feedback. | Verified by service test |
| NAR-04 | P1 | Missing object could retain ready metadata until preview. | Readiness verifies stored object existence/size; preview verifies hash; explicit retry regenerates unavailable assets. | Verified by service test |
| NAR-05 | P1 | Old drafts/invalid source revisions could crowd the work queue. | Select approved references and move invalid source/approval work to review. | Verified by focused backend suite |

## Reproduction

Use the narrow existing API and Web projects. Environment opt-ins for the synthetic provider harness are `VC_NARRATION_LIVE=1`, an existing `OPENAI_API_KEY`, and `VC_NARRATION_LIVE_DIRECTORY` pointing at an isolated repository output directory. Run only `SalesNarrationLiveTests`; it makes at most four short requests and no retries. Keep generated object/audio files out of public hosting.

Set `VC_BROWSER_UAT_DIRECTORY` to `artifacts/browser-human-room-uat`, run `SalesNarrationPreparationTests`, then run `node tests/scripts/Capture-SalesNarration.cjs` with Playwright available in `NODE_PATH`. The capture harness serves only local fixtures and the synthetic preview. It is test-only and introduces no production diagnostic endpoint.

Logs: `artifacts/prompt6-api-build.log`, `prompt6-web-build.log`, `prompt6-regressions.log`, `prompt6-final-backend-tests.log`, `prompt6-live.log`, `prompt6-narration-web-tests.log`, `prompt6-preparation-regressions.log`, `prompt6-existing-upgrade.log`, and `prompt6-pending-model.log`.

## Explicit limits

Full authenticated browser interaction against a deployed API, independent listening/pronunciation assessment, Safari/iOS, distributed object-store outage, Azure hosting and live room playback were not exercised. UI interactions used bUnit; browser visual/audio checks used isolated transport fixtures. Prompt 7 owns room-track playback and continuous consent/cancellation. Existing browser/Teams rollout gates are preserved.

