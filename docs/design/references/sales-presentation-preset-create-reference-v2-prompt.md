# Sales presentation preset creation — production UI reference prompt

Use case: ui-mockup

Asset type: high-fidelity desktop SaaS modal reference screenshot

Input image: the supplied screenshot is the current implementation and a defect reference only. Preserve the product purpose and field set, but replace its cramped browser-default form appearance with a polished production design.

Primary request: Design a professional “Create presentation preset” modal for Virtual Company, an executive business operations SaaS product. The modal creates reusable Sales presentation defaults before a PowerPoint is uploaded.

Product style: Inter typography; light mode; page background #F7F9FC; white cards; primary #2563EB; slate text; subtle cool-gray borders; 12–16 px radii; restrained shadows; crisp enterprise SaaS finish. Clarity over decoration.

Canvas and composition: 1440×1100 desktop application screenshot. Show a dimmed application shell behind a centered modal approximately 900 px wide and comfortably contained within the viewport. The modal has a fixed visual header, scrollable content, and a clear footer. Generous 24–32 px padding and a consistent 8 px spacing rhythm.

Header text (verbatim): “NEW PRESET”, “Create presentation preset”, “Define reusable defaults now. Add and process the PowerPoint after the preset is saved.” Include one quiet close control in the top-right.

Modal body structure:
- A compact blue-tinted guidance banner titled “Reusable by design” explaining that customer and meeting-specific facts are added only when the preset is used.
- Section 1 title “Identity & ownership”. A clean two-column grid with Name and Owner on the first row, Description full-width below. Owner is visibly read-only.
- Section 2 title “Presentation defaults”. Two-column grid with Default presenter and Language, Duration and Control mode, then Audience and Goal as balanced full-width fields, and Demo scenario as a full-width optional textarea.
- Section 3 title “Where this preset can be used”. Three selectable situation cards in one row: Sales meetings, Campaign activities, Ad-hoc presentations. Selected cards use a light-blue surface and blue check indicator; each card includes one short explanatory line.
- Section 4 title “Presenter behavior”. Two spacious checkbox rows: Pause for questions between sections; Show speaker notes to the presenter.

Footer: subtle top border, left-aligned helper text “You can upload the PowerPoint after creating the draft.”, secondary “Cancel” button, and prominent blue “Create preset” button on the right.

Interaction states: labels always above controls; minimum 44 px control height; visible but tasteful focus border on the Name field; helper/error space reserved without causing uneven rows; no placeholder data that looks real.

Responsive intent: design should clearly translate to a single-column mobile sheet with sticky actions, without changing the information order.

Constraints: realistic shippable product UI, not concept art; no stock photography; no decorative illustration; no extra navigation; no gradients; no glassmorphism; no oversized title; no browser-default controls; no clipped content; no overlapping labels; no tiny type; no watermark; render the supplied text exactly and do not add marketing slogans.
