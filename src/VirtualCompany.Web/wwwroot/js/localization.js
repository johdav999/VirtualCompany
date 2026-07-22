window.virtualCompanyLocalization = {
    submit: function (formId) {
        const form = document.getElementById(formId);
        if (form) {
            form.requestSubmit();
        }
    }
};
