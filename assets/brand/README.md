# Brand assets

Three logo concepts, each in its own folder with everything derived from it.

| Folder                | Mark                                                        |
| --------------------- | ----------------------------------------------------------- |
| `concept-1-letterform` | An N whose diagonal is a graph edge between two nodes. In use. |
| `concept-2-graph`      | A four node graph.                                           |
| `concept-3-open`       | An open hexagon around a two node edge.                      |

Each folder holds:

- `localnexus-<concept>.png`, the source at 1254x1254.
- `localnexus-<concept>-{512,256,128,64,48,32,16}.png`.
- `localnexus-<concept>.ico`, containing real 16, 32, 48, 64, 128 and 256 frames rather than one
  large image the shell has to scale.
- `localnexus-<concept>-social-1280x640.png`, the banner GitHub shows when a link is shared.

## Switching concept

Two edits, and nothing else in the solution names a concept.

1. `Directory.Build.props` at the repository root, the `BrandConcept` property. Set it to
   `concept-2-graph` or `concept-3-open`. That moves the application icon, the installer icon,
   the mark in the application title bar and the marks on the installer's Welcome and Finish
   pages, because all of them read `BrandIcon` and `BrandMark` from there and the chosen png is
   linked into both assemblies as `Assets\Brand\Mark.png`.
2. `README.md`, the image path in the header block at the top.

Then rebuild. Windows caches icons per file path, so an icon that looks unchanged in Explorer
after a rebuild is the cache rather than the build.

## Regenerating

The png sets, the icos and the banners are generated from the three source pngs. The sources are
the only files here that were authored rather than derived, so a change to the artwork means
replacing a source and regenerating.

## A note on the sources

The sources have no alpha channel: the mark sits on its own near black field. The icons keep that
field, which is why they read as dark tiles rather than as floating marks. The banners do not,
because pasting a near black square onto a different dark background shows the seam, so the field
is turned back into transparency first. That recovery is exact, the mark having been composited
over black to begin with.
