# LocalNEXUS

A node graph studio for Windows that orchestrates several language models, local and
hosted, to write and edit C# for Unity.

You wire model nodes together on a Blueprints style canvas, type what you want into a
chat box, and watch the models plan, write code, and put files into your Unity project
while their output streams into a live activity feed.

This repository currently holds the **vertical slice**: the smallest end to end version
that proves the architecture. It is a real graph execution engine rather than a fixed
pipeline, so the features on the roadmap drop in without rework.

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

## Tech stack

| Concern | Choice |
| --- | --- |
| Platform | Windows 11 desktop, WPF, .NET 8, C# |
| Node canvas | [Nodify](https://www.nuget.org/packages/Nodify) |
| MVVM | [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm) |
| Scripting | Roslyn (`Microsoft.CodeAnalysis.CSharp.Scripting`) |
| Local inference | bundled llama.cpp `llama-server`, spawned as a silent child process |
| Hosted inference | OpenRouter |
| Serialization | `System.Text.Json` |

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
    Inference/     IModelClient, OpenAiCompatibleClient, LlamaServerManager
    Persistence/   AppPaths, AppConfig, ModelCatalog, GraphSerializer
    Files/         UnityProjectService, FileWriter
    Dialogs/       IDialogService and its Windows implementation
  ViewModels/      MainViewModel, ActivityFeedViewModel, and friends
  Views/           XAML only: window, canvas templates, per node settings panels
  Infrastructure/  ActivityFeed, converters, behaviours
vendor/llama/      llama-server binaries, fetched not committed
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
