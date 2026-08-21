# LocalNEXUS

![The workspace during a run](docs/images/workspace-mid-run.png)

LocalNEXUS wires language models together on a canvas and points them at your codebase. You
type what you want, the graph reads your project, writes the code, compiles it, and writes
the files. Models run on your hardware, or across several machines, or through OpenRouter if
you want them to.

Unity C# is what it was built for. The engine does not know what Unity is.

## Quick start

Two ways in.

**The installer.** Take `LocalNEXUS-setup.exe` from a release and run it. It installs per user, so
there is no elevation prompt, and it fetches whichever engines you tick from their own release
pages. Re-run it later to add an engine you skipped. It is not signed, so SmartScreen will warn
you once.

**The zip**, if you would rather. Unzip it and run `LocalNEXUS.exe`. Self contained, no .NET
install needed, and you place the engine binaries yourself as described in `vendor/*/README.md`.

Five minutes to a generated file, assuming a `.gguf` on disk:

1. `File > Open Unity project`. The status bar reports how many C# files it indexed.
2. Models panel in the activity bar, add the folder your model lives in or the file itself.
   Format is detected by reading the file.
3. `Edit > Add node`. Add a Prompt, a Model and an Output. Wire Prompt `Text` to Model
   `Text`, Model `Code` to Output `Code`. Pins refuse connections that do not typecheck.
4. Click the Model node, choose Local, pick your model.
5. Click the Output node, set folder and filename. `Assets/Scripts`, `Spinner.cs`.
6. Type into the box at the bottom and press Ctrl+Enter:

> Write a MonoBehaviour called Spinner that rotates its transform around the Y axis at a
> configurable speed, with a serialized field for the speed.

Nodes light up in turn, the reply streams into the feed, the last line names the file it
wrote. Everything else is more nodes in the middle of that.

## Nodes

| Node           | Does                                                                                   |
| -------------- | -------------------------------------------------------------------------------------- |
| Prompt         | Holds what you typed. Feeds Triage or Model                                            |
| Triage         | Reads the project index, ranks existing files, decides edit or create, orders the work |
| Model          | Calls an LLM. Local, mesh, or OpenRouter                                               |
| Patch          | Applies a rule to code. Find and replace, or a Roslyn expression                       |
| Compiler check | Compiles against the project's real references, hands failures back for repair         |
| Output         | Writes files, subject to the Unity binding rules                                       |

## What makes it different from a chat window

The checks are in the pipeline, not in your review.

- The project is indexed first, so a plan can say edit this rather than always create this.
- Creating a type that already exists is refused, and the file holding the original is named.
- Code is compiled before it is written, and failures go back to the model.
- Changes that compile but break Unity are refused. Renaming a MonoBehaviour away from the
  filename Unity binds it by, for one.

## Status

Pre-1.0. No automated tests. Interfaces still move.

|                   |                                                                                                                                                                  |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Solid             | Graph engine, llama.cpp inference, project index, Unity rules, staged writes, interface                                                                          |
| Lightly exercised | safetensors through `transformers serve`, the planning and multi file path, OpenRouter                                                                           |
| Unproven          | Everything distributed. It has only run on one machine over loopback, never across two physical machines                                                         |
| Broken            | Compiler check faults the run instead of reporting. It touches the Problems panel from a background thread and WPF refuses. Leave the node out until it is fixed |

## Requirements

Windows 11. No Linux or macOS build.

A GPU is strongly recommended. Developed against an RTX 4080 Laptop with 12 GB. llama.cpp
ships Vulkan builds for AMD and Intel, and a CPU build that works slowly.

About 1 GB for the app and engines, plus whatever your models weigh.

First launch builds a Python environment in the background so safetensors models can be
served. Roughly 3 GB on NVIDIA because it pulls a CUDA torch, a few hundred megabytes
otherwise. Nothing waits on it and GGUF never touches it. That is the only thing downloaded
without you asking.

A 7B class coding model is the realistic floor for useful Unity C#. Below that you mostly
watch the compile check catch things.

## Distributed inference

![The network tab](docs/images/network.png)

A model too big for one machine can run across several. LocalNEXUS starts a
[Mesh LLM](https://github.com/Mesh-LLM/mesh-llm) node, which handles discovery, splits the
model into layer stages, and places them wherever there is room.

The Network tab lists what the mesh can serve, how many machines cover each stage, and
whether anything is standing by if one drops. You choose what to offer: a switch for the
machine, a tick per model, and a memory limit defaulting to leaving a quarter of your card
free. Private and LAN only unless you publish it.

See the status table before relying on this.

## Building from source

.NET 8 SDK, Windows.

```powershell
git clone https://github.com/You-Know-Its-Me-Studios/LocalNEXUS.git
cd LocalNEXUS
dotnet build
.\publish.ps1
```

The engine binaries are not in the repository. Fetch them into `vendor/` first or the app
builds and runs but cannot do anything. Each folder has a README naming the release to
download. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Roadmap

Fix the Compiler check threading bug. A Loop node, once a node can drive the nodes
downstream of it. Breakpoints on wires. Memory that survives between runs. Editing and
deleting from the Output node, not only writing. Running this across two physical machines,
which is embarrassing to still have on the list.

The longer goal is a network where people pool compute to run models none of them could run
alone. That shapes decisions now. Sources are interchangeable rather than mine and theirs,
identity is a persistent public key so reputation can attach later, and coverage is computed
properly even with two machines where it always passes.

Trust scoring and contribution economics are deliberately not designed. They need real use
first.

## Docs

| Doc                                              | Use it for                                         |
| ------------------------------------------------ | -------------------------------------------------- |
| [docs/models.md](docs/models.md)                 | Local and hosted models, the Python runtime        |
| [docs/unity-projects.md](docs/unity-projects.md) | The index, the Unity rules, the compile check      |
| [docs/distributed.md](docs/distributed.md)       | Mesh setup, coverage, contributing compute         |
| [docs/interface.md](docs/interface.md)           | The window, themes, settings                       |
| [docs/architecture.md](docs/architecture.md)     | How it fits together                               |
| [CONTRIBUTING.md](CONTRIBUTING.md)               | Building, conventions, getting the engine binaries |

## Contributing

Issues and pull requests welcome. [CONTRIBUTING.md](CONTRIBUTING.md) covers building from a
clean clone and which parts to leave alone. [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) applies.
Security goes through [SECURITY.md](SECURITY.md), not a public issue.

## Licence

Apache-2.0. Copyright 2026 You Know Its Me Studios. See [LICENSE](LICENSE) and
[NOTICE](NOTICE).

Engine binaries you place in `vendor/` and any model weights carry their own licences.
