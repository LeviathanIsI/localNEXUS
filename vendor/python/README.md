# vendor/python

The pinned dependency set for the Python runtime that serves safetensors models. Unlike
the other vendor folders, everything here is committed: these are small text files, and
the whole point of them is that every install resolves to the same packages.

## Files

- `requirements.in` is the input: what the runtime actually needs.
- `requirements-cu132.txt` pins the CUDA 13.2 build of torch, for a machine with an
  NVIDIA driver of 580 or newer.
- `requirements-cpu.txt` pins the processor only build, for everything else.

The two lockfiles differ by exactly one line, the torch build, and by the index they
point at. Both carry their index URLs inside them, so installing does not depend on
whoever runs it passing the right flags.

## Which one a machine gets

`AcceleratorProbe` asks `nvidia-smi` for the driver version. A driver of 580 or newer
gets the CUDA lockfile, anything else gets the processor one, and the reason it chose is
written to the activity feed. Updating a driver and repairing the runtime switches it.

The difference is not small: the CUDA torch wheel is about 1.8 GB and the processor one
is about 110 MB. That cost is paid once per install, on first launch, in the background.

## Recompiling

Do not edit the lockfiles by hand. After changing `requirements.in`, regenerate both:

```
uv pip compile requirements.in --python-version 3.12 ^
  --index-url https://download.pytorch.org/whl/cu132 ^
  --extra-index-url https://pypi.org/simple ^
  --index-strategy unsafe-best-match --emit-index-url ^
  -o requirements-cu132.txt

uv pip compile requirements.in --python-version 3.12 ^
  --index-url https://download.pytorch.org/whl/cpu ^
  --extra-index-url https://pypi.org/simple ^
  --index-strategy unsafe-best-match --emit-index-url ^
  -o requirements-cpu.txt
```

`unsafe-best-match` is needed because the pinned builds live on the torch index while
most of the tree lives on PyPI, and the default strategy stops at the first index that
has any version of a name.

## What the server is

`transformers[serving]` brings the transformers command line, whose `serve` command is a
FastAPI application exposing `/v1/chat/completions` (streaming and not), `/v1/completions`,
`/v1/models` and `/health`. It is the OpenAI compatible API this application already
talks, which is why adding it changed nothing on the request path.

`requests` is listed explicitly because the transformers command line imports it at
module scope and the serving extra does not pull it in.
