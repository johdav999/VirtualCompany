# Document publication approval reference prompt

Use case: ui-mockup

Asset type: high-fidelity desktop SaaS approval-review screen for the Virtual Company Blazor application

Primary request: Design the approval detail state for an AI agent proposing a new file in an explicitly writable Microsoft 365 document repository. The reviewer must clearly understand exactly what will be created and be able to approve or reject the immutable proposal.

Existing product context: executive control-center application with a fixed 240 px left navigation, a central Work / Approvals workspace, and a narrow right-side context rail. The approval screen already uses a list-and-detail workflow; this reference should extend that system rather than introduce a separate document app.

Layout structure: left application navigation; central two-column approval workspace with a compact approval queue on the left and the selected publication review card on the right; narrow insight rail. In the detail card show agent identity and status, exact filename, destination repository and folder, file type and byte size, immutable SHA-256/version fingerprint, a safe preview panel for text content or a download control for binary content, the policy rationale, approval expiry, and prominent Approve and Reject actions. Include a queued/delivery state strip below the review details showing queued, approved, sending, reconciliation required, failed, and delivered as possible lifecycle states without making all states look active at once. Include an empty state for no pending document publications as a small secondary panel.

Style/medium: polished high-fidelity web application mockup, Inter typography, restrained enterprise SaaS design, dense but readable, crisp native cards and controls.

Composition/framing: 16:9 desktop screen, full product shell visible, central selected review is the visual focus, approximately 1440 by 900 logical viewport.

Color palette: light mode background #F7F9FC, white cards, primary #2563EB, success #16A34A, warning #F59E0B, danger #DC2626, neutral slate borders and body text.

Text (verbatim where practical): "Work", "Approvals", "Create a repository file", "Q4-customer-brief.md", "Shared company library / Approved agent output", "18.4 KB", "SHA-256", "Preview", "Download staged file", "Approve creation", "Reject", "Waiting for approval".

Constraints: make filename, folder, preview/download, size, and immutable hash visibly reviewable before approval; clearly state that approval creates one new file and never overwrites an existing file; show plain-English policy language; use 12 px card radii, subtle shadows, generous 16–24 px spacing, accessible contrast, and an obvious responsive hierarchy.

Avoid: charts, decorative gradients, provider secrets, raw bearer URLs, clutter, generic analytics, modal-only layout, fake Office document editing, permission-sharing controls, delete controls, or any implication that an existing file will be overwritten.
