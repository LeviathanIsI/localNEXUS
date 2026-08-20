namespace LocalNEXUS.App.Services.Python;

/// <summary>
/// Which torch build this machine gets, which lockfile says so, and why that was chosen.
/// </summary>
/// <remarks>
/// The reason travels with the choice because this decision is expensive and invisible: the
/// difference between the two answers is roughly 1.8 GB of download and whether the GPU is used
/// at all. Automatic but visible, the same rule the coverage planner follows.
/// </remarks>
/// <param name="Accelerator">The build chosen.</param>
/// <param name="LockfileName">The committed lockfile that pins it.</param>
/// <param name="Reason">What was found on this machine that led to the choice.</param>
public sealed record AcceleratorChoice(PythonAccelerator Accelerator, string LockfileName, string Reason);
