# Teams presenter administration reference prompt

Use case: ui-mockup

Asset type: high-fidelity production UI reference for the Virtual Company platform-administration page at `/system/admin/teams-presenter`

Primary request: Create a shippable system-admin workspace for configuring and safely rolling out Alex as a Microsoft Teams meeting presenter. The page must answer whether a selected company is ready, what blocks it, and which controlled action the administrator should take next. Never display credentials or secret values.

Existing product context: Virtual Company is an executive control-center SaaS using Inter, `#F7F9FC` background, white cards, `#2563EB` primary blue, `#16A34A` success, `#F59E0B` warning, `#DC2626` danger, 12px card radii, restrained borders, and subtle shadows. Match the existing Settings and system-administration shell.

Canvas and layout: 1440x1000 desktop SaaS screenshot. Fixed 240px product navigation on the left, centered 1080px administration workspace, responsive card layout. Header contains kicker “SYSTEM ADMINISTRATION”, title “Teams presenter”, plain-language summary, selected company context, a “Refresh checks” button, and a disabled-by-default production status badge.

Content structure:

1. A four-card readiness row for Tenant association, App package, Calling and media, and Controlled rollout. Each card has a concise state, one explanatory sentence, and a status badge.
2. A wide “Required access” card with two columns: required versus granted Microsoft Graph application permissions, exact names visible, green matches, and an amber missing-state treatment. Include admin consent state, calling policy attestation, callback health, media host health, certificate expiry, and package compatibility without exposing secrets.
3. A “Pilot rollout” card showing disabled-by-default production gate, selected company allowed/not allowed, user allowlist count, concurrency limit, cost guardrail, package version requirement, and browser/typed fallback. Include primary “Enable pilot” only when all gates pass and a danger-outline “Emergency disable” action.
4. A “Safe remediation” card listing short next actions: Install or update Teams app, Grant admin consent, Verify Teams app/calling policies, Validate public callbacks and certificate. Each is a clearly labeled link, never a claim that Virtual Company can bypass the Teams admin center.
5. A compact “Release evidence” section with the latest automated suite state and live Teams UAT state, owner, date, and unresolved blocker count. Show live tenant evidence as “Not recorded — production remains disabled”.

States: Show a realistic partially configured state: tenant associated and package compatible; admin consent granted; Teams policy and live UAT still require evidence; rollout remains disabled. Include loading, forbidden, empty-company, and provider-outage states in the design language, but show the normal partially configured state in the reference.

Interaction and accessibility: 44px controls, visible focus rings, semantic status text in addition to color, readable permission names, no toggle that visually promises success before backend verification, clear confirmation for emergency disable, keyboard-friendly order, and responsive stacking below tablet width.

Style/medium: realistic pixel-precise production SaaS UI screenshot, not concept art

Constraints: no charts; no decorative gradients; no secret values; no raw tokens; no customer data; no fabricated certificate thumbprint; no fake live-test pass; no browser chrome; no device mockup; no watermark; no dense generic cloud-console aesthetic.
