// In-browser client for the key-protected upload + run-advisor endpoints. Requires JavaScript
// (a plain form can't send the X-Admin-Key / X-Job-Key headers). Keys are never stored.
(function () {
    const $ = (id) => document.getElementById(id);
    const uploadBtn = $("uploadBtn");
    if (!uploadBtn) {
        return;
    }
    const result = $("result"), runCard = $("runCard"), runBtn = $("runBtn"), runResult = $("runResult");

    function show(el, html, cls) {
        el.hidden = false;
        el.className = "result" + (cls ? " " + cls : "");
        el.innerHTML = html;
    }
    function esc(s) {
        return String(s).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
    }
    function fileRow(label, c) {
        return `<tr><td>${label}</td><td>${c.accepted}</td><td>${c.rejected}</td><td>${c.total}</td></tr>`;
    }
    async function busy(btn, fn) {
        const label = btn.textContent;
        btn.disabled = true;
        btn.textContent = "Working…";
        try {
            await fn();
        } finally {
            btn.disabled = false;
            btn.textContent = label;
        }
    }

    uploadBtn.addEventListener("click", () => busy(uploadBtn, async () => {
        const asOf = $("asOf").value.trim();
        const key = $("adminKey").value;
        const files = { sales: $("sales").files[0], inventory: $("inventory").files[0], suppliers: $("suppliers").files[0] };
        if (!asOf) { return show(result, "Pick an as-of date.", "result--warn"); }
        if (!key) { return show(result, "Enter the admin key.", "result--warn"); }
        if (!files.sales || !files.inventory || !files.suppliers) { return show(result, "Choose all three CSV files.", "result--warn"); }

        const form = new FormData();
        form.append("sales", files.sales);
        form.append("inventory", files.inventory);
        form.append("suppliers", files.suppliers);

        try {
            const res = await fetch(`/api/uploads?asOf=${encodeURIComponent(asOf)}`, {
                method: "POST", headers: { "X-Admin-Key": key }, body: form,
            });
            if (res.status === 401) { return show(result, "Unauthorized — check the admin key.", "result--warn"); }
            const body = await res.json().catch(() => null);
            if (!res.ok) { return show(result, "Upload failed: " + esc(body && body.error ? body.error : res.status), "result--warn"); }

            const ok = body.status === "ok";
            const pill = `<span class="pill ${ok ? "pill--ok" : "pill--warn"}">${ok ? "OK" : "ABORTED"}</span>`;
            let html = `<p>${pill} <strong>${esc(body.asOf)}</strong> · reject threshold ${Math.round(body.rejectThreshold * 100)}%</p>`;
            html += `<table class="mini"><thead><tr><th>File</th><th>Accepted</th><th>Rejected</th><th>Total</th></tr></thead><tbody>`;
            html += fileRow("Sales", body.sales) + fileRow("Inventory", body.inventory) + fileRow("Suppliers", body.suppliers);
            html += `</tbody></table>`;
            if (body.rejectsSample && body.rejectsSample.length) {
                html += `<p class="muted">First rejects:</p><ul class="rejects">`;
                body.rejectsSample.slice(0, 5).forEach((r) => { html += `<li><code>${esc(r.file)}:${r.line}</code> ${esc(r.reason)}</li>`; });
                html += `</ul>`;
            }
            if (!ok) { html += `<p class="muted">Too many rejected rows — the previous results were kept; no run needed.</p>`; }
            show(result, html, ok ? "result--ok" : "result--warn");
            if (ok) { runCard.hidden = false; }
        } catch (e) {
            show(result, "Network error: " + esc(e.message), "result--warn");
        }
    }));

    runBtn && runBtn.addEventListener("click", () => busy(runBtn, async () => {
        const asOf = $("asOf").value.trim();
        const key = $("jobKey").value;
        if (!key) { return show(runResult, "Enter the job key.", "result--warn"); }
        try {
            const res = await fetch(`/api/jobs/run-advisor?asOf=${encodeURIComponent(asOf)}`, {
                method: "POST", headers: { "X-Job-Key": key },
            });
            if (res.status === 401) { return show(runResult, "Unauthorized — check the job key.", "result--warn"); }
            if (res.status === 409) { return show(runResult, "A run is already in progress — try again shortly.", "result--warn"); }
            if (res.status === 404) { return show(runResult, "No data for this date — upload it first.", "result--warn"); }
            const body = await res.json().catch(() => null);
            if (!res.ok) { return show(runResult, "Run failed: " + esc(res.status), "result--warn"); }

            const c = body.countsByFlag || {};
            let html = `<p><span class="pill pill--ok">DONE</span> ${body.skuCount} SKUs · `;
            html += `URGENT ${c.URGENT || 0}, STOCKOUT_RISK ${c.STOCKOUT_RISK || 0}, DEAD_STOCK ${c.DEAD_STOCK || 0}, OVERSTOCK ${c.OVERSTOCK || 0}, HEALTHY ${c.HEALTHY || 0}</p>`;
            html += `<p><a href="/">View the action list →</a></p>`;
            show(runResult, html, "result--ok");
        } catch (e) {
            show(runResult, "Network error: " + esc(e.message), "result--warn");
        }
    }));
})();
