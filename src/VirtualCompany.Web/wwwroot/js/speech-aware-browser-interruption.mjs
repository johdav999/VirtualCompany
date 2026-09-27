const assetRoot = "/lib/speech-vad/";
let modelLoad;

function loadScript(path) {
    return new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = `${assetRoot}${path}`;
        script.async = false;
        script.onload = resolve;
        script.onerror = () => reject(new Error(`Speech detector asset unavailable: ${path}`));
        document.head.appendChild(script);
    });
}

async function loadModel() {
    modelLoad ??= (async () => {
        await loadScript("ort.wasm.min.js");
        await loadScript("bundle.min.js");
        if (!globalThis.ort?.env?.wasm || !globalThis.vad?.MicVAD) {
            throw new Error("The browser speech detector did not initialize.");
        }
        globalThis.ort.env.wasm.numThreads = 1;
    })();
    try { await modelLoad; }
    catch (error) { modelLoad = null; throw error; }
}

// 16 kHz Silero v5 produces 512-sample (32 ms) probability frames. A bounded
// 8-frame window confirms sustained speech without using microphone volume.
export function createSpeechOnsetGate(onConfirmed) {
    const window = [];
    let confirmed = false;
    let quietFrames = 0;
    return {
        process(probability, broadband = true) {
            if (!Number.isFinite(probability) || probability < 0 || probability > 1) return false;
            const positive = probability >= 0.5 && broadband;
            window.push(positive);
            if (window.length > 8) window.shift();
            quietFrames = positive ? 0 : quietFrames + 1;
            if (quietFrames >= 3) {
                window.length = 0;
                confirmed = false;
            }
            if (!confirmed && window.length === 8 && window.filter(Boolean).length >= 6) {
                confirmed = true;
                onConfirmed({ confidence: probability, policy: "silero-v5-6of8-v1" });
                return true;
            }
            return false;
        },
        reset() { window.length = 0; confirmed = false; quietFrames = 0; }
    };
}

export function hasBroadbandContent(frame) {
    if (!frame || frame.length < 3) return false;
    let energy = 0;
    let curvature = 0;
    for (let i = 2; i < frame.length; i++) {
        const value = frame[i];
        const secondDifference = value - 2 * frame[i - 1] + frame[i - 2];
        energy += value * value;
        curvature += secondDifference * secondDifference;
    }
    return energy > 0 && curvature * 20000 >= energy;
}

export async function createBrowserSpeechDetector(stream, onConfirmed) {
    await loadModel();
    const gate = createSpeechOnsetGate(onConfirmed);
    const detector = await globalThis.vad.MicVAD.new({
        startOnLoad: true,
        model: "v5",
        baseAssetPath: assetRoot,
        onnxWASMBasePath: assetRoot,
        getStream: async () => stream,
        pauseStream: async () => {},
        resumeStream: async () => stream,
        preSpeechPadMs: 0,
        redemptionMs: 320,
        minSpeechMs: 224,
        onFrameProcessed: ({ isSpeech }, frame) => gate.process(isSpeech, hasBroadbandContent(frame)),
        onSpeechEnd: () => {}
    });
    return {
        async dispose() { gate.reset(); await detector.destroy(); },
        reset() { gate.reset(); }
    };
}
