const teamsSdkUrl = "https://res.cdn.office.net/teams-js/2.55.0/js/MicrosoftTeams.min.js";

async function loadTeamsSdk() {
    if (window.microsoftTeams) return window.microsoftTeams;
    await new Promise((resolve, reject) => {
        const timeout = window.setTimeout(() => reject(new Error("teams_sdk_load_failed")), 5000);
        const existing = document.querySelector(`script[src="${teamsSdkUrl}"]`);
        if (existing) {
            existing.addEventListener("load", () => { window.clearTimeout(timeout); resolve(); }, { once: true });
            existing.addEventListener("error", () => { window.clearTimeout(timeout); reject(new Error("teams_sdk_load_failed")); }, { once: true });
            return;
        }
        const script = document.createElement("script");
        script.src = teamsSdkUrl;
        script.async = true;
        script.crossOrigin = "anonymous";
        script.integrity = "sha384-qOOENVJGWr7UF7wvgyycjvq3sZen4SEYQwZMKqpquvV/PNMtSJb3VGZ/sAIKsZDw";
        script.onload = () => { window.clearTimeout(timeout); resolve(); };
        script.onerror = () => { window.clearTimeout(timeout); reject(new Error("teams_sdk_load_failed")); };
        document.head.appendChild(script);
    });
    return window.microsoftTeams;
}

function callbackResult(invoke) {
    return new Promise((resolve, reject) => invoke((error, value) => error ? reject(error) : resolve(value)));
}

export async function initialize() {
    try {
        const teams = await loadTeamsSdk();
        if (!teams?.app) return unavailable("teams_sdk_unavailable");
        await teams.app.initialize();
        const context = await teams.app.getContext();
        let accessToken = null;
        try {
            accessToken = await teams.authentication.getAuthToken();
        } catch {
            // The API remains inaccessible; this is reported as a missing SSO token.
        }

        let canShareToStage = false;
        let shareReasonCode = "stage_share_unavailable";
        if (teams.meeting?.getAppContentStageSharingCapabilities) {
            try {
                const capabilities = await callbackResult(cb => teams.meeting.getAppContentStageSharingCapabilities(cb));
                canShareToStage = capabilities?.doesAppHaveSharePermission === true;
                shareReasonCode = canShareToStage ? null : "meeting_stage_permission_missing";
            } catch {
                shareReasonCode = "stage_capability_check_failed";
            }
        } else {
            shareReasonCode = "stage_share_api_unavailable";
        }

        return {
            inTeams: true,
            frameContext: context.page?.frameContext ?? null,
            tenantId: context.user?.tenant?.id ?? null,
            userId: context.user?.id ?? null,
            meetingId: context.meeting?.id ?? null,
            canShareToStage,
            shareReasonCode,
            accessToken
        };
    } catch (error) {
        return unavailable(error?.message === "teams_sdk_load_failed" ? "teams_sdk_load_failed" : "not_in_teams");
    }
}

export async function shareToMeeting(stageUrl) {
    try {
        const teams = window.microsoftTeams;
        if (!teams?.meeting?.shareAppContentToStage) {
            return failed("stage_share_api_unavailable", "This Teams client does not expose app stage sharing.");
        }
        const capabilities = await callbackResult(cb => teams.meeting.getAppContentStageSharingCapabilities(cb));
        if (capabilities?.doesAppHaveSharePermission !== true) {
            return failed("meeting_stage_permission_missing", "Teams did not grant permission to share this app to the meeting stage.");
        }
        await callbackResult(cb => teams.meeting.shareAppContentToStage(cb, stageUrl));
        return { succeeded: true, reasonCode: null, message: "Shared to the meeting stage." };
    } catch (error) {
        return failed("teams_share_denied", error?.message ?? "Teams rejected the share request.");
    }
}

export async function configureTab(sidePanelUrl, websiteUrl) {
    try {
        const teams = window.microsoftTeams;
        if (!teams?.pages?.config) return failed("configuration_api_unavailable", "Teams configuration is unavailable.");
        await teams.pages.config.setConfig({
            suggestedDisplayName: "Alex Sales Presenter",
            entityId: "alex-sales-presenter",
            contentUrl: sidePanelUrl,
            websiteUrl
        });
        teams.pages.config.setValidityState(true);
        return { succeeded: true, reasonCode: null, message: "Meeting tab configured." };
    } catch (error) {
        return failed("configuration_failed", error?.message ?? "Teams could not configure the meeting tab.");
    }
}

function unavailable(reasonCode) {
    return {
        inTeams: false,
        frameContext: null,
        tenantId: null,
        userId: null,
        meetingId: null,
        canShareToStage: false,
        shareReasonCode: reasonCode,
        accessToken: null
    };
}

function failed(reasonCode, message) {
    return { succeeded: false, reasonCode, message };
}
