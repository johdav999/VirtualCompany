# Daily treasury workspace reference prompt

Use case: ui-mockup

Asset type: High-fidelity desktop SaaS product reference screenshot for implementation of the Virtual Company Finance daily treasury workspace.

Primary request: Create a polished 1536×1024 desktop application screenshot for the existing Virtual Company product. The page is the consolidated Finance “Cash” workspace used by a finance manager at the start of each day. It must answer what cash is available, how fresh the source evidence is, what will move in the next 14 days, which bank feeds or payments need attention, and what the user should do next.

Product context: Virtual Company is a calm, web-first executive control room where named AI agents help operate a company and humans supervise decisions. Laura is the Finance Manager. The established shell has a fixed 240px left sidebar, a light `#F7F9FC` workspace, white cards, Inter typography, soft borders, restrained shadows, and blue `#2563EB` primary actions. The Finance section navigation includes Overview, Cash, Customer invoices, Supplier bills, Payments, Transactions, Accounting, and Issues. Connection administration is not part of this daily workspace.

Layout structure:

- Fixed white left sidebar with company switcher, primary navigation, Finance highlighted, and a small Laura finance-manager card near the bottom.
- Main workspace with 28px padding and a compact header: uppercase “FINANCE”, title “Daily cash”, subtitle “Review cash coverage, payment work, and the exceptions that need action.” Top-right actions: “Refresh evidence” secondary and “Review payment batches” primary.
- Horizontal Finance section chips below the header with Cash active.
- First row: four KPI cards for “Available cash” (SEK 1,248,500), “14-day projected cash” (SEK 1,084,200), “Expected in” (SEK 386,000), and “Expected out” (SEK 550,300). Each card includes a small evidence/freshness caption; use a warning tone on projected cash if it approaches a threshold.
- Second row is a 2/3 + 1/3 layout. Left: “Account coverage” card with three connected account rows. Show account name, masked number, current balance, last evidence time, feed coverage through date, and a health badge. One row is healthy; one says “Gap detected” with an orange “Recover gap” action; one says “Consent expired” with a red “Reconnect” action. Never imply these exception rows are current or successful.
- Right: “14-day outlook” card with a simple horizontal daily cash projection or restrained line/area chart, threshold line, starting cash, lowest point, and plain-language note. No decorative analytics overload.
- Third row is a 2/3 + 1/3 layout. Left: “Needs attention” prioritized action queue. Include: ambiguous payment submission with red “Reconciliation required” badge and “Review payment”; unreconciled bank transaction aged 9 days with orange “Reconcile”; missing feed range with “Recover gap”; approved payment waiting to be queued with “Review payment”. Each row includes severity, amount where relevant, source freshness, and a direct action.
- Right: “Payment work” summary showing Approved, Queued, Awaiting bank authorization, Rejected, and Reconciliation required counts. Visually emphasize rejected/ambiguous states. Provide a “Open payments” action.
- Bottom full-width Laura recommendation card in recommend-only mode. Show Laura avatar, “Laura, Finance Manager”, one concise evidence-grounded recommendation, a “Recommendation only” badge, three cited record chips/links under “Data used”, and a visible disclosure: “Missing evidence: operating account feed has a two-day gap.” Actions: “Review evidence” and “Message Laura”.
- Include a small page-level “Evidence updated 08:42” indicator and one stale-data warning banner when some sources are older than policy permits.

Responsive intent: The desktop reference should clearly imply that KPI cards wrap, the two-column sections stack, the chip navigation scrolls, and all row actions remain reachable on narrow screens.

Accessibility intent: Clear focusable buttons and links, high contrast, visible text labels beyond color, readable status badges, logical headings, no icon-only action controls.

Style/medium: High-fidelity production SaaS UI screenshot, crisp and implementation-ready, clean modern Nordic business software, restrained and trustworthy.

Color palette: Background `#F7F9FC`, white cards, primary blue `#2563EB`, text `#0F172A`, secondary text `#64748B`, borders `#E5E7EB`, success `#16A34A`, warning `#F59E0B`, danger `#DC2626`; use pale tonal fills for statuses.

Typography and spacing: Inter-like sans serif, headings at 600 weight, 14–16px card radii, 16–24px internal padding, consistent 24px section gaps, dense but readable operational rows.

Text constraints: Use only concise English business labels described above. All amounts are Swedish kronor (SEK). Make “Reconciliation required”, “Gap detected”, “Consent expired”, “Recommendation only”, and “Missing evidence” clearly legible.

Avoid: dark futuristic styling, gradients, glassmorphism, neon, chat-first layout, generic admin dashboard appearance, mock-device frame, excessive charts, decorative illustrations, provider logos, exposed IDs, raw enum values, technical provider errors, or any state that makes accepted/submitted/ambiguous payments look settled.
