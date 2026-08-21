namespace LocalNEXUS.Installer.Models;

/// <summary>
/// The seven steps, in the order the rail lists them.
/// </summary>
public enum WizardStep
{
    Welcome,
    License,
    Components,
    Build,
    Ready,
    Installing,
    Finish
}

/// <summary>
/// How a step is drawn in the rail. Three states rather than a pair of booleans, because the
/// three are visually distinct and the rail has to read at a glance.
/// </summary>
public enum StepState
{
    /// <summary>Not reached. A muted outline and a number.</summary>
    Upcoming,

    /// <summary>Where the user is. An outlined circle with an accent ring.</summary>
    Active,

    /// <summary>Passed. A filled gradient circle with a check.</summary>
    Done
}

/// <summary>
/// What the installer is doing, which is what decides whether Cancel and Back exist.
/// </summary>
/// <remarks>
/// Explicit rather than inferred from the step, because Installing and Finish differ in more than
/// which page is showing: one forbids going back because files are being written, the other
/// because there is nothing left to go back to.
/// </remarks>
public enum SetupPhase
{
    /// <summary>Choosing. Everything is reversible and Cancel is offered.</summary>
    Configuring,

    /// <summary>Writing. Cancel and Back are gone.</summary>
    Installing,

    /// <summary>Done, successfully.</summary>
    Completed,

    /// <summary>Stopped by a failure. The log holds the reason and a retry is offered.</summary>
    Failed
}

/// <summary>Which llama.cpp build to fetch.</summary>
public enum LlamaFlavour
{
    /// <summary>NVIDIA, driver 580 or newer.</summary>
    Cuda13,

    /// <summary>NVIDIA, older driver.</summary>
    Cuda12,

    /// <summary>AMD, Intel and NVIDIA. The safe answer.</summary>
    Vulkan,

    /// <summary>No graphics card needed, and slow.</summary>
    Cpu
}

/// <summary>The optional parts of an install.</summary>
public enum EngineComponent
{
    /// <summary>llama.cpp, which runs GGUF models locally.</summary>
    Llama,

    /// <summary>Mesh LLM, which splits a model across machines.</summary>
    Mesh,

    /// <summary>uv, which builds the Python runtime that serves safetensors models.</summary>
    Uv
}
