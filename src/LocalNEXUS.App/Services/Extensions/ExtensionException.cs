namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// Something went wrong with an extension, described the way the person who has to fix it needs
/// to hear it.
/// </summary>
/// <remarks>
/// Every message thrown here names what failed and, where there is one, what to do about it.
/// "The extension exited immediately" is a bug report somebody can act on. "Object reference not
/// set" is not, and an extension host is exactly the place where the second kind escapes if
/// nobody insists otherwise.
/// </remarks>
public sealed class ExtensionException : Exception
{
    public ExtensionException(string message)
        : base(message)
    {
    }

    public ExtensionException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
