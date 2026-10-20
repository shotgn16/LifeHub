// LifeHub barcode scanner.
// Uses the browser's built-in BarcodeDetector where available (Chrome on Android) and falls back
// to ZXing (bundled, loaded on demand) everywhere else, e.g. iPhone Safari and desktop browsers.
// Live camera needs a secure context (HTTPS or localhost); "take a photo" works everywhere.

const FORMATS = ["ean_13", "ean_8", "upc_a", "upc_e", "code_128", "code_39", "itf", "qr_code"];
let zxingLoading = null;
let decoderPromise = null;
let session = null;
let audioCtx = null;

function loadZxing() {
    if (window.ZXing) return Promise.resolve(window.ZXing);
    zxingLoading ??= new Promise((resolve, reject) => {
        const s = document.createElement("script");
        s.src = new URL("./zxing.min.js", import.meta.url).href;
        s.onload = () => resolve(window.ZXing);
        s.onerror = () => { zxingLoading = null; reject(new Error("Couldn't load the barcode library")); };
        document.head.appendChild(s);
    });
    return zxingLoading;
}

function sourceSize(src) {
    return [src.videoWidth || src.naturalWidth || src.width || 0, src.videoHeight || src.naturalHeight || src.height || 0];
}

async function createDecoder() {
    if ("BarcodeDetector" in window) {
        try {
            const supported = await window.BarcodeDetector.getSupportedFormats();
            const formats = FORMATS.filter(f => supported.includes(f));
            if (formats.length) {
                const detector = new window.BarcodeDetector({ formats });
                return {
                    kind: "native",
                    decode: async (src) => {
                        const found = await detector.detect(src);
                        return found.length ? found[0].rawValue : null;
                    }
                };
            }
        } catch { /* fall through to ZXing */ }
    }

    const Z = await loadZxing();
    const hints = new Map();
    const F = Z.BarcodeFormat;
    hints.set(Z.DecodeHintType.POSSIBLE_FORMATS, [F.EAN_13, F.EAN_8, F.UPC_A, F.UPC_E, F.CODE_128, F.CODE_39, F.ITF, F.QR_CODE]);
    hints.set(Z.DecodeHintType.TRY_HARDER, true);
    const reader = new Z.MultiFormatReader();
    reader.setHints(hints);
    const canvas = document.createElement("canvas");
    const ctx = canvas.getContext("2d", { willReadFrequently: true });

    return {
        kind: "zxing",
        decode: async (src, maxDim = 1280) => {
            const [w, h] = sourceSize(src);
            if (!w || !h) return null;
            const scale = Math.min(1, maxDim / Math.max(w, h));
            canvas.width = Math.round(w * scale);
            canvas.height = Math.round(h * scale);
            ctx.drawImage(src, 0, 0, canvas.width, canvas.height);
            try {
                const bitmap = new Z.BinaryBitmap(new Z.HybridBinarizer(new Z.HTMLCanvasElementLuminanceSource(canvas)));
                return reader.decodeWithState(bitmap).getText();
            } catch {
                return null; // NotFound / checksum errors are normal between frames
            } finally {
                reader.reset();
            }
        }
    };
}

function decoder() {
    decoderPromise ??= createDecoder().catch(e => { decoderPromise = null; throw e; });
    return decoderPromise;
}

function feedback() {
    try { if (navigator.userActivation?.hasBeenActive !== false) navigator.vibrate?.(60); } catch { }
    try {
        audioCtx ??= new (window.AudioContext || window.webkitAudioContext)();
        const o = audioCtx.createOscillator(), g = audioCtx.createGain();
        o.frequency.value = 1320; g.gain.value = 0.08;
        o.connect(g); g.connect(audioCtx.destination);
        o.start(); o.stop(audioCtx.currentTime + 0.08);
    } catch { }
}

export function capabilities() {
    return {
        secure: window.isSecureContext === true,
        camera: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia),
        native: "BarcodeDetector" in window
    };
}

export async function start(video, dotnet) {
    await stop();
    const dec = await decoder();
    const stream = await navigator.mediaDevices.getUserMedia({
        audio: false,
        video: { facingMode: { ideal: "environment" }, width: { ideal: 1280 }, height: { ideal: 720 } }
    });
    video.setAttribute("playsinline", "");
    video.muted = true;
    video.srcObject = stream;
    await video.play();
    const track = stream.getVideoTracks()[0];
    const caps = track.getCapabilities ? track.getCapabilities() : {};
    const s = { stream, track, video, dotnet, dec, running: true, busy: false, last: "", lastSeen: 0, lastReport: 0, torch: false };
    session = s;
    tick(s);
    return { torch: !!caps.torch, decoder: dec.kind };
}

function tick(s) {
    if (!s.running) return;
    setTimeout(async () => {
        if (!s.running) return;
        if (!s.busy && s.video.readyState >= 2 && !document.hidden) {
            s.busy = true;
            try {
                const code = await s.dec.decode(s.video);
                const now = Date.now();
                if (code) {
                    // The same code is ignored while it stays in view (and for 3s after it was reported),
                    // so a slightly blurry barcode can't be counted twice. Take it away for ~2s to scan another.
                    const repeat = code === s.last && (now - s.lastSeen < 2000 || now - s.lastReport < 3000);
                    s.lastSeen = now;
                    if (!repeat) {
                        s.last = code;
                        s.lastReport = now;
                        feedback();
                        s.dotnet.invokeMethodAsync("OnScanned", code);
                    }
                }
            } catch { }
            s.busy = false;
        }
        tick(s);
    }, s.dec.kind === "native" ? 120 : 200);
}

export async function setTorch(on) {
    if (!session) return false;
    try {
        await session.track.applyConstraints({ advanced: [{ torch: !!on }] });
        session.torch = !!on;
    } catch { }
    return session.torch;
}

export async function stop() {
    const s = session;
    session = null;
    if (!s) return;
    s.running = false;
    try { s.stream.getTracks().forEach(t => t.stop()); } catch { }
    try { s.video.srcObject = null; } catch { }
}

function loadImage(file) {
    return new Promise((resolve, reject) => {
        const url = URL.createObjectURL(file);
        const img = new Image();
        img.onload = () => resolve({ img, url });
        img.onerror = () => { URL.revokeObjectURL(url); reject(new Error("Couldn't read that photo")); };
        img.src = url;
    });
}

/** Decodes a photo chosen with the file input (works without HTTPS). */
export function bindPhoto(input, dotnet) {
    input.onchange = async () => {
        const file = input.files && input.files[0];
        input.value = "";
        if (!file) return;
        let code = null;
        try {
            const dec = await decoder();
            const { img, url } = await loadImage(file);
            try {
                for (const size of [1280, 1920, 800, 2600]) {
                    code = await dec.decode(img, size);
                    if (code) break;
                    if (dec.kind === "native") break;
                }
            } finally { URL.revokeObjectURL(url); }
        } catch { }
        if (code) feedback();
        dotnet.invokeMethodAsync(code ? "OnScanned" : "OnPhotoFailed", code || "");
    };
}

document.addEventListener("visibilitychange", () => {
    // Free the camera if the phone is locked or the tab is switched for a while.
    if (document.hidden && session) {
        const s = session;
        setTimeout(() => { if (document.hidden && session === s) { stop(); s.dotnet.invokeMethodAsync("OnCameraStopped"); } }, 30000);
    }
});
