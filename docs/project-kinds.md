# Unity as a mode, not an assumption

The engine was built to know nothing about Unity, and that held for the parts that were designed.
It did not hold at the edges. This is the audit of where Unity was assumed, what each assumption
did to somebody opening a plain C# project, and what was done about it.

## What was found

### The project is called a Unity project everywhere

`UnityProjectService` tracks the open folder, resolves paths inside it and is the security boundary
that stops a write escaping the project. None of that is about Unity. The name was, and it spread:
`ExecutionServices.UnityProject`, `AppConfig.LastUnityProjectPath`, `MainViewModel.OpenUnityProject`,
`File > Open Unity project`, and the message a Triage node gives when nothing is open.

### It looked for a Unity project by looking for one folder

`UnityProjectService.Open` set `LooksLikeUnityProject` from whether an `Assets` folder existed. That
is both too weak and too strong. Too weak because plenty of things that are not Unity projects have
an `Assets` folder, a web project most obviously. Too strong because the answer was only ever used
to append "no Assets folder found" to a status string, so nothing acted on it.

### The index scanned Assets and refused anything else

`ProjectIndexService.EnsureAsync` built its scan root as `Path.Combine(projectPath, "Assets")` and
returned `Unavailable` with the stage "No Assets folder" when that did not exist. A plain C# project
therefore indexed zero files, which is not a degraded experience but a broken one: the Triage node
needs the index and refuses without it, the duplicate guard has nothing to compare against, and the
elicitation check concludes that a request naming a real type names nothing.

The exclusions it did apply are Unity's: a folder ending in a tilde or starting with a dot. Nothing
skipped `bin`, `obj` or `node_modules`, because under `Assets` none of those exist.

### The write guardrails would fire on a project that has no scenes

`UnityScriptRules.Enforce` runs five rules. Two of them are already inert outside Unity because they
only look at types deriving from `MonoBehaviour`, which a plain project has none of:
`FileNameMustMatchBehaviour` and `BehaviourMustStayBehaviour`.

Three would have fired, and all three would have been wrong:

- `TypeMayNotDisappear` refuses removing or renaming a type unless a `[MovedFrom]` shim is added.
  Outside Unity, renaming a type is a rename, and the thing that catches its consequences is the
  compiler.
- `NamespaceMayNotChange` refuses moving a type between namespaces without `[MovedFrom]`. Outside
  Unity, moving a namespace is a normal edit.
- `SerializedFieldMayNotBeRenamed` refuses renaming a field unless `[FormerlySerializedAs]` is
  added. This is the worst of the three, because `IsSerialized` treats any public instance field as
  serialized. On a plain C# project, renaming a public field would have demanded a Unity attribute
  from a project with no Unity in it.

`DescribeAttachmentNeeded` also tells the feed that a new MonoBehaviour will not run until it is
attached to a GameObject. Inert outside Unity for the same reason as the first two.

### The prompts told the model it was working on a Unity project

`PlanPrompt.PlannerSystemPrompt` opened with "You plan changes to an existing Unity project", the
planner message called the project map "This Unity project already contains the following", and the
rules included "A MonoBehaviour file name must match its class name exactly". A model told about
MonoBehaviours by a project that has none is being given noise, and worse, an instruction it may try
to satisfy.

### The compiler treats Unity as the primary path and everything else as a fallback

`UnityReferenceResolver` tries the project's `Library\ScriptAssemblies`, then the editor's
assemblies, and falls through to `FrameworkReferenceResolver` when any of that is missing. The
fallback works and has since v1.12. What it costs is real and is now stated rather than implied:
outside Unity the check sees the framework and the accumulated files of the current plan, and
nothing else the project declares, so every reference to an existing project type reads as a missing
type. `CompileReferenceState.FrameworkOnly` already says exactly that, and `CompilerCheckNode`
already refuses to trust a missing-type error under it.

`RoslynUnityCompiler` is the only compiler in the application and works without Unity, so its name
was the assumption rather than its behaviour.

### The meta siblings

`ProjectWriteBatch` deletes a `.cs.meta` beside a file it rolls back. On a non-Unity project no such
file exists and the delete is skipped, so this is correct as written and was left alone. The
neighbouring rule, that writes are in place and never delete-and-recreate, is good practice
everywhere and is not Unity specific even though the reason it was written down is.

### Documentation, README, installer

`README.md` step one said `File > Open Unity project` and step five used `Assets/Scripts`.
`docs/unity-projects.md` describes `Assets/**/*.cs` as what the index reads. The installer's welcome
text says the app points you at your Unity project. `CONTRIBUTING.md` describes the compile check
against the open Unity project.

### Found and deliberately not changed

- The extension catalogue is Unity MCP servers. `ExtensionPresets` offers three, with prerequisites
  of kind `UnityPackage` and `UnityEditor`. These are Unity integrations that a person chooses to
  install, correctly labelled as such. A catalogue of Unity tools is not a Unity assumption.
- `IndexedTypeKind.MonoBehaviour` and `IndexedType.SerializedFields`. The parser records these from
  base type names and attributes. On a project with no MonoBehaviours they are simply never set, and
  the index naming is settled. Nothing reads them except the Unity rules.
- `OutputNode.DefaultSubfolder` is `Assets/Scripts`. It is the value a newly added node starts from,
  saved with the graph, and changing it per project kind would reach into graphs already saved.
  Reported, not changed.
- The planner's worked examples name `Assets/Scripts/Thermostat.cs`. The path in an example is not
  an instruction, and the examples exist because without them the two row formats get merged.
  Changing them is the change that cost `edit-existing` seven runs out of ten in v1.31. Left alone
  deliberately.

## Detection

A folder is treated as a Unity project when either:

- `ProjectSettings/ProjectVersion.txt` exists, or
- an `Assets` folder exists alongside `ProjectSettings` or `Packages/manifest.json`.

`ProjectVersion.txt` is written by the editor and by nothing else, which makes it the signal worth
leading with. An `Assets` folder on its own is not enough, because that name is common outside
Unity; pairing it with a second Unity-only folder is what makes it mean something. Anything else is
a plain project, which is a real answer rather than a failure to detect one.

Detection happens when the folder is opened, and what it decided is said in two places: the activity
feed entry for the open, and the status bar beside the project name.

## What is Unity only and what is universal

The five rules in `UnityScriptRules` are Unity only and now run only on a Unity project. Each exists
because Unity binds a scene to a script through a GUID and resolves the data inside it by namespace
plus class name plus field name, so each of these edits compiles cleanly and destroys data. None of
that is true anywhere else, and a refusal that makes no sense in the project somebody opened is
worse than no refusal.

One rule is universal and keeps running everywhere: nothing is declared twice. Two types with one
name is a problem in any C# project, it is the thing this application was built to prevent, and it
is enforced in `OutputNode` rather than in the Unity rules, so it was already on the right side of
the line.

The tempting one is filename matching class name. It is a C# convention, and IDEs offer to fix it,
but it is a convention rather than a rule: a file may declare several types, and nothing breaks when
the name differs. Only Unity enforces it, and only Unity enforces it destructively, by silently
refusing to let the component be added. It stays Unity only.

`TypeMayNotDisappear` is the other one worth arguing about, since deleting a public type does break
its callers. It stays Unity only too, because outside Unity that break is a compiler error, the
compile check already catches it, and refactoring a type away is a thing people legitimately do.
Unity's case is special precisely because it produces no error at all.
