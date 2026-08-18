const sessions = new Map();
const starts = new Map();
const maxReconnects = 2;
const logPrefix = "[guided-realtime]";

function debug(state, event, details = {}) {
    console.debug(logPrefix, event, {
        sessionId: state?.sessionId ?? null,
        bindingId: state?.bindingId ?? null,
        ...details
    });
}

function warn(state, event, error, details = {}) {
    console.warn(logPrefix, event, {
        sessionId: state?.sessionId ?? null,
        bindingId: state?.bindingId ?? null,
        errorName: typeof error?.name === "string" ? error.name : null,
        errorMessage: typeof error?.message === "string" ? error.message : String(error ?? "Unknown error"),
        ...details
    });
}

export function start(dotnet, companyId, sessionId, reconnectAttempt = 0) {
    const existing = starts.get(sessionId);
    if (existing) return existing;
    const operation = startCore(dotnet, companyId, sessionId, reconnectAttempt)
        .finally(() => { if (starts.get(sessionId) === operation) starts.delete(sessionId); });
    starts.set(sessionId, operation);
    return operation;
}

async function startCore(dotnet, companyId, sessionId, reconnectAttempt) {
    if (!navigator.mediaDevices?.getUserMedia || !window.RTCPeerConnection) throw new Error("Voice conversations are not supported by this browser.");
    await stop(sessionId, reconnectAttempt > 0);
    await dotnet.invokeMethodAsync("OnVoiceState", reconnectAttempt > 0 ? "reconnecting" : "connecting", null);
    let media;
    try {
        media = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true } });
    } catch (error) {
        const failure = describeMicrophoneFailure(error);
        await dotnet.invokeMethodAsync("OnVoiceState", failure.state, failure.message);
        // Do not leak browser-specific values such as "undefined" into the UI.
        // Blazor catches this error after OnVoiceState, so keep both paths consistent.
        throw new Error(failure.message);
    }

    const pc = new RTCPeerConnection();
    const audio = document.createElement("audio");
    audio.autoplay = true;
    audio.setAttribute("aria-hidden", "true");
    document.body.appendChild(audio);
    for (const track of media.getTracks()) pc.addTrack(track, media);
    const state = { pc, media, audio, companyId, sessionId, dotnet, reconnectAttempt, bindingId: null, channel: null, muted: false, inputSuspended: false, toolContinuationPending: false, toolWorkActive: false, toolName: null, interrupted: false, speechStartedAt: null, speechStoppedAt: null, responseCreatedAt: null, audioStartedAt: null, lastSpeechDurationMs: 0, ending: false, responseId: null, responseInProgress: false, outputAudioActive: false, speakingLogged: false, agentTranscript: "", agentTranscriptResponseId: null, agentTranscriptUpdateTimer: null, reconnectScheduled: false, reconnectTimer: null, pageHideHandler: null };
    sessions.set(sessionId, state);

    audio.onplay = () => debug(state, "remote_audio_play", { muted: audio.muted, volume: audio.volume, readyState: audio.readyState });
    audio.onplaying = () => debug(state, "remote_audio_playing", { currentTime: audio.currentTime, readyState: audio.readyState });
    audio.onpause = () => debug(state, "remote_audio_pause", { currentTime: audio.currentTime, ended: audio.ended });
    audio.onended = () => debug(state, "remote_audio_ended", { currentTime: audio.currentTime });
    audio.onerror = () => warn(state, "remote_audio_error", audio.error ?? new Error("The remote audio element reported an error."));
    pc.ontrack = event => {
        debug(state, "remote_track_received", {
            kind: event.track?.kind ?? null,
            trackState: event.track?.readyState ?? null,
            streamCount: event.streams?.length ?? 0
        });
        audio.srcObject = event.streams[0] ?? new MediaStream([event.track]);
        const playback = audio.play();
        if (playback) playback.catch(error => warn(state, "remote_audio_play_rejected", error));
    };
    debug(state, "voice_start", { reconnectAttempt, microphoneTrackCount: media.getAudioTracks().length });

    pc.onconnectionstatechange = async () => {
        const current = sessions.get(sessionId);
        if (!current || current.pc !== pc || current.ending) return;
        debug(current, "peer_connection_state", { connectionState: pc.connectionState, iceConnectionState: pc.iceConnectionState });
        if (pc.connectionState === "connected") await dotnet.invokeMethodAsync("OnVoiceState", current.muted ? "muted" : "listening", null);
        else if ((pc.connectionState === "disconnected" || pc.connectionState === "failed") && reconnectAttempt < maxReconnects && !current.reconnectScheduled) {
            current.reconnectScheduled = true;
            await dotnet.invokeMethodAsync("OnVoiceState", "reconnecting", null);
            current.reconnectTimer = window.setTimeout(() => start(dotnet, companyId, sessionId, reconnectAttempt + 1).catch(() => dotnet.invokeMethodAsync("OnVoiceState", "unavailable", "Voice could not reconnect. You can continue by typing.")), 750 * (reconnectAttempt + 1));
        } else if (pc.connectionState === "failed") await dotnet.invokeMethodAsync("OnVoiceState", "unavailable", "Voice is unavailable. You can continue by typing.");
    };

    const channel = pc.createDataChannel("oai-events");
    state.channel = channel;
    channel.onopen = () => {
        debug(state, "data_channel_open", { readyState: channel.readyState });
        return dotnet.invokeMethodAsync("OnVoiceState", state.muted ? "muted" : "listening", null);
    };
    channel.onerror = error => {
        warn(state, "data_channel_error", error);
        return dotnet.invokeMethodAsync("OnVoiceState", "unavailable", "The voice data channel failed. You can continue by typing.");
    };
    channel.onmessage = event => handleProviderEvent(state, event.data);

    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);
    const response = await fetch(`/api/companies/${companyId}/guided-work-sessions/${sessionId}/voice/calls`, {
        method: "POST", credentials: "same-origin", headers: { "Content-Type": "application/sdp", "X-Company-Id": companyId }, body: offer.sdp
    });
    if (!response.ok) {
        const raw = await response.text();
        const detail = readProblemDetail(raw);
        await cleanup(state, true);
        if (response.status === 429) await dotnet.invokeMethodAsync("OnVoiceState", "unavailable", detail || "Voice is temporarily rate limited. Wait briefly and try again.");
        throw new Error(detail || "Voice conversation could not be started.");
    }
    state.bindingId = response.headers.get("X-Guided-Voice-Binding");
    state.pageHideHandler = () => notifyServerStop(state, true);
    window.addEventListener("pagehide", state.pageHideHandler, { once: true });
    await pc.setRemoteDescription({ type: "answer", sdp: await response.text() });
    debug(state, "remote_description_set", { connectionState: pc.connectionState });
    return { bindingId: state.bindingId, state: pc.connectionState };
}

function readProblemDetail(raw) {
    if (!raw) return "";
    try {
        const problem = JSON.parse(raw);
        return typeof problem?.detail === "string" ? problem.detail : typeof problem?.title === "string" ? problem.title : raw;
    } catch { return raw; }
}

function describeMicrophoneFailure(error) {
    const name = typeof error?.name === "string" ? error.name : "";
    const message = typeof error?.message === "string" ? error.message : "";
    const detail = `${name} ${message}`.trim().toLowerCase();

    if (detail.includes("permission denied by system")) {
        return {
            state: "permission_denied",
            message: "Microphone access is blocked by Windows. Turn on microphone access for desktop apps in Windows Privacy settings, then try again. You can continue by typing."
        };
    }

    if (name === "NotAllowedError" || name === "SecurityError" || detail.includes("permission") || detail.includes("denied")) {
        return {
            state: "permission_denied",
            message: "Microphone access is blocked. Allow microphone access for this site in your browser, then try again. You can continue by typing."
        };
    }

    if (name === "NotFoundError" || name === "DevicesNotFoundError") {
        return {
            state: "unavailable",
            message: "No microphone was found. Connect or enable a microphone, then try again. You can continue by typing."
        };
    }

    if (name === "NotReadableError" || name === "TrackStartError") {
        return {
            state: "unavailable",
            message: "The microphone could not be opened. Close other apps using it, then try again. You can continue by typing."
        };
    }

    return {
        state: "unavailable",
        message: "The microphone could not be started. Check your microphone settings and try again. You can continue by typing."
    };
}

async function handleProviderEvent(state, raw) {
    try {
        const payload = JSON.parse(raw);
        if (payload.type === "input_audio_buffer.speech_started") {
            state.speechStartedAt = performance.now();
            state.speechStoppedAt = null;
            debug(state, state.outputAudioActive ? "speech_detected_during_agent_audio" : "input_speech_started", {
                responseId: state.responseId,
                outputAudioActive: state.outputAudioActive,
                audioPlaybackMs: state.audioStartedAt == null ? null : elapsedMs(state.audioStartedAt)
            });
            // Automatic VAD interruption is disabled server-side to avoid false barge-in
            // from speaker echo or room noise. The explicit Interrupt control remains available.
            await state.dotnet.invokeMethodAsync("OnVoiceState", state.outputAudioActive ? "speaking" : state.muted ? "muted" : "listening", null);
        } else if (payload.type === "input_audio_buffer.speech_stopped") {
            state.speechStoppedAt = performance.now();
            state.lastSpeechDurationMs = state.speechStartedAt == null ? 0 : elapsedMs(state.speechStartedAt);
            debug(state, "input_speech_stopped", {
                speechDurationMs: state.lastSpeechDurationMs,
                responseId: state.responseId,
                outputAudioActive: state.outputAudioActive
            });
        } else if (payload.type === "response.function_call_arguments.done") {
            state.toolContinuationPending = true;
            setInputSuspended(state, true, "tool_started");
            if (state.outputAudioActive && state.channel?.readyState === "open") {
                state.channel.send(JSON.stringify({ type: "output_audio_buffer.clear" }));
                state.outputAudioActive = false;
            }
            resetAgentTranscript(state);
            await setToolWorkState(state, payload.name, "working", true);
            debug(state, "tool_work_started", { toolName: state.toolName, callId: payload.call_id ?? null });
            await state.dotnet.invokeMethodAsync("OnVoiceState", "thinking", null);
        } else if (payload.type === "response.created") {
            const continuingFromTool = state.toolContinuationPending || state.toolWorkActive;
            state.responseId = payload.response?.id ?? null;
            state.responseInProgress = true;
            state.toolContinuationPending = false;
            setInputSuspended(state, true, "response.created");
            state.responseCreatedAt = performance.now();
            state.speakingLogged = false;
            debug(state, "response_created", {
                responseId: state.responseId,
                speechDurationMs: state.lastSpeechDurationMs,
                turnEndToResponseCreatedMs: state.speechStoppedAt == null ? null : elapsedMs(state.speechStoppedAt),
                continuingFromTool
            });
            if (continuingFromTool) await setToolWorkState(state, state.toolName, "preparing", true);
            await state.dotnet.invokeMethodAsync("OnVoiceState", "thinking", null);
        } else if (payload.type === "response.audio.delta" || payload.type === "response.output_audio.delta") {
            if (state.toolWorkActive) await setToolWorkState(state, null, null, false);
            if (!state.speakingLogged) {
                state.speakingLogged = true;
                debug(state, "response_audio_started", { responseId: payload.response_id ?? state.responseId, eventType: payload.type });
            }
            await state.dotnet.invokeMethodAsync("OnVoiceState", "speaking", null);
        } else if (payload.type === "response.audio.done" || payload.type === "response.output_audio.done") {
            debug(state, "response_audio_done", { responseId: payload.response_id ?? state.responseId, eventType: payload.type });
        } else if (payload.type === "response.output_audio_transcript.delta" || payload.type === "response.audio_transcript.delta") {
            const responseId = payload.response_id ?? state.responseId ?? payload.item_id ?? crypto.randomUUID();
            beginAgentTranscript(state, responseId);
            if (typeof payload.delta === "string") state.agentTranscript += payload.delta;
            queueAgentTranscriptUpdate(state);
        } else if (payload.type === "response.output_audio_transcript.done" || payload.type === "response.audio_transcript.done") {
            const responseId = payload.response_id ?? state.responseId ?? payload.item_id ?? state.agentTranscriptResponseId ?? crypto.randomUUID();
            beginAgentTranscript(state, responseId);
            if (typeof payload.transcript === "string" && payload.transcript.trim()) state.agentTranscript = payload.transcript;
            // Keep the completed caption visible, but do not persist it until response.done
            // confirms that this response did not lead into a tool call. Realtime can emit
            // spoken filler before a function call; that must not become a durable reply.
            await flushAgentTranscript(state, false);
        } else if (payload.type === "output_audio_buffer.started") {
            if (state.toolWorkActive) await setToolWorkState(state, null, null, false);
            state.outputAudioActive = true;
            state.speakingLogged = true;
            state.audioStartedAt = performance.now();
            debug(state, "output_audio_buffer_started", {
                responseId: payload.response_id ?? state.responseId,
                responseCreatedToAudioMs: state.responseCreatedAt == null ? null : elapsedMs(state.responseCreatedAt),
                turnEndToAudioMs: state.speechStoppedAt == null ? null : elapsedMs(state.speechStoppedAt)
            });
            state.speechStoppedAt = null;
            await ensureRemoteAudioPlayback(state, "output_audio_buffer.started");
            await state.dotnet.invokeMethodAsync("OnVoiceState", "speaking", null);
        } else if (payload.type === "output_audio_buffer.stopped" || payload.type === "output_audio_buffer.cleared") {
            state.outputAudioActive = false;
            debug(state, payload.type.replaceAll(".", "_"), {
                responseId: payload.response_id ?? state.responseId,
                audioPlaybackMs: state.audioStartedAt == null ? null : elapsedMs(state.audioStartedAt),
                interrupted: payload.type === "output_audio_buffer.cleared"
            });
            state.audioStartedAt = null;
            await state.dotnet.invokeMethodAsync("OnVoiceState", state.muted ? "muted" : "listening", null);
        } else if (payload.type === "response.done") {
            const hasFunctionCall = Array.isArray(payload.response?.output) && payload.response.output.some(item => item?.type === "function_call");
            debug(state, "response_done", {
                responseId: payload.response?.id ?? state.responseId,
                status: payload.response?.status ?? null,
                statusReason: payload.response?.status_details?.reason ?? null,
                audioWasProduced: state.speakingLogged,
                responseDurationMs: state.responseCreatedAt == null ? null : elapsedMs(state.responseCreatedAt),
                outputAudioActive: state.outputAudioActive,
                toolContinuationPending: hasFunctionCall
            });
            if (hasFunctionCall) resetAgentTranscript(state);
            else await flushAgentTranscript(state, true);
            state.responseId = null;
            state.responseCreatedAt = null;
            state.responseInProgress = false;
            state.toolContinuationPending = hasFunctionCall;
            if (!state.toolContinuationPending) setInputSuspended(state, false, "response.done");
            if (!hasFunctionCall && state.toolWorkActive && !state.outputAudioActive) await setToolWorkState(state, null, null, false);
            if (!state.outputAudioActive) {
                state.speakingLogged = false;
                await state.dotnet.invokeMethodAsync("OnVoiceState", state.muted ? "muted" : "listening", null);
            }
        } else if (payload.type === "error") {
            console.error(logPrefix, "provider_error", {
                sessionId: state.sessionId,
                bindingId: state.bindingId,
                errorType: payload.error?.type ?? null,
                errorCode: payload.error?.code ?? null,
                eventId: payload.event_id ?? null
            });
        }
        else if (payload.type === "conversation.item.input_audio_transcription.completed" && payload.transcript?.trim()) {
            const durationMs = state.lastSpeechDurationMs;
            debug(state, "input_transcription_completed", {
                speechDurationMs: durationMs,
                transcriptionDelayMs: state.speechStoppedAt == null ? null : elapsedMs(state.speechStoppedAt)
            });
            await state.dotnet.invokeMethodAsync("OnVoiceTranscript", payload.event_id || payload.item_id || crypto.randomUUID(), payload.transcript.trim(), state.interrupted, durationMs);
            state.interrupted = false;
            state.speechStartedAt = null;
        }
    } catch (error) {
        warn(state, "provider_event_handling_failed", error, {
            eventType: (() => { try { return JSON.parse(raw)?.type ?? null; } catch { return null; } })()
        });
    }
}

function beginAgentTranscript(state, responseId) {
    if (state.agentTranscriptResponseId === responseId) return;
    if (state.agentTranscriptUpdateTimer != null) window.clearTimeout(state.agentTranscriptUpdateTimer);
    state.agentTranscriptUpdateTimer = null;
    state.agentTranscriptResponseId = responseId;
    state.agentTranscript = "";
    debug(state, "agent_transcript_started", { responseId });
}

function resetAgentTranscript(state) {
    if (state.agentTranscriptUpdateTimer != null) window.clearTimeout(state.agentTranscriptUpdateTimer);
    state.agentTranscriptUpdateTimer = null;
    state.agentTranscriptResponseId = null;
    state.agentTranscript = "";
}

async function setToolWorkState(state, toolName, phase, active) {
    state.toolWorkActive = !!active;
    state.toolName = active ? (toolName ?? state.toolName ?? "workshop_tool") : null;
    state.audio.muted = state.toolWorkActive;
    await state.dotnet.invokeMethodAsync("OnVoiceWorkState", state.toolName, phase, state.toolWorkActive);
}

function queueAgentTranscriptUpdate(state) {
    if (state.agentTranscriptUpdateTimer != null) return;
    state.agentTranscriptUpdateTimer = window.setTimeout(() => {
        state.agentTranscriptUpdateTimer = null;
        flushAgentTranscript(state, false).catch(error => warn(state, "agent_transcript_update_failed", error));
    }, 75);
}

async function flushAgentTranscript(state, isFinal) {
    if (state.agentTranscriptUpdateTimer != null) window.clearTimeout(state.agentTranscriptUpdateTimer);
    state.agentTranscriptUpdateTimer = null;
    const transcript = state.agentTranscript.trim();
    if (!transcript || !state.agentTranscriptResponseId) return;
    debug(state, isFinal ? "agent_transcript_completed" : "agent_transcript_updated", {
        responseId: state.agentTranscriptResponseId,
        characterCount: transcript.length
    });
    await state.dotnet.invokeMethodAsync("OnAgentVoiceTranscript", state.agentTranscriptResponseId, transcript, isFinal);
}

function elapsedMs(startedAt) {
    return Math.max(0, Math.round(performance.now() - startedAt));
}

export async function setMuted(sessionId, muted) {
    const current = sessions.get(sessionId);
    if (!current) return false;
    current.muted = !!muted;
    current.media.getAudioTracks().forEach(track => { track.enabled = !current.muted && !current.inputSuspended; });
    await current.dotnet.invokeMethodAsync("OnVoiceState", current.muted ? "muted" : "listening", null);
    return current.muted;
}

export async function interrupt(sessionId, automatic = false) {
    const current = sessions.get(sessionId);
    if (!current?.channel || current.channel.readyState !== "open") return;
    if (!current.responseId && !current.outputAudioActive && !current.toolWorkActive) {
        debug(current, "response_interrupt_ignored", { automatic, reason: "no_active_response" });
        return;
    }
    current.interrupted = true;
    debug(current, "response_interrupt", { automatic, responseId: current.responseId });
    if (current.responseId) current.channel.send(JSON.stringify({ type: "response.cancel", response_id: current.responseId }));
    if (current.outputAudioActive) current.channel.send(JSON.stringify({ type: "output_audio_buffer.clear" }));
    current.outputAudioActive = false;
    current.responseInProgress = false;
    current.toolContinuationPending = false;
    await setToolWorkState(current, null, null, false);
    setInputSuspended(current, false, "explicit_interrupt");
    await current.dotnet.invokeMethodAsync("OnVoiceState", current.muted ? "muted" : "listening", automatic ? null : "The agent was interrupted.");
}

function setInputSuspended(state, suspended, reason) {
    state.inputSuspended = !!suspended;
    state.media.getAudioTracks().forEach(track => { track.enabled = !state.muted && !state.inputSuspended; });
    debug(state, state.inputSuspended ? "microphone_suspended" : "microphone_resumed", { reason, responseId: state.responseId });
}

export function scrollTranscriptToEnd() {
    const transcript = document.getElementById("guided-transcript");
    if (!transcript) return;
    const distanceFromBottom = transcript.scrollHeight - transcript.scrollTop - transcript.clientHeight;
    if (distanceFromBottom <= 240) transcript.scrollTop = transcript.scrollHeight;
}

async function ensureRemoteAudioPlayback(state, reason) {
    if (!state.audio?.paused) return;
    try {
        await state.audio.play();
        debug(state, "remote_audio_resumed", { reason, readyState: state.audio.readyState });
    } catch (error) {
        warn(state, "remote_audio_resume_rejected", error);
        await state.dotnet.invokeMethodAsync("OnVoiceState", "unavailable", "Agent audio could not be played. Check this site's sound permission and your output device.");
    }
}

export async function stop(sessionId, reconnecting = false) {
    const current = sessions.get(sessionId);
    if (!current) return;
    sessions.delete(sessionId);
    await cleanup(current, true);
    if (!reconnecting) await current.dotnet.invokeMethodAsync("OnVoiceState", "ended", null);
}

async function cleanup(current, notifyServer) {
    current.ending = true;
    current.audio.muted = false;
    debug(current, "voice_cleanup", { notifyServer, connectionState: current.pc?.connectionState ?? null });
    if (current.agentTranscriptUpdateTimer != null) window.clearTimeout(current.agentTranscriptUpdateTimer);
    if (current.reconnectTimer != null) window.clearTimeout(current.reconnectTimer);
    if (current.pageHideHandler) window.removeEventListener("pagehide", current.pageHideHandler);
    current.channel?.close();
    current.media?.getTracks().forEach(track => track.stop());
    current.pc?.close();
    current.audio?.remove();
    if (notifyServer) await notifyServerStop(current, false);
}

async function notifyServerStop(current, keepalive) {
    if (!current.bindingId) return;
    const bindingId = current.bindingId;
    current.bindingId = null;
    try {
        await fetch(`/api/companies/${current.companyId}/guided-work-sessions/${current.sessionId}/voice/calls/${bindingId}`, { method: "DELETE", credentials: "same-origin", keepalive, headers: { "X-Company-Id": current.companyId } });
    } catch (error) {
        warn(current, "voice_stop_notification_failed", error);
    }
}
