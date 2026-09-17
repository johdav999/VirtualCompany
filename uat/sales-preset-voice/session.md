# Preset narration voice — UAT session

2026-09-15. Local Virtual Company web/API working tree.
Route: /app/sales/presentation-presets, Script & audio tab.
Role: preset owner; processed draft required for authoring.
Baseline: user screenshot has a read-only Voice input showing marin.
Scope: voice selection, revision persistence/cache isolation, approval safety.

| ID | Severity | Finding | Acceptance / evidence | Status |
|---|---|---|---|---|
| VOICE-001 | P1 | Voice cannot be changed | Compiled component test changes selector to cedar and verifies company-scoped prepare payload | Verified with bUnit substitute |
| VOICE-002 | P1 | Selected voice must reach synthesis and use separate cache | Preset service test creates distinct unapproved revision, persists cedar, generates through fake gateway, preserves prior ready audio; PCM payload test checks selected output voice | Verified with isolated backend tests |
| VOICE-003 | P1 | Unsaved voice must not approve old revision | Component prevents approval until selection is saved; unavailable voice and cross-company command rejected by backend | Verified with automated substitutes |

Provider voice catalog checked against official OpenAI documentation:
https://developers.openai.com/api/docs/guides/realtime-conversations#voice-options
Shared speech profile exposes available voices. Existing default and configuration fingerprint remain unchanged. Each saved revision persists its own voice; asset keys already include voice. No schema change required.

Validation: 31 backend tests and 20 web tests passed. API and Web compiled.
First parallel build's compiler crashed in the existing migration project; serial build with UseSharedCompilation=false passed.
No paid provider call, live user-preset write, database change, or process restart.
Live browser appearance and acoustic listening remain untested; compiled components and isolated SQLite/synthetic audio were the safe UAT substitutes.

Next live check after API/Web restart:
1. Open a draft, Script & audio.
2. Select a voice and Prepare script (or Save as new revision for edited wording).
3. Reload and confirm selected voice.
4. Review and approve generation, then listen to the preview.
5. Confirm published versions remain read-only and older audio is unchanged.

