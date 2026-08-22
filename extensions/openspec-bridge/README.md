# OpenSpec bridge

Speaks the LocalNEXUS spec contract over stdio and answers every call by asking the
[OpenSpec](https://github.com/Fission-AI/OpenSpec) CLI. It reimplements nothing: which artifact is
ready, whether a change is finished and what a delta merges to are all read from what OpenSpec
prints.

## Installing it

It is not published yet. Until it is, add it from the Extensions window as a command:

- Command: `node`
- Arguments: the absolute path to `src/index.js` in this folder
- Contracts: tick **A spec tab**

Run `npm install` in this folder first, which fetches OpenSpec as a dependency.

To make it installable by anyone else, one of two things has to change. Publishing
`@localnexus/openspec-bridge` to npm makes the shipped preset work as written, because the preset
launches it through `npx --yes`. Or the preset's launch changes to a git URL, which npx also
accepts, at the cost of every start cloning rather than resolving a cached package.

## The project it reads

The working directory, or `LOCALNEXUS_PROJECT` when the host sets it, which LocalNEXUS does. It has
to be a folder OpenSpec has been initialised in, meaning one with an `openspec/` directory.

## What it answers

| Call | What it runs |
| --- | --- |
| `spec/describe` | `openspec --version`, and `openspec list --json` for the resolved root |
| `spec/changes` | `openspec list --json`, then `openspec status --change <id> --json` for each |
| `spec/artifact` | `openspec status --json` for the path, then reads the file |
| `spec/advance` | `openspec status --json` for what is ready, then `openspec instructions <id> --json` |

`spec/log` goes the other way, and reaches the activity feed.

## What advance does, and does not

It does not write the artifact. OpenSpec has no command that writes one: creating an artifact is an
agent's job, driven by the instructions the CLI hands out. So advance returns those instructions and
the template, and says the writing is still to be done. Sending them to the Workspace is what makes
the graph write it.
