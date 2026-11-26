(() => {
    const key = "zava.reading-options.v1";
    const scales = [1, 1.25, 1.5, 1.75, 2];
    let scale = 1;
    let contrast = false;
    let notice = "";

    try {
        const saved = localStorage.getItem(key);
        if (saved !== null) {
            const options = JSON.parse(saved);
            if (!options || typeof options !== "object"
                || !scales.includes(options.scale) || typeof options.contrast !== "boolean") {
                throw new SyntaxError("Invalid display preferences.");
            }
            scale = options.scale;
            contrast = options.contrast;
        }
    } catch (error) {
        if (!(error instanceof DOMException || error instanceof SyntaxError)) throw error;
        notice = "Saved display settings were unavailable. You can still adjust this page.";
    }

    function apply() {
        document.documentElement.style.setProperty("--reading-scale", String(scale));
        document.documentElement.dataset.contrast = contrast ? "high" : "standard";
    }
    apply();

    document.addEventListener("DOMContentLoaded", () => {
        const controls = document.querySelectorAll("[data-reading-action]");
        const status = document.getElementById("reading-status");

        function update() {
            controls.forEach((button) => {
                const action = button.dataset.readingAction;
                button.disabled = action === "decrease" && scale === 1 || action === "increase" && scale === 2;
                if (action === "contrast") {
                    button.setAttribute("aria-pressed", String(contrast));
                    button.textContent = `High contrast: ${contrast ? "On" : "Off"}`;
                }
            });
            if (status) status.textContent = `Text size ${Math.round(scale * 100)}%. ${notice}`;
        }

        controls.forEach((button) => {
            button.addEventListener("click", () => {
                const action = button.dataset.readingAction;
                if (action === "increase") scale = Math.min(2, scale + 0.25);
                if (action === "decrease") scale = Math.max(1, scale - 0.25);
                if (action === "contrast") contrast = !contrast;
                if (action === "reset") { scale = 1; contrast = false; }
                apply();
                notice = "";
                try {
                    localStorage.setItem(key, JSON.stringify({ scale, contrast }));
                } catch (error) {
                    if (!(error instanceof DOMException)) throw error;
                    notice = "This browser could not save your settings; they still apply to this page.";
                }
                update();
            });
        });
        update();

        document.querySelectorAll(".field-validation-error").forEach((message, index) => {
            const field = document.getElementsByName(message.dataset.valmsgFor)[0];
            if (!(field instanceof HTMLInputElement || field instanceof HTMLTextAreaElement)) return;
            message.id ||= `validation-message-${index}`;
            field.setAttribute("aria-invalid", "true");
            field.setAttribute("aria-describedby", `${field.getAttribute("aria-describedby") || ""} ${message.id}`.trim());
        });
        const summary = document.querySelector(".validation-summary-errors");
        if (summary) {
            summary.tabIndex = -1;
            summary.focus();
        } else {
            document.querySelector(".input-validation-error")?.focus();
        }
    });
})();
