"""Bounded provider microbenchmark, NOT a browser-call or Azure load benchmark.
Run --self-test offline or --live for three synthetic OpenAI requests.
No customer data, production service changes, retries, or cloud deployment.
"""
import argparse
import asyncio
import base64
import difflib
import hashlib
import json
import math
import os
from pathlib import Path
import statistics
import time
import unittest
from datetime import datetime, timezone

MODEL = 'gpt-realtime-2.1-mini'
VOICE = 'marin'
RATE = {'text_input': .60, 'text_cached': .06, 'audio_input': 10.,
        'audio_cached': .30, 'text_output': 2.40, 'audio_output': 20.}
TEXT = ('Welcome. This is a fictional sales presentation used only for a technical benchmark. '
        'Our example service helps a sales team prepare meetings, present approved material, '
        'and keep track of questions. The salesperson remains in control throughout the call. '
        'Customers can interrupt the presentation to ask a question, and the assistant pauses '
        'before responding. When a fact is not supported by the approved material, the assistant '
        'asks the salesperson to follow up. After the meeting, the team reviews the notes '
        'before sending anything to the customer. Let us now look at an example workflow.')
INSTRUCTIONS = 'Read the provided script verbatim in English at a natural presentation pace. Add nothing.'


def price(usage):
    i, o = usage['input_token_details'], usage['output_token_details']
    cached = i.get('cached_tokens_details', {})
    if i.get('image_tokens', 0):
        raise ValueError('Unpriced image input')
    if i.get('cached_tokens', 0) != sum(cached.values()):
        raise ValueError('Unattributed cached tokens')
    if usage['input_tokens'] != i.get('text_tokens', 0) + i.get('audio_tokens', 0):
        raise ValueError('Unattributed input tokens')
    if usage['output_tokens'] != o.get('text_tokens', 0) + o.get('audio_tokens', 0):
        raise ValueError('Unattributed output tokens')
    total = 0.
    for modality in ('text', 'audio'):
        count = i.get(modality + '_tokens', 0)
        hit = cached.get(modality + '_tokens', 0)
        if not 0 <= hit <= count:
            raise ValueError('Invalid cached count')
        total += (count-hit)*RATE[modality+'_input'] + hit*RATE[modality+'_cached']
        total += o.get(modality+'_tokens', 0)*RATE[modality+'_output']
    return total / 1_000_000


def normalize(text):
    return ''.join(c.lower() for c in text if c.isalnum() or c.isspace()).split()


async def generate(index, output):
    import websockets
    start = time.perf_counter()
    cpu_start = time.process_time()
    audio = bytearray()
    transcript = ''
    first = None
    async with asyncio.timeout(90):
        async with websockets.connect(
            'wss://api.openai.com/v1/realtime?model=' + MODEL,
            additional_headers={'Authorization': 'Bearer ' + os.environ['OPENAI_API_KEY']},
            max_size=4*1024*1024, open_timeout=15, close_timeout=5,
        ) as ws:
            await ws.send(json.dumps({'type': 'session.update', 'session': {
                'type': 'realtime', 'model': MODEL, 'instructions': INSTRUCTIONS,
                'output_modalities': ['audio'], 'max_output_tokens': 2048,
                'audio': {'input': {'turn_detection': None}, 'output': {
                    'format': {'type': 'audio/pcm', 'rate': 24000}, 'voice': VOICE}},
                'tools': []}}))
            while True:
                e = json.loads(await ws.recv())
                if e['type'] == 'error':
                    raise RuntimeError('provider_error:' + str(e.get('error', {}).get('code', 'unknown')))
                if e['type'] == 'session.updated':
                    actual_model = e['session'].get('model', MODEL)
                    break
            ready = time.perf_counter()
            await ws.send(json.dumps({'type': 'conversation.item.create', 'item': {
                'type': 'message', 'role': 'user', 'content': [{'type': 'input_text', 'text': TEXT}]}}))
            await ws.send(json.dumps({'type': 'response.create'}))
            while True:
                e = json.loads(await ws.recv())
                if e['type'] == 'error':
                    raise RuntimeError('provider_error:' + str(e.get('error', {}).get('code', 'unknown')))
                if e['type'] == 'response.output_audio.delta':
                    if first is None:
                        first = time.perf_counter()
                    audio.extend(base64.b64decode(e['delta']))
                elif e['type'] == 'response.output_audio_transcript.delta':
                    transcript += e['delta']
                elif e['type'] == 'response.done':
                    response = e['response']
                    usage = response.get('usage')
                    result = {'trial': index, 'status': response.get('status'), 'model': actual_model,
                              'usage': usage, 'transcript': transcript,
                              'generated_audio_seconds': len(audio)/48000,
                              'elapsed_seconds': time.perf_counter()-start,
                              'local_client_cpu_seconds': time.process_time()-cpu_start,
                              'first_audio_ms_including_connect': (first-start)*1000 if first else None,
                              'first_audio_ms_after_session_ready': (first-ready)*1000 if first else None,
                              'script_similarity': difflib.SequenceMatcher(None, normalize(TEXT), normalize(transcript)).ratio()}
                    if usage:
                        result['calculated_usd'] = price(usage)
                    if response.get('status') != 'completed' or not audio or not usage:
                        result['eligible_for_projection'] = False
                    else:
                        result['eligible_for_projection'] = result['script_similarity'] >= .98
                    audio_path = output / ('trial-' + str(index) + '.pcm')
                    audio_path.write_bytes(audio)
                    result['audio_sha256'] = hashlib.sha256(audio).hexdigest()
                    return result


def summary(rows):
    ok = [r for r in rows if r.get('eligible_for_projection')]
    if len(ok) != 3:
        return {'status': 'insufficient_completed_trials', 'completed_eligible': len(ok)}
    per_min = sum(r['calculated_usd'] for r in ok) / (sum(r['generated_audio_seconds'] for r in ok)/60)
    return {'status': 'microbenchmark_only', 'usd_per_generated_audio_minute': per_min,
            'median_first_audio_ms_including_connect': statistics.median(r['first_audio_ms_including_connect'] for r in ok),
            'observed_first_audio_ms_range': [min(r['first_audio_ms_including_connect'] for r in ok), max(r['first_audio_ms_including_connect'] for r in ok)],
            'projections_NOT_completed_call_measurements': [
                {'calls_reusing_one_approved_deck': n,
                 'live_narration_usd_per_call_for_18_spoken_minutes': per_min*18,
                 'cached_narration_usd_per_call_amortized': per_min*18/n,
                 'excluded': 'dialogue, listening/transcription, preparation reasoning, Azure, LiveKit, storage, transfer, retries, tax'}
                for n in (1, 10, 40, 173)],
            'azure_capacity_per_concurrent_call': None,
            'full_30_minute_call_cost': None}


async def run(output):
    output.mkdir(parents=True, exist_ok=True)
    rows = []
    for index in range(1, 4):
        try:
            row = await generate(index, output)
        except Exception as exc:
            # Do not log exception text, headers, tokens, or provider payloads.
            row = {'trial': index, 'status': 'failed', 'error_type': type(exc).__name__,
                   'error_code': str(exc) if str(exc).startswith('provider_error:') else None}
        rows.append(row)
        (output/'results.json').write_text(json.dumps({'utc': datetime.now(timezone.utc).isoformat(),
            'kind': 'direct_provider_component_benchmark', 'model': MODEL, 'voice': VOICE,
            'fixture': TEXT, 'rates_usd_per_million_tokens': RATE,
            'pricing_source': 'https://developers.openai.com/api/docs/pricing',
            'pricing_checked': '2026-09-09', 'rows': rows, 'summary': summary(rows)}, indent=2), encoding='utf-8')
        print(json.dumps({k: row[k] for k in ('trial','status','calculated_usd','generated_audio_seconds','script_similarity','error_type','error_code') if k in row}), flush=True)
        if row.get('status') != 'completed':
            break
    if len(rows) == 3 and all(r.get('eligible_for_projection') for r in rows):
        path = output/'trial-1.pcm'
        data_hash = hashlib.sha256(path.read_bytes()).hexdigest()
        timings = []
        for _ in range(10):
            t = time.perf_counter()
            with path.open('rb') as f:
                block = f.read(960)
            assert len(block) == 960
            timings.append((time.perf_counter()-t)*1000)
        cache = {'kind': 'local_filesystem_read_only_NOT_audible_playback_latency',
                 'reads': 10, 'median_first_20ms_frame_read_ms': statistics.median(timings),
                 'provider_requests_for_warm_cache_reads': 0, 'provider_usd_for_warm_cache_reads': 0,
                 'artifact_sha256': data_hash, 'notes': 'OS-warm local cache. No WebRTC, Azure storage, playback or network measured.'}
        (output/'cache-results.json').write_text(json.dumps(cache, indent=2), encoding='utf-8')
    print(json.dumps(summary(rows)), flush=True)


class AccountingTests(unittest.TestCase):
    def test_cached_usage(self):
        u = {'input_tokens': 100, 'output_tokens': 200,
             'input_token_details': {'text_tokens': 100, 'cached_tokens': 50, 'cached_tokens_details': {'text_tokens': 50}},
             'output_token_details': {'audio_tokens': 200}}
        self.assertAlmostEqual(price(u), (50*.6+50*.06+200*20)/1e6)
    def test_unpriced_usage_rejected(self):
        with self.assertRaises(ValueError):
            price({'input_token_details': {'image_tokens': 1}, 'output_token_details': {}})
    def test_missing_trials_not_projected(self):
        self.assertEqual(summary([])['status'], 'insufficient_completed_trials')


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--live', action='store_true')
    p.add_argument('--self-test', action='store_true')
    p.add_argument('--output', type=Path, default=Path('artifacts/sales-narration-benchmark/2026-09-09'))
    args = p.parse_args()
    if args.self_test:
        unittest.main(argv=['benchmark'], exit=True)
    elif args.live:
        if not os.environ.get('OPENAI_API_KEY'):
            raise SystemExit('OPENAI_API_KEY is not configured')
        if args.output.exists():
            raise SystemExit('Choose a new output directory; existing evidence is never overwritten')
        asyncio.run(run(args.output))
    else:
        p.print_help()
