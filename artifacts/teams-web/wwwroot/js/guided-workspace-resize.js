const instances = new Map();

export function initialize(elementId, storageKey) {
    dispose(elementId);
    const container = document.getElementById(elementId);
    const separator = container?.querySelector(".guided-workspace__resizer");
    if (!container || !separator) return;

    const defaultRatio = 0.42;
    let currentWidth = null;

    const limits = () => {
        const width = container.getBoundingClientRect().width;
        const compact = width <= 1100;
        const minimumConversation = compact ? 300 : 340;
        const minimumDraft = compact ? 380 : 460;
        return { width, minimumConversation, maximumConversation: Math.max(minimumConversation, width - minimumDraft - 11) };
    };
    const apply = value => {
        const boundary = limits();
        currentWidth = Math.min(boundary.maximumConversation, Math.max(boundary.minimumConversation, value));
        container.style.setProperty("--conversation-width", `${Math.round(currentWidth)}px`);
        separator.setAttribute("aria-valuemin", Math.round(boundary.minimumConversation * 100 / boundary.width));
        separator.setAttribute("aria-valuemax", Math.round(boundary.maximumConversation * 100 / boundary.width));
        separator.setAttribute("aria-valuenow", Math.round(currentWidth * 100 / boundary.width));
    };
    const reset = () => {
        const boundary = limits();
        currentWidth = boundary.width * defaultRatio;
        apply(currentWidth);
        try { window.localStorage.removeItem(storageKey); } catch { }
    };
    const persist = () => {
        if (currentWidth == null) return;
        try { window.localStorage.setItem(storageKey, String(Math.round(currentWidth))); } catch { }
    };
    const onPointerDown = event => {
        if (event.button !== 0 || window.matchMedia("(max-width: 900px)").matches) return;
        event.preventDefault();
        separator.setPointerCapture(event.pointerId);
        document.body.classList.add("guided-workspace-resizing");
    };
    const onPointerMove = event => {
        if (!separator.hasPointerCapture(event.pointerId)) return;
        apply(event.clientX - container.getBoundingClientRect().left);
    };
    const onPointerUp = event => {
        if (!separator.hasPointerCapture(event.pointerId)) return;
        separator.releasePointerCapture(event.pointerId);
        document.body.classList.remove("guided-workspace-resizing");
        persist();
    };
    const onKeyDown = event => {
        if (event.key === "Home") { event.preventDefault(); reset(); return; }
        if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
        event.preventDefault();
        const step = event.shiftKey ? 64 : 24;
        apply((currentWidth ?? limits().width * defaultRatio) + (event.key === "ArrowRight" ? step : -step));
        persist();
    };
    const onResize = () => apply(currentWidth ?? limits().width * defaultRatio);
    const onDoubleClick = () => reset();

    separator.addEventListener("pointerdown", onPointerDown);
    separator.addEventListener("pointermove", onPointerMove);
    separator.addEventListener("pointerup", onPointerUp);
    separator.addEventListener("pointercancel", onPointerUp);
    separator.addEventListener("keydown", onKeyDown);
    separator.addEventListener("dblclick", onDoubleClick);
    window.addEventListener("resize", onResize);

    let restored = null;
    try { restored = Number.parseInt(window.localStorage.getItem(storageKey), 10); } catch { }
    apply(Number.isFinite(restored) ? restored : limits().width * defaultRatio);
    instances.set(elementId, { separator, onPointerDown, onPointerMove, onPointerUp, onKeyDown, onDoubleClick, onResize });
}

export function dispose(elementId) {
    const instance = instances.get(elementId);
    if (!instance) return;
    instance.separator.removeEventListener("pointerdown", instance.onPointerDown);
    instance.separator.removeEventListener("pointermove", instance.onPointerMove);
    instance.separator.removeEventListener("pointerup", instance.onPointerUp);
    instance.separator.removeEventListener("pointercancel", instance.onPointerUp);
    instance.separator.removeEventListener("keydown", instance.onKeyDown);
    instance.separator.removeEventListener("dblclick", instance.onDoubleClick);
    window.removeEventListener("resize", instance.onResize);
    document.body.classList.remove("guided-workspace-resizing");
    instances.delete(elementId);
}
