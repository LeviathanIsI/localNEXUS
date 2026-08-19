# vendor/llama

Put a llama.cpp server build here. The contents of this folder are not committed:
they are large, they are specific to your GPU and driver stack, and they ship under
llama.cpp's own license.

LocalNEXUS looks for `llama-server.exe` and `ggml-rpc-server.exe` in this folder (and
in the same folder next to the built application). `llama-server` serves models and
coordinates distributed runs; `ggml-rpc-server` is what runs when this machine
contributes to someone else's pipeline. Both ship in the same release archive.
Everything the executables need, which on Windows means their `.dll` files, has to
sit beside them.

## Getting a build

1. Open <https://github.com/ggml-org/llama.cpp/releases>.
2. Download the Windows binary archive that matches your hardware. For an NVIDIA
   card, take a CUDA build, for example `llama-<version>-bin-win-cuda-x64.zip`.
   CUDA builds also expect the matching `cudart` archive from the same release.
3. Extract the archive, and the cudart archive if you took one, directly into this
   folder so the layout is flat:

```
vendor/llama/
  llama-server.exe
  ggml.dll
  llama.dll
  ... the rest of the dlls from the archive
```

## Checking it works

Start LocalNEXUS. The activity feed reports `Local inference ready` and the full
path on startup when the executable was found, and `Local inference unavailable`
when it was not.

You can also confirm the build outside the application:

```powershell
.\llama-server.exe --version
```

LocalNEXUS starts and stops this process for you, with no console window, and
reuses one server per model file. You never have to run it yourself.
