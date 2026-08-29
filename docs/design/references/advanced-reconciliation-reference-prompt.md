# Advanced reconciliation workspace reference prompt

Use case: ui-mockup

Asset type: high-fidelity desktop SaaS product UI reference screenshot

Primary request: Design the Virtual Company Finance advanced reconciliation workspace for explainable split, partial, one-to-many, many-to-one, batch, fee, rounding, and residual settlement review. It must clearly answer what needs attention and what the finance user should do next, while keeping acceptance a deliberate human action.

Current product context: Virtual Company is a calm, web-first executive control room. This screen sits under Finance > Transactions > Reconciliation and is supervised by Laura, Finance Manager. Preserve the established left application sidebar, Finance secondary pill navigation, light app background, white rounded cards, soft borders, subtle shadows, blue primary actions, restrained status colors, and dense-but-readable operational content.

Scene/backdrop: full 16:9 desktop application screenshot on a #F7F9FC background, with the existing 240px white left sidebar and a main workspace around 1240px wide.

Style/medium: realistic shippable B2B SaaS UI, Inter-like sans-serif typography, polished but restrained; not concept art.

Composition/framing:

- Left sidebar with Virtual Company identity, primary navigation, Finance active, and a compact Laura contextual card near the bottom.
- Main header labeled "FINANCE", title "Advanced reconciliation", subtitle "Review explainable settlement groups and approve balanced outcomes.", and a secondary Finance navigation row with "Transactions" active.
- A four-card KPI row for "Needs review", "Low confidence", "Conflicts", and "Stale suggestions".
- A white filter card with search, queue status, confidence, date range, "Clear filters", and blue "Apply filters" action.
- Below, a 40/60 list-detail layout. The left queue lists settlement groups with friendly badges, counterparties, bank totals, item cardinality such as "1 bank row · 3 invoices", confidence, freshness, and selected-row treatment.
- The right detail panel for a selected batch deposit. At the top show the expected bank total, allocated amount, fee, rounding, residual, and a prominent green "Balanced" indicator.
- Show an auditable result graph as grouped rows: one bank deposit, two completed payments, three customer invoices, plus fee/rounding/residual rows. Use connecting alignment and indentation rather than decorative diagrams.
- Include a "Why this was suggested" card with five compact reason contributions: normalized reference, counterparty, amount, timing, and provider pattern. Show rule version "Rule v7" and confidence "92%".
- Include a "Review required" warning explaining the material batch needs authorized acceptance, along with a concurrency/version note.
- Bottom actions: outlined "Reject suggestion", secondary "Send to suspense", and primary blue "Accept & post". Include a compact immutable history timeline beneath or beside the actions.
- Include an empty-state treatment in a small inactive panel: "No group selected — Select a settlement group to review its evidence."

Color palette: #F7F9FC background, #FFFFFF cards, #2563EB primary, #0F172A text, #64748B secondary text, #E5E7EB borders, #16A34A success, #F59E0B warning, #DC2626 danger.

Text (verbatim): "FINANCE", "Advanced reconciliation", "Review explainable settlement groups and approve balanced outcomes.", "Needs review", "Low confidence", "Conflicts", "Stale suggestions", "Balanced", "Why this was suggested", "Rule v7", "92% confidence", "Review required", "Reject suggestion", "Send to suspense", "Accept & post", "No group selected", "Select a settlement group to review its evidence."

Constraints: operational clarity over decoration; practical implementation-ready layout; no mock charts; no gradients; no dark futuristic styling; no raw enum labels or internal identifiers; no logos beyond the generic Virtual Company product mark; no watermark. The result graph and evidence contributions must be legible and visually central. Preserve responsive intent by keeping cards and columns modular.

