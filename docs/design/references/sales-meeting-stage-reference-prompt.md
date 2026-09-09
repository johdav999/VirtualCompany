# Alex sales meeting stage reference prompt

Use case: ui-mockup

Asset type: high-fidelity production UI reference for the Blazor `meetingStage` surface of a Microsoft Teams meeting app

Primary request: Create a shippable customer-visible presentation stage for Alex, the Virtual Company Sales Manager. This is the content rendered inside the Teams shared-meeting-stage iframe, not a marketing illustration and not a static PowerPoint editor.

Existing product context: Virtual Company is an executive control-center SaaS. Its current Sales experience uses Inter, white cards, restrained borders, subtle shadows, `#2563EB` blue, `#16A34A` success, `#F59E0B` warning, and `#DC2626` danger. In a Teams meeting, adapt the surrounding chrome to a quiet dark theme while preserving a bright, exact-aspect-ratio rendered slide.

Canvas and layout: Landscape desktop reference with the proportions of the default 994×678 Teams shared stage. Full-bleed responsive app surface with 20px outer padding and no horizontal scrolling. A very slim top status bar contains the Alex sparkle mark, “Alex presentation”, a green “Connected” indicator, and “Slide 4 of 12”. The central focus is one large 16:9 rendered slide, centered and completely visible with letterboxing where needed. Below it, a compact status strip shows “Presenting” and “Shared by Alex Morgan”. Keep all app controls and navigation visually secondary to the slide.

Rendered slide content: A refined white SaaS presentation slide titled “One operating view for the whole company”. Show three simple connected capability cards labeled “Finance”, “Sales”, and “Support”, one concise line of supporting copy, and a small blue Virtual Company mark. Typography must be readable and the slide must look like a deterministic rendered asset rather than editable HTML.

State language: The live state is calm and explicit. The same hierarchy must clearly accommodate paused, answering, closing, and reconnecting banners without moving or cropping the slide. Show only the active “Presenting” state in this reference.

Style/medium: realistic product UI screenshot, pixel-precise SaaS design, clean Microsoft Teams-compatible dark meeting canvas, not concept art

Visual hierarchy: the slide is dominant; presentation state is second; connection and attribution are tertiary. Generous negative space, crisp 1px borders, 12px radii only on app-owned cards, subtle shadows, restrained blue accents.

Privacy constraints: Customer-safe content only. Absolutely no speaker notes, talking points, sources, confidence, questions, internal intelligence, deal context, customer profile, controls, search, or salesperson suggestions.

Constraints: no desktop PowerPoint chrome; no browser chrome; no device mockup; no Teams participant video tiles; no fake chat; no gradients; no glassmorphism; no decorative analytics; no horizontal scrolling; no watermark; no extra text beyond the specified interface and slide copy.

