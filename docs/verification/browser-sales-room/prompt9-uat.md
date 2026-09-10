# Prompt 9 — capture, closing and reviewed follow-up

Implemented 10 September 2026. Organizer journey: end call → review permitted excerpts → save selections → generate and edit customer minutes → review private internal notes → approve minutes → request owner approval for the exact recipient → queue canonical delivery.

## Evidence and consent

The browser worker binds provider commit item IDs to utterances before accepting completion callbacks. Completion order and duplicate callbacks cannot change participant attribution. Pending audio is cleared after submission; pending contexts are bounded. A dedicated serializable capture transaction rechecks tenant, admitted participant, participant version, consent grant time, active room and agent lease before committing canonical evidence and browser provenance together. Consent given after an utterance began does not authorize retaining that utterance. Stable IDs deduplicate participant/track-generation/start-time evidence.

Browser provenance retains participant and agent generations, consent version, hashed track ID, track generation, timestamps and overlap. Graph reconciliation explicitly excludes browser evidence from matching/correction. Browser source cannot be forged through capture autosave; questions using that source require exact canonical retained evidence. No raw audio/video recording, transcript retry payload or transcript logging was added. Worker failure logging records exception type rather than exception content.

Withdrawal stops future processing/capture. Earlier permitted evidence remains subject to meeting retention, rather than being retroactively treated as never consented. End fences new commits, cancels processing and drops unfinished utterances; the UI always describes capture as partial and never reconstructs missing sections. Separate end command IDs enqueue one end operation; closing transition and completion replay are idempotent.

## Review, approval and retention

Only human-reviewed browser excerpts enter the new extraction reasoning context. Existing shared reasoning and closing services produce source-bound customer statements and deduplicated action candidates; candidates remain unreviewed. Internal intelligence stays separate. Preparation does not approve delivery or execute CRM mutations. The existing Sales proposal, owner approval, outbox and mailbox dispatcher remain authoritative. Immediately before dispatch, approval target/binding/status, current capture version, review state and retention are revalidated. Revoked approval and changed evidence prevent sending.

A scoped worker runs expiry cleanup every 15 minutes. It removes eligible browser transcript/provenance and derived action candidates, clears browser question/answer/evidence content, room speech content, correlated reasoning result content and browser-generated minutes/internal item content. Audit and delivery lifecycle metadata remain. Longer minutes/internal retention protects required evidence and those rooms do not block the cleanup batch. Unrelated Teams/Graph transcripts and minutes keep their original source and policy.

Additive migrations: `AddBrowserMeetingCaptureProvenance` and `AddBrowserCaptureParticipantGeneration`. Nullable fields permit legacy rows without invented provenance.

## Verification

- Backend capture/lifecycle/closing/delivery/Graph/conductor/floor selection: 67 passed, zero skipped, including SQL Server fresh-chain and upgrade tests.
- Teams/shared-media plus expanded derived-content retention selection: 57 passed, zero skipped.
- Closing and human-room Web components: 16 passed.
- Final cleanup refinement: 12 focused capture tests passed.
- API and Web builds succeed; EF model consistency is checked.
- SQL Server Express tests used disposable databases. Upgrade fixtures preserve Teams call state, invitation URLs, Graph transcript source/content and existing minutes content; fresh migration applies the complete chain with no pending model changes.
- [Preservation comparison](prompt9-preservation-comparison.json): all 792 baseline paths still exist. The changed shared paths are explained in the Teams preservation guide.
- [Browser checks](prompt9-browser-checks.json): actual compiled component HTML and scoped CSS rendered in local Chrome at 1440 and 390 px, with no horizontal overflow and visible draft text.

## UAT and defect ledger

Reference: [design image](../../design/references/browser-sales-room-closing-reference.png) and [written prompt](../../design/references/browser-sales-room-closing-reference-prompt.md).
Role: company organizer; fictional evidence and customer recipient. Tested surfaces: actual Blazor components with HTTP fixtures, rendered HTML in Chrome, and service/SQL fixtures.

| Flow / issue | Evidence | Outcome |
| --- | --- | --- |
| Partial or absent consented capture | Capture tests; partial notice on both widths | Unpermitted text discarded; missing content never invented |
| Organizer access / permission failure | Component retry test and service isolation tests | No private evidence on denied access |
| Explicit excerpt selection | Component change and autosave assertion | Only explicit reviewed selections posted |
| Customer/private separation | Closing source-exclusion test and private details panel | Private context excluded from customer reasoning |
| Draft textarea appeared empty in static rendered HTML | Visual inspection | Fixed child-content rendering; Chrome now asserts actual input value |
| Source link ID format differed | Source anchor inspection | Canonical ID format aligned |
| Delivery and stale/revoked approval | Dispatcher regressions | No send without current approval and evidence |
| Repeated end/completion | Lifecycle and closing tests | One transition/result |
| Expiry and protected evidence | Transcript, question, speech, run-content and longer-retention tests | Eligible content cleared; Teams evidence preserved |

Final screenshots: [desktop draft](prompt9-closing-draft-1440.png), [phone draft](prompt9-closing-draft-390.png), [desktop partial](prompt9-closing-partial-1440.png), [phone partial](prompt9-closing-partial-390.png). Visual review confirms readable hierarchy, explicit partial status, separate private notes, readable draft text, and accessible labels/disabled actions.

## Remaining live verification

This was component/service/SQL UAT, not an authenticated deployed full-call test. Live LiveKit speech, physical devices, provider callback timing, a production reasoning response, and a real approved mailbox send were not exercised. No message was sent and no Teams resource was activated. These live checks require configured provider access, participants, and an authorized test mailbox/recipient; the implementation does not substitute synthetic results for them.
