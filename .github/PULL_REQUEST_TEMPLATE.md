## What changed

<!-- One or two sentences. What the change does, not which files moved. -->

## Why

<!--
The diff shows what changed. It cannot show why, and that is the part reviewers need.
If this closes an issue, say "Closes #123".
-->

## How it was verified

<!--
Say what you actually ran, and be honest about what you did not. "I could not test the mesh
path, I only have one machine" is useful and nobody will hold it against you. Guessing is
what causes trouble.

If it touches inference, say which model and which engine you ran it through.
-->

## Checklist

- [ ] Builds clean, with no new warnings. CI builds with `-warnaserror`.
- [ ] `.\publish.ps1` produces an exe in `dist\` that runs. Required for anything touching
      startup, file paths, process handling or resource loading, because single file
      publishing behaves differently from `dotnet run`.
- [ ] Follows the conventions in [CLAUDE.md](../CLAUDE.md): strict MVVM, no hardcoded
      colours, enums over scattered booleans, no em dashes or en dashes.
- [ ] No secrets, model weights, binaries or build output added. Saved graphs store API keys
      in plain text, so do not commit one.
- [ ] Documentation updated if behaviour a user can see has changed.

<!--
If this touches GraphExecutor, the model client interfaces, pin compatibility, graph
serialization of historical type keys, or engine process ownership, please say why here.
CONTRIBUTING.md explains why those are worth a conversation first.
-->
