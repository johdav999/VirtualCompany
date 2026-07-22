# API problem-code convention

User-visible API failures use RFC 7807 with an invariant namespaced `code`, allow-listed structured `arguments`, an English `title`/`detail` compatibility fallback, and `traceId`. Codes are append-only contracts and do not vary with `Accept-Language`.

Web maps known codes to feature or shared resources. Unknown and legacy problems retain a safe fallback and correlation guidance. Arguments must never contain stack traces, SQL/provider payloads, credentials, tokens, recipient content, or identifiers that would reveal cross-company resource existence.
