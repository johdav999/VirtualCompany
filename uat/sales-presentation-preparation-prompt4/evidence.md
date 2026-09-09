# Prompt 4 UAT evidence

## FLOW-001 — Lead invitation to preparation

Revision: working tree on 2026-09-05; environment: automated Blazor component
substitute; role: authorized Sales company member; viewport: responsive markup
and CSS checks.

Expected: session-required, processing, and ready projections render distinct
accessible statuses and route without copied IDs.

Observed: component tests verify **Prepare presentation**, **Continue
preparation**, and **Open presenter controls**, including exact company,
invitation, and session context. The existing `meetingSessionId` deep link remains
present.

Result: pass using the strongest safe substitute.

## FLOW-002 — Prepare and activate Wellheld deck

Expected: upload the documented fixture, process 3 slides, activate the deck,
and refresh readiness.

Observed: the Prompt 3 lifecycle tests and Prompt 4 final-review tests verify
upload validation, accepted/processing/processed transitions, 3 slides,
activation, readiness, and navigation. A live upload could not be repeated
because API startup is blocked before listening.

Result: blocked for live browser replay; automated acceptance pass.

## FLOW-003 — Private browser presenter controls

Expected: first slide preview, next, previous, goto, search, mode switching,
pause/take-control, and reconnect recovery operate against authoritative state.

Observed: runtime-client and surface tests cover the commands, private/public
separation, diagnostic labels, and the new reconnect/deactivated-deck recovery.
No live browser was available because the API never reached port 5301.

Result: blocked for live browser replay; automated acceptance pass.

## FLOW-004 — Diagnostics and tenant fail-closed paths

Expected: diagnostics-disabled browser access and cross-company/unauthorized
requests disclose no meeting state.

Observed: component tests omit browser launch when diagnostics is disabled,
render installed-Teams guidance, and collapse authorization failures to a safe
unavailable state. Existing API integration tests pass for unauthorized and
cross-company preparation access. The side panel retains its server-configured
fail-closed guard.

Result: pass using automated integration and component evidence.

## Verification record

- Focused Prompt 4 Web suite: 35 passed, 0 failed.
- Focused migration, preparation API, tenant-isolation, and runtime suite:
  15 passed, 0 failed.
- Cascade-path regression check: 2 passed, 0 failed. It verifies both the
  migration operations and runtime model keep the direct session-to-provenance
  relationship non-cascading while provider transcript remains cascading.
- Web build: succeeded with 0 errors.
- API build: succeeded serially with 0 errors and 0 warnings.
- EF model/snapshot check: no pending model changes.
- First API startup attempt: blocked at
  `20260904063139_AddSalesMeetingTranscriptReconciliation` by SQL Server multiple
  cascade paths. The direct provenance-to-session relation was corrected to
  `NoAction`; its provider-transcript cascade remains the deletion path.
- Second API startup attempt: that migration succeeded, then startup stopped at
  `20260904072045_AddSalesMeetingRealtimeVoicePilot` because its foreign key
  references missing historical column `agents.company_id`.
- Follow-up on 2026-09-05: SQL Server inspection confirmed the legacy `agents`
  composite key uses `CompanyId` and `Id`. The voice migration was corrected to
  reference those exact principal columns, its focused regression test passed,
  all remaining migrations applied, and the API began listening on port 5301.
- Live API replay with the configured development identity returned one Sales
  lead for the test company. Database migration readiness is healthy; overall
  `/health` remains degraded only because optional external/voice providers are
  disabled or unavailable in local development.

No new desktop or narrow screenshots are claimed. The browser automation
connection failed after backend recovery, so the original page was not
programmatically refreshed; the backend prerequisite now passes and the live UI
replay can proceed after a normal browser refresh.
