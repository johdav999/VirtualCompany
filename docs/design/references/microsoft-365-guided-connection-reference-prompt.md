# Microsoft 365 guided connection reference prompt

Use case: ui-mockup

Asset type: high-fidelity product-design reference for the existing Virtual Company Blazor settings page, not a shipped UI asset.

Primary request: Design a polished guided Microsoft 365 repository connection workflow inside Virtual Company Settings. Show the review step of an eight-milestone journey, with enough surrounding context to establish the full workflow and responsive behavior.

Product context: Virtual Company is a calm, trustworthy, web-first executive control room where named AI agents help run a company under human supervision. This screen belongs under Settings → Document repositories. It connects a folder-bounded OneDrive for Business or SharePoint knowledge source using an administrator sign-in, selected-resource application access, explicit optional write access, and explicit agent grants. Normal administrators never see or enter tenant IDs, application IDs, credentials, drive IDs, folder IDs, or Graph URLs.

Desktop composition: 1440×1000 application view with the canonical light Virtual Company shell; fixed white 240px left sidebar, pale #F7F9FC workspace, main content constrained to about 1200px. Settings breadcrumb, small uppercase SETTINGS kicker, page title “Connect Microsoft 365”, concise explanatory subtitle, and a close/cancel action. Under the header, show a horizontal eight-step progress treatment that remains legible: 1 Connect, 2 Admin sign-in, 3 Approve, 4 Source type, 5 Choose folders, 6 Access, 7 Agents, 8 Review. Steps 1–7 are visibly completed with restrained checkmarks; Review is active in blue.

Main layout: a wide white rounded review card on the left and a narrow “What happens next” reassurance card on the right. The review card contains clear grouped rows for Microsoft tenant, SharePoint source and approved root folder, read-only recommended access, no output folder, no agents selected, company publication audience, automatic initial import, and the exact Microsoft permission change. Use human-readable names only. Include a calm amber note saying no agents receive access until selected later. End with a required confirmation checkbox and a prominent blue “Connect repository” button plus secondary Back. The right card explains Connecting → Grant verification → Validation → Import queued → Connected, with small status icons and a security note that runtime access uses the Virtual Company application, not the administrator session.

Also show a compact secondary panel below the review explaining that customer-managed setup is available under a collapsed disclosure labelled “Advanced: use your own Entra application”, with an operational-burden warning. Do not show the legacy technical fields while collapsed.

Responsive intent: include a small inset mobile/narrow preview at the right edge or bottom showing a 390px-wide layout where the progress becomes a compact “Step 8 of 8 · Review” treatment, cards stack vertically, review rows remain readable, buttons become full-width, and every control has at least a 44px target.

Visual style: Inter typography; #0F172A primary text; #64748B secondary text; #2563EB primary; white cards with 1px #E5E7EB borders, 14–16px radii, subtle shadows, 16–24px padding, generous whitespace, restrained green completion and amber warning. Use familiar Microsoft, SharePoint, folder, shield, agents, and check icons without decorative illustration. Calm, operational, accessible, and action-oriented.

Text (verbatim where shown): “Connect Microsoft 365”; “Published to Virtual Company”; “Read only · Recommended”; “No agents selected”; “Connect repository”; “Advanced: use your own Entra application”; “Step 8 of 8 · Review”.

Constraints: preserve the canonical Settings shell and existing repository-management context; communicate folder-bounded access and company publication clearly; show server-derived review information, deliberate confirmation, pending background finalization, safe back/cancel behavior, keyboard-visible focus, and narrow-screen usability.

Avoid: dark or futuristic styling, gradients, chat UI, raw IDs, secrets, URLs, technical Graph payloads, tenant jargon, giant forms, synchronous-success fiction, mock data labels, dense tables, tiny controls, popup-only authentication, or a new primary navigation destination.
