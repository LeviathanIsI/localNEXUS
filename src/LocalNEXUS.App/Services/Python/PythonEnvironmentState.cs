namespace LocalNEXUS.App.Services.Python;

/// <summary>
/// Where the supervised Python environment has got to.
/// </summary>
/// <remarks>
/// An enum rather than a pair of booleans for the same reason the mesh states are: the interesting
/// distinction is between not yet knowing and knowing it is broken, and a boolean cannot hold it.
/// The first value is <see cref="Unknown"/> so an unset field reads as a question rather than as
/// a verdict, and nothing renders as a failure until something has actually failed.
/// </remarks>
public enum PythonEnvironmentState
{
    /// <summary>Nothing has looked yet. Says so rather than guessing in either direction.</summary>
    Unknown,

    /// <summary>Checked, and there is no environment. The normal state of a fresh install.</summary>
    Missing,

    /// <summary>Being built right now. Progress is on the feed and in the panel.</summary>
    Provisioning,

    /// <summary>Built, and the packages it needs were imported successfully.</summary>
    Ready,

    /// <summary>Provisioning or verification failed. The reason is carried alongside.</summary>
    Failed
}
