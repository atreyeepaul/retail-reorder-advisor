# Demo script

A timed runbook for the live demo, mapped to the 5-slide deck and the running app. Target ~5 minutes live
(a 3-minute recording cut is at the end).

**Live URL:** `https://reorder-vmtiy7-web.azurewebsites.net/`

## Before you present (5-min prep)

- [ ] **Warm the Azure app** — open the live URL once, ~2 minutes before. F1 cold-starts in 30–60s; don't let that
  happen on stage.
- [ ] **Start Docker Desktop**, then `docker compose up -d` (for the containerization proof).
- [ ] **Open these tabs, in order:** ① the deck · ② action list (`/`) · ③ health summary (`/health-summary`) ·
  ④ the Teams **Reorder Test** channel · ⑤ the GitHub repo · ⑥ GitHub → **Actions** · ⑦ Azure Portal → resource
  group → **Cost Analysis** · ⑧ a terminal in the repo root.
- [ ] **Set the keys** in the terminal so the live commands just work:
  ```powershell
  $env:REORDER_ADMIN_KEY = "<admin key>"; $env:REORDER_JOB_KEY = "<job key>"
  ```
- [ ] **Create the "bad file"** for the failure demo (once):
  ```powershell
  "date,sku,qty_sold,unit_price`n2026-06-01,1001,5,10`n2026-06-01,999999,5,10`n2026-06-01,999998,5,10`n2026-06-01,999997,5,10`n2026-06-01,999996,5,10" | Set-Content demo-bad-sales.csv
  ```

## The run of show (~5 min)

### 1. Title — hook (0:00–0:30)
- **Say:** "A retail buyer with ~200 products decides every day what to reorder. This tells them *what, how much,
  and why* — a nightly cloud pipeline on Azure, running at ₹0."
- **Show:** the title slide.

### 2. Problem → Solution (0:30–1:10)
- **Say:** "Today it's guesswork — overstock ties up cash, stockouts lose sales. My fix: one nightly job scores
  every product, ranks it, and gives the exact quantity with a plain reason."
- **Show:** slide 2 (the two cards).

### 3. Architecture + Docker proof (1:10–2:00)
- **Say:** "Upload the day's files → validate → the advisor runs → results land on a dashboard and a Teams alert.
  All managed Azure services, and the app is containerized."
- **Do:** in the terminal —
  ```bash
  docker compose ps
  ```
  **Say:** "Same app, running in a Docker container locally." (Optionally open `http://localhost:8080`.)

### 4. Live app — the core (2:00–3:15)  ·  *do this on the Azure URL*
- **Show — action list (tab ②):** click the **URGENT** chip → click **Days of cover** to sort → point at a
  **reason** line → click **Export CSV**.
  - **Say:** "A real run — 200 products ranked; the urgent ones are what to order now, each with the math behind it."
- **Show — health summary (tab ③):** point at the tiles (at-risk, overstock ₹, dead-stock ₹, lost sales avoided)
  and the flag bar.
  - **Say:** "The portfolio view — where cash is stuck, and what a good order-day recovers."

### 5. Reliability + failure-and-recovery (3:15–4:15)  ·  *the money moment*
- **Say:** "It's built to survive bad input." **Do** — upload the bad file to Azure:
  ```powershell
  $b="https://reorder-vmtiy7-web.azurewebsites.net"
  $f=@{sales=Get-Item demo-bad-sales.csv; inventory=Get-Item data/2026-09-05/inventory.csv; suppliers=Get-Item data/2026-09-05/suppliers.csv}
  Invoke-RestMethod -Method Post -Uri "$b/api/uploads?asOf=2026-09-09" -Headers @{'X-Admin-Key'=$env:REORDER_ADMIN_KEY} -Form $f
  ```
  - **Show:** the response `status: aborted` (80% rejected) → switch to **Teams (tab ④)** → the
    **⚠ Ingestion aborted** alert card.
  - **Say:** "Too many bad rows, so it refused to publish, kept the last good result, and alerted the team instead."
- **Say (recovery / TTR):** "If the whole environment is lost, it rebuilds from an empty resource group to the same
  URL in about 10 minutes — drill-verified." (Point at slide 4's *~10 min* tile; do **not** do a live teardown.)

### 6. Results + close (4:15–5:00)
- **Show — Actions (tab ⑥):** the green **CI** and **Nightly** runs — "tests + the forecast backtest on every push;
  the advisor fires nightly on a schedule."
- **Show — Cost Analysis (tab ⑦):** "₹0 — free tier throughout."
- **Say:** "Seven clean simulated days, 80%+ of stockouts caught 3 days early, ₹0 cost. Next: multiple stores and
  richer forecasting. Happy to take questions."

## Backup plans
- **App slow/cold:** you pre-warmed it; if it still lags, run the dashboard demo on **Docker**
  (`http://localhost:8080`), but do the **Teams alert** part on Azure (that's where the webhook lives).
- **No internet / Azure down:** run everything on Docker + `./scripts/simulate-7-days.ps1 -BaseUrl
  http://localhost:8080 -AdminKey dev -JobKey dev` to populate it; show the Teams card from a screenshot.
- **Teams card is slow:** it's async (a few seconds) — keep talking; keep a screenshot as fallback.

## 3-minute recording cut
Title (10s) → Problem/Solution (20s) → live dashboards (60s) → failure → Teams alert (60s) → results + cost (30s).
Drop the `docker compose ps` beat and the CI tab.
