# vendor/uv

Put a `uv` build here. The executable itself is not committed: it is roughly 48 MB, it
is platform specific, and it ships under its own license (Apache-2.0 or MIT, at your
option).

LocalNEXUS looks for `uv.exe` in this folder, in the same folder next to the built
application, and in the repository while running from a development build. It is used
for one thing: building the isolated Python environment that serves safetensors models.

## Why it is here

`uv` downloads a standalone CPython and creates a virtual environment from it, so the
application never depends on a Python already being installed and never writes into one
that is. Everything it creates lives under `%LOCALAPPDATA%\LocalNEXUS\runtime\python\`,
including the interpreter and the download cache, so uninstalling means deleting a
folder.

## Getting a build

1. Open <https://github.com/astral-sh/uv/releases>.
2. Download `uv-x86_64-pc-windows-msvc.zip`.
3. Extract `uv.exe` directly into this folder. Nothing else from the archive is needed.

The build this was developed against is uv 0.12.3. Newer builds are expected to work;
the two commands used are `uv venv` and `uv pip sync`.

## What it installs

The package set is pinned by the lockfiles in `vendor/python`, which are committed.
Nothing is resolved on the user's machine, so two installs of the same build of
LocalNEXUS get the same packages.
