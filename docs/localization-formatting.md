# Localized presentation formatting

User-visible dates, numbers, percentages, and money are formatted by the Web presentation services in `Localization/Formatting`. They use the active formatting culture, which falls back to the UI culture.

Instants must be supplied as UTC and converted with the active company's IANA/Windows timezone identifier. An absent or invalid timezone deterministically falls back to UTC; server-local time is never used. Money always keeps the supplied ISO 4217 code and never infers currency from culture. Machine payloads, exports, provider messages, logs, hashes, and identifiers remain invariant.

Examples for `22000 SEK` are `SEK 22,000.00` in `en-GB` and `22 000,00 SEK` in `sv-SE`. The same UTC instant formatted for `Europe/Stockholm` represents the same point in time in either culture.
