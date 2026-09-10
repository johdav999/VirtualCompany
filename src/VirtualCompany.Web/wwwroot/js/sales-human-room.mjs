// Human room transport only. All admission and participant names come from the application.
export class HumanRoomMedia {
    constructor(root, bridge, sdk, environment = globalThis) {
        this.root = root; this.bridge = bridge; this.sdk = sdk; this.env = environment;
        this.room = null; this.preview = null; this.generation = 0; this.disposed = false;
        this.blockedAgentResponseGeneration = 0;
        this.audience = new Map(); this.attachments = new Map(); this.listeners = [];
        this.mic = false; this.camera = false; this.sharing = false; this.busy = false;
        this.lastPulse = Date.now(); this.status = 'prejoin';
        this.onClick = e => { const button = e.target.closest('[data-room-action]'); if (button && root.contains(button)) void this.action(button.dataset.roomAction); };
        this.onDevice = () => { void this.devices(); };
        root.addEventListener('click', this.onClick);
        environment.navigator.mediaDevices?.addEventListener('devicechange', this.onDevice);
        this.pageHide = () => { void this.disconnect(); };
        environment.addEventListener?.('pagehide', this.pageHide);
        this.invitationChanged = () => { void bridge.invokeMethodAsync('InvitationChanged').catch(() => {}); };
        environment.addEventListener?.('sales-room-invitation', this.invitationChanged);
        this.watchdog = environment.setInterval(() => {
            if (Date.now() - this.lastPulse > 15000 && (this.room || this.preview)) {
                void this.disconnect(); this.report('control_lost');
            }
            if (!root.isConnected) void this.dispose();
        }, 1000);
    }
    report(status, problem) {
        if (problem !== undefined) this.problem = problem;
        this.status = status;
        if (!this.disposed) void this.bridge.invokeMethodAsync('MediaChanged', {
            status, problem: this.problem || '', microphone: this.mic, camera: this.camera, sharing: this.sharing,
            canShare: !!this.env.navigator.mediaDevices?.getDisplayMedia
        }).catch(() => { /* The independent watchdog stops media if the circuit cannot recover. */ });
    }
    heartbeat(audience) {
        this.lastPulse = Date.now();
        this.audience = new Map((audience || []).slice(0, 6).map(p => [p.mediaIdentity, p.displayName]));
        this.sync();
    }
    async devices() {
        if (!this.env.navigator.mediaDevices?.enumerateDevices) return this.report(this.status, 'unsupported');
        const devices = await this.env.navigator.mediaDevices.enumerateDevices();
        for (const kind of ['audioinput', 'videoinput', 'audiooutput']) {
            const select = this.root.querySelector(`[data-device="${kind}"]`); if (!select) continue;
            const previous = select.value; select.replaceChildren();
            const fallback = this.env.document.createElement('option'); fallback.value = ''; fallback.textContent = 'System default'; select.append(fallback);
            for (const [i, device] of devices.filter(d => d.kind === kind).entries()) {
                const option = this.env.document.createElement('option'); option.value = device.deviceId;
                option.textContent = device.label || `${kind === 'audioinput' ? 'Microphone' : kind === 'videoinput' ? 'Camera' : 'Speaker'} ${i + 1}`; select.append(option);
            }
            if ([...select.options].some(o => o.value === previous)) select.value = previous;
        }
    }
    selected(kind) { return this.root.querySelector(`[data-device="${kind}"]`)?.value || undefined; }
    stopPreview() {
        this.preview?.getTracks().forEach(t => t.stop()); this.preview = null;
        const video = this.root.querySelector('[data-preview]'); if (video) video.srcObject = null;
    }
    async previewDevices(checkMicrophone = true) {
        const generation = ++this.generation; this.stopPreview();
        if (!this.env.navigator.mediaDevices?.getUserMedia) { this.report('prejoin', 'unsupported'); return; }
        // Camera is independently optional; microphone denial still permits a listening-only call.
        const tracks = [];
        for (const kind of ['audio', 'video']) {
            if (this.disposed || generation !== this.generation) break;
            if (kind === 'audio' && !checkMicrophone && !this.mic) continue;
            if (kind === 'video' && !this.camera) continue;
            try {
                const device = this.selected(kind === 'audio' ? 'audioinput' : 'videoinput');
                const stream = await this.env.navigator.mediaDevices.getUserMedia({
                    [kind]: { ...(device ? { deviceId: { exact: device } } : {}), ...(kind === 'audio' ? { echoCancellation: true, noiseSuppression: true } : {}) }
                });
                if (this.disposed || generation !== this.generation) { stream.getTracks().forEach(t => t.stop()); continue; }
                tracks.push(...stream.getTracks()); if (kind === 'audio') this.mic = true;
            } catch (error) { if (kind === 'audio') this.mic = false; else this.camera = false; this.report('prejoin', this.deviceProblem(error)); }
        }
        if (this.disposed || generation !== this.generation) { tracks.forEach(t => t.stop()); return; }
        this.preview = new this.env.MediaStream(tracks);
        const video = this.root.querySelector('[data-preview]'); if (video) { video.srcObject = this.preview; void video.play().catch(() => {}); }
        await this.devices(); this.report('prejoin');
    }
    deviceProblem(error) {
        return error?.name === 'NotAllowedError' ? 'permission_denied' : error?.name === 'NotFoundError' || error?.name === 'OverconstrainedError' ? 'device_missing' : 'device_unavailable';
    }
    async action(action) {
        if (this.disposed || this.busy) return;
        this.busy = true; this.report(this.status, '');
        try {
            switch (action) {
                case 'preview': await this.previewDevices(); break;
                case 'microphone':
                    if (this.room) await this.room.localParticipant.setMicrophoneEnabled(!this.mic, { deviceId: this.selected('audioinput'), echoCancellation: true, noiseSuppression: true });
                    else this.preview?.getAudioTracks().forEach(t => { t.enabled = !this.mic; });
                    this.mic = !this.mic; break;
                case 'camera':
                    if (this.room) { await this.room.localParticipant.setCameraEnabled(!this.camera, { deviceId: this.selected('videoinput') }); this.camera = !this.camera; }
                    else { this.camera = !this.camera; await this.previewDevices(false); }
                    break;
                // These calls run directly in a native click handler: preserve Safari user activation.
                case 'sound':
                    if (this.room) { await Promise.all([this.room.startAudio(), this.room.startVideo()]); }
                    else {
                        const AudioContext = this.env.AudioContext || this.env.webkitAudioContext;
                        if (AudioContext) { const context = new AudioContext(); await context.resume(); const tone = context.createOscillator(); const gain = context.createGain(); gain.gain.value = 0.05; tone.connect(gain).connect(context.destination); tone.start(); tone.stop(context.currentTime + 0.15); tone.onended = () => void context.close(); }
                    }
                    break;
                case 'share': if (this.room) await this.room.localParticipant.setScreenShareEnabled(!this.room.localParticipant.isScreenShareEnabled, { audio: false }); break;
                case 'devices':
                    if (this.room) for (const kind of ['audioinput', 'videoinput', 'audiooutput']) {
                        const device = this.selected(kind); if (device) await this.room.switchActiveDevice(kind, device);
                    }
                    else await this.previewDevices(false);
                    break;
            }
            this.sync(); this.report(this.status);
        } catch (error) { this.report(this.status, action === 'share' ? 'share_unavailable' : action === 'sound' ? 'sound_blocked' : this.deviceProblem(error)); }
        finally { this.busy = false; }
    }
    async connect(token) {
        if (this.disposed || this.room) return;
        if (this.env.navigator.locks) {
            const acquired = await new Promise(resolve => {
                void this.env.navigator.locks.request(`sales-human-room:${token.identity}`, { ifAvailable: true }, lock => {
                    if (!lock) { resolve(false); return; }
                    return new Promise(release => { this.releaseIdentity = release; resolve(true); });
                }).catch(() => resolve(false));
            });
            if (!acquired) { this.report('disconnected', 'other_tab'); return; }
            if (this.disposed) { this.releaseIdentity?.(); this.releaseIdentity = null; return; }
        }
        const microphone = this.mic, camera = this.camera;
        this.stopPreview(); const generation = ++this.generation;
        const room = new this.sdk.Room({ adaptiveStream: true, dynacast: true, disconnectOnPageLeave: true });
        this.room = room; this.identity = token.identity;
        const on = (event, handler) => { room.on(event, handler); this.listeners.push([event, handler]); };
        for (const key of ['ParticipantConnected', 'ParticipantDisconnected', 'TrackSubscribed', 'TrackUnsubscribed', 'TrackMuted', 'TrackUnmuted', 'LocalTrackPublished', 'LocalTrackUnpublished'])
            on(this.sdk.RoomEvent[key], () => { this.sync(); this.report(this.status); });
        on(this.sdk.RoomEvent.Reconnecting, () => this.report('reconnecting'));
        on(this.sdk.RoomEvent.Reconnected, () => { this.sync(); this.report('connected'); });
        on(this.sdk.RoomEvent.AudioPlaybackStatusChanged, () => { if (!room.canPlaybackAudio) this.report(this.status, 'sound_blocked'); });
        on(this.sdk.RoomEvent.Disconnected, () => { void this.disconnect(); this.report('disconnected'); });
        this.report('connecting', '');
        try {
            await room.connect(token.url, token.token, { autoSubscribe: true });
            if (this.disposed || generation !== this.generation || this.room !== room) { await room.disconnect(true); return; }
            if (microphone) try { await room.localParticipant.setMicrophoneEnabled(true, { deviceId: this.selected('audioinput'), echoCancellation: true, noiseSuppression: true }); } catch (error) { this.mic = false; this.report('connected', this.deviceProblem(error)); }
            if (generation !== this.generation) { await room.disconnect(true); return; }
            if (camera) try { await room.localParticipant.setCameraEnabled(true, { deviceId: this.selected('videoinput') }); } catch (error) { this.camera = false; this.report('connected', this.deviceProblem(error)); }
            if (generation !== this.generation) { await room.disconnect(true); return; }
            this.sync(); this.report('connected', room.canPlaybackAudio ? '' : 'sound_blocked');
        } catch { if (generation === this.generation) { await this.disconnect(); this.report('provider_unavailable'); } }
    }
    sync() {
        const grid = this.root.querySelector('[data-participants]'); const room = this.room; if (!grid || !room) return;
        this.mic = room.localParticipant.isMicrophoneEnabled; this.camera = room.localParticipant.isCameraEnabled;
        this.sharing = room.localParticipant.isScreenShareEnabled;
        const participants = [room.localParticipant, ...room.remoteParticipants.values()]
            .filter(p => this.audience.has(p.identity) || p.identity?.startsWith('agent-')).slice(0, 7);
        const wanted = new Set();
        for (const participant of participants) {
            const local = participant === room.localParticipant;
            const agent = participant.identity?.startsWith('agent-');
            const sources = [...participant.trackPublications.values()];
            for (const screen of [false, true]) {
                if (agent && screen) continue;
                const publications = sources.filter(p => screen ? p.source === 'screen_share' :
                    agent ? p.source === 'microphone' : p.source === 'camera' || p.source === 'microphone');
                if (screen && !publications.some(p => p.track && !p.isMuted)) continue;
                const key = `${participant.identity}:${screen ? 'screen' : 'person'}`; wanted.add(key);
                let entry = this.attachments.get(key);
                if (!entry) {
                    const tile = this.env.document.createElement('section'); tile.className = 'room-tile'; if (screen) tile.classList.add('room-tile--screen');
                    const fallback = this.env.document.createElement('span'); fallback.className = 'room-avatar';
                    const label = this.env.document.createElement('span'); label.className = 'room-tile-label'; tile.append(fallback, label); grid.append(tile);
                    entry = { tile, fallback, label, tracks: new Map() }; this.attachments.set(key, entry);
                }
                const name = agent ? 'Alex · Sales agent' : this.audience.get(participant.identity) || 'Participant';
                entry.fallback.textContent = name.split(/\s+/).slice(0, 2).map(n => n[0]).join('').toUpperCase();
                entry.label.textContent = `${name}${local ? ' (you)' : ''}${screen ? ' · Screen' : participant.isMicrophoneEnabled ? '' : ' · Muted'}`;
                const active = new Set();
                for (const pub of publications) {
                    const track = pub.track; if (!track || pub.isMuted || (local && track.kind === 'audio') ||
                        (agent && this.blockedAgentResponseGeneration > 0)) continue;
                    active.add(pub.trackSid);
                    if (entry.tracks.get(pub.trackSid)?.track === track) continue;
                    this.detach(entry, pub.trackSid);
                    const element = track.attach(); element.autoplay = true; element.playsInline = true; if (local) element.muted = true;
                    entry.tile.append(element); entry.tracks.set(pub.trackSid, { track, element });
                    void element.play().catch(() => this.report(this.status, 'sound_blocked'));
                }
                for (const sid of [...entry.tracks.keys()]) if (!active.has(sid)) this.detach(entry, sid);
            }
        }
        for (const [key, entry] of this.attachments) if (!wanted.has(key)) { for (const sid of [...entry.tracks.keys()]) this.detach(entry, sid); entry.tile.remove(); this.attachments.delete(key); }
    }
    detach(entry, sid) { const item = entry.tracks.get(sid); if (item) { item.track.detach(item.element); item.element.srcObject = null; item.element.remove(); entry.tracks.delete(sid); } }
    stopAgentPlayback(responseGeneration) {
        this.blockedAgentResponseGeneration = Math.max(this.blockedAgentResponseGeneration, responseGeneration || 1);
        for (const [key, entry] of this.attachments) if (key.startsWith('agent-'))
            for (const sid of [...entry.tracks.keys()]) this.detach(entry, sid);
    }
    allowAgentPlayback(responseGeneration) {
        if ((responseGeneration || 0) >= this.blockedAgentResponseGeneration) {
            this.blockedAgentResponseGeneration = 0;
            this.sync();
        }
    }
    async disconnect() {
        ++this.generation; this.stopPreview(); const room = this.room; this.room = null;
        if (room) {
            for (const [event, handler] of this.listeners) room.off(event, handler);
            this.listeners = [];
            for (const pub of room.localParticipant.trackPublications.values()) pub.track?.stop();
            try { await room.disconnect(true); } catch { this.report('disconnected', 'disconnect_unconfirmed'); }
        }
        for (const entry of this.attachments.values()) { for (const sid of [...entry.tracks.keys()]) this.detach(entry, sid); entry.tile.remove(); }
        this.attachments.clear(); this.sharing = false;
        this.releaseIdentity?.(); this.releaseIdentity = null;
    }
    async dispose() {
        if (this.disposed) return; this.disposed = true;
        this.env.clearInterval(this.watchdog); this.root.removeEventListener('click', this.onClick);
        this.env.navigator.mediaDevices?.removeEventListener('devicechange', this.onDevice);
        this.env.removeEventListener?.('pagehide', this.pageHide);
        this.env.removeEventListener?.('sales-room-invitation', this.invitationChanged); await this.disconnect();
    }
}
let controller;
export async function initialize(root, bridge) {
    await controller?.dispose();
    const sdk = await import('../lib/livekit/livekit-client-2.22.3.mjs');
    sdk.setLogLevel('silent');
    controller = new HumanRoomMedia(root, bridge, sdk); await controller.devices();
}
export function heartbeat(audience) { controller?.heartbeat(audience); }
export function connect(token) { return controller?.connect(token); }
export function disconnect() { return controller?.disconnect(); }
export function stopAgentPlayback(responseGeneration) { return controller?.stopAgentPlayback(responseGeneration); }
export function allowAgentPlayback(responseGeneration) { return controller?.allowAgentPlayback(responseGeneration); }
export function dispose() { return controller?.dispose(); }
export function entry(room) {
    // The bootstrap removed the capability before Blazor or other app scripts started.
    const secret = window.__salesRoomInvitation || ''; delete window.__salesRoomInvitation;
    let credential = ''; try { credential = sessionStorage.getItem(`sales-room:${room}`) || ''; } catch { }
    return { secret, credential };
}
export function saveSession(room, credential) { try { sessionStorage.setItem(`sales-room:${room}`, credential); return true; } catch { return false; } }
export function forgetSession(room) { try { sessionStorage.removeItem(`sales-room:${room}`); } catch { } }






