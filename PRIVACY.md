# Privacy Policy

**System Optimizer 2.0 collects nothing, sends nothing, and makes no network
connections.**

This is not a promise about intent — it is a statement about what the code does.
There is no telemetry, no analytics, no crash reporting, no update ping, and no
account. There is no licensing, so there is nothing to check in with.

## What stays on your machine

Everything the program writes lives under one folder: **`%APPDATA%\System
Optimizer`**.

| Data | Where | Why |
|---|---|---|
| Preferences | `preferences.json` | Your cleanup choices and overlay settings |
| Sanity Check state | `sanity-state.json` | Which checks you switched off, and which findings you dismissed |
| Logs | `logs\` | So you can see what a cleanup actually did |
| Cleanup history | `history\` | One manifest per run — this is what "Restore a previous clean" reads |
| Sanity Check guide | `guide\` | Written out when you ask to read it, so it opens without an internet connection |

These are plain files on your own disk. None is transmitted anywhere. Delete them
at any time; the app will recreate defaults — but deleting `history\` also
discards your ability to undo previous cleanups.

Cleanup necessarily inspects files on your system — temporary files, browser
caches, event logs and so on, according to the options you select — and moves the
ones you chose into the Recycle Bin. That inspection happens locally and the
results are written only to the local log.

## The one check that would need the network, and does not exist

**Sanity Check ships with eight checks and every one of them reads local system
state only.** No check makes a network request.

One further check has been designed and deliberately not built: it would compare
where your internet traffic appears to exit against your system's locale, to
catch a VPN silently routing you through the wrong country. **That requires an
outbound request**, so if it is ever built it will be off by default and opt-in,
it will say so plainly in the UI at the point you enable it, and this document
will be updated to describe exactly what is sent and to whom.

The same reasoning is why the Sanity Check guide is a local file rather than a
web page: fetching help for a finding like `NET.DNS_MISMATCH` would tell a third
party that this machine's DNS looks hijacked.

## Contact

Questions or corrections: open an issue at
<https://github.com/MikieLedingham/SystemOptimizer/issues>.

---

*If this document and the code ever disagree, the code is the truth and this
document is a bug — please report it.*
