import test from "node:test";
import assert from "node:assert/strict";
import { createConversationTurnController, parseTurnIntentResponse } from "../../../src/VirtualCompany.Web/wwwroot/js/conversation-turn-controller.mjs";

function harness(decisions = [], policy = {}) {
    const calls = { interruptions: [], accepted: [], responses: [], states: [], diagnostics: [] };
    const adapter = {
        classifyTurn: async () => decisions.shift() ?? { intent: "incomplete_turn", confidence: 0 },
        interruptActive: async request => calls.interruptions.push(request),
        acceptTurn: turn => calls.accepted.push(turn),
        requestResponse: async request => calls.responses.push(request),
        onStateChanged: state => calls.states.push(state),
        onDiagnostic: event => calls.diagnostics.push(event)
    };
    return { controller: createConversationTurnController(adapter, policy), calls };
}

test("speech immediately interrupts active agent audio", async () => {
    const { controller, calls } = harness([{ intent: "complete_turn", confidence: 0.98 }]);
    await controller.speechStarted();
    await controller.transcriptionCompleted({ eventId: "one", transcript: "Increase revenue.", durationMs: 400 });
    assert.equal(controller.responseCreated("response-1"), true);
    assert.equal(controller.outputStarted("response-1"), true);

    await controller.speechStarted({ agentActive: true });

    assert.equal(calls.interruptions.length, 1);
    assert.equal(calls.interruptions[0].reason, "user_speech_started");
    assert.equal(controller.getSnapshot().phase, "user_speaking");
});

test("a pause request latches silence until the user resumes", async () => {
    const { controller, calls } = harness([{ intent: "complete_turn", confidence: 0.95 }], { incompleteTurnGraceMs: 5 });
    await controller.speechStarted();
    await controller.transcriptionCompleted({ eventId: "pause", transcript: "Hang on", durationMs: 200 });
    await new Promise(resolve => setTimeout(resolve, 10));

    assert.equal(controller.getSnapshot().phase, "user_thinking");
    assert.equal(calls.responses.length, 0);

    await controller.speechStarted();
    await controller.transcriptionCompleted({ eventId: "resume", transcript: "it should increase revenue.", durationMs: 700 });

    assert.equal(calls.accepted.length, 1);
    assert.equal(calls.accepted[0].transcript, "Hang on it should increase revenue.");
    assert.equal(calls.responses.length, 1);
});

test("incomplete fragments are retained and accepted once", async () => {
    const { controller, calls } = harness([
        { intent: "incomplete_turn", confidence: 0.93 },
        { intent: "complete_turn", confidence: 0.97 }
    ]);
    await controller.speechStarted();
    await controller.transcriptionCompleted({ eventId: "fragment-1", transcript: "It should increase the", durationMs: 350 });
    assert.equal(calls.responses.length, 0);

    await controller.speechStarted();
    await controller.transcriptionCompleted({ eventId: "fragment-2", transcript: "revenue by ten percent.", durationMs: 500 });

    assert.equal(calls.accepted.length, 1);
    assert.equal(calls.accepted[0].eventId, "fragment-1|fragment-2");
    assert.equal(calls.responses.length, 1);
});

test("an incomplete classification cannot hold a detected utterance forever", async () => {
    const { controller, calls } = harness(
        [{ intent: "incomplete_turn", confidence: 0.93 }],
        { incompleteTurnGraceMs: 5 }
    );
    await controller.speechStarted();
    await controller.transcriptionCompleted({ eventId: "misclassified", transcript: "Small businesses are our primary audience.", durationMs: 450 });

    await new Promise(resolve => setTimeout(resolve, 20));

    assert.equal(calls.accepted.length, 1);
    assert.equal(calls.accepted[0].transcript, "Small businesses are our primary audience.");
    assert.equal(calls.responses.length, 1);
    assert.equal(controller.getSnapshot().phase, "agent_thinking");
});

test("new speech within the incomplete-turn grace period keeps one combined turn", async () => {
    const { controller, calls } = harness([
        { intent: "incomplete_turn", confidence: 0.93 },
        { intent: "complete_turn", confidence: 0.97 }
    ], { incompleteTurnGraceMs: 40 });
    await controller.speechStarted();
    await controller.transcriptionCompleted({ eventId: "fragment-1", transcript: "We should focus on", durationMs: 300 });
    await new Promise(resolve => setTimeout(resolve, 5));
    await controller.speechStarted();
    await controller.transcriptionCompleted({ eventId: "fragment-2", transcript: "small service companies.", durationMs: 350 });
    await new Promise(resolve => setTimeout(resolve, 50));

    assert.equal(calls.accepted.length, 1);
    assert.equal(calls.accepted[0].transcript, "We should focus on small service companies.");
    assert.equal(calls.responses.length, 1);
});

test("low-confidence and malformed classifications initially favor silence", async () => {
    const { controller, calls } = harness([{ intent: "complete_turn", confidence: 0.2 }]);
    await controller.speechStarted();
    await controller.transcriptionCompleted({ eventId: "uncertain", transcript: "So with", durationMs: 250 });

    assert.equal(controller.getSnapshot().phase, "listening");
    assert.equal(calls.accepted.length, 0);
    assert.deepEqual(parseTurnIntentResponse("not json"), { intent: "incomplete_turn", confidence: 0 });
    controller.dispose();
});

test("a response created for a superseded epoch is rejected", async () => {
    const { controller } = harness([{ intent: "complete_turn", confidence: 0.99 }]);
    await controller.speechStarted();
    await controller.transcriptionCompleted({ eventId: "complete", transcript: "That is complete.", durationMs: 300 });
    await controller.speechStarted({ agentActive: true });

    assert.equal(controller.responseCreated("late-response"), false);
});

test("speech after an active response is accepted as a new turn after cancellation", async () => {
    const { controller, calls } = harness([
        { intent: "complete_turn", confidence: 0.99 },
        { intent: "complete_turn", confidence: 0.99 }
    ]);
    await controller.speechStarted();
    await controller.transcriptionCompleted({ eventId: "first", transcript: "The first point is complete.", durationMs: 300 });
    assert.equal(controller.responseCreated("response-1"), true);
    assert.equal(controller.outputStarted("response-1"), true);

    await controller.speechStarted({ agentActive: true });
    await controller.transcriptionCompleted({ eventId: "second", transcript: "Now capture the second point.", durationMs: 350 });

    assert.equal(calls.interruptions.length, 1);
    assert.equal(calls.accepted.length, 2);
    assert.equal(calls.responses.length, 2);
    assert.equal(controller.responseCreated("response-2"), true);
    assert.equal(controller.getSnapshot().phase, "agent_thinking");
});

test("duplicate transcription events are accepted only once", async () => {
    const { controller, calls } = harness([{ intent: "incomplete_turn", confidence: 0.99 }]);
    await controller.speechStarted();
    await controller.transcriptionCompleted({ eventId: "duplicate", transcript: "It should", durationMs: 100 });
    await controller.transcriptionCompleted({ eventId: "duplicate", transcript: "It should", durationMs: 100 });

    assert.equal(controller.getSnapshot().bufferedFragmentCount, 1);
    assert.equal(calls.responses.length, 0);
    controller.dispose();
});

test("a stop control cancels the buffered control turn without a reply", async () => {
    const { controller, calls } = harness();
    await controller.speechStarted({ agentActive: true });
    await controller.transcriptionCompleted({ eventId: "stop", transcript: "Stop", durationMs: 120 });

    assert.equal(controller.getSnapshot().phase, "listening");
    assert.equal(controller.getSnapshot().bufferedFragmentCount, 0);
    assert.equal(calls.responses.length, 0);
});
