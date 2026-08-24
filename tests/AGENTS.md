# Test guidance

These instructions apply to test code under `/tests`.

- MUST read and follow the `Test Architecture` section of
  `/docs/architecture-rules.md`.
- Use that document as the sole authority for test-project ownership,
  tenant-isolation coverage, provider compatibility, fixture isolation, and
  sensitive-behavior coverage; do not duplicate those rules here.
- Follow the repository-wide execution discipline: run the narrowest relevant
  tests first and avoid repeating an unchanged full matrix without a concrete
  reason.
