const defaultPolicy = Object.freeze({
    maximumBufferedCharacters: 12000,
    maximumFragments: 20,
    minimumClassificationConfidence: 0.68,
    incompleteTurnGraceMs: 1200
});

const intents = new Set(["pause", "stop", "continue", "incomplete_turn", "complete_turn"]);
const pauseFastPath = new Set(["wait", "wait please", "hang on", "hold on", "give me a moment", "one moment", "vanta", "vanta lite", "vaenta", "ett ogonblick"]);
const stopFastPath = new Set(["stop", "stop talking", "be quiet", "stopp", "sluta", "sluta prata"]);
const continueFastPath = new Set(["continue", "go on", "you can continue", "fortsatt", "fortsaett", "du kan fortsatta"]);

export function createConversationTurnController(adapter, configuredPolicy = {}) {
    if (!adapter || typeof adapter.classifyTurn !== "function" || typeof adapter.requestResponse !== "function") {
        throw new TypeError("A turn-control adapter with classifyTurn and requestResponse is required.");
    }

    const policy = { ...defaultPolicy, ...configuredPolicy };
    let phase = "listening";
    let epoch = 0;
    let classificationSequence = 0;
    let fragments = [];
    let interrupted = false;
    let disposed = false;
    let responseEpoch = null;
    let incompleteTurnTimer = null;

    function snapshot() {
        return Object.freeze({
            phase,
            epoch,
            bufferedFragmentCount: fragments.length,
            bufferedCharacterCount: joinedTranscript().length,
            interrupted,
            responseEpoch
        });
    }

    function transition(next, reason) {
        if (phase === next) return;
        const previous = phase;
        phase = next;
        adapter.onStateChanged?.(next, { previous, reason, epoch });
    }

    function diagnostic(event, details = {}) {
        adapter.onDiagnostic?.(event, { epoch, phase, ...details });
    }

    function joinedTranscript() {
        return fragments.map(fragment => fragment.transcript).join(" ").replace(/\s+/g, " ").trim();
    }

    function resetBufferedTurn() {
        clearIncompleteTurnTimer();
        fragments = [];
        interrupted = false;
    }

    function clearIncompleteTurnTimer() {
        if (incompleteTurnTimer == null) return;
        globalThis.clearTimeout(incompleteTurnTimer);
        incompleteTurnTimer = null;
    }

    function appendFragment(fragment) {
        const transcript = String(fragment.transcript ?? "").trim();
        if (!transcript) return false;
        if (fragments.some(current => current.eventId === fragment.eventId)) return false;
        fragments.push({
            eventId: String(fragment.eventId ?? `turn-${epoch}-${fragments.length + 1}`),
            transcript,
            durationMs: Math.max(0, Number(fragment.durationMs) || 0)
        });
        while (fragments.length > policy.maximumFragments) fragments.shift();
        while (joinedTranscript().length > policy.maximumBufferedCharacters && fragments.length > 1) fragments.shift();
        if (joinedTranscript().length > policy.maximumBufferedCharacters) {
            const current = fragments[0];
            current.transcript = current.transcript.slice(-policy.maximumBufferedCharacters);
        }
        return true;
    }

    async function speechStarted({ agentActive = false } = {}) {
        if (disposed) return snapshot();
        clearIncompleteTurnTimer();
        epoch += 1;
        classificationSequence += 1;
        responseEpoch = null;
        const shouldInterrupt = agentActive || phase === "agent_speaking" || phase === "agent_thinking" || phase === "classifying_turn";
        if (shouldInterrupt) {
            interrupted = true;
            await adapter.interruptActive?.({ automatic: true, epoch, reason: "user_speech_started" });
            diagnostic("automatic_barge_in");
        }
        transition("user_speaking", "speech_started");
        return snapshot();
    }

    function speechStopped() {
        if (disposed) return snapshot();
        diagnostic("speech_stopped");
        return snapshot();
    }

    async function transcriptionCompleted(fragment) {
        if (disposed || !appendFragment(fragment)) return snapshot();
        const classifiedEpoch = epoch;
        const classificationId = ++classificationSequence;
        const transcript = joinedTranscript();
        transition("classifying_turn", "transcription_completed");

        let classification = deterministicControlIntent(transcript);
        if (!classification) {
            try {
                classification = await adapter.classifyTurn({ transcript, epoch: classifiedEpoch, classificationId });
            } catch (error) {
                diagnostic("classification_failed", { errorName: error?.name ?? "Error" });
                classification = { intent: "incomplete_turn", confidence: 0 };
            }
        }

        if (disposed || classifiedEpoch !== epoch || classificationId !== classificationSequence) {
            diagnostic("stale_classification_ignored", { classifiedEpoch, classificationId });
            return snapshot();
        }

        const decision = normalizeDecision(classification, policy.minimumClassificationConfidence);
        diagnostic("turn_intent", { intent: decision.intent, confidence: decision.confidence });

        if (decision.intent === "pause") {
            transition("user_thinking", "pause_intent");
            diagnostic("pause_intent_detected");
            return snapshot();
        }
        if (decision.intent === "stop") {
            resetBufferedTurn();
            transition("listening", "stop_intent");
            adapter.onControlIntent?.("stop", { epoch });
            return snapshot();
        }
        if (decision.intent === "incomplete_turn") {
            transition("listening", "incomplete_turn");
            diagnostic("incomplete_turn_retained", { fragmentCount: fragments.length });
            scheduleIncompleteTurnFallback(classifiedEpoch);
            return snapshot();
        }

        await acceptBufferedTurn(classifiedEpoch, decision.intent);
        return snapshot();
    }

    function scheduleIncompleteTurnFallback(classifiedEpoch) {
        clearIncompleteTurnTimer();
        const graceMs = Math.max(0, Number(policy.incompleteTurnGraceMs) || 0);
        incompleteTurnTimer = globalThis.setTimeout(() => {
            incompleteTurnTimer = null;
            if (disposed || classifiedEpoch !== epoch || phase !== "listening" || fragments.length === 0) return;
            diagnostic("incomplete_turn_grace_expired", { fragmentCount: fragments.length, graceMs });
            acceptBufferedTurn(classifiedEpoch, "complete_turn").catch(error => {
                diagnostic("response_request_failed", { errorName: error?.name ?? "Error" });
            });
        }, graceMs);
    }

    async function acceptBufferedTurn(classifiedEpoch, intent) {
        clearIncompleteTurnTimer();
        const transcript = joinedTranscript();
        const accepted = {
            eventId: fragments.map(current => current.eventId).join("|").slice(0, 2000),
            transcript,
            durationMs: fragments.reduce((total, current) => total + current.durationMs, 0),
            interrupted,
            epoch: classifiedEpoch
        };
        resetBufferedTurn();
        responseEpoch = classifiedEpoch;
        transition("agent_thinking", intent === "continue" ? "continue_intent" : "complete_turn");
        if (intent === "continue") diagnostic("turn_resumed");

        try {
            adapter.acceptTurn?.(accepted);
            await adapter.requestResponse({ epoch: classifiedEpoch });
        } catch (error) {
            diagnostic("response_request_failed", { errorName: error?.name ?? "Error" });
            responseEpoch = null;
            transition("listening", "response_request_failed");
        }
    }

    function responseCreated(responseId) {
        if (disposed || phase !== "agent_thinking" || responseEpoch !== epoch) {
            diagnostic("stale_response_ignored", { responseId: responseId ?? null });
            return false;
        }
        return true;
    }

    function outputStarted(responseId) {
        if (phase === "agent_speaking" && responseEpoch === epoch) return true;
        if (!responseCreated(responseId)) return false;
        transition("agent_speaking", "output_started");
        return true;
    }

    function toolStarted() {
        if (disposed || (phase !== "agent_thinking" && phase !== "agent_speaking")) return false;
        transition("agent_thinking", "tool_started");
        return true;
    }

    function responseDone({ hasFunctionCall = false } = {}) {
        if (disposed) return snapshot();
        if (hasFunctionCall && responseEpoch === epoch) transition("agent_thinking", "tool_continuation_pending");
        else {
            responseEpoch = null;
            transition("listening", "response_done");
        }
        return snapshot();
    }

    async function manualInterrupt() {
        if (disposed) return snapshot();
        epoch += 1;
        classificationSequence += 1;
        responseEpoch = null;
        interrupted = true;
        await adapter.interruptActive?.({ automatic: false, epoch, reason: "explicit_interrupt" });
        transition("listening", "explicit_interrupt");
        diagnostic("explicit_interrupt");
        return snapshot();
    }

    function dispose() {
        disposed = true;
        classificationSequence += 1;
        responseEpoch = null;
        resetBufferedTurn();
        phase = "stopped";
    }

    return Object.freeze({
        speechStarted,
        speechStopped,
        transcriptionCompleted,
        responseCreated,
        outputStarted,
        toolStarted,
        responseDone,
        manualInterrupt,
        getSnapshot: snapshot,
        dispose
    });
}

export function normalizeDecision(value, minimumConfidence = defaultPolicy.minimumClassificationConfidence) {
    const intent = intents.has(value?.intent) ? value.intent : "incomplete_turn";
    const numericConfidence = Number(value?.confidence);
    const confidence = Number.isFinite(numericConfidence) ? Math.min(1, Math.max(0, numericConfidence)) : 0;
    if (confidence < minimumConfidence) return { intent: "incomplete_turn", confidence };
    return { intent, confidence };
}

export function parseTurnIntentResponse(text) {
    if (typeof text !== "string" || !text.trim()) return { intent: "incomplete_turn", confidence: 0 };
    try {
        const candidate = text.slice(text.indexOf("{"), text.lastIndexOf("}") + 1);
        return normalizeDecision(JSON.parse(candidate), 0);
    } catch {
        return { intent: "incomplete_turn", confidence: 0 };
    }
}

function deterministicControlIntent(transcript) {
    const normalized = normalizeControlText(transcript);
    if (pauseFastPath.has(normalized)) return { intent: "pause", confidence: 1 };
    if (stopFastPath.has(normalized)) return { intent: "stop", confidence: 1 };
    if (continueFastPath.has(normalized)) return { intent: "continue", confidence: 1 };
    return null;
}

function normalizeControlText(value) {
    return String(value ?? "")
        .normalize("NFKD")
        .replace(/[\u0300-\u036f]/g, "")
        .toLowerCase()
        .replace(/[^a-z0-9\s]/g, " ")
        .replace(/\s+/g, " ")
        .trim();
}
