# Tests

Two layers, run separately, both from the command line. Either returns a non-zero exit code if
anything fails.

## The deterministic layer

No model, no network, no engine process. The model is replaced by a stub, so a repair loop, a
debate and a plan all run the same way every time.

```
dotnet test tests/LocalNEXUS.Tests --filter Layer=Deterministic
```

Seconds, not minutes. This is the one to run after any change.

## The end to end layer

A real GGUF served by the real `llama-server`, with real requests going over the real client.

```
dotnet test tests/LocalNEXUS.Tests --filter Layer=EndToEnd
```

It needs two things already on the machine, and it downloads neither:

- `llama-server.exe` in `vendor/llama`, which is where the build already expects it.
- Any `.gguf` under `%LOCALAPPDATA%\LocalNEXUS\models\gguf`. The first one found alphabetically is
  used.

If either is missing the test says which and fails, rather than passing quietly. This layer only
runs when it is asked for, so a failure there is the honest answer.

Nothing in it asserts on what a model said. A model is not deterministic and a test expecting
particular words would be a test of that model on that day. What is asserted is everything around
the reply: that a server started, that tokens streamed, that the result is filled in, and that code
reaching the writer compiles.

## Everything

```
dotnet test tests/LocalNEXUS.Tests
```

Runs both layers.

## Where tests write

Every test that touches the disk creates a folder under the system temp directory named
`localnexus-tests`, and deletes it afterwards. Nothing is written into the repository, into a real
Unity project, or into `%LOCALAPPDATA%\LocalNEXUS`.

The one thing that follows from this: the real credential store cannot be exercised, because its
file path is a static reading from the user's own application data. Constructing one reads their
keys and setting one overwrites their file. What is tested instead is the rule that matters, which
is that a saved graph never contains a key.

## What the suite cannot see

- Anything drawn. No view, no theme, no binding, no converter.
- The mesh, entirely, by scope.
- The Python runtime, which needs a two gigabyte environment provisioned.
- Cloud providers, which need keys and would cost money per run.
- Whether the published single file exe behaves the same as the build output. The suite runs
  against the assembly, and single file publishing has already changed behaviour three times.
