# P28 reference comparison

Reference prompts were saved before production panel work in `docs/design/references/review-work-p28-prompts.md`. Built-in image generation produced `create-review-work-p28.png` and `review-work-source-p28.png`; they remain design targets, with illustrative values excluded from production. Existing P12 canonical decision UI remains the approval surface.

The built creation screen follows the white card hierarchy, editable material fields, compact provenance, visible preview/confirmation and 300px authority rail. Review found missing page spacing and kicker styles; applying native `vc-page` spacing restored shell gutters and the header boundary. The actual app shell, typed data, permission checks and native review content remain authoritative. The generated global-search/avatar controls are illustrative, outside the implemented P28 panel.

The retained source view exposes source identity/saved version, accountable owner/due date/outcome, proposed collaborators/constraints, current work status and original source/Work/canonical review links. Evidence and its checksum are inspectable in a disclosure. Status is derived from native Work; generated example acceptance badges are not fabricated. Source revisions and execution boundaries are visible beside the primary cards.

At 390px the native shell collapses, fields and definition lists stack, primary workflow cards precede the authority rail, and long evidence wraps within its disclosure. Desktop/narrow captures were visually inspected; the repeatable browser checks also assert no horizontal spill and keyboard focus in both creation and source views. Exact accepted captures and final browser results are recorded in `verification.json`.
