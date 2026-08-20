# LocalNEXUS

A node graph studio for Windows that orchestrates several language models, local and
hosted, to write and edit C# for Unity.

You wire model nodes together on a Blueprints style canvas, type what you want into a
chat box, and watch the models plan, write code, and put files into your Unity project
while their output streams into a live activity feed.

This repository holds the **vertical slice** plus **distributed inference**: one model run
across several machines on a Mesh LLM node, with live mesh state, stage coverage tracking and a
Network tab that browses what the network can serve. It is a real graph execution engine rather
than a fixed pipeline, so the features on the roadmap drop in without rework.

## What the slice does

- **Node canvas.** Add, drag, wire and delete nodes. Built on
  [Nodify](https://miroiu.github.io/nodify).
- **Four node types.** Input, Model, Transform, Output.
- **Typed pins.** Pins are colour coded by what they carry (`Text` is blue, `Code` is
  amber). Invalid drops are refused while you drag, and the wire tells you why.
- **A general graph executor.** Nodes are ordered by their connections using Kahn's
  algorithm, cycles are rejected with a clear message, and every node reports its state
  (`Pending`, `Running`, `Completed`, `Faulted`) on the canvas as it goes.
- **One request path for both providers.** A local GGUF served by a llama.cpp server
  that LocalNEXUS starts silently, and a hosted model on OpenRouter, are the same code
  path over the OpenAI compatible chat completions API.
- **Live streaming.** Tokens land in the activity feed as they arrive, followed by token
  counts, throughput and elapsed time.
- **File writing.** The Output node writes into the Unity project you opened, optionally
  asking for confirmation in the feed first.
- **Save and load.** Graphs round trip through JSON, positions and settings included.
- **Distributed inference.** A model that does not fit on one machine is run across a mesh of
  peers, the run is gated on the mesh being able to assemble it, and the Network tab shows which
  source holds which layer range of the assembled pipeline.
- **Model browsing.** The Network tab lists every model the mesh knows about, with its metadata,
  how many sources hold pieces of it, the slack behind the weakest section and a clear verdict:
  Complete, Starting while it is still coming up, or Blocked when the mesh is known to be unable
  to assemble it; a Model node on the Network provider picks from that list and refuses anything
  that is not ready, with the reason.
- **Contribution.** Any install can serve its GPU to another orchestrator with one
  toggle; roles are interchangeable, there is no dedicated worker machine.

## Tech stack

| Concern          | Choice                                                                        |
| ---------------- | ----------------------------------------------------------------------------- |
| Platform         | Windows 11 desktop, WPF, .NET 8, C#                                           |
| Node canvas      | [Nodify](https://www.nuget.org/packages/Nodify)                               |
| MVVM             | [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm) |
| Scripting        | Roslyn (`Microsoft.CodeAnalysis.CSharp.Scripting`)                            |
| Local inference  | bundled llama.cpp `llama-server`, spawned as a silent child process           |
| Distributed      | bundled [Mesh LLM](https://github.com/Mesh-LLM/mesh-llm) node, spawned as a silent child process |
| Hosted inference | OpenRouter                                                                    |
| Serialization    | `System.Text.Json`                                                            |

Strict MVVM throughout: views are XAML, code behind is `InitializeComponent` and nothing
else, and all logic lives in view models and services.

## Prerequisites

- **Windows 11** (Windows 10 works too).
- **.NET 8 SDK** to build, or the **.NET 8 Desktop Runtime** to run a published build.
  <https://dotnet.microsoft.com/download/dotnet/8.0>
- **A llama.cpp server build** in `vendor/llama/`, only if you want local models.
  See [vendor/llama/README.md](vendor/llama/README.md).
- **At least one GGUF model file**, only if you want local models.
- **An OpenRouter API key**, only if you want hosted models.

Local and hosted are independent. You can run the whole application with just an
OpenRouter key and no llama.cpp build at all, or entirely offline with no key.

## Git

- Commit after each logical unit of work, not one giant commit at the end of a task. A logical unit is something that builds and leaves the app in a working state.
- Never commit broken code to main. If a change spans multiple commits, each one should build.
- Write real commit messages: what changed and why, not "update files." Subject line under ~70 characters, body when the change needs explanation.
- Do not commit build artifacts, binaries, models, or secrets. `dist/`, `bin/`, `obj/`, `vendor/llama/`, `*.gguf`, and any API keys stay out of the repo.
- Push to main when the task is complete and the exe in `dist/` runs.
- Report the commit hash and a one-line summary of what landed when a task finishes.

## Setup and run

```powershell
git clone https://github.com/LeviathanIsI/LocalNEXUS.git
cd LocalNEXUS

# Optional, for local models: place a llama.cpp build in vendor/llama/
#   see vendor/llama/README.md

dotnet restore
dotnet build
dotnet run --project src/LocalNEXUS.App
```

## Publishing a build to hand to someone

```powershell
.\publish.ps1
```

publishes a self contained single file build to `dist\`, overwriting what is there. The
person you give it to needs no .NET install: `dist\LocalNEXUS.exe` runs on its own.
Whatever llama.cpp build sits in `vendor\llama\` is copied to `dist\vendor\llama\`
automatically so local and distributed inference work from the published folder too. If
`dist\vendor\llama\` is missing on the target machine, drop a llama.cpp build there;
the app looks next to its own exe first.

`dist\` is a build artifact and stays out of git.

On first run LocalNEXUS creates its data folder:

```
%LOCALAPPDATA%\LocalNEXUS\
  models\        GGUF files are discovered here
  graphs\        saved graphs
  logs\          llama-server output and crash reports
  config.json    last opened Unity project, extra model folders
```

Nothing is written inside the repository, so a clone stays clean.

## Adding local models

Either drop a `.gguf` file into `%LOCALAPPDATA%\LocalNEXUS\models\` (subfolders are
scanned, so one folder per model is fine), or select a Model node and use **Add folder**
in the settings panel to register a folder you already keep models in. **Rescan** picks
up files added while the application is open.

For a model that simply lives somewhere else, **Browse for a file** on a Model node points
that one node at a `.gguf` anywhere on disk without registering its folder for the whole
application. The panel then says the node runs that file rather than the dropdown selection,
and shows the path. **Use the catalogue** drops it and returns the node to the dropdown, which
keeps its selection underneath the whole time. Two nodes in one graph can run two models from
two different drives this way. The choice is saved with the graph; if the file has gone by the
time the graph is opened again, the panel says so in red and the run refuses with the path
named rather than quietly running something else.

Pick a model from the dropdown on any Model node. Leave **Base URL** empty and
LocalNEXUS starts a `llama-server` process for that file on a free loopback port, waits
for its health endpoint, and reuses it for every later request. The process runs with no
console window and is killed when the application exits. Its output goes to
`%LOCALAPPDATA%\LocalNEXUS\logs\`.

Servers are started with all layers offloaded to the GPU (`-ngl 999`) and an 8192 token
context.

If you already run a llama.cpp server yourself, put its URL in **Base URL** and
LocalNEXUS will use it instead of starting one.

## Using OpenRouter

Select a Model node, switch **Provider** to `OpenRouter`, and fill in:

- **Model slug**, for example `anthropic/claude-sonnet-4` or
  `meta-llama/llama-3.3-70b-instruct`.
- **API key**, from <https://openrouter.ai/keys>.

**Base URL** is filled in as `https://openrouter.ai/api/v1` automatically.

> Keys are stored in plain text inside the saved graph file. Strip them before sharing
> a graph.

## Distributed inference: splitting a model across machines

One model can run across several machines over [Mesh LLM](https://github.com/Mesh-LLM/mesh-llm),
which LocalNEXUS starts as a silent child process. The mesh pools the GPUs of every machine in
it behind one OpenAI-compatible API, routes each request to whichever peer can serve the model,
and splits models too large for one box into contiguous layer stages.

Local single machine inference does not use any of this. A model that fits on one GPU is served
by llama.cpp exactly as it always was, whether or not a mesh node is running.

**Place the engine.** Put a Mesh LLM Windows build in `vendor\mesh` (see
`vendor\mesh\README.md` for which flavour and the expected layout). A published `dist\`
folder already carries it.

**Start your node.** Open the **Network** tab and press **Start mesh node**. With nothing else
configured this hosts a private mesh on the local network: LAN-scoped discovery only, no public
relays, joinable only by the invite token shown on the card. Publishing it for public discovery
is a separate tick box and is the only setting that reaches beyond your network.

**Contribute.** Tick **Offer this machine's compute**, choose which local GGUF this machine
serves, and press **Apply**. Unlike the previous engine's declared offer, the memory cap here is
enforced by the mesh planner: a model that does not fit inside it is never placed on this
machine.

**Add a second machine.** Install LocalNEXUS there, copy the invite token from the first
machine's Network tab, press **+** next to Sources, paste it, and press **Join**. Both machines
then appear in each other's source lists with their announced memory and measured latency. The
first time, allow the node through the Windows firewall.

**Browse what the network can serve.** The Available models list leads the tab: every model the
mesh knows about, its metadata, how many sources hold pieces of it, and a verdict. Selecting one
draws its coverage chain: the pipeline of sections in order, which source holds which layer
range, and how much slack stands behind each.

The verdict is three way, because a model that is still loading has not failed at anything. A
section still coming up is blue and says what it is waiting on; only a section the mesh is known
to be unable to serve, because no source holds it or the source holding it reports it stopped,
is red and named as the reason the model is Blocked. The same distinction runs through the tab:
before the node has answered, the model and source lists say the mesh is starting rather than
sitting empty as though the network were bare.

**Run across the mesh.** Set a Model node's provider to **Network** and pick a model from the
list. Only a Complete model can be picked, and a model that stops being complete between
selection and run refuses the run, saying whether it is still coming up or blocked outright. Whether the model runs on one peer or as layer stages
across several is the mesh's decision, made at run time and echoed to the activity feed.

Nothing about sources is configured by hand any more. Membership, placement, liveness and
recovery all belong to the engine; the Network tab renders what it reports.

Nothing LocalNEXUS starts outlives it. Engine processes are held by the operating system on the
application's behalf and are stopped when it closes, whether it closes normally or is killed
outright, and anything a previous session left behind is cleaned up at the next launch. An engine
you started yourself is never touched.

Four things worth knowing about the layer underneath, all established by testing the bundled
build rather than by reading its documentation:

- **A splittable model is not the same as a local GGUF.** Stage splits need a published layer
  package (a repository of per-layer GGUF fragments). A plain local GGUF can be served whole by
  one machine and routed to across the mesh, but it cannot be split.
- **The memory cap is real.** `--max-vram` is honoured by the planner, which will refuse to
  place a model that does not fit rather than trying and failing.
- **One node can serve many consumers at once.** This is the limitation that ended the previous
  engine, and it is genuinely gone.
- **A peer dying mid request no longer takes the pipeline down.** An in-flight streaming request
  survived its stage peer being killed. Re-convergence afterwards is less certain: on a single
  GPU loopback topology the mesh replanned onto a replacement node but did not become routable
  again within the test window. Treat recovery-after-replacement as unproven rather than
  guaranteed.

## Opening a Unity project

**File > Open Unity Project or Folder**, and choose the project root, the folder that
contains `Assets`. The choice is remembered between sessions. Output nodes resolve their
paths inside this folder and refuse anything that would land outside it.

## Building the demo graph

1. Open your Unity project.
2. Add **Input**, two **Model** nodes, and an **Output** node from the palette.
3. Wire them: `Input.Text` to the first model's `Text`, the first model's `Code` to the
   second model's `Text`, the second model's `Code` to `Output.Code`.
4. Configure the first model as the planner. A system prompt along these lines works
   well:

   > You are a senior Unity gameplay engineer. Given a request, write a short, concrete
   > implementation plan: the class name, the serialized fields, the methods, and the
   > Unity lifecycle hooks involved. Do not write the code itself.

5. Leave the second model on its default system prompt, which asks for raw compilable
   C# with no markdown fences.
6. On the Output node set **File name** to `PlayerMovement.cs`. **Target subfolder**
   already defaults to `Assets/Scripts`.
7. Type into the chat box at the bottom:
   `Create a Unity C# script for basic player movement with jump and dash`
8. Press **Run**.

Both models stream into the feed, and the file appears in `Assets/Scripts`.

If a model wraps its reply in a markdown code fence despite the prompt, drop a
**Transform** node between it and the Output node and leave it in **Script** mode. Its
default expression removes a surrounding fence and leaves anything else alone.

## Notes on the design

**Pin types.** Connections require the source and target pin types to match, with one
deliberate exception: a `Code` output may feed a `Text` input. Without it a Model node,
which takes `Text` and emits `Code`, could only ever be fed by an Input node, so chaining
a planning model into a coding model would be impossible. The exception is one
directional: a `Text` output still cannot reach a `Code` input, so prose cannot be piped
straight into a file writer. The rule lives in one place,
`Models/PinTypeCompatibility.cs`.

**Transform script mode** evaluates a single C# expression through Roslyn with the
incoming value bound to `input`. `System`, `System.Linq`, `System.Text` and
`System.Text.RegularExpressions` are imported. Compilation is cached per expression, and
compile errors surface in the activity feed when the node runs.

**One run at a time.** The slice permits a single active run. Run, Pause and Stop are
driven by the `RunState` enum rather than by scattered flags. Pausing takes effect
between nodes, so it never interrupts a model mid stream.

## Project layout

```
src/LocalNEXUS.App/
  Models/          NodeBase, Pin, Connection, GraphModel, pin typing and validation
  Nodes/           InputNode, ModelNode, TransformNode, OutputNode, NodeFactory
  Services/
    Execution/     GraphExecutor, RunContext, RunState, topological sort
    Inference/     IModelClient, OpenAiCompatibleClient, LlamaServerManager,
                   LlamaLaunchOptions
    Distributed/   the mesh and what it reports: MeshManager, MeshStatusReader,
                   InferenceSource, ModelSection, CoveragePlan, NetworkServedModel
    Persistence/   AppPaths, AppConfig, ModelCatalog, GraphSerializer
    Files/         UnityProjectService, FileWriter
    Dialogs/       IDialogService and its Windows implementation
  ViewModels/      MainViewModel, ActivityFeedViewModel, NetworkViewModel, and friends
  Views/           XAML only: window, canvas templates, network tab, settings panels
  Infrastructure/  ActivityFeed, converters, behaviours
vendor/llama/      llama.cpp binaries, fetched not committed
publish.ps1        self contained single file publish into dist/
```

## Roadmap

Deliberately not in the slice, planned next:

- **Memory node** and cross run persistence.
- **Compile check node** with an automatic repair loop.
- **Loop node** and automatic iteration over lists.
- **Breakpoints on wires**, to inspect a value mid graph.
- **Per node detail tabs** and expandable inline diffs in the feed.
- **Queued and concurrent runs**.
- **Export and import of shared graph templates**.
- **A global settings screen**.
- **Live Unity Editor control** over MCP.
- **Edit and delete file modes** on the Output node, alongside write.

## Licence and intent

LocalNEXUS is intended to be open source. Contributions and issues are welcome once the
slice settles. llama.cpp binaries placed in `vendor/llama/` and any model weights you
download carry their own licences and are not covered by this repository.
