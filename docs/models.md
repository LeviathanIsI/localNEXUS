# Models

How LocalNEXUS finds models, what it can serve, and how to point it at a hosted one instead.

## Adding local models

A local model is one of two things:

- a **GGUF file**, served by llama.cpp, or
- a **safetensors model folder**, meaning a folder holding a `config.json` beside one or
  more `.safetensors` files, served by the Python runtime.

Both appear in the same dropdown with their format shown beside them. Which is which is
worked out by reading the file, not from its name: a GGUF renamed to `model.bin` is still
found, and a text file named `model.gguf` is not mistaken for one. A `.safetensors` file
on its own, with no config beside it, is reported as a component of a model rather than
run and failed.

Either drop a model into `%LOCALAPPDATA%\LocalNEXUS\models\` (subfolders are scanned, so
one folder per model is fine), or select a Model node and use **Add folder** in the
settings panel to register a folder you already keep models in. **Rescan** picks up
anything added while the application is open.

For a folder you would rather keep in a file than click through, **Edit model folders**
opens `%LOCALAPPDATA%\LocalNEXUS\model-paths.txt`: one folder per line, `#` for comments,
environment variables expanded. It is scanned for both formats alongside everything else.

For a model that simply lives somewhere else, **Browse for a file** on a Model node points
that one node at a model file anywhere on disk without registering its folder for the whole
application, and **Browse for a folder** does the same for a safetensors model, which is a
folder rather than a file. The panel then says the node runs that file rather than the dropdown selection,
and shows the path. **Use the catalogue** drops it and returns the node to the dropdown, which
keeps its selection underneath the whole time. Two nodes in one graph can run two models from
two different drives this way. The choice is saved with the graph; if the file has gone by the
time the graph is opened again, the panel says so in red and the run refuses with the path
named rather than quietly running something else.

Pick a model from the dropdown on any Model node. Leave **Base URL** empty and
LocalNEXUS starts a server for that model on a free loopback port, waits for its health
endpoint, and reuses it for every later request. A GGUF gets a `llama-server` process; a
safetensors folder gets a Python one. Either way the process runs with no console window
and is killed when the application exits, and its output goes to
`%LOCALAPPDATA%\LocalNEXUS\logs\`.

## The Python runtime

Safetensors models are served by `transformers serve`, which exposes the same OpenAI
compatible API everything else here speaks. It runs in an environment LocalNEXUS builds
and owns, never a Python already on the machine and never anything loaded into the
application process.

The environment is built in the background on first launch, using the `uv` bundled in
`vendor\uv` and a standalone CPython that uv downloads. It lands in
`%LOCALAPPDATA%\LocalNEXUS\runtime\python\`, never inside the install folder. GGUF models
work throughout, and the **Python runtime** section of a Model node settings panel shows
the stage, live output, and which build of torch this machine was given.

That last choice matters, because it is most of the download. `nvidia-smi` is asked for
the driver version: 580 or newer gets the CUDA build of torch, roughly 1.8 GB, and
anything else gets the processor build, roughly 110 MB. The reason it chose is written to
the activity feed. The finished environment is about 2.9 GB on a CUDA machine.

Packages are pinned by the lockfiles in `vendor\python`, which are committed and are not
resolved on your machine. The environment is verified by importing what it needs rather
than by trusting an install command's exit code, and an interrupted install leaves no
record behind, so the next launch finishes the job from the cached downloads. **Repair**
builds whatever is missing, **Set up again** deletes the environment and rebuilds it, and
neither downloads anything twice.

Servers are started with all layers offloaded to the GPU (`-ngl 999`) and an 8192 token
context.

If you already run a llama.cpp server yourself, put its URL in **Base URL** and
LocalNEXUS will use it instead of starting one.

## Using OpenRouter

Select a Model node, switch **Provider** to `OpenRouter`, and fill in:

- **Model slug**, for example `anthropic/claude-sonnet-4` or
  `meta-llama/llama-3.3-70b-instruct`.
- **API key**, from <https://openrouter.ai/keys>.

**Base URL** is filled in as `https://openrouter.ai/api/v1` automatically.

> Keys are stored in plain text inside the saved graph file. Strip them before sharing
> a graph.

