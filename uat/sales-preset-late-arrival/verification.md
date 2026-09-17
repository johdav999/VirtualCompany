# PRESET-001: Late arrival and stale meeting state

Local Virtual Company. Organizer preparation route: /app/sales/meeting-invitations/4c3c2af2-98ba-4ea5-8045-ed49d4e7445f/prepare.
Reported screenshot: selector disabled after scheduled time, with "already started" notice.
Evidence: the selector and apply service gated on session Status != Ready, not the clock. This reused invitation retained Presenting, slide 1, sequence 22, with room LiveStartedUtc from September 14 despite its September 16 schedule.

P1 acceptance: organizer may attach/replace a preset while the browser room is unoccupied and agent not started/stopped/paused, including after scheduled start. Connected participants, a running agent, ended room, or terminal session block changes. Non-browser meetings preserve existing lifecycle policy.

Implementation: shared company-scoped eligibility query used by read model and command; UI consumes change_preset action. Applying reopens preparation in the existing transaction, increments concurrency version, resets presentation cursor, retains prior decks/run history and sequence. Serializable command transaction protects occupancy checks from concurrent changes.

Validation: 18 focused backend tests and 2 Blazor selector tests passed. Original browser flow was checked through component rendering and local database evidence; browser automation remains unavailable due to the Windows sandbox ACL startup failure. No claim of a live click-through or customer-visible preset application (a preset selection was not requested).

Deployment: API and Web LocalRun builds succeeded. Tracked local hosts restarted; API database/browser-room checks Healthy and Web HTTP 200. Reported room has zero connected participants and agent stopped, so shared eligibility permits preset changes.
