# Immediate narration pause — 2026-09-16

## Evidence and scope
- Organizer report: AI immediately pauses on presenting in room d998f346-a6bc-4c40-8182-d19251f3d5ea.
- Read-only local SQL inspection: latest three narration attempts reference revision f871eb1e-ed1a-4658-96ad-4ec126556302 for retired deck f9e8bede-4c04-48a5-9dc9-e40fcedefde8 (version 1). Active deck is 0ce5092e-38f4-4d3b-8235-a76d925ca3a8 (version 2).
- API log api-runs/20260916110341241/api.stdout.log records SalesNarrationException on each attempt. EnsureCurrentAsync rejects the inactive deck. The exception was escaping the speech handler; worker lease expiry later replaced the original room error.
- There is one meeting narration revision and no preset-owned narration revisions for this company. No valid approved audio exists for the active deck. No approvals were bypassed or changed.
- This evidence identifies a separate failure from the previous acoustic interruption report; no microphone recording is available.

## Issue ledger
| ID | Severity | Expected / observed | Acceptance | Status |
|---|---|---|---|---|
| NAR-001 | P1 | Room should only offer active deck narration; it selected approved narration for a retired deck. | Selection and enqueue require matching company, session, active processed deck and deck/processing versions, plus unexpired approval. | Passed relational regression tests. |
| NAR-002 | P1 | Release failure should remain actionable; exception escaped and became lease-expired. | Handle SalesNarrationException in the existing withheld-speech path, persist failure and pause floor without terminating worker. | Code path reviewed; full live playback not exercised. |
| NAR-003 | P2 | Missing narration should tell the host what to do. | Host sees prepare/approve audio guidance when current slide has no release. | Component build and existing host-room tests pass. |

## Changes
- Shared tenant-scoped active deck query for host status, direct enqueue, resume, and autonomous next-slide selection.
- Keep playback's independent source/audience/approval/integrity checks.
- Handle narration release exceptions as withheld speech with narration_release_invalid and the authored error message.
- Show missing narration guidance in private host controls.

## Validation and remaining prerequisite
- API narration regression tests: 27 passed, including 7 new selection cases and existing generation, playback and preset binding/revocation tests.
- Web SalesHumanRoomTests: 12 passed.
- Browser automation unavailable due sandbox ACL launch failure; no live microphone/playback verification claimed.
- The selected preset requires prepared and approved narration before presenting can speak. The old release was intentionally not transplanted to changed content.

## Deployment
- Local API rebuilt and running as PID 44920; Web rebuilt and running as PID 35188. Both launch builds succeeded. Web responds over port 5062.

## Follow-up: identical preset materialization (resolved)
- The previous prerequisite conclusion was incomplete: absence of a preset-owned revision did not mean reusable approved audio was absent.
- Further comparison proves the active and original decks have the same file SHA-256, all three slide hashes, exact extracted text and exact speaker notes. The current audience hash equals the original approval audience hash. All 25 original assets are ready.
- Root cause: preset application creates a new compatibility deck ID, while the immutable meeting narration references the original deck ID. The original playback gate and subsequent selection fix treated that identity change as content change.
- Fix: allow the original release for an identical active snapshot in the same company and meeting. Require matching file hash, complete slide set, slide hashes, text and notes. Playback additionally compares strings ordinally to avoid SQL collation differences. Keep original revision, audience, approval, expiry, asset integrity and revocation checks intact. No new approval, database repair, or audio regeneration.
- Regression coverage: identical replacement selects and plays the original audio; original revocation blocks it; changed file, text, notes, slide hash and missing slide block it. Existing tenant/session/version/expiry tests remain green.
- Focused suite: 33 passed, 0 failed.
- Actual SQL Server verification: invoked the production selection and OpenPlaybackAsync for the affected meeting; 25 segments across 3 slides returned 3,832,800 PCM bytes successfully. Diagnostic preview counters were rolled back. No audio was published into the room and no provider generation was requested.
- Browser playback and acoustic interruption remain untested by automation due the sandbox launch limitation. The exact original backend failure is verified resolved against the real meeting and storage.
- Deployed API build 20260916115330501, PID 18076; startup succeeded and health endpoint responds (overall 503 includes existing unrelated integration checks).
