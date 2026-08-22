namespace LocalNEXUS.App.Services.History;

/// <summary>
/// A query against the run history could not be run.
/// </summary>
/// <remarks>
/// Exists so that a search which failed and a search which matched nothing are not the same
/// answer. They were, and the cost of that was every search in the application quietly returning
/// nothing from the day the feature shipped: the query was malformed, the database said so, and
/// the message was discarded on the reasonable sounding grounds that somebody had probably
/// mistyped something.
/// </remarks>
public sealed class HistoryQueryException : Exception
{
    public HistoryQueryException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
