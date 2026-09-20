# Document repository operator recovery reference prompt

Use case: ui-mockup

Asset type: high-fidelity desktop SaaS settings screen reference for Virtual Company

Primary request: Extend the existing Document repositories settings experience with production operator recovery controls for Microsoft 365 sources. Show one connected SharePoint document library card and a compact operator recovery panel that makes pause semantics explicit for retrieval, synchronization, and queued approved writes. Include recovery actions for retrying failed items, running a full resynchronization, and reconciling an uncertain approved upload.

Existing product context: Virtual Company is an executive control center where users supervise AI agents. This is a secondary Settings destination, not a new primary navigation page. Reuse the current document repository card structure, Microsoft source identity, connection badges, freshness, indexed/processing/failed counts, agent grants, and attention sidebar.

Scene/backdrop: desktop web application at 1440 by 1000 pixels, fixed 240-pixel left navigation, pale blue-gray application background, centered settings workspace, white cards.

Composition/framing: full-page screenshot. Header and information notice at top. Main column contains one repository card with an expanded "Operator recovery" section directly below its health summary. Right column contains "What needs attention" and "Recovery guide" cards. The recovery section uses three compact status rows—Agent retrieval, Synchronization, Approved writes—with plain-English current state and individual pause/resume controls. Beneath them, show an operational health strip for queue age, last successful sync, stale lease warning, scanner/embedding health, and throttling. Place bounded recovery actions in a clearly separated footer.

Style/medium: polished modern B2B SaaS UI, implementation-realistic HTML/CSS visual language, Inter typography, dense but readable, subtle borders and shadows, 12–16 pixel card radii, strong hierarchy, no decorative illustration.

Color palette: background #F7F9FC, cards #FFFFFF, primary #2563EB, success #16A34A, warning #F59E0B, danger #DC2626, ink #0F172A, muted #64748B.

Text (verbatim where legible): "Document repositories", "Operator recovery", "Agent retrieval", "Synchronization", "Approved writes", "Queue age", "Last successful sync", "Scanner & embeddings", "Run full resync", "Retry failed items", "Reconcile uncertain upload", "Disconnect".

Interaction states: synchronization is paused while retrieval remains available; approved writes are paused with queued files preserved; one unresolved upload is highlighted in warning orange. Controls are reachable and clearly associated with each scope. Show that disconnect is stronger than pause and immediately denies retrieval.

Constraints: match `/docs/design.md`; clarify what is happening and what the administrator should do; use plain English; no raw IDs, secrets, tokens, provider payloads, or internal enum names; no charts; no mock-data feel; do not add a new primary navigation destination; preserve the existing two-column settings layout and repository-card visual system; responsive intent should be apparent through stackable panels.

Avoid: glassmorphism, gradients, excessive icons, dark mode, tiny text, decorative graphs, generic analytics dashboard, credential fields in the recovery panel, destructive reset language, or implying that local cleanup deletes remote Microsoft files.
