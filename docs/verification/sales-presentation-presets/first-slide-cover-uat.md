# First-slide preset covers

## Scope

Preset library, existing company member role, uploaded and processed PowerPoint. This is a small addition to the existing design, not a screen redesign. The actual rendered first slide is used; no generated substitute or public storage URL is introduced.

## Acceptance / UAT ledger

| ID | Priority | Expected result | Evidence | Status |
| --- | --- | --- | --- | --- |
| COVER-01 | P2 | Library card identifies the preset with slide 1 | Backend test verifies the list cover ID and returned PNG bytes match the first slide; component verifies the scoped image URL and descriptive alt text | Verified with automated substitute |
| COVER-02 | P1 | Private slides remain company-scoped | Wrong preset, wrong company, non-member and slide 2 requests are rejected before storage delivery | Verified |
| COVER-03 | P2 | Missing/processing images do not leave broken-image icons | Component tests verify placeholder states, image-error fallback and recovery with a replacement source ID | Verified |
| COVER-04 | P1 | Image endpoint does not serve active SVG/HTML or oversized payloads | Backend tests reject non-raster content and payloads over 10 MiB; no-store and nosniff response headers | Verified |

Presentation: full slide retained with `object-fit: contain` inside a 16:9 card area, lazy loading, no added controls within the selectable library row.

Validation: 6 focused backend tests and 11 focused Web tests passed (17 total); both API and Web compiled through those test builds.

Live browser recheck was not performed against the user's running hosts, which have not been restarted with this change. Automated component/service checks are the explicitly stated substitute. After restarting API and Web, open the preset library and confirm the uploaded PowerPoint's first slide appears above its name. Replacing and processing the source changes the cover slide ID and image URL automatically.

No database migration, new PowerPoint upload, speech generation or changes to existing preset data were required.
