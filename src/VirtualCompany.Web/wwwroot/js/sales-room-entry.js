// Runs before framework, analytics, and application scripts, including same-page invitation links.
(() => {
    function consume() {
        if (!/^\/sales\/rooms\/[0-9a-f-]{36}\/?$/i.test(location.pathname) || !location.hash) return;
        const fragment = location.hash;
        history.replaceState(history.state, '', location.pathname + location.search);
        const value = new URLSearchParams(fragment.slice(1)).get('invite');
        if (value && /^[A-Za-z0-9_-]{43}$/.test(value)) {
            window.__salesRoomInvitation = value;
            window.dispatchEvent(new Event('sales-room-invitation'));
        }
    }
    consume();
    window.addEventListener('hashchange', consume);
})();
