namespace LocalNEXUS.App.Services.Python;

/// <summary>
/// What the environment on disk was last built from.
/// </summary>
/// <remarks>
/// Written only after the packages have been imported successfully, so its presence means a
/// finished install rather than an attempted one. That is what makes provisioning resumable:
/// an install interrupted halfway leaves no record, and the next run does the work again with a
/// warm download cache instead of trusting a half built environment.
/// </remarks>
public sealed class PythonEnvironmentRecord
{
    /// <summary>The lockfile the environment was installed from.</summary>
    public string LockfileName { get; set; } = string.Empty;

    /// <summary>A hash of that lockfile's contents, so editing it invalidates the environment.</summary>
    public string LockfileHash { get; set; } = string.Empty;

    /// <summary>Which torch build was chosen when it was built.</summary>
    public PythonAccelerator Accelerator { get; set; }

    /// <summary>When the install finished, in UTC.</summary>
    public DateTime CompletedUtc { get; set; }
}
