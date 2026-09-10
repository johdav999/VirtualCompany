# Sales narration benchmark

Run date: 9 September 2026, updated 10 September 2026. Status: provider component benchmark completed; the Prompt 10 full-call harness and instrumentation are implemented, while live 30-minute/Azure measurement remains blocked by missing authorized runtime prerequisites.

## What ran

Three real, sequential OpenAI Realtime requests generated the same fictional English narration, using the repository-default `gpt-realtime-2.1-mini` and `marin`. Each used a fresh session, fixed script, no customer data, no tools, no audio input/transcription, a 2,048 output-token ceiling and a 90-second deadline. There were no retries. Output was requested as PCM16 mono at 24 kHz.

The benchmark is an isolated external test client, not a second production orchestration stack. It exercises the provider directly and does not certify Virtual Company's .NET gateways, policy, media worker, WebRTC, human playback or Azure deployment. Teams code/configuration/resources were not changed.

The returned provider transcripts matched the script after case/punctuation normalization. This is an automated content check, not an independent listening assessment. Three trials are too few to claim reliable p95 latency or a general cost guarantee.

## Measured usage and calculated cost

Cost is calculated from returned usage at the public Standard USD rate card checked on the run date, not an invoice reconciliation: text input $0.60/M, cached text $0.06/M, audio input $10/M, cached audio $0.30/M, text output $2.40/M, audio output $20/M. The script rejects unattributed input/output or cache tokens. Reasoning tokens are a detail within output accounting and must not be charged again on top of the modality totals. No region uplift, negotiated discount or tax is assumed. Source: https://developers.openai.com/api/docs/pricing

| Trial | Generated audio | Cost from returned usage | First audio, including connection | First audio after session ready |
| --- | ---: | ---: | ---: | ---: |
| 1 | 31.45 s | $0.0130612 | 2.689 s | 0.628 s |
| 2 | 32.75 s | $0.0135740 | 2.414 s | 0.571 s |
| 3 | 33.20 s | $0.0137684 | 1.322 s | 0.585 s |

Total generated audio: 97.4 seconds. Total calculated provider cost: $0.0404036. Weighted cost: **$0.024889 per generated audio minute**. Median first chunk after the session was ready: 0.585 seconds. These are server-received chunks, not time to audible customer playback.

The first generated artifact was reused for ten local reads of its first 20-ms frame. These caused zero additional provider requests/cost. Median local read time was 0.038 ms, but this is an OS-warm filesystem measurement and must not be compared with audible/network latency. No cached playback capacity was measured.

Evidence: `/artifacts/sales-narration-benchmark/2026-09-09/results.json` and `cache-results.json`. Generated synthetic PCM artifacts sit beside them; they contain no human meeting recordings. Source: `/tests/benchmarks/sales-narration/benchmark.py`. Three offline accounting/validation tests passed.

## Projection for a 30-minute meeting, NOT a measured full call

Assume 18 minutes of reusable approved narration, six minutes of human questions/discussion, three minutes of live agent answers and three minutes of pauses/host activity. The following covers ONLY those 18 narration minutes, scaled from this short fixed-script sample. It does not include Q&A, listening/transcription, text/script preparation, longer conversation context, wasted/cancelled output, retries, cache storage/delivery, Azure or LiveKit.

| Narration approach | Projected narration generation cost per meeting |
| --- | ---: |
| Regenerate all 18 minutes each call | $0.4480 |
| Cache, first use only | $0.4480 |
| Cache, generation amortized across 10 identical-deck uses | $0.0448 |
| Cache, amortized across 40 uses | $0.0112 |
| Cache, amortized across 173 uses | $0.0026 |

After a valid cached asset exists, warm reuse itself adds no OpenAI generation charge. It still incurs playback/transport/storage work. Amortization includes the one-time generation cost. The assumed reuse count applies per company, approved deck revision, language and voice; do not pool private/customer-specific narration across tenants. Changes invalidate affected segments and incur fresh generation cost.

At 40 calls/week and 52/12 weeks/month, the 173.33 calls/customer/month imply about **$77.65/customer/month for regenerated narration alone**, or **$1.94/customer/month when generation is amortized across 40 uses**. These are limited projections, not a replacement for the full AI budget. Savings on one component do not imply the same percentage saving on the total bill.

A separate potentially large cost is transcription. The current repository defaults to `gpt-realtime-whisper`, whose published live-transcription rate is $0.017 per streamed minute. If the future implementation streams two human tracks for all 30 minutes, that assumption alone means 60 billed audio minutes or $1.02/call, before any speech generation. This is a pricing sensitivity, NOT a measured bill or confirmation of actual silence/track billing for our unfinished implementation. Avoid combining continuous streaming minutes with speech-only minutes in estimates. Verify duration metering and alternative supported transcription paths during the full test. Source: https://developers.openai.com/api/docs/pricing

## What prevents the requested full measurement

- Prompts 1–9 now provide production LiveKit, narration-cache, speaking-agent, floor-control and closing paths, and Prompt 10 adds the production metrics, frozen full-call harness and disposable deployment template.
- `OPENAI_API_KEY` is available; LiveKit URL/key/secret and permitted synthetic participant credentials are not present in this process environment.
- Azure CLI is signed in, but no named, explicitly authorized isolated browser-room target or separate load generator has been supplied. No existing App Service was treated as authorized, and no load or deployment occurred.
- No named authorization owner, maximum spend or reviewed concurrency ceiling has been supplied.

Therefore `full_30_minute_call_cost` and `azure_capacity_per_concurrent_call` are explicitly null in evidence. Local Python CPU timings are diagnostic only and cannot be used to size the .NET Azure worker.

The current machine-readable blocked result and reproduction instructions are in [the Prompt 10 benchmark report](verification/browser-sales-room/prompt10-benchmark.md).

## Full-call benchmark protocol

Prerequisites: implemented browser room and approved-speech publication boundary, configured LiveKit test project, isolated authorized Azure worker/app deployment, test tenant/fixture, actual regional/SKU rates and existing policy/consent gates. Follow `/production-implementation.md`, `/docs/architecture-rules.md`, applicable AGENTS and `/docs/teams-preservation-and-reactivation.md`. No Teams reactivation or production load is required.

Compare matched 30-minute calls:

- A: approved narration generated live per segment, grounded questions answered live.
- B: the same approved narration from cache; the identical questions answered live using the same model, policy, voice and knowledge.
- Report cache construction/cold miss separately. Evaluate reuse counts 1/10/40/80 and per-segment invalidation.
- Add a separate exploratory A2 arm for genuinely improvised live narration only if desired; do not confuse changing content/reasoning with the isolated caching effect measured by A versus B.

Use the 18/6/3/3 minute schedule above, two human participants and one agent. Freeze fixture hashes, deck content, language, question script, models/version, region, audio rates, video settings and grounding results. Include fixed-time barge-in, human takeover, resume, slide render delay and reconnect in both arms. Replay consented synthetic human audio with stable track identities. Do not replay agent audio into its own input. Run English and Swedish separately. Verify script accuracy, grounded Q&A, public/private isolation and interruption/resume quality; cheaper incorrect output fails.

Measure a baseline with no calls, then identical concurrency steps of 1, 5, 10, 25 and 50, with warmup, a full 30-minute measured interval and drain for each arm. Randomize A/B order and repeat low-concurrency paired runs. Increase to 100 only if the environment budget/capacity is explicitly suitable. At 4,000 weekly calls and a 40-hour business week, the average is 50 simultaneous calls; peaks need headroom and measurement. Never infer peak capacity from monthly minutes alone.

Capture once per second: host/container CPU seconds, allocated and working-set memory, GC/thread-pool delay, active/queued sessions, reconnects, dropped/late frames, network bytes, process/revision/instance identity and scale events. Keep the load generator on separate resources and exclude it from the application cost. Count all application instances/sidecars/backplane resources required by the feature. Use Azure Monitor/platform metrics appropriate to the selected service; do not provision telemetry per audio frame.

Per call capture actual model request/session IDs and usage by modality/cache, transcription metered duration, generated versus played versus cancelled audio, cache key/hit/miss, grounding/tool usage, first audible speech, takeover-to-silence, slide acknowledgement and outcome. Keep credentials, raw provider payloads and real customer content out of logs. Collect all costs for failed attempts too; successful-only usage understates operating cost.

Compute:

- Variable provider cost per completed call = all arm provider charges, including failures/retries, divided by successfully completed quality-passing calls.
- Cache cost per use = generation/refresh cost divided by actual authorized uses, plus storage/reads/transfer; report warm marginal cost separately.
- Incremental CPU per active call = (loaded CPU core-seconds - matched baseline core-seconds) / integrated active-call seconds. Measure memory at each concurrency; do not assume linearity.
- Sustainable capacity = highest tested concurrency meeting quality/latency thresholds, without growing queues/errors or resource exhaustion. It is not the same as maximum sockets opened.
- Incremental Azure cost per completed call = actual extra billed compute, data/storage/operations and telemetry divided by completions. Report existing fixed hosting separately. Reserved capacity, idle time and autoscaling minimums require explicit allocation, not an invented flat per-call price.

Target the architecture's p95 join <5 s, takeover-to-silence <300 ms, answer start <3 s and slide sync <500 ms under the agreed network; record misses rather than silently dropping difficult calls. No p95 claim from the three microbenchmark samples. Full cost must also report LiveKit separately, so narration savings do not hide unchanged transport costs.

## Reproduce the completed component test

Requires Python and `websockets` (tested with Python 3.12 and websockets 16.0). The script reads `OPENAI_API_KEY` from the environment and never prints it. Run from repository root:

```powershell
python tests/benchmarks/sales-narration/benchmark.py --self-test
python tests/benchmarks/sales-narration/benchmark.py --live --output artifacts/sales-narration-benchmark/new-run
```

The live command makes at most three sequential requests, with no automatic retries, 2,048 output tokens each, fixed short input and a 90-second timeout each. It refuses to overwrite an existing evidence directory. Provider failure or incomplete/unpriced data stops reliable projection. There is no Azure deploy/load action in this script.

## Additional benchmark requirement: speech detection first

The initial browser input policy now uses local per-participant VAD before external transcription/AI processing. This decision does not change the completed narration-only measurements above; input audio and transcription were not exercised in those runs.

In the full-call benchmark, compare continuous input with speech-detected input using identical audio fixtures at 10%, 30% and 60% speech duty cycles. Include quiet speech, short words, mid-sentence pauses, English/Swedish, overlapping speakers, background noise and agent-output echo. Preserve immediate speech-start interruption of cached and live narration. Keep utterance-finalization delay separate from interruption latency.

Record worker CPU/memory overhead, first/last-word loss, missed speech, false triggers, transcription quality, time to first answer and received/detected/forwarded audio durations. Reconcile actual billed provider duration/tokens, session minimums and reconnect overhead. Do not reduce the cost estimate by the silence percentage unless provider usage confirms it. The earlier $1.02/call continuous-two-track example remains a comparison scenario, not the chosen operating policy or a measured bill.
