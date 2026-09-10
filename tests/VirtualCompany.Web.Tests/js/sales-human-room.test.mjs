import { test } from 'node:test';
import assert from 'node:assert/strict';
import { HumanRoomMedia } from '../../../src/VirtualCompany.Web/wwwroot/js/sales-human-room.mjs';

class Element {
    constructor() { this.children = []; this.dataset = {}; this.value = ''; this.classList = { add() {} }; this.isConnected = true; }
    append(...items) { this.children.push(...items); }
    replaceChildren(...items) { this.children = items; }
    remove() { this.removed = true; }
    addEventListener() {} removeEventListener() {} contains() { return true; }
    play() { return Promise.resolve(); }
    get options() { return this.children; }
}
function track(kind = 'audio') { return { kind, stopped: 0, detached: 0, stop() { this.stopped++; }, attach() { return new Element(); }, detach() { this.detached++; } }; }
function setup() {
    const grid = new Element(), video = new Element(); const root = new Element();
    root.querySelector = selector => selector === '[data-participants]' ? grid : selector === '[data-preview]' ? video : null;
    const reports = []; let tick;
    const environment = { document: { createElement: () => new Element() }, navigator: { mediaDevices: { addEventListener() {}, removeEventListener() {}, enumerateDevices: async () => [] } },
        setInterval(callback) { tick = callback; return 1; }, clearInterval() {}, MediaStream: class { constructor(tracks) { this.tracks = tracks; } getTracks() { return this.tracks; } } };
    class Room {
        constructor() { this.remoteParticipants = new Map(); this.handlers = new Map(); this.canPlaybackAudio = true; this.localParticipant = { identity: 'human-local', trackPublications: new Map(), isMicrophoneEnabled: false, isCameraEnabled: false, isScreenShareEnabled: false,
            setMicrophoneEnabled: async value => { this.localParticipant.isMicrophoneEnabled = value; }, setCameraEnabled: async value => { this.localParticipant.isCameraEnabled = value; } }; }
        on(event, handler) { this.handlers.set(event, handler); }
        off(event) { this.handlers.delete(event); }
        async connect() {} async disconnect() { this.disconnected = true; }
    }
    const events = Object.fromEntries(['ParticipantConnected','ParticipantDisconnected','TrackSubscribed','TrackUnsubscribed','TrackMuted','TrackUnmuted','LocalTrackPublished','LocalTrackUnpublished','Reconnecting','Reconnected','AudioPlaybackStatusChanged','Disconnected'].map(x => [x, x]));
    const media = new HumanRoomMedia(root, { invokeMethodAsync(_, state) { reports.push(state); return Promise.resolve(); } }, { Room, RoomEvent: events }, environment);
    return { media, environment, reports, grid, video, tick: () => tick() };
}
const token = { identity: 'human-local', url: 'wss://test.example', token: 'synthetic' };
test('preview never connects and permission denial retains listening-only access', async () => {
    const f = setup(); f.environment.navigator.mediaDevices.getUserMedia = async () => { throw Object.assign(new Error(), { name: 'NotAllowedError' }); };
    await f.media.previewDevices(); assert.equal(f.media.room, null); assert.equal(f.media.mic, false);
    assert.ok(f.reports.some(x => x.problem === 'permission_denied')); await f.media.dispose();
});
test('a late device grant after disposal stops every track without attaching it', async () => {
    const f = setup(); const local = track(); let grant;
    f.environment.navigator.mediaDevices.getUserMedia = () => new Promise(resolve => grant = resolve);
    const pending = f.media.previewDevices(); await f.media.dispose(); grant({ getTracks: () => [local] }); await pending;
    assert.equal(local.stopped, 1); assert.equal(f.media.preview, null); assert.equal(f.video.srcObject, null);
});
test('leave stops local microphone, camera and screen tracks and removes SDK handlers', async () => {
    const f = setup(); await f.media.connect(token); const room = f.media.room;
    const tracks = [track(), track('video'), track('video')]; tracks.forEach((t, i) => room.localParticipant.trackPublications.set(i, { track: t }));
    await f.media.disconnect(); assert.ok(tracks.every(t => t.stopped === 1)); assert.equal(room.handlers.size, 0); assert.equal(f.media.room, null);
    await f.media.dispose();
});
test('repeated connection and duplicate subscription events produce one audio attachment', async () => {
    const f = setup(); f.media.heartbeat([{ mediaIdentity: 'human-remote', displayName: '<script>Untrusted name</script>' }]); await f.media.connect(token);
    const original = f.media.room; await f.media.connect(token); assert.equal(f.media.room, original);
    const remote = track(); original.remoteParticipants.set('human-remote', { identity: 'human-remote', trackPublications: new Map([['sid', { source: 'microphone', trackSid: 'sid', track: remote }]]) });
    f.media.sync(); f.media.sync(); assert.equal(f.media.attachments.size, 1); const entry = [...f.media.attachments.values()][0]; assert.equal(entry.tracks.size, 1);
    assert.ok(entry.label.textContent.includes('<script>')); // Assigned as text, never HTML.
    f.media.heartbeat([]); assert.equal(remote.detached, 1); assert.equal(f.media.attachments.size, 0); await f.media.dispose();
});
test('takeover detaches agent audio and only a current response generation can restore it', async () => {
    const f = setup(); await f.media.connect(token); const room = f.media.room;
    const agentAudio = track(); room.remoteParticipants.set('agent-1-4', {
        identity: 'agent-1-4', trackPublications: new Map([['agent-audio', {
            source: 'microphone', trackSid: 'agent-audio', track: agentAudio
        }]])
    });
    f.media.sync(); assert.equal(f.media.attachments.get('agent-1-4:person').tracks.size, 1);
    f.media.stopAgentPlayback(9); assert.equal(agentAudio.detached, 1);
    assert.equal(f.media.attachments.get('agent-1-4:person').tracks.size, 0);
    f.media.allowAgentPlayback(8); assert.equal(f.media.attachments.get('agent-1-4:person').tracks.size, 0);
    f.media.allowAgentPlayback(9); assert.equal(f.media.attachments.get('agent-1-4:person').tracks.size, 1);
    await f.media.dispose();
});
test('control watchdog stops media independently of a lost Blazor callback', async () => {
    const f = setup(); await f.media.connect(token); const room = f.media.room; f.media.lastPulse = Date.now() - 16000;
    f.tick(); await Promise.resolve(); assert.equal(f.media.room, null); assert.ok(room.disconnected); assert.ok(f.reports.some(x => x.status === 'control_lost')); await f.media.dispose();
});
test('a stale connection completion after leave cannot publish', async () => {
    const f = setup(); let finish; const original = f.media.sdk.Room.prototype.connect;
    f.media.sdk.Room.prototype.connect = () => new Promise(resolve => finish = resolve);
    f.media.mic = true; f.media.camera = true; const pending = f.media.connect(token); const room = f.media.room;
    await f.media.disconnect(); finish(); await pending; assert.equal(room.localParticipant.isMicrophoneEnabled, false); assert.equal(room.localParticipant.isCameraEnabled, false);
    f.media.sdk.Room.prototype.connect = original; await f.media.dispose();
});
test('a second tab cannot connect while the identity lock is held', async () => {
    const f = setup(); f.environment.navigator.locks = { request: async (_, __, callback) => callback(null) };
    await f.media.connect(token); assert.equal(f.media.room, null); assert.ok(f.reports.some(x => x.problem === 'other_tab')); await f.media.dispose();
});

test('invitation bootstrap consumes both initial and same-page fragments without retaining URL secrets', async () => {
    const { readFile } = await import('node:fs/promises'); const { runInNewContext } = await import('node:vm');
    const source = await readFile(new URL('../../../src/VirtualCompany.Web/wwwroot/js/sales-room-entry.js', import.meta.url), 'utf8');
    const location = { pathname: '/sales/rooms/00000000-0000-0000-0000-000000000001', search: '', hash: '#invite=' + 'x'.repeat(43) };
    const handlers = new Map(); let notifications = 0;
    const window = { addEventListener(name, handler) { handlers.set(name, handler); }, dispatchEvent(event) { if (event.type === 'sales-room-invitation') notifications++; } };
    const history = { state: null, replaceState(_, __, path) { assert.ok(!path.includes('#')); location.hash = ''; } };
    runInNewContext(source, { window, location, history, URLSearchParams, Event });
    assert.equal(location.hash, ''); assert.equal(window.__salesRoomInvitation, 'x'.repeat(43));
    location.hash = '#invite=' + 'y'.repeat(43); handlers.get('hashchange')();
    assert.equal(location.hash, ''); assert.equal(window.__salesRoomInvitation, 'y'.repeat(43)); assert.equal(notifications, 2);
});

test('turning on a prejoin camera preserves an explicitly muted microphone', async () => {
    const f = setup(); const requested = [];
    f.environment.navigator.mediaDevices.getUserMedia = async options => { requested.push(options); return {getTracks:()=>[track('video')]}; };
    await f.media.action('camera'); assert.equal(f.media.mic, false); assert.equal(f.media.camera, true);
    assert.equal(requested.length, 1); assert.ok(requested[0].video); assert.equal(requested[0].audio, undefined); await f.media.dispose();
});
test('cancelling screen selection reports a recoverable error and keeps human media connected', async () => {
    const f = setup(); await f.media.connect(token); const room = f.media.room;
    room.localParticipant.setScreenShareEnabled = async () => { throw Object.assign(new Error(), {name:'NotAllowedError'}); };
    await f.media.action('share'); assert.equal(f.media.room, room); assert.ok(f.reports.some(x=>x.problem==='share_unavailable')); await f.media.dispose();
});
