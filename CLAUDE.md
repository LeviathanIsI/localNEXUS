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
- Local inference: bundled llama.cpp (`llama-server`) for GGUF, and `transformers serve` in a bundled-`uv`-built Python environment for safetensors, both spawned as silent child processes and called over the OpenAI-compatible HTTP API
- Distributed inference: bundled Mesh LLM (`mesh-llm`), spawned as a silent child process, called over the same OpenAI-compatible HTTP API
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
- Bundled engine binaries (llama.cpp in `vendor/llama`, Mesh LLM in `vendor/mesh`) land in `dist/` alongside the exe with their expected relative paths intact.
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

**Failure assumptions.** Sources drop without warning. The engine owns recovery: it detects a dead peer, retires it, and replans. Do not add a competing retry or replan layer above it, because anything doing that is racing the only component that actually knows the topology. Surface what the engine reports and refuse clearly when it cannot assemble a model.

**The distributed engine is Mesh LLM, and llama.cpp RPC is history.** The v0.2 slice ran distribution on llama.cpp's `rpc-server`. Two limitations, both found by testing rather than by reading documentation, made it unfit for the end goal:

1. **No in-flight failover.** Killing a worker left the coordinator's `/health` still reporting ok, and the next completion crashed the coordinator process outright. Recovery could only be relaunch-shaped, which does not survive a network where peers drop constantly.
2. **One coordinator per worker.** `rpc-server` serves a single coordinator at a time, so a machine with spare VRAM could contribute to exactly one consumer. The vision requires a peer to participate in many pipelines at once.

Mesh LLM (Apache-2.0) replaced it in v0.5. It pools GPUs behind one OpenAI-compatible API, routes by model, splits models too large for one box into contiguous layer stages, and brings its own discovery and NAT-traversing transport. Verified on this hardware: multi-consumer works, and an in-flight streaming request survives its stage peer being killed rather than taking the coordinator down. Not verified: prompt re-convergence after a peer is replaced, which did not complete within the test window on a single-GPU loopback topology. Treat that as an open question, not a settled property.

**llama.cpp is still the local engine.** A model that fits on one machine is served by `llama-server` exactly as before. The mesh is for what one machine cannot do alone. Never route a purely local run through the mesh to make the code look uniform.

**Local runtimes are chosen by format, behind one interface.** `IModelRuntime` answers three questions: can you serve this, bring it up and say where, and stop. `LlamaServerManager` serves GGUF and `PythonRuntimeManager` serves safetensors; `RuntimeResolver` picks between them from a `ModelDescriptor`. A model node asks for a path and gets an endpoint back, so "local" means "whatever local runtime can serve this" and adding a third runtime is one entry in the resolver. Because every runtime exposes the same OpenAI-compatible API, `IModelClient`, `OpenAiCompatibleClient`, `ModelEndpoint` and `ChatCompletionResult` are untouched by any of it. If a change to one of those looks necessary, the design has gone wrong somewhere else.

**Format is detected by content, never by extension.** `ModelFormatDetector` is the only place that answers what a path holds: GGUF by its magic bytes, a safetensors model by a folder holding `config.json` beside `.safetensors` weights. A lone `.safetensors` file and a folder of weights with no config are their own reported state, not a model to attempt. Anything unrecognised is reported as unrecognised rather than guessed at.

**The Python environment is isolated and owned.** It is built by the bundled `uv` from a committed lockfile into `%LOCALAPPDATA%\LocalNEXUS\runtime\python\`, never the install directory, never a Python already on the machine, and never loaded in-process. Provisioning runs in the background on first launch for every install, because a two gigabyte download that starts in the middle of real work is a worse failure than one that starts while the app is being opened. It is verified by importing the packages, not by an exit code, and the state record is written only after that passes so an interrupted install resumes rather than being trusted.

**Child processes belong to this application, and the operating system enforces it.** Every engine process started here goes into its own Windows job object, so everything it goes on to start joins the same job and cannot leave it, and the kernel kills the whole job when the application's handle to it closes. That covers the paths where no code of ours runs at all, which a tree kill at exit cannot. A process handle is never treated as proof of anything: the mesh executable is a launcher that re-executes itself and then exits, so the process we started is often legitimately gone while the node it left behind is still running and is nobody's child. Termination is asked for, forced, verified against the job's own process list, and retried.

Two rules follow and are not negotiable. The application never terminates an engine process it did not start, which is why a process id alone is never an identity: a record must match on id, start time and binary, and its owning session must be gone. And engine processes a previous session left behind are terminated at startup rather than adopted and reused, because a leftover node was launched with settings this session cannot read back out of it, while restarting one costs nothing that matters since the engine keeps its identity in its own key file.

**The engine owns discovery, placement and liveness.** This application starts the node process, reads what it reports, and renders it. It does not probe peers, address them by host and port, plan layer ranges, or manage the process lifecycle of remote workers. Anything that assumes otherwise is a bug, not a feature.

**Identity and trust seams.** A source's stable id is its mesh peer public key, assigned by the engine and persistent across sessions, which is what reputation attaches to later. Sources carry a trust attribute that today always resolves to trusted, because a private mesh is joined by invitation and so every peer in it was let in deliberately. The code asks the question now even though the answer is always yes.

**Private by default.** The node hosts or joins a private mesh over LAN-scoped discovery, which keeps the engine off public relays entirely. Publishing to the public mesh is a separate, explicit opt-in and is the only setting that causes any contact beyond the local network.

**Coverage is a real computed concept.** Sections, who covers each, redundancy count, weakest link. A run is gated on complete coverage: a gap in any section means no valid pipeline. Compute and expose this properly even when there are only two machines and it always passes.

**Model discovery does not filter by format.** Both formats are one list, format is a label rather than a choice, and nobody is asked which engine they want. Extra search folders are first-class: `model-paths.txt` beside the config, one folder per line, scanned for both formats.

**Generated code is compiled before it is written, and by Roslyn rather than by Unity.** A run that reports success has to mean the code compiles, so the Compile-check node sits between the coder and the file writer and nothing is written until it passes. It compiles with Roslyn against the reference set assembled from the open project: the project's own `Library\ScriptAssemblies`, the editor's `Managed\UnityEngine` modules and `UnityEditor.dll`, and the editor's netstandard 2.1 reference assembly, at C# 9 because that is what Unity accepts. Invoking the Unity editor in batch mode was measured and rejected on two counts, both fatal for a repair loop: it takes seconds rather than milliseconds per attempt, and a second instance refuses to open a project the editor already has open, which is exactly the situation of anyone using this tool while working in Unity. What Roslyn gives up is real and is not hidden: it compiles the one file rather than the project, so it cannot see a sibling generated in the same run, and it does not run the project's source generators or analyzers. When no Unity install or project can be found the check reports that it could not run and passes the code through, because code that cannot be checked is not code that is broken.

**The repair loop belongs to the node, not the executor.** The executor orders nodes and knows nothing about any of them, and that does not change. The Compile-check node follows its own incoming wire, asks whatever it finds there whether it implements `ICodeRepairSource`, and if it does, hands over the failing code and the diagnostics and asks for another attempt, bounded by a retry cap on the node. No node type is named anywhere in the loop. A cycle in the graph is not the mechanism and must not become one: the executor rejects cycles, and a retry count belongs in a setting rather than in how many times somebody drew a loop. `TransformNode` implements the interface by passing the request upstream and applying itself to whatever comes back, which is what keeps a repaired reply going through the same fence stripping the first one did.

**The project index is parsed, never loaded through a workspace.** What the open Unity project contains is read by parsing each `Assets/**/*.cs` with `CSharpSyntaxTree.ParseText`, in parallel, and cached per file by write time and length. `MSBuildWorkspace` is not an option and is not to be reconsidered: it runs a design time build per project, it is documented to fail on Unity generated csproj files, and Unity rewrites those files on every recompile. `SymbolFinder.FindReferencesAsync` is likewise out, because it generates compilations for every project in the solution; the index keeps its own reference graph instead. A real `CSharpCompilation` is built only where semantics are genuinely needed, and it reuses the reference set the compile checker already assembles.

**Nothing new is created that already exists.** `DuplicateTypeGuard` refuses a planned type the index already knows about, and the refusal names the existing type and its file so the plan can become an edit or a reference instead. This is enforced by the index rather than asked of the model, because the shortest path for a coder is always a new file and that is exactly how a project acquires a second half wired copy of something it had.

**A wire carries one item or many, identically.** A plan is a list of file tasks on an ordinary wire, and a node that receives a list runs once per entry and emits a list. That is the whole of fan out: the graph that writes five files is the graph that writes one, and `GraphExecutor` is not involved. Files are generated in dependency order, each shown the signatures the earlier ones actually declared, because a file generated in parallel with the thing it calls will guess at the name and be wrong. A Loop node making the iteration visible is not built and cannot be until a node can drive its downstream neighbours, which the executor deliberately does not allow; the README roadmap still lists it.

**A capability is advertised by interface, never named in the executor.** `ICodeRepairSource` was the first, `IPlanningModel` is the second. A node that needs something looks along its own wires, upstream or downstream, and asks whatever implements it. The planner borrows the model that is going to do the writing rather than carrying a second copy of every model setting.

**A multi file check compiles the accumulated set.** Each file of a plan is compiled together with every file settled before it, so a script that calls into a sibling generated moments earlier resolves. Only the file being checked is offered for repair; one that already passed is never rewritten because a later one broke.

**Nothing is written until the whole plan succeeds.** Writes are staged in a `ProjectWriteBatch` and committed together, and a failure part way restores what it already wrote. Writes are in place and never delete and recreate, because a Unity script is bound to scenes through the GUID in its `.cs.meta` sibling and recreating the file issues a new one.

**Unity binding rules are refusals, not warnings.** A MonoBehaviour file name must match its class name; a type may not disappear, change namespace, or stop deriving from MonoBehaviour without a `[MovedFrom]` shim; a serialized field may not be renamed without `[FormerlySerializedAs]`. Every one of these compiles cleanly and silently breaks a scene, which is why a warning is not a defence. A new MonoBehaviour is reported as needing attaching, because one that is never attached silently never runs.

**Context is budgeted explicitly and disclosed progressively.** The whole project as one line per type, member signatures for ranked candidates only, and full contents for nothing that did not survive ranking. The budget is a setting on the plan node, is written to the feed, and what does not fit is dropped in rank order with a note rather than truncated silently.

**Deliberately deferred.** Do not design these until asked, they need real-world shape first: the trust scoring algorithm and the contribution/barter economics. Peer discovery is no longer on this list because the engine provides it; do not build a second one.

## Resources and the user's machine

- Never download models, datasets, or large binaries to the user's machine without asking first. This includes anything fetched for testing.
- Never create projects, folders, or files outside the repo and the app's own scratch locations without asking.
- If a test needs a resource that is not already present, stop and ask. Do not acquire it and do not silently skip the test: say which leg is unexercised and why.
- Vendored tool binaries the build genuinely requires (llama.cpp, mesh-llm, uv) are the exception, and even those get named before they are fetched.

## Verification

- Build the work. Do not construct test harnesses, download models, or create test projects to prove it.
- Confirm it compiles clean and the exe publishes and launches. That is the bar.
- If something cannot be verified without resources that are not present, say so in one line and move on. Do not acquire anything.
- Report what was built, what was found, and anything that needs a decision. Not a test log.

## Working style

- One part at a time. Do not list everything that could be done next or get ahead of what is being asked.
- When a spec has an internal conflict or an assumption that does not hold, say so plainly and explain the tradeoff rather than silently picking. Flagging the pin-type conflict in the first build was the correct behavior.
- Verify claims. Do not report something as working that was not exercised. State clearly what was tested and what was not.
- Investigate rather than assume when the behavior of an external tool matters (for example, what llama.cpp RPC actually exposes).
- Do not narrate reasoning at length. Report what was done, what was found, and what needs a decision.
