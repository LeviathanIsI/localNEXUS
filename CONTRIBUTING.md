# Contributing

## Get the engine binaries first

This is what stops people. The repository does not contain the inference engines. They are
large, GPU specific, and carry their own licences, so you fetch them into folders that
already exist and are gitignored.

If you only want to run the application rather than work on it, do not do any of this: take the
installer from a release and tick the engines you want, and it fetches them for you. This
section is for working from a clone.

The app builds and runs without them. It just cannot do anything, and the feed says so on
startup.

| Folder          | What goes there                             | Needed for                                          |
| --------------- | ------------------------------------------- | --------------------------------------------------- |
| `vendor/llama/` | A llama.cpp Windows release, extracted flat | Any GGUF model. Get this one                        |
| `vendor/mesh/`  | A Mesh LLM Windows bundle                   | The Network tab only                                |
| `vendor/uv/`    | `uv.exe`                                    | safetensors models, which need a Python environment |

Each folder has a `README.md` with the exact release and layout. Start with
`vendor/llama/README.md`, the other two are optional.

`vendor/python/` is different. Its lockfiles are committed, because the point of them is that
every install resolves identically. Do not regenerate them casually.

## Build

.NET 8 SDK and Windows. It is WPF, so it does not build on Linux or macOS.

```powershell
git clone https://github.com/You-Know-Its-Me-Studios/LocalNEXUS.git
cd LocalNEXUS
dotnet restore
dotnet build
dotnet run --project src/LocalNEXUS.App
```

One solution, two projects. The second one is the installer.

```
LocalNEXUS.sln
src/LocalNEXUS.App/          the application
src/LocalNEXUS.Installer/    the installer, a WPF app of its own
vendor/                      engine binaries you fetch, plus committed Python lockfiles
docs/                        documentation
publish.ps1                  produces the runnable exe
release.ps1                  produces the installer and the zip
dist/                        build output, gitignored
```

`.\publish.ps1` is the real build. It publishes a self contained single file exe to `dist\`
and copies whatever engines you have into `dist\vendor\` with paths intact.

`.\release.ps1` produces what a release consists of, the installer and the plain zip, both
into `dist\release\`. It calls `publish.ps1` rather than reimplementing it, so a release build
and a development build are the same build. It also copies the published exe into the
installer's `Payload/` folder, because the installer carries the application inside itself and
only fetches the engines.

The installer is WPF rather than a setup tool, which is why it looks like the product. It
reuses the application's conventions and its own theme dictionary, so the no colour literals
rule applies there exactly as it does here. Inno Setup was tried first and could not produce
the design: its page structure is fixed and its styling skins existing controls rather than
letting new ones be laid out.

**"It compiles" is not the bar. "The exe in `dist\` runs and does the thing" is.** Single
file publishing has traps, the sharpest being that `Assembly.Location` is empty inside a
bundle, which silently breaks anything resolving paths that way. Works under `dotnet run`,
dies in `dist\` is a common mistake.

CI builds with `-warnaserror`. A new warning fails the PR. Do not suppress one to get green.

## How it fits together

**The graph executor knows nothing about nodes.** `GraphExecutor` topologically sorts, gathers
each node's inputs from its wires, runs it. Adding a node type must never require touching
it. If your change would, the design has gone wrong somewhere.

**Capabilities are advertised by interface.** When a node needs something from a neighbour it
looks along its own wires and asks whatever it finds whether it implements the interface.
`ICodeRepairSource` and `IPlanningModel` work this way. No node type is named in that
machinery.

**Every model is reached the same way.** llama.cpp, `transformers serve`, the mesh and
OpenRouter all speak an OpenAI compatible HTTP API, so there is one request path.
`IModelRuntime` answers three questions about a local engine and `RuntimeResolver` picks
between them by inspecting the file. A third local engine should be one entry in the
resolver.

**The project index is parsed, not loaded through a workspace.** Every `Assets/**/*.cs` goes
through `CSharpSyntaxTree.ParseText` in parallel, cached by write time and length.
`MSBuildWorkspace` runs a design time build per project and is documented to fail on Unity
generated `csproj` files, which Unity rewrites on every recompile anyway.

**Generated code is compiled before it is written.** Roslyn, against reference assemblies from
the open project, failures handed back upstream. Unity batch mode was measured and
rejected: seconds rather than milliseconds per attempt, and a second instance refuses to open
a project the editor already has open, which is exactly the situation of anyone using this.

**Writes are staged.** Nothing reaches the project until the plan succeeds, and writes are in
place rather than delete and recreate, because a Unity script is bound to scenes through the
GUID in its `.cs.meta` sibling.

More in [docs/architecture.md](docs/architecture.md).

## Leave these alone

Not sacred, but changing them usually means something else is wrong. Raise an issue first.

- `GraphExecutor`, and anything making it aware of node types.
- `IModelClient`, `OpenAiCompatibleClient`, `ModelEndpoint`, `ChatCompletionResult`. If a
  runtime change seems to need these, the problem is elsewhere.
- `PinTypeCompatibility`. Pin rules live in one table, never as scattered special cases.
- The serializer's handling of historical type keys. Every key the app has ever written still
  loads. Dropping one eats somebody's saved graph.
- Child process ownership. Engines are held in Windows job objects so the kernel cleans up.
  It is more careful than it looks, on purpose.

Two absolutes there: the app never terminates an engine process it did not start, and
processes left by a previous session are terminated at startup rather than adopted.

## Conventions

[CLAUDE.md](CLAUDE.md) at the root is the working brief used when developing this with an AI
assistant, and it doubles as the style guide because it is where the reasoning behind settled
decisions lives. Not generated, not decoration.

- Strict MVVM. Code behind is `InitializeComponent()` and nothing else. Platform specifics
  like file dialogs go behind a service interface.
- Enums over scattered booleans for anything with more than two states.
- No hardcoded colours. Everything resolves through the semantic brush table or the five
  themes break. A literal colour in a view is a bug.
- No DI container. `App.Compose()` builds the object graph by hand, deliberately, so the whole
  app is constructible in one readable method.
- `ConfigureAwait(false)` in services.
- Sentence case in the interface. No em dashes or en dashes anywhere, in code, comments, docs
  or user facing strings.
- No `// TODO: implement later` in a pull request.

Consistency with surrounding code beats personal preference.

## Pull requests

Open an issue first for anything substantial. The architecture is opinionated and a lot of it
is settled deliberately, so a conversation before you write code can save you a rewrite.
Typos and docs can go straight to a PR.

A good one does one thing, explains why in the description rather than what, builds clean, and
says how you verified it. Be honest about what you did not exercise. "Could not test the mesh
path, no second machine" is useful and nobody will hold it against you. For anything touching
startup, paths, process handling or resource loading, confirm `.\publish.ps1` produced an exe
that runs. For anything touching the installer, confirm `.\release.ps1` produced one that
installs, because the installer is published single file too and has the same traps.

Commits should build individually, and their messages are conventional commits:
`type(scope): subject`, subject in the imperative and under 70 characters, body when the
subject cannot hold the reason.

Types are `feat`, `fix`, `docs`, `refactor`, `test`, `chore` and `build`. Scope is the part of
the application it touches, and is left off rather than invented.

```
fix(project-index): record a nested type under its containing types

A class ItemStack inside a class Inventory was indexed as Game.ItemStack, which
is the name of a different type that may or may not exist.
```

Every message before 22 August 2026 was rewritten into this format in one pass, so an old hash
from a report or an issue no longer resolves. `docs/history-rewrite.md` maps each one to what
it became.

## Reporting

Bugs and features go through the issue templates, which ask for GPU and VRAM, which model and
quantization, and whether the run was local or over the network.
[SECURITY.md](SECURITY.md) for security, not a public issue.
[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) for conduct.

Contributions are licensed under Apache-2.0, matching the rest of the project.
