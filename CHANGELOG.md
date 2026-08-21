# Changelog

What changed, from the point of view of somebody using LocalNEXUS rather than reading its
commits.

No release before this file existed was tagged, and the whole history so far ran across two
days, so the versions below are reconstructed from the project documents that name them.
Dates are the day the work landed. Anything from here on will be tagged properly.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions are not
semantic yet, because nothing has been stable long enough to break.

## Unreleased

### Added

- Apache-2.0 licence, a NOTICE naming every dependency and its licence, and the contributing,
  conduct and security documents a public project needs.
- Issue and pull request templates, and a build that runs on every pull request and fails on
  any warning.

### Changed

- The README is now written for somebody who has never seen the project. The reference
  material it used to hold moved into `docs/` and is all still there.

### Known issues

- **The Compiler check node faults the run instead of reporting problems.** It updates the
  Problems panel from the run's background thread without marshalling to the interface
  thread, which WPF refuses. Leave the node out of your graph until this is fixed.
- Shutting the application down can log an unhandled exception from its own cleanup, for the
  same class of reason. It happens after everything has been saved, so nothing is lost.

## v1.4, 2026-08-20

### Added

- **A model can be added as a single file**, not only as a folder to scan. Point at a `.gguf`
  directly and it is used from wherever it lives.

### Changed

- **Nodes are named after what they hold and do.** Input became **Prompt**, Plan became
  **Triage**, Transform became **Patch**, and Compile check became **Compiler check**. Model
  and Output kept their names. Every graph saved by an earlier version still loads: the old
  names are read and the new ones written back.
- Folder scanning no longer gives up four levels down, so a deeply nested model library is
  found. A scan that hits its budget says so instead of quietly returning less.
- A file that is not a valid model now says what it is rather than failing silently.

## v1.3, 2026-08-20

### Added

- **You choose what this machine offers.** A switch for the machine and a tick per model.
  Nothing is shared until you say so.
- **A memory limit grounded in the actual card.** A slider that defaults to leaving a quarter
  of your graphics memory free, never less than 1.5 GB, and tells you in words what it worked
  out and why.
- **An invite token you can copy**, and a way to replace this machine's identity.

### Changed

- Menu commands now apply to the view you are looking at, instead of being available and
  doing nothing.
- Interface text rewritten throughout to say what things do rather than how they were built.
- The themes were renamed. Your saved choice carries over.

## v1.2, 2026-08-20

### Added

- **The window is now an IDE.** Menu bar, activity bar, a side bar that doubles as the live
  run outline, tabbed editor area, a bottom panel with Problems, Activity and Output, and a
  status bar.
- **Five themes, switched without restarting**, and remembered. Editor dark, deep slate, warm
  charcoal, near black, and a light theme whose colours were chosen for a light background
  rather than inverted from a dark one.
- **A settings panel**, and per node settings in one reused inspector.
- JetBrains Mono is bundled, so paths, identifiers and diagnostics line up.

### Changed

- Nodes are neutral, with colour only on the accent bar and in four honest states. A fault
  stays on the node that faulted: nodes before it keep their green, and a node that was never
  reached says skipped rather than wearing somebody else's failure.
- The Network tab reads as a table of what the network can serve.

### Fixed

- Ten core files had never been committed, hidden since the first commit by an ignore rule
  that matched `Models/` on a case insensitive filesystem. A fresh clone now builds.

## v1.1, 2026-08-19

### Added

- **The project is read before anything is written.** Every C# file is parsed and indexed, and
  the model is shown one line per type, signatures for the files that matter, and full
  contents for nothing that did not earn it. The budget is a setting and is written to the
  feed.
- **Nothing is created that already exists.** A plan naming a type the project already has is
  refused, and the refusal names the existing type and its file.
- **Code is compiled before it is written**, and failures go back to the model for another
  attempt, up to a limit you set.
- **A plan writes many files or none.** Files are generated in dependency order, each shown
  what the earlier ones actually declared, and the whole set is written together or not at
  all.
- **Changes that would silently break Unity are refused**, not warned about: a MonoBehaviour
  whose file name stops matching its class, a type that moves namespace or stops being a
  MonoBehaviour without a shim, a serialized field renamed without one. Each of those
  compiles cleanly and breaks a scene.
- **Safetensors models are served too**, through an isolated Python environment built on
  first launch in the background. Format is detected by reading the file, so nobody is asked
  which engine to use.

### Changed

- Engine processes are owned through Windows job objects, so the operating system terminates
  them even when the application does not get the chance. Processes left behind by a previous
  session are terminated at startup rather than reused.

## v0.5, 2026-08-19

### Changed

- **The distributed engine is now Mesh LLM**, replacing llama.cpp RPC. Two things made the
  old one unfit, both found by testing rather than by reading documentation: killing a worker
  left the coordinator reporting healthy and then crashing outright on the next request, and
  a machine with spare memory could contribute to exactly one consumer at a time. Mesh LLM
  survives a peer being killed mid stream and serves many consumers at once.

## v0.2, 2026-08-19

### Added

- **A model can be split across machines**, with a source registry, health monitoring, and
  coverage tracking that gates a run on every section being covered.
- A Network tab that browses what the network can serve, and a panel for contribution.

## v0.1, 2026-08-19

### Added

- The first working version: a node canvas, typed pins that refuse a wrong connection while
  you drag it, a general graph executor that orders nodes by their wiring and rejects cycles,
  model nodes that talk to local llama.cpp or to OpenRouter through one request path, and an
  Output node that writes into a Unity project.
