const trackers = new Map();
const milestones = [25, 50, 75, 100];

function computeDepth() {
    const doc = document.documentElement;
    const body = document.body;
    const top = window.scrollY || doc.scrollTop || body.scrollTop || 0;
    const height = Math.max(body.scrollHeight, doc.scrollHeight, body.offsetHeight, doc.offsetHeight, body.clientHeight, doc.clientHeight);
    const viewport = window.innerHeight || doc.clientHeight || 0;
    const scrollable = height - viewport;
    if (scrollable <= 1) {
        return 100;
    }

    return Math.max(0, Math.min(100, Math.round((top / scrollable) * 100)));
}

export function registerDashboardScrollTracker(dotNetHelper) {
    const id = `${Date.now()}-${Math.random().toString(16).slice(2)}`;
    let frame = null;
    let milestoneIndex = 0;
    let disposed = false;

    const publish = () => {
        frame = null;
        if (disposed) return;
        const depth = computeDepth();

        while (milestoneIndex < milestones.length && depth >= milestones[milestoneIndex]) {
            // The Overview component can be disposed while an invocation is in flight.
            // Telemetry is best effort; a closed circuit must not create an unhandled rejection.
            dotNetHelper.invokeMethodAsync("OnDashboardScrollDepthChanged", milestones[milestoneIndex])
                .catch(error => { if (!disposed) console.debug("Dashboard scroll telemetry unavailable.", error); });
            milestoneIndex += 1;
        }
    };

    const schedule = () => {
        if (disposed || frame !== null) {
            return;
        }
        frame = window.requestAnimationFrame(publish);
    };

    const dispose = () => {
        disposed = true;
        if (frame !== null) {
            window.cancelAnimationFrame(frame);
            frame = null;
        }

        window.removeEventListener("scroll", schedule);
        window.removeEventListener("resize", schedule);
        trackers.delete(id);
    };

    const entry = { schedule, dispose };
    trackers.set(id, entry);
    window.addEventListener("scroll", schedule, { passive: true });
    window.addEventListener("resize", schedule, { passive: true });
    schedule();
    return id;
}

export function disposeDashboardScrollTracker(id) {
    const entry = trackers.get(id);
    if (!entry) {
        return;
    }

    entry.dispose();
}
