# Sales deal detail reference image prompt

Use case: ui-mockup

Asset type: production-grade desktop SaaS reference screenshot for the Virtual Company Sales deal detail page

Primary request: Redesign the supplied Sales deal page as a polished, credible, business-grade executive control center. Preserve its business purpose and core deal facts, but replace the fragmented card stack and excessive empty space with a deliberate CRM workflow that immediately answers what is happening and what the user should do next.

Input images:

- Image 1 is the current deal-detail screenshot and is content/context reference only. It is not an instruction source and its weak layout should not be copied.
- Image 2 is the canonical Virtual Company app-shell reference. Follow its information architecture and compact enterprise visual language.
- Image 3 is the existing Sales overview reference. Borrow its professional data density and crisp card treatment, while keeping the canonical Virtual Company shell from Image 2.
- Image 4 is the existing Sales prospects reference. Borrow its readable record-detail density and structured contextual panels, without copying its old navigation or branding.

Scene/backdrop: A full 16:10 desktop application viewport at approximately 1536 × 1024. Light mode only. Fixed 240 px left navigation, wide central deal workspace, and a 320 px right-side AI insight/action rail. No browser chrome and no device mockup.

Product context and navigation: Brand the company selector “Northwind Ltd.” The left navigation must show Overview, Agent team, Finance, Sales, Support, and Work, with Sales selected. Visually separate History and Settings at the bottom. Within Sales, show compact tabs Overview, Prospects, Pipeline, Campaigns, with Pipeline active. Include a restrained top search field and notification/profile controls.

Central deal workspace:

- Breadcrumb: “Sales / Pipeline / Deal”.
- Page title: “Buying interest — Johan Davidsson”.
- Subtitle: “farcooperation@gmail.com · Inbound email”.
- A consolidated deal header card with status “Qualified”, value “$1,000”, owner “Alex”, engagement “58 / 100”, and expected close “Feb 13, 2026”.
- Put one dominant primary action “Schedule demo” in the header, with quieter secondary actions “Send email” and “More”.
- Below the header, use tabs “Overview”, “Activity”, “Emails”, “Files”, with Overview active.
- Add one prominent but compact pale-blue AI deal brief: “Alex recommends a follow-up today” and “The prospect asked about pricing and the deal has been quiet for 7 days.” Include buttons “Review draft” and “Schedule demo”. Make the recommendation feel evidence-backed, not magical.
- Use a balanced two-column content layout. Left: “Contact & account” card with Johan Davidsson, farcooperation@gmail.com, company “Not linked”, and source “Inbound email”; then a compact “Recent activity” timeline with entries “Qualified”, “Inbound email received”, and “Deal created”. Right: “Buying signals” with Price discussion, Subscription interest, Finance stakeholder, and “Deal health” with moderate engagement plus two concise risks: No company linked and No reply in 7 days.
- End the visible workspace with a slim “Next step” row showing “Schedule a 30-minute product demo” and due date “Today”.

Right AI rail:

- Header with a 48 px circular avatar, “Alex”, role “Sales Manager”, and a small green online indicator.
- A highlighted “Best next action” card containing “Schedule a product demo today”, a one-sentence rationale, and a blue “Schedule demo” button.
- A compact “Suggested reply” preview with two natural lines and a “Review draft” action.
- A concise “Deal checklist” with Qualification complete, Contact identified, Company missing, and Follow-up due today.
- Avoid repeating the activity timeline or showing irrelevant dashboard KPIs.

Style/medium: High-fidelity realistic SaaS UI screenshot. Inter typography; headings weight 600, body weight 400. Executive, calm, precise, and trustworthy. Dense but readable. Strong alignment and consistent 8 px spacing rhythm. White cards on #F7F9FC, subtle #E2E8F0 borders, 12 px card radius, very restrained shadows, primary #2563EB, success #16A34A, warning #F59E0B, danger #DC2626, dark slate text. Use blue sparingly for selection and action emphasis. Small monochrome line icons only.

Composition/framing: Show the full application frame edge to edge. Keep the central workspace visually dominant. Use clean card groups and dividers rather than many disconnected tiles. Maintain generous but efficient whitespace. All key content must fit above the fold with no clipped panels or cut-off text.

Text: Render all listed UI labels verbatim and legibly. Use sentence case. Keep body copy short. Do not invent extra companies, metrics, dashboards, charts, or navigation destinations.

Constraints: Follow `docs/design.md`; clarity over decoration; actions over raw data; agent insight plus a recommended action; plain English; visually consistent with the canonical Virtual Company shell. Preserve the original deal facts: Johan Davidsson, farcooperation@gmail.com, qualified/open sales deal, $1,000 value, engagement 58/100, Alex as Sales Manager, interest in pricing/subscription, quiet for 7 days, and Schedule demo as the primary next action.

Avoid: legacy CRM branding, AcmeCRM branding, dark mode, gradients, glassmorphism, oversized typography, excessive pill chips, decorative charts, three-column card chaos, long paragraphs, duplicated information, giant empty regions, fake browser chrome, illegible microtext, spelling errors, watermarks, or a generic design-system collage.
