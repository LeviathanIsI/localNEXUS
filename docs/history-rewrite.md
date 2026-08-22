# Commit history rewrite, v1.39

Every commit message became a conventional commit on 22 August 2026. The tree was not
touched: the tree object at HEAD is byte identical to what it was before, and only the
messages and therefore the hashes changed.

This table is the way back. An old report, issue or note that quotes a short hash can be
resolved here: find the old hash, read across for what it became and what it used to say.

`git filter-repo` rewrote the three in message hash references automatically. The two
documents that referenced a commit in prose were updated by hand in the commit that adds
this file.

| old | new | original subject | new subject |
| --- | --- | --- | --- |
| `d0caacb` | `a98b46b` | first commit | docs: add the readme |
| `0f4e807` | `1606a4d` | Build the LocalNEXUS vertical slice | feat: build the node graph vertical slice |
| `0c99970` | `0645530` | fresh start | docs: add the project brief and restate the readme |
| `b58b3f3` | `49db522` | Serialise llama-server startup per key and make launch settings configurable | feat(llama): serialise server startup per key and expose settings |
| `e6b1ddb` | `68dd2e7` | Add the distributed inference domain model, source registry and health monitor | feat(distributed): add the domain model, source registry and health |
| `baed16d` | `e258f1e` | Teach the launch path rpc topologies and add the rpc worker manager | feat(distributed): add rpc topologies and the worker manager |
| `b5bb113` | `9b9054b` | Separate provider meanings and let resolution split a model across sources | feat(distributed): split a model across sources by provider |
| `283e058` | `ac0edef` | Add the peer panel: sources, contribution and the coverage chain | feat(network): add the peer panel, contribution and coverage chain |
| `fb8259b` | `6325ba2` | Add the publish pipeline and document distributed inference | build: add the publish pipeline and document distributed inference |
| `0d3aaf6` | `d661476` | Restyle menus and add tab and coverage chrome to the theme | feat(theme): restyle menus and add tab and coverage chrome |
| `da0c2ad` | `ec06431` | Index what the network can serve and let model nodes pick from it | feat(network): index served models and let nodes pick from them |
| `a90b702` | `874d877` | Split the window into Workspace and Network tabs and build the network surface | feat(ui): split the window into Workspace and Network tabs |
| `04a5aff` | `b92f414` | Evict conflicting coordinators when a new plan claims their rpc worker | fix(distributed): evict coordinators when a plan claims their worker |
| `555ad24` | `54e033e` | Swap the distributed engine from llama.cpp RPC to Mesh LLM | refactor(distributed): swap llama.cpp RPC for Mesh LLM |
| `fac8c15` | `5d0c466` | Document the engine swap and bundle the mesh binaries | docs: document the engine swap and bundle the mesh binaries |
| `5042cbb` | `a77a694` | Give a model coming up its own state instead of calling it blocked | fix(models): give a model coming up its own state |
| `775241f` | `19d32c6` | Own engine processes with the operating system so none can outlive the app | fix(processes): own engine processes with a job object |
| `3e4df30` | `16beb49` | Let one node run a GGUF from anywhere without cataloguing its folder | feat(models): run a GGUF from anywhere without cataloguing it |
| `60303fc` | `558885b` | catching up | feat(models): serve safetensors through a bundled Python runtime |
| `92405e2` | `6912fa5` | catch up | feat(project-index): index the project and rank files by relevance |
| `c9d911e` | `85c2b35` | catch up | feat(triage): add the plan parser, prompt and duplicate guard |
| `23969db` | `a908b18` | Commit the graph model, which an ignore rule had been hiding since the first commit | fix(build): stop the ignore rule hiding the graph model |
| `c81c557` | `42c2eae` | catching up because the agent isn't commiting | feat(ui): add the IDE shell, five themes and a bundled monospace face |
| `1663209` | `a964add` | Dress the window as an IDE, and make a theme something that can actually change | feat(ui): dress the window as an IDE and make themes swap live |
| `2c06635` | `8377b5c` | Make contributing a decision somebody makes, and stop the interface explaining itself | feat(network): make contributing an explicit choice |
| `4ff2956` | `a2d156a` | Let a model be added by name, and stop the scan giving up four folders down | fix(models): add by name and scan deeper than four folders |
| `57ffbf1` | `978b1ef` | Name the nodes after what they hold and do | refactor(nodes): rename nodes after what they hold and do |
| `4b9b310` | `f544f33` | Prepare the repository for people who did not write it | docs: add contributing, licence, issue templates and CI |
| `9a90d45` | `aaf7f33` | Move the build off a deprecated runtime | build(ci): move off a deprecated runtime |
| `5b2d084` | `c8fc870` | updated readme, security, and contrib files | docs: update the readme, security policy and contributing guide |
| `35724b2` | `69429c6` | updated contact email in code of conduct | docs: update the contact email in the code of conduct |
| `9861f03` | `448f6f0` | updated email in security.md | docs: update the contact email in the security policy |
| `1003703` | `1fc4580` | Add an installer that fetches the engines rather than carrying them | build(installer): fetch the engines rather than carrying them |
| `e9e2b5d` | `4d84a3e` | Let the application grow capabilities its author did not build | feat(extensions): let the application load contributed nodes |
| `52843a6` | `aa480b8` | Rebuild the installer as a WPF application and drop Inno | refactor(installer): rebuild as a WPF application and drop Inno |
| `0c1b746` | `de9e6a8` | Make the docs say what the installer actually is | docs: say what the installer actually is |
| `40ba90a` | `46cca86` | Give extensions a window instead of a column in settings | feat(extensions): give them a window instead of a settings column |
| `ea11c41` | `4f957ba` | Stop the empty message drawing over the presets list | fix(extensions): stop the empty message drawing over the presets |
| `73c877b` | `1fab1a7` | Use the cloud accounts people already pay for | feat(models): use the cloud accounts people already pay for |
| `5363092` | `37dfe9b` | Give the product a mark, and one line that decides which one | feat(brand): add the product mark and one line that picks it |
| `2d902b0` | `b0a06f2` | Panels that get out of the way, and a theme that looks like the installer | feat(ui): collapsible panels and a theme matching the installer |
| `53ba722` | `a34ac9c` | Make the window actually transparent, the installer's way | fix(ui): make the window actually transparent |
| `e02a936` | `3f6514c` | Open maximised | feat(ui): open maximised |
| `5ca2b04` | `63c13bf` | Let the Network table sit on the window base layer too | fix(ui): let the Network table sit on the base layer |
| `59a5913` | `61a1cfa` | Stop writing a crash report on every ordinary exit | fix(app): stop writing a crash report on every ordinary exit |
| `5d8431d` | `3d0ddf8` | Sit the state dots on the text rather than above it | fix(ui): sit the state dots on the text rather than above it |
| `d1da98b` | `4182330` | Halve the state dot nudge, which overshot | fix(ui): halve the state dot nudge, which overshot |
| `8bc1905` | `94a0cb7` | Centre the status bar row, which is what the dots were misaligned by | fix(ui): centre the status bar row to align the state dots |
| `d1bca4d` | `0f025ff` | Drop the state dots the last pixel onto the text | fix(ui): drop the state dots the last pixel onto the text |
| `e6462de` | `a8930d4` | A run ends in a state you can pick back up | feat(run): end a run in a state that can be picked back up |
| `68f8375` | `70d00db` | Put the request box under the canvas it drives | feat(ui): put the request box under the canvas it drives |
| `5b61908` | `cee099e` | Keep the whole record, and be able to put a run back | feat(history): keep the whole record and allow a run to be undone |
| `00acfec` | `401b1fc` | The box becomes a conversation, and the graph can ask | feat(chat): let the graph ask a question in the request box |
| `f43f98c` | `3b22319` | A model is something you hand to a node, not something you find behind it | refactor(nodes): hand a model to a node through a pin |
| `8c030dd` | `acdb80b` | Nobody asked for the fence, so nothing should need wiring to remove it | fix(output): strip code fences without needing a node wired in |
| `4920cde` | `17d3b71` | Two models arguing, and something to settle it | feat(debate): add the Debate and Judge nodes |
| `7e9cd9d` | `0dd32c8` | Give the models folder somewhere obvious to put things | feat(models): give the models folder somewhere obvious to put things |
| `ed41f6e` | `3f3ff35` | Make the model folder notes tell you what to do | docs(models): make the folder notes say what to do |
| `69753fc` | `2fd14f3` | Measure convergence instead of asking a debater about it | feat(debate): measure convergence instead of asking a debater |
| `9c0d7dc` | `e648993` | Correct what was stale, and give a graph a name | feat(graph): name a graph, and correct stale documentation |
| `034a6d3` | `ae331b6` | A suite that runs the parts of this nobody can watch | test: add the deterministic and end to end suites |
| `a167e71` | `8448ee7` | Close the three the suite found, and the gap that let two of them ship | fix: close the three defects the suite found |
| `ba6bb14` | `e037920` | Numbers that move, rather than a verdict | test(evals): add the evaluation harness |
| `139e2dd` | `153c674` | Keep what the model wrote, and stop counting newlines as an edit | fix(evals): keep the reply and stop counting newlines as edits |
| `57dfc3c` | `fc034e7` | Record the two judgements this application exists to make | feat(run): record triage decisions and guardrail refusals |
| `0cefe0a` | `263790a` | Stop a file that did not compile failing every file after it | fix(compiler-check): compile against files settled earlier in the run |
| `1374fce` | `158e0b6` | Start an overnight eval log | docs(evals): start the overnight log |
| `6adfdd7` | `de3796d` | Twenty eval tasks, with the original six left alone | test(evals): add fourteen tasks, leaving the original six alone |
| `520f72e` | `2951c74` | Ten stability runs, and a history file that had been lying | fix(evals): repair the history file and run ten repeats |
| `c65c9f6` | `e44962c` | Exercise Debate and Judge, and write the morning report | test(evals): exercise Debate and Judge, and report the results |
| `c275895` | `4bac806` | A model handed to something is read, not run | fix(executor): do not run a model wired only as a reference |
| `58db62f` | `93240ae` | Convergence says when it cannot tell | fix(debate): say when convergence cannot be measured |
| `8b437cc` | `81f5c2e` | A word is a name because the project has one, not because it looks like one | fix(debate): match identifiers against the project index |
| `45b4346` | `7a34e4e` | Say what came back when a plan will not parse | fix(triage): say what came back when a plan will not parse |
| `94c3b06` | `255d110` | Keep eval results out of the application data folder | fix(evals): keep results out of the application data folder |
| `c5a0e8a` | `226397d` | Say where eval results live, in the two documents that read them | docs(evals): say where results live |
| `990c6d7` | `f31e499` | Ask when the request names nothing, and decide that in the app | feat(triage): ask when the request names nothing in the project |
| `2c0c7d0` | `aadd63b` | An example has to be about something no project contains | fix(triage): use a worked example no project would contain |
| `09e0948` | `2a98535` | Refuse a file declaring a name another file of the same plan declared | fix(output): refuse a name another file of the same plan declares |
| `929f4ba` | `c7e7430` | Compare a generated file against the project, not only against its plan | fix(output): compare a generated file against the project too |
| `db9810a` | `7439a02` | A nested type is recorded under the type it is nested in | fix(project-index): record a nested type under its containing types |
| `96b837d` | `a1adc3b` | A reply that wrote decisions and no plan is a plan | fix(triage): derive a plan from decisions when none was written |
| `e4a0c00` | `93ee9fb` | updated readme.md | docs(readme): rewrite the quick start and trim the readme |
| `1abc85f` | `f274b09` | updated readme.md | docs(readme): reword the line about Unity |
| `ba865ac` | `ca2f8cc` | Write down where Unity was assumed | docs: audit where Unity was assumed |
| `b179826` | `bbbba18` | Detect what sort of project is open, and say so | feat(project): detect whether a project is Unity, and say so |
| `2289d2c` | `4761d8b` | Behave like the project that is actually open | feat(project): apply the Unity rules only to Unity projects |
| `75f0664` | `b59bbd1` | Stop a run on a wire and change what is passing | feat(breakpoints): stop a run on a wire and edit what is passing |
| `f69e76a` | `da5321c` | Place a node from the canvas, by name or by what it can connect to | feat(canvas): add node search and drag to empty space |
| `8b48e94` | `979bdea` | Ship graphs to start from, and let somebody save their own | feat(templates): ship starter graphs and allow saving your own |
| `551bbf1` | `47200e7` | A first launch that says what to do next | feat(walkthrough): add a first run checklist |
