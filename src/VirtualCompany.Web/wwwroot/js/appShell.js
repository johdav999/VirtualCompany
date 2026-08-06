window.virtualCompanyShell = (() => {
    let escapeHandler = null;

    function setDrawerState(open, toggleButton, drawer) {
        document.body.classList.toggle("vc-nav-open", open);

        if (escapeHandler) {
            document.removeEventListener("keydown", escapeHandler);
            escapeHandler = null;
        }

        if (open) {
            escapeHandler = (event) => {
                if (event.key === "Escape") {
                    toggleButton?.click();
                }
            };
            document.addEventListener("keydown", escapeHandler);
            window.requestAnimationFrame(() => {
                drawer?.querySelector("a, button")?.focus();
            });
            return;
        }

        toggleButton?.focus();
    }

    return { setDrawerState };
})();
