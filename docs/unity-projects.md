# Working with a Unity project

This page is about what a Unity project adds. For what applies to any codebase, and for how a
Unity project is told apart from an ordinary one, see [project-kinds.md](project-kinds.md).

## Working against an existing project

Wire the Triage node between the prompt and the model:

```
Prompt -> Triage -> Model -> Compiler check -> Output
```

The Triage node reads the project, works out which files the request is about, and emits an ordered
list of files to write. Everything downstream then runs once per file: the model writes them in
order, the compile check checks each against the ones before it, and the writer applies them
together or not at all.

### What it reads, and how much

The project is indexed by parsing every C# file in parallel, recording what each file declares and
which type names it mentions. In a Unity project the scan starts at `Assets`, because that is where
the editor compiles from; in any other project it starts at the root and skips build output, fetched
dependencies, and folders the project's own `.gitignore` names. The result is cached per file by write time and
length, so editing one script re-reads one script. Indexing runs when a project is opened and its
timing appears in the activity feed.

Ranking is by name and member matches against the request, spread through the reference graph by
personalized PageRank, so a file the request never names but everything relevant depends on still
surfaces. Only the files that survive ranking are read from disk at all.

The context budget is a setting on the node and is written to the feed at the start of every run:
by default about 4,000 characters of project map, 16,000 of candidate detail and 4,000 of
signatures produced earlier in the same run, which is roughly 6,000 tokens and fits an 8K window
with room for the reply. Whatever does not fit is dropped in rank order with a note saying so.

### Use, edit, or create

For each candidate the planner must say `USE_AS_IS`, `EDIT`, `CREATE_NEW_REFERENCING <Type>` or
`IGNORE`, and every decision appears in the feed. A plan that asks to create a type the project
already declares is refused by the index, not by the model's judgement, and the refusal names the
existing type and its file so the work becomes an edit or a reference instead.

### Editing

A change to an existing file comes back as a line-tagged diff rather than the whole file: blocks
introduced by `@@`, lines prefixed with a space to keep, a minus to remove and a plus to add. That
is the format the research finds smaller models handle best, and local models are the small ones.
Set **Edits** on a Model node to override it per node.

The applier is deliberately forgiving, because the failure that actually happens is a reproduced
line with different whitespace rather than a wrong idea. It looks for each block exactly, then
ignoring trailing whitespace, then ignoring indentation, and only then fails, naming the lines it
could not find so the repair loop has something to act on. Every change to one file becomes one
write.

### Unity rules that are refusals

Unity binds a script to scenes and prefabs by the GUID in its `.cs.meta` file, and resolves the
type inside by namespace and class name. Several ordinary looking edits therefore compile
perfectly and silently break a scene, so the writer refuses them rather than warning:

- a MonoBehaviour whose file name does not match its class name
- removing or renaming a type that scenes may reference, without a `[MovedFrom]` shim
- moving a type into or out of a namespace, without the same
- renaming or removing a serialized field, without `[FormerlySerializedAs]`
- taking MonoBehaviour off a type that instances may be attached to

When a new MonoBehaviour is written, the feed says it must be attached to a GameObject to run.
Nothing here attaches it.


## Checking that generated code compiles

Drop a **Compiler check** node between the model and the Output node:

```
Prompt -> Model -> Compiler check -> Output
```

It compiles what passes through it and only lets it onward if it compiles. Nothing is written
until it passes, so a failed run leaves the project exactly as it found it. That ordering is the
whole of the file writing story: there is no staging folder and no promote step, because the
check happens before the writer runs at all.

### What it compiles against

The Unity editor version the open project records, or the newest one installed if that version
is not on the machine, plus the assemblies the project has already compiled into
`Library\ScriptAssemblies`. That means a misspelled Unity member or a type that does not exist
is caught exactly as Unity would catch it, and code can use the types the project already
defines. The panel says which of those it found, because a pass against a partial reference set
is a weaker claim than a pass against a complete one.

It compiles the one file, not the project. It cannot see another file generated in the same run,
and it does not run whatever source generators or analyzers the project configures. If no Unity
install or no open project can be found, the node says the check could not be run and passes the
code through: code that cannot be checked is not code that is broken, and it is not reported as
though it were.

### The repair loop

When the code does not compile, the node follows its own incoming wire back to whoever produced
the code and asks for another attempt, handing over the original request, the failing file and
the compiler errors. It repeats until the code compiles or the **retry limit** is reached, three
by default. Every attempt appears in the activity feed with its number and the errors it was
given, so a loop is never silent.

A Reshape node in between is not in the way: it passes the request further upstream and applies
itself to whatever comes back, so a repaired reply is reshaped exactly as the first one was.
Stripping a markdown fence is no longer one of the things it has to do, because a Model node
takes its own fence off unless you tell it not to.

If the code still does not compile, the file is kept with its errors and the rest of the plan
carries on. Everything that did compile is written, and what did not sits waiting to be
resolved from the box under the canvas. The node can also be set to fault the run, or to pass
the last attempt on with a warning, but leaving it for later is the default: stopping at the
third file of eight throws away the four that would have worked.

## Opening a project

**File > Open Unity Project or Folder**, and choose the project root, the folder that
contains `Assets`. The choice is remembered between sessions. Output nodes resolve their
paths inside this folder and refuse anything that would land outside it.

