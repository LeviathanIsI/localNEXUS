# Mesh LLM binaries

The distributed path runs on a [Mesh LLM](https://github.com/Mesh-LLM/mesh-llm) node, which
LocalNEXUS starts as a silent child process. The binaries are fetched per machine rather than
committed: they are large, they are GPU build specific, and they carry their own licence
(Apache-2.0).

Purely local inference does not need any of this. It runs on the llama.cpp build in
`vendor\llama` and works whether or not this folder is populated.

## What to place here

Download a Windows bundle from the
[releases page](https://github.com/Mesh-LLM/mesh-llm/releases) and extract it so the layout is:

```text
vendor\mesh\mesh-bundle\mesh-llm.exe
vendor\mesh\mesh-bundle\native-runtimes\...
```

Extracting the archive directly into `vendor\mesh` produces exactly that shape. The executable
is also found if it sits at `vendor\mesh\mesh-llm.exe` with its `native-runtimes` folder beside
it; both layouts resolve identically from a development run and from the published single file
executable.

## Which flavour

| Bundle | When |
|---|---|
| `...-x86_64-pc-windows-msvc-vulkan.zip` | The default choice on Windows. Works on NVIDIA, AMD and Intel GPUs. |
| `...-x86_64-pc-windows-msvc-cuda.zip` | NVIDIA only, and only with a CUDA 12 era driver. |
| `...-x86_64-pc-windows-msvc-rocm.zip` | AMD ROCm. |
| `...-x86_64-pc-windows-msvc.zip` | CPU only. |

The Vulkan bundle is what this project is developed against. On a machine with a CUDA 13 era
driver the CUDA bundle can report zero GPUs and fall back anyway, so Vulkan is both the simpler
and the more reliable default.

Verify the node sees your GPU before expecting the Network tab to do anything useful:

```powershell
vendor\mesh\mesh-bundle\mesh-llm.exe gpus
```

## Multi GPU machines

A laptop with both an integrated and a discrete GPU announces both, and the node may pick the
integrated one. If stages fail to load, pin the discrete card:

```powershell
vendor\mesh\mesh-bundle\mesh-llm.exe serve --device "NVIDIA GeForce RTX 4080 Laptop GPU" ...
```
