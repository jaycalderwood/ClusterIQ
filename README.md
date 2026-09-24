---

## 📥 Blog Posting

👉 <https://www.cloudythoughts.cloud/2026/04/20/introducing-cluster-iq/>

---

## 📥 Download
 
👉 <https://github.com/jaycalderwood/ClusterIQ/releases/latest>

---

## 🆕 What's New in v2.0

ClusterIQ 2.0 is the biggest release yet — the jump from *visibility tool* to *automation platform*.

* **🤖 Autopilot maintenance playbooks** — chain steps like snapshot cleanup, patching readiness, live-migrate-to-balance, orphaned VHD detection, and Integration Services verification. Schedule them or run on demand. Every run gets a full per-step audit trail and an auto-opened Markdown summary.
* **🛠️ Headless Windows service** — `ClusterIQ.Headless.exe` runs scheduled playbooks as a Windows service, even when the GUI is closed. The GUI detects the service and yields scheduling automatically.
* **📊 Operational reports** — ten built-in report templates (Cluster Health Overview, VM Inventory, Host Capacity, Right-sizing Candidates, Storage/CSV Health, Snapshot Hygiene, Patching Compliance, Drift History, Network Audit, Executive Roll-up). One-click generate → saves Markdown + HTML → opens in your browser. Clone, customize, and schedule any of them.
* **🔍 Troubleshooting tool** — scenario-based diagnostics for stuck live migrations, CSV redirected I/O, VMs stuck in Saved state, Replica out of sync, unregistered cluster roles, and inter-node network issues. Read-only diagnostics with viewable fix scripts.
* **✨ AI assistant (optional)** — configure Claude, OpenAI, Azure OpenAI, or local Ollama and get Explain buttons on drift events, recommendations, insights, troubleshooting findings, and playbook runs. Plus an Ask AI toolbar button. Off by default.
* **📋 Recommendations queue** — scriptable, risk-rated recommendations with full script preview and an engine diagnostic panel.
* **💡 Workload Insights** — right-sizing candidates, noisy neighbors, and utilization patterns surfaced from performance data.
* **🔔 Smart notifications** — configurable alert rules.
* **📈 Drift tracking** — severity-classified "what changed?" surface with full history.
* **🌙 Dark mode everywhere** — every grid, every tab.

---

## 📸 Screenshots

### HV Cluster View

[![HV Cluster](https://github.com/jaycalderwood/ClusterIQ/raw/main/docs/images/hv-cluster.png)](/jaycalderwood/ClusterIQ/blob/main/docs/images/hv-cluster.png)

### VM View

[![VM View](https://github.com/jaycalderwood/ClusterIQ/raw/main/docs/images/vm-view.png)](/jaycalderwood/ClusterIQ/blob/main/docs/images/vm-view.png)

### HV Perf

[![HV Perf](https://github.com/jaycalderwood/ClusterIQ/raw/main/docs/images/hv-perf.png)](/jaycalderwood/ClusterIQ/blob/main/docs/images/hv-perf.png)

### AZS2D

[![AZS2D](https://github.com/jaycalderwood/ClusterIQ/raw/main/docs/images/azs2d.png)](/jaycalderwood/ClusterIQ/blob/main/docs/images/azs2d.png)

### Autopilot Playbooks (v2.0)

[![Autopilot](https://github.com/jaycalderwood/ClusterIQ/raw/main/docs/images/v2-autopilot.png)](/jaycalderwood/ClusterIQ/blob/main/docs/images/v2-autopilot.png)

### Reports (v2.0)

[![Reports](https://github.com/jaycalderwood/ClusterIQ/raw/main/docs/images/v2-reports.png)](/jaycalderwood/ClusterIQ/blob/main/docs/images/v2-reports.png)

### Troubleshooting (v2.0)

[![Troubleshoot](https://github.com/jaycalderwood/ClusterIQ/raw/main/docs/images/v2-troubleshoot.png)](/jaycalderwood/ClusterIQ/blob/main/docs/images/v2-troubleshoot.png)

---

## ✨ Key Features

* Cluster-aware VM management
* Live migration with Kerberos / CredSSP
* Real-time performance monitoring
* Azure Local / S2D insights
* **Maintenance playbooks with scheduling + headless service** *(v2.0)*
* **Operational report templates with one-click generate + schedule** *(v2.0)*
* **Scenario-based troubleshooting with previewable fixes** *(v2.0)*
* **Optional AI assistant — Claude / OpenAI / Azure OpenAI / Ollama** *(v2.0)*
* **Recommendations queue, workload insights, drift tracking, smart notifications** *(v2.0)*
* Export to CSV / Excel
* Built-in documentation
* GitHub-powered auto update
* Dark mode

---

## 🧭 Application Walkthrough

### HV Cluster

* Node status and health
* Cluster validation

### VM View

* VM ownership and state
* Start / Stop / Restart
* Live migration (cluster-aware)

### HV Perf

* CPU and memory metrics
* Graph-based visualization

### AZS2D

* Storage pools and disks
* Capacity insights
* Version-aware queries

### hvAutopilot *(v2.0)*

* Build playbooks from a step library
* Schedule (weekly/daily) or Run Now
* Preview Mode — see the PowerShell before anything executes
* Per-step audit trail + auto-opened run summaries

### hvReports *(v2.0)*

* Ten built-in operational templates, ready on first launch
* Generate now → saves .md + .html → opens in browser
* Manage templates & schedules from the tab
* Scheduled generation on a cadence

### hvTroubleshoot *(v2.0)*

* Six diagnostic scenarios for common failure patterns
* Read-only diagnostics, symptom explanations
* Fix scripts with full preview

### hvRecommendations *(v2.0)*

* Risk-rated actionable recommendations
* Script preview before apply
* Engine diagnostics — an empty queue tells you why

### hvInsights *(v2.0)*

* Right-sizing candidates
* Noisy neighbor detection
* Utilization pattern analysis

---

## ⚙️ Settings

* Authentication mode (Kerberos / CredSSP)
* Performance refresh interval
* AI Assistant provider (Claude / OpenAI / Azure OpenAI / Ollama) *(v2.0)*
* Notification rules *(v2.0)*
* Report automation *(v2.0)*
* Persisted configuration

---

## 🛠️ Headless Service *(v2.0)*

Run scheduled playbooks without the GUI open:

```
ClusterIQ.Headless.exe --install   # from an elevated prompt
```

The GUI detects the service lease and yields scheduling to it. Uninstall with `--uninstall`. See [docs/HEADLESS.md](docs/HEADLESS.md) for all five CLI modes, connection and service-account setup, GUI coordination, and known limitations.

---

## 🔄 Update Mechanism

1. Query GitHub API
2. Compare versions
3. Download latest EXE
4. Replace binary
5. Restart app

---

## 🧰 Requirements

* Windows
* No .NET install required — release builds are self-contained single-file win-x64 executables (the .NET 8 Desktop Runtime is bundled)
* Hyper-V / Azure Local environment
* Admin privileges
* Network access to hosts
* *(Optional)* An LLM API key or local Ollama for AI features

---

## ⚠️ Known Behavior

* Some views require manual refresh
* Performance depends on available metrics
* S2D output varies by environment
* AI features require a configured provider (off by default)
* Report inventory sections are empty until first cluster refresh
