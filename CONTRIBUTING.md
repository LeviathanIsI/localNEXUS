# Contributing to LocalNEXUS

Thanks for looking. This file assumes you have never seen the codebase and want to get it
building, understand roughly how it fits together, and land a change.

## Before anything else: the engine binaries

**This is the thing that stops people.** The repository does not contain the inference
engines. They are large, they are specific to your GPU, and they carry their own licences,
so you fetch them yourself into folders that already exist and are gitignored.

The application builds and runs without them. It just cannot do anything useful, and the
activity feed will say so on startup.

| Folder | What goes there | Needed for |
|---|---|---|
| `vendor/llama/` | A llama.cpp Windows release, extracted flat | Running any GGUF model locally. Get this one. |
| `vendor/mesh/` | A Mesh LLM Windows bundle | The Network tab and distributed inference only |
| `vendor/uv/` | `uv.exe` | Serving safetensors models, which needs a Python environment |

Each folder has a `README.md` with the exact release to download and the layout to extract
it into. Start with `vendor/llama/README.md`; the other two are optional.

`vendor/python/` is different: its lockfiles **are** committed, because the whole point of
them is that every install resolves to identical packages. Do not regenerate them casually.

## Building

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and Windows.
The project is WPF, so it does not build on Linux or macOS.

```powershell
git clone https://github.com/You-Know-Its-Me-Studios/LocalNEXUS.git
cd LocalNEXUS

dotnet restore
dotnet build
dotnet run --project src/LocalNEXUS.App
```

The layout is deliberately small: one solution, one project.

```
LocalNEXUS.sln
src/LocalNEXUS.App/     everything
vendor/                 engine binaries you fetch, plus committed Python lockfiles
docs/                   documentation
publish.ps1             produces the runnable exe
dist/                   build output, gitignored
```

### Producing the exe

```powershell
.\publish.ps1
```

This is the real build. It publishes a self contained single file executable to `dist\` and
copies whatever engine binaries you have into `dist\vendor\` with their relative paths
intact, so the published folder behaves exactly like a development run.

**"It compiles" is not the bar here. "The exe in `dist\` runs and does the thing" is.**
Single file publishing has real traps, the sharpest being that `Assembly.Location` is empty
inside a single file bundle, which silently breaks anything resolving paths that way. A
change that works under `dotnet run` and dies in `dist\` is a common and easy mistake.

### Warnings

The project is expected to build with zero warnings. CI builds with `-warnaserror`, so a new
warning fails the pull request. Please do not suppress one to get green; if a warning is
genuinely wrong, say so in the pull request.

## How it fits together

Six ideas cover most of the codebase.

**The graph executor knows nothing about nodes.** `GraphExecutor` topologically sorts the
nodes, gathers each one's inputs from its incoming wires, and runs it. Adding a node type
must never require touching the executor. If your change would, that is the signal that the
design has gone somewhere wrong.

**Capabilities are advertised by interface, never named in the executor.** When a node needs
something from a neighbour, it looks along its own wires and asks whatever it finds whether
it implements the relevant interface. `ICodeRepairSource` and `IPlanningModel` work this way.
No node type is named anywhere in that machinery.

**Every model is reached the same way.** Local llama.cpp, local `transformers serve`, the
mesh and OpenRouter all speak an OpenAI compatible HTTP API, so there is exactly one request
path. `IModelRuntime` answers three questions about a local engine, and `RuntimeResolver`
picks between them by inspecting the model file. Adding a third local engine should be one
entry in the resolver, not a change to the client.

**The project index is parsed, not loaded through a workspace.** Every `Assets/**/*.cs` is
parsed with `CSharpSyntaxTree.ParseText` in parallel and cached by write time and length.
`MSBuildWorkspace` is not an option: it runs a design time build per project and is
documented to fail on Unity generated `csproj` files, which Unity rewrites on every
recompile anyway.

**Generated code is compiled before it is written.** The Compile check node compiles with
Roslyn against reference assemblies assembled from the open Unity project, and hands
failures back upstream for another attempt. Invoking the Unity editor in batch mode was
measured and rejected: seconds rather than milliseconds per attempt, and a second instance
refuses to open a project the editor already has open, which is exactly the situation of
anyone using this tool.

**Writes are staged and committed together.** Nothing reaches the project until the whole
plan succeeds, and writes are in place rather than delete-and-recreate, because a Unity
script is bound to scenes through the GUID in its `.cs.meta` sibling.

For more, read [docs/architecture.md](docs/architecture.md).

## Parts to leave alone

Not because they are sacred, but because changing them usually means something else is
wrong, so please raise an issue before you start:

- `GraphExecutor`, and anything that would make it aware of specific node types.
- `IModelClient`, `OpenAiCompatibleClient`, `ModelEndpoint`, `ChatCompletionResult`. If a
  runtime change seems to require touching these, the problem is probably elsewhere.
- `PinTypeCompatibility`. Pin rules live in that one table and never as special cases
  scattered around.
- The graph serializer's handling of historical type keys. Every key the application has ever
  written still loads. Dropping one eats somebody's saved graph.
- The child process ownership code. Engine processes are held in Windows job objects so the
  kernel cleans them up. It is more careful than it looks, and it is careful on purpose.

Two rules there are absolute: the application never terminates an engine process it did not
start, and processes left behind by a previous session are terminated at startup rather than
adopted.

## House conventions

The project follows the conventions in [CLAUDE.md](CLAUDE.md) at the repository root. That
file is the working brief used when developing LocalNEXUS with an AI coding assistant, and it
doubles as the style guide, because it is where the reasoning behind the settled decisions is
written down. It is not generated, and it is not decoration. If a convention below is unclear,
CLAUDE.md explains why it exists.

The short version:

- **Strict MVVM.** Logic lives in view models and services. Code behind is
  `InitializeComponent()` and nothing else. Anything platform specific, such as file dialogs,
  goes behind a service interface.
- **Explicit state machines over scattered booleans.** Run and node lifecycles are enums that
  drive both the flow and the interface.
- **No hardcoded colours.** Everything resolves through the semantic brush table so the five
  themes keep working. A literal colour in a view is a bug.
- **Hand wired construction.** There is no dependency injection container. `App.Compose()`
  builds the object graph by hand, and that is intentional: the whole application is
  constructible in one readable method.
- **`ConfigureAwait(false)` in services**, and follow existing async conventions.
- **Sentence case in the interface, and no em dashes or en dashes anywhere**, in code,
  comments, documentation or user facing strings.
- **Small focused files, one responsibility each.**
- **No placeholders.** No `// TODO: implement later` in a pull request.

Consistency with the surrounding code beats personal preference.

## Proposing a change

**Open an issue first for anything substantial.** Architecture here is opinionated and a lot
of it is settled deliberately, so a conversation before you write code can save you a
rewrite. Small fixes, typos and documentation can go straight to a pull request.

A good pull request:

- Does one thing, with a title that says what changed rather than which files moved.
- Explains **why** in the description. What changed is visible in the diff; why is not.
- Builds clean with no new warnings.
- Says how you verified it, and is honest about what you did not exercise. "I could not test
  the mesh path, no second machine" is a genuinely useful sentence and nobody will hold it
  against you.
- Confirms `.\publish.ps1` produced an exe that runs, for anything touching startup, paths,
  process handling or resource loading.

Commits should build individually. Write real commit messages: a subject line under about 70
characters, and a body when the change needs explaining.

## Reporting things

- **Bugs and features:** the issue templates ask for what is actually needed to reproduce a
  problem, particularly your GPU and VRAM, which model and quantization, and whether the run
  was local or over the network.
- **Security:** [SECURITY.md](SECURITY.md). Please do not open a public issue for one.
- **Conduct:** [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

By contributing, you agree that your contributions are licensed under Apache-2.0, matching
the rest of the project.
