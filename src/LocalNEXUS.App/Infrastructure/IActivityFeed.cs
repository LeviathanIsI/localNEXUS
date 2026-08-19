namespace LocalNEXUS.App.Infrastructure;

/// <summary>
/// The write side of the activity feed, handed to nodes and to the executor so they can
/// report progress without knowing anything about the view.
/// </summary>
public interface IActivityFeed
{
    /// <summary>Appends an entry and returns it so the caller can keep streaming into it.</summary>
    ActivityEvent Add(ActivityKind kind, string title, string? text = null, Guid? nodeId = null);

    /// <summary>Appends a plain informational entry.</summary>
    ActivityEvent Info(string title, string? text = null);

    /// <summary>Appends an error entry.</summary>
    ActivityEvent Error(string title, string? text = null);

    /// <summary>
    /// Appends a question and waits for the user to answer it. Returns false if the user
    /// declines or the run is cancelled while the question is outstanding.
    /// </summary>
    Task<bool> RequestConfirmationAsync(string title, string message, CancellationToken ct);

    /// <summary>Removes every entry.</summary>
    void Clear();
}
