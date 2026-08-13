# System Optimizer

Windows desktop cleanup, RAM and monitoring utility. **Free**, MIT licensed, and
makes **no network calls at all**.

Version **2.0**, targeting `net10.0-windows`, shipped as a single self-contained
`SystemOptimizer.exe` that runs on a machine with no .NET installed.

## Installing

Download the setup program from
[Releases](https://github.com/MikieLedingham/SystemOptimizer/releases/latest) and
run it. It installs to `Program Files\System Optimizer`, and everything the
program writes afterwards stays in `%APPDATA%\System Optimizer`.

There is no automatic update check, because there is no network call to make one
with. **Check for updates** in the About window opens the releases page in your
browser; that is the whole of it.

## Nothing is destroyed

**System Optimizer finds and collates candidate files. It does not decide that
they should stop existing — you do.**

Everything it removes goes to the **Recycle Bin**. That is the whole design, not
a politeness:

- Files sit in the bin for as long as you like. Nothing is expunged on a
  schedule, and System Optimizer never empties the bin on its own.
- **The disk space is not reclaimed until you empty the bin.** That is
  deliberate. If your aim is free space, the final step is yours to take, after
  you have looked at what is in there. Check it regularly, and conservatively.
- **There are two independent ways back.** `Restore a previous clean` in the
  application menu lists the last ten runs and puts a whole run back; and
  Windows' own Recycle Bin restore works on anything, one item at a time,
  whether or not System Optimizer is installed or even still on the machine.
- Anything written in the last 24 hours is left alone, because a temp folder is
  working storage for a running program, not rubbish.
- Junctions and symbolic links are never followed, so a link inside a cleaned
  folder cannot lead the cleanup out of it.
- Anything currently in use is skipped and reported as skipped, not as an error.

The cleanup engine builds a full plan first, performs no deletion while it is
deciding, and then records exactly what moved so the run can be undone.

### The two exceptions, stated plainly

A blanket claim would be untrue, and a promise you can check is worth more than
one you cannot. Exactly two operations permanently remove data, both admin-only
and both labelled where you choose them:

| Operation | Why it cannot be undone |
|---|---|
| **Empty Recycle Bin** | It *is* the permanent step. Labelled "permanent — cannot be undone" on the checkbox. |
| **Old Windows installations** (`Windows.old`) | Routinely 10–30 GB, far past the Recycle Bin's quota — and Windows silently permanently deletes anything over that quota, so "recycling" it would destroy the data anyway while claiming to be reversible. It asks a second time, showing the real size, with **Cancel as the default answer**. |

Everything else on every page goes to the bin.

## What it does

**Cleanup** — temporary files, browser caches, downloads untouched for 30 days,
recent items, DNS cache. With administrator rights: Windows
temp, crash dumps, old Windows installations, and emptying the Recycle Bin.

Browser cache covers Chrome, Edge, Brave, Vivaldi, Chromium, Opera and Firefox,
and enumerates every profile rather than assuming `Default`. It clears cache
only — never cookies, logins, history, bookmarks or extensions, which live in
the same profile folder.

**RAM** — a one-shot working-set trim, an automatic boost above a threshold you
set, and a **no-boost list** of programs that pause automatic boosting while
they are running.

**Overlay** — a floating, customisable display of CPU, RAM, disk, network,
battery, uptime and running apps.

**Sanity Check** — eight checks for things that are working and still wrong:
hardware running far below what it can do, or set up in a way nothing else
reports. Every check reads local system state only, and you choose which ones
run.

**Diagnostics** — a full system report with self-tests, with the username
redacted and the machine name omitted.

### Deliberately not included

Clearing event logs (it destroys your machine's own troubleshooting history),
temp profile deletion (too easy to match a real account), Windows Update cache
(can corrupt an update in flight), prefetch (no benefit — it makes the next
launch of everything slower), deleting restore points, and the thumbnail cache.

The thumbnail cache is worth explaining, because most cleaners offer it: Explorer
holds those files open for as long as it is running, so they cannot be removed
without shutting Explorer down or scheduling deletion for the next reboot. Both
are far more disruptive than the few megabytes justify, and Windows rebuilds the
cache on demand anyway. Offering the option and quietly failing would be worse
than not offering it.

## Settings and options

There is no Settings button, deliberately. **Right-click anywhere on the main
window** for the full menu; the same menu is on the tray icon. `Appearance`
offers Dark (the default), Light, and Follow Windows, applied immediately
including the title bar.

All application data lives in **`%APPDATA%\System Optimizer\`** — preferences,
the no-boost list, the theme, cleanup history and `logs\`.

## Building

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and
nothing else. `dotnet publish` on its own is enough — it builds first, and writes
the same single self-contained `SystemOptimizer.exe` that the installer ships:

```
dotnet publish src\SystemOptimizer.csproj -p:PublishProfile=win-x64
```

To build without publishing:

```
dotnet build src\SystemOptimizer.csproj -c Release
```

No `-p:` overrides of any kind. Publish output is one file in
`src\bin\publish\win-x64\`. Two NuGet packages, restored normally.

The installer needs [Inno Setup 6](https://jrsoftware.org/isinfo.php) and is
built with one command, which publishes first:

```
powershell -File installer\build-installer.ps1
```

It writes `installer\output\System Optimizer 2.0.0 Setup.exe`. The wizard's
README and licence pages are generated from `README.md` and `LICENSE` at build
time rather than kept as copies, so they cannot describe a different product than
this file does.

**Released builds are not code-signed**, so SmartScreen warns on first download.
Signing usefully would need a CA-issued certificate; a self-signed one changes
nothing for anybody but the machine that created it. The `dotnet publish` command
above is the way around the warning — it produces the executable directly, with
no installer involved.

## Layout

| Path | Contents |
|---|---|
| `src/` | The application source |
| `installer/` | The Inno Setup script and its build script — run `build-installer.ps1` |

## Licence

**MIT** — see [`LICENSE`](LICENSE). Third-party components are in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

[`PRIVACY.md`](PRIVACY.md) is very short, because System Optimizer makes no
network calls.
