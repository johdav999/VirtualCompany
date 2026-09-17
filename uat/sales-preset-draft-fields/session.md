# Draft fields from Overview — UAT

Date: 2026-09-15. Virtual Company local working tree.
Route: /app/sales/presentation-presets.
Role: authorized company member. Precondition: draft v2 alongside published v1.
Baseline: supplied screenshot shows version fields as text in Overview, without an edit action.

Scope: discover draft editing, save changed fields, preserve published version.
Implementation reuses the existing Settings editor and save endpoint. Overview now exposes Edit draft, gated on selected draft state and not archived. Save also checks the selected version matches the current draft. No schema or backend production changes.

| ID | Severity | Finding | Acceptance | Evidence / result |
|---|---|---|---|---|
| DRAFT-001 | P2 | Overview does not expose editing | Selected draft offers Edit draft; action opens Settings using existing model | Compiled Razor wiring and bUnit callback test; passed |
| DRAFT-002 | P1 | Published version must stay immutable | Published/archived selection has no edit action; saving draft goal/audience/duration/demo does not change published fields | Component state theory and service reload regression; passed |
| DRAFT-003 | P2 | Field edits must reach save | Name and duration changes reach existing Save draft callback and expected version token | bUnit editor regression; passed |

Verification substitute: compiled component interactions and isolated backend service tests. No live user preset was changed.
17 web tests and 6 backend tests passed. Web and API compiled; existing unrelated warnings remain.
Live browser visual verification remains unperformed. No process was restarted and no migration is required.

Next check after rebuilding/restarting Web: select v2 Draft, choose Edit draft in Overview, edit Settings, Save draft, return to Overview, then select v1 and confirm published fields remain unchanged.

