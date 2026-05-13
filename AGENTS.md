## Architecture



When changing backend, data, workflow, agent, integration, approval, or AI orchestration code:



- MUST follow `/docs/architecture-rules.md`

- Use `/docs/architecture-overview.md` only as background context

- Existing repository implementation wins if it conflicts with older planning documents



## Design / UI

When changing frontend UI, layout, styling, dashboards, agent cards, approval views, finance views, or user-facing text:

- MUST follow `/docs/design.md`
- Reuse existing design tokens, components, spacing, typography, colors, and interaction patterns defined there
- Do not introduce a new visual style unless explicitly requested
- Existing implemented design system wins if it conflicts with older planning documents
- Keep user-facing language plain English; avoid internal workflow names, enum names, or technical identifiers

<!-- design-addin-instructions:start -->
# Design Add-in Workspace Instructions

- When a prompt asks for architecture-sensitive implementation, include and follow `architecture-inst.md`.
- When a prompt asks for UI, UX, styling, layout, components, navigation, or frontend implementation, include and follow `ui-instructions.md` if it exists.
- Always include and follow `production-implementation.md` in every prompt.
- Treat these generated instruction files as project context. If they conflict with explicit user instructions, follow the user and note the conflict.
<!-- design-addin-instructions:end -->
