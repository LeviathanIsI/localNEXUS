Project instructions for LocalNEXUS. Read this before any task.

## What LocalNEXUS is

A Windows 11 desktop app: a node-graph studio for orchestrating multiple local and cloud LLMs. The user wires model nodes on a Blueprints-style canvas, types a request into a chat-style input, and watches the models plan, code, and act in a live streaming activity feed.

It is a general orchestration platform, not a Unity tool. Unity C# game development is the first proving ground, not the purpose. Nothing in the core should be Unity-flavored: nodes, models, transforms, and outputs stay domain-neutral, and Unity is one output target among future many.

Two feature areas share one spine:

1. **Graph orchestration** (built): the canvas, execution engine, model nodes, file output.
2. **Distributed inference** (next): splitting one model across multiple machines, with a source registry, coverage tracking, and a peer/health panel.

Both use the same node engine, model layer, and activity feed. Work alternates between them, so neither may be allowed to entangle the other.

## The end goal, and why it shapes everything

The long-term target is a network where people pool compute to collectively run large open-source models none of them could run alone. Legal open weights (Apache/MIT), so peers can be persistent and accountable rather than anonymous, which is what makes real reputation and reliability possible.

**Standing instruction: build with the end goal in mind so architecture never has to be rewritten later.** For anything new, ask whether the assumption survives N untrusted peers over the internet. If it does not, design the seam now even when the current task does not use it.

The counterbalance, equally important: build seams and interfaces for the vision, implementations only for what is actually in front of us. An abstraction that happens to hold two entries today is correct. A full protocol with nothing to talk to is waste. Do not implement speculative features.

## Stack

- Windows 11 desktop, WPF, .NET 8, C#
- Node canvas: Nodify (MVVM). Wiki: https://miroiu.github.io/nodify
- MVVM: CommunityToolkit.Mvvm
- Local inference: bundled llama.cpp (`llama-server`, and `rpc-server` for distributed), spawned as silent child processes, called over the OpenAI-compatible HTTP API
- Cloud inference: OpenRouter, same OpenAI-compatible API
- Serialization: System.Text.Json

## Coding standards

- Complete, production-ready code. No placeholders, no truncation, no `// TODO: implement later`.
- Single responsibility. One class per node type, per service. Small focused files.
- Strict MVVM. All logic in ViewModels and services, never in code-behind. Code-behind is `InitializeComponent()` only. Views are XAML. Anything platform-specific (file dialogs, etc.) goes behind a service abstraction.
- Explicit state machines over scattered booleans. Run and node lifecycle are enums that drive both flow and UI.
- Build only what the current task asks for. Do not build ahead.
- Fix all compiler errors and warnings before finishing. Zero errors, zero warnings.
- No em dashes or en dashes anywhere: code, comments, docs, or UI strings.
- Follow existing patterns in the repo for naming, async conventions, and how services get their dependencies. Consistency over personal preference.

## Build and distribution

**Every task ends with a fresh, runnable exe in `dist/`, ready to hand to someone else.**

- Publish self-contained single-file to `dist/`, overwriting what is there. No separate .NET install required by the user.
- `dist/` is gitignored (build artifact).
- Bundled llama.cpp binaries land in `dist/` alongside the exe with their expected relative paths intact.
- Binary path resolution must work identically whether running from the IDE or from the published single-file exe. This is a known single-file publishing gotcha; handle it explicitly.
- "It compiles" is not done. "The exe in `dist/` runs and works" is done.
- No reliance on opening the solution in an IDE for normal use. Debug runs only when chasing a specific bug.

## Architecture decisions already made

These are settled. Do not relitigate them without being asked.

**Graph model.** Freeform canvas, wire any node to any node. The execution engine is a general graph walker (topological sort, gather inputs from incoming wires) and knows nothing about specific node types. Adding a node type must never require changing the executor.

**Typed pins.** Pins are color-coded by type and only compatible pins connect. Compatibility rules live in one explicit table (`PinTypeCompatibility`), never scattered as special cases. Current rule: types must match, with one exception, `Code` may flow into `Text` but not the reverse.

**Run model.** The graph is a reusable template. The request is separate per-run input typed into a chat-style box. Auto-run by default, with breakpoints droppable on wires to pause, inspect, and edit a payload mid-run.

**Model nodes are provider-agnostic.** One node type for all roles. A model node is a base URL, an optional API key, a model id, and its settings. Local and cloud differ only by URL and auth header, so there is one request path. Where inference physically happens is an implementation detail the graph does not care about.

**Sources are fungible.** For distributed work, the core concept is sections and who can fill them, not "peers" or "machines." A section is a slot with a spec (model, layer range, quantization). Anything matching that spec can fill it: the local GPU, a LAN machine, a stranger over the internet. Locality is a routing attribute, not a category. Never write code that assumes "the remote machine"; always "the sources covering section N."

**Roles are interchangeable.** Any install can orchestrate or serve. There is no dedicated worker machine.

**Distribution is a capability unlock, not a speedup.** Every layer boundary crossed is a network hop. Split only when a model does not fit locally. Provide a manual override to force a split (needed for testing) and expose split proportions for tuning, but automatic-by-memory is the default, because strangers cannot hand-assign layers.

**Automatic but visible.** Wherever the system decides something for the user (source assembly, split proportions, routing), show what it chose and allow an override. Same philosophy as breakpoints on wires: hands-off until you need to reach in.

**Failure assumptions.** Sources drop without warning. Structure request paths so a section can be re-attempted against a different source. Cache activations per section where the underlying engine exposes the seam (the Petals technique). Where llama.cpp RPC does not expose that seam, build the interface anyway and leave it unwired rather than designing around its absence.

**Identity and trust seams.** Sources have a stable id that persists across sessions, ready for reputation later. Sources carry a trust attribute that today always resolves to trusted for the user's own machines. The code asks the question now even though the answer is always yes.

**Coverage is a real computed concept.** Sections, who covers each, redundancy count, weakest link. A run is gated on complete coverage: a gap in any section means no valid pipeline. Compute and expose this properly even when there are only two machines and it always passes.

**Deliberately deferred.** Do not design these until asked, they need real-world shape first: the peer discovery mechanism (how strangers find each other), the trust scoring algorithm, and the contribution/barter economics.

## Working style

- One part at a time. Do not list everything that could be done next or get ahead of what is being asked.
- When a spec has an internal conflict or an assumption that does not hold, say so plainly and explain the tradeoff rather than silently picking. Flagging the pin-type conflict in the first build was the correct behavior.
- Verify claims. Do not report something as working that was not exercised. State clearly what was tested and what was not.
- Investigate rather than assume when the behavior of an external tool matters (for example, what llama.cpp RPC actually exposes).
- Do not narrate reasoning at length. Report what was done, what was found, and what needs a decision.
