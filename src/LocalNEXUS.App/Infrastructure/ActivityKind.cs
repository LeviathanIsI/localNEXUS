namespace LocalNEXUS.App.Infrastructure;

/// <summary>
/// Classifies an entry in the activity feed. The view uses this to pick an icon and an
/// accent colour, and the output node uses <see cref="Confirmation"/> to ask a question.
/// </summary>
public enum ActivityKind
{
    /// <summary>General information that does not belong to a node.</summary>
    Info,

    /// <summary>Echo of the request the user typed before pressing Run.</summary>
    Request,

    /// <summary>A run has begun.</summary>
    RunStarted,

    /// <summary>A run finished with every node completed.</summary>
    RunCompleted,

    /// <summary>A run stopped because a node threw or the user cancelled.</summary>
    RunFaulted,

    /// <summary>A node started executing.</summary>
    NodeStarted,

    /// <summary>A node finished successfully.</summary>
    NodeCompleted,

    /// <summary>A node threw.</summary>
    NodeFaulted,

    /// <summary>Streamed model output. The text of this entry grows while tokens arrive.</summary>
    ModelStream,

    /// <summary>A file was written to disk.</summary>
    FileWritten,

    /// <summary>A question the run is blocked on until the user answers.</summary>
    Confirmation,

    /// <summary>Something went wrong outside of a node execution.</summary>
    Error
}
