# Browser sales room floor-control reference prompt

Create a polished desktop SaaS product screenshot for Virtual Company’s browser sales room during a live customer presentation. The product is an executive control center where a human salesperson supervises one AI sales agent. Use the established Virtual Company style: warm off-white page background, white cards, charcoal text, restrained indigo primary actions, muted blue and green status accents, subtle borders and shadows, 12-pixel corner radii, compact professional typography, generous but efficient spacing, and no decorative gradients.

Show a 1440×1000 desktop browser view. The main content uses a wide two-column layout. On the left, show the live meeting stage with a customer-safe slide, compact participant tiles for one salesperson and two customers, microphone/camera controls, and a clear banner that the human call continues while AI is paused. On the right, show a private “Alex · Sales agent” control card focused on dialogue and floor control.

The agent card must show:

- A prominent floor status row: “Floor: Sofia (host)” with a calm green human-owned indicator, and a smaller line “AI paused at slide 4 · point 2”.
- Three compact presentation mode choices: Manual selected, Assisted, Autonomous.
- A large high-priority indigo “Resume from current point” button and a prominent red-outline “Take over” button.
- A concise pending-turn card: “Customer asked Alex” with an addressed-question label, speaker name, and actions appropriate to the selected mode. Show “Approve answer” and “Dismiss” in assisted mode examples.
- An interruption state with “Playback stopped · 3/3 clients acknowledged” and a small measured time badge such as “184 ms”.
- Audience readiness for the current slide: “3 of 3 rendered”. Also show a small degraded example note saying autonomous narration pauses when a required client is missing, with an explicit “Continue anyway” host override.
- A bounded activity list showing: human speech detected, narration paused, addressed question proposed, evidence checked, answer released, floor returned to host. Keep it concise and scannable.
- A reassuring fallback note that manual slides and human audio continue if the agent is unavailable.

The visual hierarchy must emphasize who owns the floor, whether the agent may speak, and the two host actions “Take over” and “Resume”. Make private controls visually distinct from the customer-visible stage. Avoid chat-bot styling, waveform decoration, dense dashboards, tiny text, or a second navigation system.

Also imply responsive behavior: cards should be able to stack cleanly on a narrow mobile viewport, with primary controls full width and no horizontal overflow.
