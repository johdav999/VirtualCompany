# Issue ledger

- Fixed: request changes did not record actual step reviewer/time; original linked work could remain executable.
- Fixed: stale material/step and repeated commands could cross review versions; transaction claim and bound replay now tested, including isolated SQL Server.
- Fixed: offline approval commands fabricated success; unavailable is explicit.
- Fixed: narrow comparison/policy columns clipped, list buttons lacked useful styling; container columns and visible focus/wrapping accepted in desktop/mobile captures.
- Fixed: source task link used approval itemId; taskId now selects the actual task.
- Fixed: same-company Work navigation did not update the decision return component; explicit query binding and location refresh, with regression and final real-browser acceptance. Host restart also required a fresh circuit reload; stopped-host negotiation errors are diagnostic only.
- Diagnostic: initial `p12-focused-api.trx` has three unrelated pre-existing Sales session/presentation assertions (already-started wording, change_preset action). Their production/test files are unchanged by P12. Final relevant owner and decision suite 152/152 passes. This is not a claim that every repository test passes.
- Diagnostic: initial SQL check failed sandbox SSPI; isolated SQLEXPRESS check passed outside sandbox. Initial EF check failed the sandbox AppData key ring; workspace-local key ring check passed without a schema change. Test-only Newtonsoft JsonNode transport loop corrected by explicit System.Text.Json HTTP extension; assertions unchanged.
- Remaining independent acceptance: live-provider credentials/approved real recipient, deployed tenant, named human release approval and earlier physical/native/statutory gates. No P12 implementation work deferred.
