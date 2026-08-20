namespace LocalNEXUS.App.Services.Editing;

/// <summary>
/// Thrown when a change could not be applied to the file it was written against.
/// </summary>
/// <remarks>
/// Its own type, and its message names the lines that failed to match, because this is the
/// failure the repair loop is best placed to fix: a model that was shown which of its context
/// lines does not exist will usually write the block again correctly, and one told only that the
/// edit failed will not.
/// </remarks>
public sealed class EditApplyException : Exception
{
    public EditApplyException(string message)
        : base(message)
    {
    }

    public EditApplyException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
