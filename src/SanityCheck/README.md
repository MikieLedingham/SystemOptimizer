# Sanity Check

Checks for things that are working and still wrong — hardware running far below
what it can do, or set up in a way nothing else reports.

```
Core          the cleanup engine, RAM boost, monitoring, settings, platform, logging
SanityCheck   the relationship checks          <- here
Tools         optional modules; No-boost is the one resident
```

## The rules a check has to follow

- Checks assert **relationships** — two independently observed facts that ought to
  agree — not states. "A gigabit NIC negotiated 100 Mb" is a finding; "link speed is
  100 Mb" is not.
- Every check carries a `CheckDoc`, and **`WhenToIgnore` is required and must be
  non-empty or the build fails**. Every check has users for whom the finding is a
  deliberate choice. An author who cannot name those users has not finished thinking.
- A check that cannot observe one side of its assertion returns **`Inconclusive` with a
  reason** — never `Pass`, never a finding. That, plus self-quarantine after three
  Inconclusives and a permanent mute after two dismissals, is what keeps it from
  becoming the thing everyone ignores.
- The user guide is **generated from the check registry at build time**, so the docs
  cannot drift from the code.
- Prefer a few high-confidence checks over many mediocre ones. Most checks are
  irrelevant to most machines, which is why the user chooses which ones run.

Sanity Check is **not** a Tool. Tools are optional modules that Core must never name;
this is Core-adjacent and shipped to everyone.
