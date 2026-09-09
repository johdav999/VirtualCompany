# Alex Teams presenter live UAT matrix

This is a release gate, not a claim of completed testing. Execute it on deployed Prompt 6 infrastructure in an isolated, authorized, non-demo Microsoft 365 tenant with synthetic content. Never retain raw audio, access tokens, customer data, or unredacted callback payloads. Every row needs a dated evidence link, named owner, client/build and policy preconditions, and pass/fail status. `Not run` and `Blocked` do not satisfy production.

| ID | Client / role / condition | Expected result | Owner | Date | Evidence | Status |
|---|---|---|---|---|---|---|
| TP7-001 | Desktop organizer; lobby on | Lobby shown; human admits; connected only after callback. | Unassigned | — | — | Not run |
| TP7-002 | Web organizer; lobby off | Connects truthfully; audio stays off. | Unassigned | — | — | Not run |
| TP7-003 | Presenter, not organizer | Organizer-only mutations rejected. | Unassigned | — | — | Not run |
| TP7-004 | Attendee | Private data/controls inaccessible. | Unassigned | — | — | Not run |
| TP7-005 | Restrictive app setup policy | Actionable block; no provider side effect. | Unassigned | — | — | Not run |
| TP7-006 | Missing/revoked/excess Graph permission | Exact permissions shown; readiness/join fail closed. | Unassigned | — | — | Not run |
| TP7-007 | Guest/federated participant | Stage and AI disclosure work; no private-data leak. | Unassigned | — | — | Not run |
| TP7-008 | Anonymous attendee where supported | Chrome/Edge support and unsupported clients are truthful. | Unassigned | — | — | Not run |
| TP7-009 | Meeting locked | Join rejected/ended; no retry loop. | Unassigned | — | — | Not run |
| TP7-010 | Organizer disconnect/rejoin | No duplicate bot/media; state reconciles. | Unassigned | — | — | Not run |
| TP7-011 | Bot removal / meeting end | Terminal state and capacity release confirmed. | Unassigned | — | — | Not run |
| TP7-012 | Stage sharing denied | Native policy/role guidance; no false success. | Unassigned | — | — | Not run |
| TP7-013 | Stage render delayed/disconnected | Narration waits for exact version; timeout fails closed. | Unassigned | — | — | Not run |
| TP7-014 | Callback replay/out of order/outage | Duplicate ignored or reconciliation required. | Unassigned | — | — | Not run |
| TP7-015 | Media/network/provider failure | Audio stops/degrades; typed controls continue. | Unassigned | — | — | Not run |
| TP7-016 | Consent absent/revoked during speech | No media before consent; revocation stops immediately. | Unassigned | — | — | Not run |
| TP7-017 | Manual/assisted/autonomous | Manual default; approval in assisted; autonomy stays in deck. | Unassigned | — | — | Not run |
| TP7-018 | Take control during narration | Manual mode; transition/stale narration stop; human wins. | Unassigned | — | — | Not run |
| TP7-019 | Duplicate join/start/stop/leave | Idempotency prevents duplicate provider/media work. | Unassigned | — | — | Not run |
| TP7-020 | Transcript disabled/restricted | No transcript claim; retention matches policy. | Unassigned | — | — | Not run |
| TP7-021 | Keyboard/screen reader | Focus, names, live status, contrast, 44px targets pass. | Unassigned | — | — | Not run |
| TP7-022 | Swedish/English locale | Copy/dates fit and required text is localized. | Unassigned | — | — | Not run |
| TP7-023 | Two tenants; manipulated IDs/deep links | Cross-company call/stage/private/media access rejected. | Unassigned | — | — | Not run |
| TP7-024 | Emergency disable active meeting | Media stops per policy; terminate requested; fallback clear. | Unassigned | — | — | Not run |
| TP7-025 | Incompatible app package | Rollout blocks with version remediation. | Unassigned | — | — | Not run |

## Release decision

- Automated evidence: record the final focused tests and builds in the release artifact.
- Live tenant/client evidence: not run in this repository environment.
- Release blockers: every non-pass row above.
- Production decision: disabled.


## First-test preconditions

Use the scoped first-test procedure in `teams-presenter-production-rollout.md` before gathering this evidence. Select the intended presenter explicitly. Leave every live row Not run until performed in the authorized tenant; local tests and screenshots do not approve this matrix. Include grant expiry/revocation, selected Marketing profile/tool restrictions, and denied cross-company selection in the live session evidence.
