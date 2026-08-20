namespace LocalNEXUS.App.Services.Python;

/// <summary>Which build of torch this machine gets.</summary>
public enum PythonAccelerator
{
    /// <summary>The processor only build. Small, slow, and always correct.</summary>
    Cpu,

    /// <summary>The CUDA build, for an NVIDIA GPU with a driver new enough to run it.</summary>
    Cuda
}
