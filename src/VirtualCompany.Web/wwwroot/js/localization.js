window.virtualCompanyLocalization = {
    submit: async function (formId) {
        const form = document.getElementById(formId);
        if (!form || form.dataset.submitting === "true") {
            return;
        }

        form.dataset.submitting = "true";

        try {
            const response = await fetch(form.action, {
                method: "POST",
                body: new FormData(form),
                credentials: "same-origin",
                redirect: "manual"
            });

            // A manual redirect is the successful response from /localization/apply.
            // Reload the route that is current now instead of following the stale
            // return URL captured before the user navigated.
            if (!response.ok && response.type !== "opaqueredirect") {
                throw new Error(`Culture synchronization failed with status ${response.status}.`);
            }

            window.location.reload();
        } catch {
            form.dataset.submitting = "false";
            form.requestSubmit();
        }
    }
};
