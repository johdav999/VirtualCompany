import test from "node:test";
import assert from "node:assert/strict";
import { createSpeechOnsetGate, hasBroadbandContent } from "../../../src/VirtualCompany.Web/wwwroot/js/speech-aware-browser-interruption.mjs";

test("noise and isolated impulses cannot confirm speech", () => {
    const decisions = [];
    const gate = createSpeechOnsetGate(decision => decisions.push(decision));
    for (const probability of [0.1, 0.9, 0.1, 0.1, 0.1, 0.8, 0.1, 0.1, 0.1, 0.1]) gate.process(probability);
    assert.equal(decisions.length, 0);
});

test("sustained speech confirms once and rearms after silence", () => {
    const decisions = [];
    const gate = createSpeechOnsetGate(decision => decisions.push(decision));
    for (const probability of [0.7, 0.8, 0.9, 0.8, 0.7, 0.8, 0.1, 0.6]) gate.process(probability);
    assert.equal(decisions.length, 1);
    assert.equal(decisions[0].policy, "silero-v5-6of8-v1");
    for (let i = 0; i < 20; i++) gate.process(0.9);
    assert.equal(decisions.length, 1);
    for (let i = 0; i < 3; i++) gate.process(0.1);
    for (let i = 0; i < 8; i++) gate.process(0.9);
    assert.equal(decisions.length, 2);
});

test("invalid confidence never confirms speech", () => {
    const decisions = [];
    const gate = createSpeechOnsetGate(decision => decisions.push(decision));
    for (let i = 0; i < 20; i++) gate.process(Number.NaN);
    assert.equal(decisions.length, 0);
});

test("a narrow 200 Hz tone is rejected even if the model reports high speech probability", () => {
    const decisions = [];
    const gate = createSpeechOnsetGate(decision => decisions.push(decision));
    const tone = Float32Array.from({ length: 512 }, (_, i) => 0.15 * Math.sin(2 * Math.PI * 200 * i / 16000));
    assert.equal(hasBroadbandContent(tone), false);
    for (let i = 0; i < 20; i++) gate.process(0.99, hasBroadbandContent(tone));
    assert.equal(decisions.length, 0);
});
