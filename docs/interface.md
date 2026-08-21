# The window


An activity bar down the left switches between the Workspace and the Network and holds the
settings gear. The side bar next to it is the explorer, and during a run it is the run
outline: the same nodes the canvas draws, in graph order, each with its state dot and its
elapsed time. The editor area holds the graph and the request being executed. One inspector
on the right serves both sections, always answering the same question: what can I do about
the thing I just clicked. The bottom panel has Problems, Activity and Output, the chat box
sits under it, and the status bar carries the run, the mesh node, the Python runtime and the
open project.

Panels resize with splitters, and the side bar, the inspector and the bottom panel each
collapse. Nodes are added from the Edit menu or by right clicking the canvas.

### Themes

Settings, Appearance. Themes apply as they are picked and are remembered for the next
session. A theme is a dictionary of about thirty colours and nothing else; which colour each
brush takes is a table in `SemanticBrushes`, so a new state is one line rather than five
edits across five palettes. No literal colour appears anywhere else in the source.

The Appearance section shows the states in the theme being previewed, because the rule the
whole application follows is that healthy, working and failed have to stay distinct: a mesh
node still discovering peers and a Python environment mid download are both blue, never red,
and a node that has not run yet is a quiet grey rather than a warning.

### Settings and per node settings

Settings holds what belongs to this install: the theme, the folders scanned for models, the
cloud key, the Python runtime and the mesh, and the values a newly added node starts from.
Anything that belongs to a graph stays on the node and is saved with the graph, which is why
changing a default here can never reach back into a graph that already exists.

