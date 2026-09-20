# Document update approval reference prompt

Use case: ui-mockup

Asset type: high-fidelity desktop SaaS approval-review screen for the Virtual Company Blazor application

Primary request: Design the approval detail state for an AI agent proposing a whole-file replacement of an existing Microsoft 365 repository document. The reviewer must compare the permitted original evidence version with the immutable replacement, understand that this is not coauthoring, and see that any intervening human edit will stop delivery and require a new proposal and approval.

Existing product context: executive control-center application with a fixed 240 px left navigation, a central Work / Approvals list-and-detail workspace, and a narrow right-side context rail. Extend the established document-publication approval design rather than introducing a separate document editor.

Layout structure: left application navigation; central two-column approval workspace with a compact approval queue on the left and the selected update review card on the right; narrow insight rail. In the detail card show agent identity and waiting status, exact filename, repository and destination, expected Microsoft version, original evidence version, replacement SHA-256 and byte size, and a prominent warning that approval authorizes whole-file replacement only while the remote version is unchanged. For text or Markdown, show a compact side-by-side or unified diff with removed and added lines; for binary files, show two download cards labeled Original version and Proposed replacement plus a plain-English no-text-diff explanation. Include conflict state treatment stating "A newer human edit was detected" with actions to review the current version and prepare a new proposal; the stale approval is visibly unusable. Show Approve replacement and Reject actions only for the current proposal.

Style/medium: polished high-fidelity web application mockup, Inter typography, restrained enterprise SaaS design, dense but readable, crisp native cards and controls.

Composition/framing: 16:9 desktop screen, full product shell visible, central comparison and concurrency warning are the visual focus, approximately 1440 by 900 logical viewport.

Color palette: light mode background #F7F9FC, white cards, primary #2563EB, success #16A34A, warning #F59E0B, danger #DC2626, neutral slate borders and body text.

Text (verbatim where practical): "Work", "Approvals", "Replace repository file", "Q4-customer-brief.md", "Shared company library / Approved agent output", "Expected version", "Original evidence", "Proposed replacement", "Review changes", "Approve replacement", "Reject", "Waiting for approval", "A newer human edit was detected", "Prepare a new proposal".

Constraints: make both original and replacement artifacts reviewable; visibly bind the proposal to the expected remote version and immutable replacement hash; clearly explain that Microsoft Graph enforces the version check at upload time; keep stale/conflicted proposals as audit evidence but never present them as approvable; use 12 px card radii, subtle shadows, 16–24 px spacing, accessible contrast, and an obvious responsive hierarchy.

Avoid: charts, decorative gradients, provider secrets, raw bearer URLs, clutter, generic analytics, modal-only layout, fake Office coauthoring, merge controls, force-overwrite controls, permission-sharing controls, delete/recreate controls, or any implication that an approval survives changed content.
