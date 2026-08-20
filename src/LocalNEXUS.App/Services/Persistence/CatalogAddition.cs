namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// What happened when something was added to the catalogue, and what to say about it.
/// </summary>
/// <remarks>
/// A boolean was not enough. Adding a model can fail for several reasons that a person can act
/// on, and "that did not work" makes them guess which one it was: a file that is not a model, a
/// safetensors weight file that needs its folder picked instead, a path that is already in the
/// list. Carrying the reason back is what lets the panel say the useful thing.
/// </remarks>
/// <param name="Added">True when the catalogue grew.</param>
/// <param name="Message">What to tell the person, whether it worked or not.</param>
public sealed record CatalogAddition(bool Added, string Message)
{
    /// <summary>Something was added.</summary>
    public static CatalogAddition Success(string message) => new(true, message);

    /// <summary>Nothing was added, and this is why.</summary>
    public static CatalogAddition Refused(string message) => new(false, message);
}
