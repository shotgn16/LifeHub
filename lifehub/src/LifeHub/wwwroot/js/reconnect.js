// Getting back to LifeHub after the phone or tab has been asleep.
// The live connection to the server drops while the page is in the background. Blazor tries to reconnect;
// if the server has already let the old session go, the only cure is reloading the page — so LifeHub does it
// for you instead of showing a message you have to find and tap.
(function () {
    // Retry every 2 seconds (instead of slowing down), so coming back to the app reconnects quickly.
    Blazor.start({ circuit: { reconnectionOptions: { maxRetries: 60, retryIntervalMilliseconds: 2000 } } });

    const modal = () => document.getElementById("components-reconnect-modal");
    const is = (c) => modal()?.classList.contains(c);
    let reloading = false;

    function reload() {
        if (reloading) return;
        reloading = true;
        // Only reload when the server answers; otherwise keep the "can't reach LifeHub" message up.
        fetch(document.baseURI + "health", { cache: "no-store" })
            .then(r => { if (r.ok) location.reload(); else reloading = false; })
            .catch(() => { reloading = false; });
    }

    async function tryNow() {
        if (is("components-reconnect-rejected")) return reload();
        if (is("components-reconnect-failed") || is("components-reconnect-show")) {
            try {
                const ok = window.Blazor && Blazor.reconnect ? await Blazor.reconnect() : false;
                if (!ok) reload();
            } catch { reload(); }
        }
    }

    // Watch the reconnect message: a rejected session reloads at once, a failed one when the page is in view.
    function watch() {
        const m = modal();
        if (!m) return setTimeout(watch, 500);
        new MutationObserver(() => {
            if (is("components-reconnect-rejected")) reload();
            else if (is("components-reconnect-failed") && document.visibilityState === "visible") tryNow();
        }).observe(m, { attributes: true, attributeFilter: ["class"] });
    }
    watch();

    // Coming back to the app or tab: check straight away rather than waiting for the next retry.
    document.addEventListener("visibilitychange", () => { if (document.visibilityState === "visible") setTimeout(tryNow, 300); });
    window.addEventListener("pageshow", (e) => { if (e.persisted) setTimeout(tryNow, 300); });
    window.addEventListener("online", () => setTimeout(tryNow, 300));
    // The phone app calls this when it's reopened.
    window.lifehubResume = () => setTimeout(tryNow, 300);
})();
