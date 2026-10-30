// Captura de prints/PDFs e leitura de texto (OCR) no navegador.
// O C# (Blazor) recebe os resultados pelos métodos [JSInvokable] da página Home.
(function () {
    "use strict";

    const THEME_KEY = "myquestion-theme";
    const MAX_PDF_PAGES = 15;
    const state = { dotnet: null, dropZone: null, fileInput: null, queue: Promise.resolve(), worker: null, current: null, handlers: [] };

    // ---------- Tema ----------
    function getTheme() {
        try { return localStorage.getItem(THEME_KEY) || "dark"; } catch { return "dark"; }
    }
    function setTheme(theme) {
        document.documentElement.dataset.theme = theme;
        try { localStorage.setItem(THEME_KEY, theme); } catch { /* sem persistência */ }
    }
    document.documentElement.dataset.theme = getTheme();

    // ---------- Utilidades ----------
    let seq = 0;
    const newId = () => `f${Date.now().toString(36)}${(seq++).toString(36)}`;
    const call = (method, ...args) => state.dotnet ? state.dotnet.invokeMethodAsync(method, ...args).catch(() => { }) : Promise.resolve();

    const STATUS = {
        "loading tesseract core": "Carregando leitor",
        "initializing tesseract": "Iniciando leitor",
        "initialized tesseract": "Leitor pronto",
        "loading language traineddata": "Baixando idioma português",
        "loading language traineddata (from cache)": "Carregando idioma",
        "loaded language traineddata": "Idioma carregado",
        "initializing api": "Preparando",
        "initialized api": "Preparando",
        "recognizing text": "Lendo texto"
    };

    let lastReport = 0;
    function report(id, pct, status, force) {
        const now = performance.now();
        if (!force && now - lastReport < 120) return;
        lastReport = now;
        call("FileProgress", id, Math.round(pct), status);
    }

    // ---------- OCR (Tesseract.js) ----------
    async function getWorker() {
        if (state.worker) return state.worker;
        if (!window.Tesseract) throw new Error("Leitor de texto (OCR) não carregou. Verifique a internet ou o bloqueio do navegador.");
        state.worker = await Tesseract.createWorker("por", 1, {
            logger: (m) => {
                const job = state.current;
                if (!job) return;
                const label = STATUS[m.status] || "Processando";
                const base = m.status === "recognizing text" ? job.base + job.span * (m.progress || 0) : job.base * 0.9 + 5 * (m.progress || 0);
                report(job.id, Math.min(99, base), job.prefix + label);
            }
        });
        await state.worker.setParameters({ preserve_interword_spaces: "1" });
        return state.worker;
    }

    async function ocrCanvas(id, canvas, base, span, prefix) {
        state.current = { id, base, span, prefix: prefix || "" };
        try {
            const worker = await getWorker();
            const { data } = await worker.recognize(canvas);
            return data.text || "";
        } finally {
            state.current = null;
        }
    }

    function loadImage(file) {
        return new Promise((resolve, reject) => {
            const url = URL.createObjectURL(file);
            const img = new Image();
            img.onload = () => resolve({ img, url });
            img.onerror = () => { URL.revokeObjectURL(url); reject(new Error("Não foi possível abrir a imagem.")); };
            img.src = url;
        });
    }

    // Amplia prints pequenos, converte para cinza e inverte tema escuro (Teams dark) para melhorar o OCR.
    function prepareForOcr(source, width, height) {
        const scale = width < 1800 ? Math.min(3, 1800 / width) : 1;
        const canvas = document.createElement("canvas");
        canvas.width = Math.round(width * scale);
        canvas.height = Math.round(height * scale);
        const ctx = canvas.getContext("2d", { willReadFrequently: true });
        ctx.imageSmoothingQuality = "high";
        ctx.drawImage(source, 0, 0, canvas.width, canvas.height);

        const image = ctx.getImageData(0, 0, canvas.width, canvas.height);
        const d = image.data;
        let sum = 0;
        for (let i = 0; i < d.length; i += 4) {
            const g = 0.299 * d[i] + 0.587 * d[i + 1] + 0.114 * d[i + 2];
            d[i] = d[i + 1] = d[i + 2] = g;
            sum += g;
        }
        const dark = sum / (d.length / 4) < 110;
        if (dark) {
            for (let i = 0; i < d.length; i += 4) d[i] = d[i + 1] = d[i + 2] = 255 - d[i];
        }
        ctx.putImageData(image, 0, 0);
        return canvas;
    }

    function thumbnail(source, width, height) {
        const max = 160;
        const scale = Math.min(1, max / Math.max(width, height));
        const c = document.createElement("canvas");
        c.width = Math.max(1, Math.round(width * scale));
        c.height = Math.max(1, Math.round(height * scale));
        c.getContext("2d").drawImage(source, 0, 0, c.width, c.height);
        return c.toDataURL("image/jpeg", 0.8);
    }

    async function processImage(id, file) {
        report(id, 2, "Abrindo imagem", true);
        const { img, url } = await loadImage(file);
        try {
            call("FilePreview", id, thumbnail(img, img.naturalWidth, img.naturalHeight));
            const canvas = prepareForOcr(img, img.naturalWidth, img.naturalHeight);
            return await ocrCanvas(id, canvas, 10, 89);
        } finally {
            URL.revokeObjectURL(url);
        }
    }

    // ---------- PDF (pdf.js) ----------
    async function processPdf(id, file) {
        if (!window.pdfjsLib) throw new Error("Leitor de PDF não carregou. Verifique a internet.");
        report(id, 2, "Abrindo PDF", true);
        const data = new Uint8Array(await file.arrayBuffer());
        const pdf = await pdfjsLib.getDocument({ data }).promise;
        const total = Math.min(pdf.numPages, MAX_PDF_PAGES);
        const parts = [];

        for (let n = 1; n <= total; n++) {
            const page = await pdf.getPage(n);
            const base = 5 + ((n - 1) / total) * 94;
            const span = 94 / total;

            if (n === 1) {
                const vp = page.getViewport({ scale: 0.4 });
                const c = document.createElement("canvas");
                c.width = vp.width; c.height = vp.height;
                await page.render({ canvasContext: c.getContext("2d"), viewport: vp }).promise;
                call("FilePreview", id, c.toDataURL("image/jpeg", 0.8));
            }

            const content = await page.getTextContent();
            let text = content.items.map((i) => (i.str || "") + (i.hasEOL ? "\n" : " ")).join("").trim();

            if (text.replace(/\s/g, "").length < 30) {
                // Página escaneada: renderiza e faz OCR.
                const vp = page.getViewport({ scale: 2 });
                const c = document.createElement("canvas");
                c.width = vp.width; c.height = vp.height;
                await page.render({ canvasContext: c.getContext("2d"), viewport: vp }).promise;
                text = await ocrCanvas(id, prepareForOcr(c, c.width, c.height), base, span, `Página ${n}/${total} · `);
            } else {
                report(id, base + span, `Página ${n}/${total} · texto extraído`, true);
            }
            if (text) parts.push(text);
        }

        if (pdf.numPages > total) parts.push(`[PDF com ${pdf.numPages} páginas: foram lidas as ${total} primeiras]`);
        return parts.join("\n\n");
    }

    // ---------- Fila ----------
    function cleanText(text) {
        return (text || "")
            .replace(/[ \t]+\n/g, "\n")
            .replace(/\n{3,}/g, "\n\n")
            .replace(/[ \t]{2,}/g, " ")
            .trim();
    }

    function accept(files) {
        for (const file of files) {
            const isPdf = file.type === "application/pdf" || /\.pdf$/i.test(file.name);
            const isImage = file.type.startsWith("image/");
            if (!isPdf && !isImage) continue;

            const id = newId();
            const name = file.name && file.name !== "image.png" ? file.name : `print-${new Date().toLocaleTimeString("pt-BR")}.png`;
            call("FileAdded", id, name, isPdf ? "pdf" : "image");

            state.queue = state.queue.then(async () => {
                try {
                    const text = isPdf ? await processPdf(id, file) : await processImage(id, file);
                    await call("FileDone", id, cleanText(text));
                } catch (err) {
                    console.error(err);
                    await call("FileFailed", id, (err && err.message) || "Falha ao ler o arquivo.");
                }
            });
        }
    }

    // ---------- Eventos ----------
    function on(target, type, fn) {
        target.addEventListener(type, fn);
        state.handlers.push(() => target.removeEventListener(type, fn));
    }

    function init(dotnetRef, dropZone, fileInput) {
        dispose();
        state.dotnet = dotnetRef;
        state.dropZone = dropZone;
        state.fileInput = fileInput;

        on(window, "paste", (e) => {
            const items = Array.from((e.clipboardData && e.clipboardData.items) || []);
            const files = items.filter((i) => i.kind === "file").map((i) => i.getAsFile()).filter(Boolean);
            if (files.length) {
                e.preventDefault();
                accept(files);
            }
        });

        let depth = 0;
        on(dropZone, "dragenter", (e) => { e.preventDefault(); if (depth++ === 0) call("DragState", true); });
        on(dropZone, "dragover", (e) => { e.preventDefault(); e.dataTransfer.dropEffect = "copy"; });
        on(dropZone, "dragleave", () => { if (--depth <= 0) { depth = 0; call("DragState", false); } });
        on(dropZone, "drop", (e) => {
            e.preventDefault();
            depth = 0;
            call("DragState", false);
            accept(Array.from(e.dataTransfer.files || []));
        });
        // Evita que soltar fora da área abra o arquivo no navegador.
        on(window, "dragover", (e) => e.preventDefault());
        on(window, "drop", (e) => e.preventDefault());

        on(fileInput, "change", () => {
            accept(Array.from(fileInput.files || []));
            fileInput.value = "";
        });
    }

    function dispose() {
        state.handlers.splice(0).forEach((off) => off());
        state.dotnet = null;
    }

    function pick(input) { (input || state.fileInput)?.click(); }

    async function copy(text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            const ta = document.createElement("textarea");
            ta.value = text;
            ta.style.position = "fixed"; ta.style.opacity = "0";
            document.body.appendChild(ta);
            ta.select();
            const ok = document.execCommand("copy");
            ta.remove();
            return ok;
        }
    }

    if (window.pdfjsLib) {
        pdfjsLib.GlobalWorkerOptions.workerSrc = "https://cdn.jsdelivr.net/npm/pdfjs-dist@3.11.174/build/pdf.worker.min.js";
    }

    window.myq = { init, dispose, pick, copy, getTheme, setTheme };
})();
