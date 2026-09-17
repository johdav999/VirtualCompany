# Sales calendar recovery — 2026-09-15

Product: Virtual Company local Web/API, current working tree. Role: company member preparing a sales demo. Entry: lead detail, Schedule demo. Baselines: user screenshots with expired Google grant and generic sales validation toast.

## Scope and evidence

| ID | Severity | Flow | Expected / implemented | Verification |
|---|---|---|---|---|
| CAL-001 | P2 | Submit / availability with expired authorization | Recognize exact provider invalid_grant, translate to calendar.reconnect_required, redirect within the same company to Calendar connections with a persistent explanation. Do not sign in or resubmit automatically. | OAuth adapter tests, typed-client tests, actual page navigation method with bUnit navigation substitute |
| CAL-002 | P2 | Invalid scheduling fields | Preserve backend field validation messages instead of replacing them with a generic localized error. Show the reason beside Submit and keep entered data. | Client regression with generic resolver; compiled Razor/source review for inline alert and retained form |
| CAL-003 | P3 | Meeting type | Evaluate OnlineMeetingLabel rather than rendering its identifier literally. | Compiled Razor/source review |

The start-time rule rejects starts within five minutes of the current UTC time. The screenshot alone does not identify the exact failed field or submission timestamp; no claim that this was definitively the failed rule. Backend validation remains authoritative and was not relaxed.

## Verification results

- SalesBrowserMeetingSchedulingTests: 8 passed (Web).
- MailboxCalendarOAuthScopeTests: 5 passed (API). Temporary provider errors do not become reconnect errors.
- A damaged compiler-reference cache initially prevented tests; rebuilt the migrations project successfully to regenerate build artifacts. No database operation or schema change occurred.
- API and Web compiled through the focused test builds. Existing unrelated warnings remain.
- Scoped git diff --check passed.

Live Google OAuth and actual approval submission were not exercised: doing so would alter the user's authorization or create an approval request. No live invitation was created, retried, or sent. Existing screens/messages are reused; this is not a redesign requiring new imagery.

Next manual check after normal API/Web restart: choose an invalid time and submit to see the precise persistent validation reason. With an expired test connection, submit or check availability and confirm the company-scoped Calendar connections destination and notice. Reconnect explicitly, return to the lead, choose a future time, and submit only when intended.
