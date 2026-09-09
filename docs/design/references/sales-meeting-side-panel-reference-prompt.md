# Alex sales meeting side-panel reference prompt

Use case: ui-mockup

Asset type: high-fidelity production UI reference for the salesperson-private Blazor `meetingSidePanel` surface of a Microsoft Teams meeting app

Primary request: Create a shippable private Alex sales cockpit that runs inside the narrow Microsoft Teams in-meeting side-panel iframe. It helps one salesperson present confidently while the customer sees only the separate shared stage.

Existing product context: Virtual Company is an executive control-center SaaS. Its Sales and agent workspaces use Inter, clear plain-English hierarchy, white cards, restrained borders, subtle shadows, `#2563EB` primary blue, `#16A34A` success, `#F59E0B` warning, and `#DC2626` danger. Adapt these tokens to a calm Teams dark meeting theme without introducing a second brand language. Alex is optimistic and opportunity-focused.

Canvas and layout: Tall narrow desktop panel representing the documented 280px-wide Teams in-meeting content area, with 20px left/right padding, one vertical column, sticky compact header and sticky bottom presentation controls, vertical scrolling only, no clipped primary action. The panel must remain useful at roughly 280×720 and scale cleanly to a wider browser-hosted test view.

Header: Alex sparkle avatar, “Alex · Sales sidekick”, a small green “Synced” status with autosave copy “Saved just now”, and the standard share icon action labeled “Share to meeting”.

Content order:

1. A compact lifecycle/status row: “Presenting” and “12:18 remaining”.
2. A 16:9 current-slide thumbnail titled “One operating view”, with “4 of 12”.
3. The primary blue-tinted card titled “Next talking point” containing “Connect the finance, sales, and support handoffs.”, a “~45 sec” timing chip, source “Operating model · p. 6”, and a clearly labeled “High confidence” evidence state.
4. A compact “After this” transition line: “Next: show how approvals stay human-controlled.”
5. A “Questions” section in a truthful empty/unavailable state: “No questions entered” and “Live question detection is not available yet.” Include one enabled text entry affordance labeled “Add a question” only if it can be clearly distinguished from unavailable automatic detection.
6. A compact slide finder with search field placeholder “Search slides” and a “Go to slide” affordance.
7. Sticky bottom controls in this order: icon button “Previous slide”, central pause button “Pause presentation”, primary icon button “Next slide”. Keyboard shortcut hints may be subtle but readable.

Interaction states: Loading, empty deck, conflict/resync, offline/reconnecting, and command-in-progress must fit this structure without layout jumps. Show the normal connected state in this reference. Controls need strong focus rings, 44px minimum targets, plain-English labels, and screen-reader-compatible status placement.

Style/medium: realistic product UI screenshot, pixel-precise narrow SaaS panel, Microsoft Teams-compatible dark meeting surface, not concept art

Visual hierarchy: next talking point first, current slide second, state/timing third, search and unavailable future capture lower. Use compact spacing without feeling dense; 8px spacing rhythm; crisp borders; subtle shadows; restrained blue accents.

Privacy constraints: This surface is explicitly salesperson-private. It may show talking-point guidance, timing, source, and confidence, but it must not imply the customer can see them. Do not show invented customer facts or mock detected speech.

Constraints: single column only; no horizontal scrolling; no multi-column dashboard; no modal; no charts; no raw transcript; no microphone control; no raw audio claim; no fake detected questions; no unverified pricing claim; no browser chrome; no device mockup; no watermark; no extra text beyond the specified interface copy.

