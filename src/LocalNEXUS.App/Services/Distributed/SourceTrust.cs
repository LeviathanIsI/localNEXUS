namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// How far this install trusts a source.
/// </summary>
/// <remarks>
/// Today every source a user registers is their own machine, so the answer is always
/// <see cref="Trusted"/>. The attribute exists so the question is asked everywhere it will
/// matter once strangers can appear in the registry, at which point new values slot in here
/// and reputation attaches to the source's stable id.
/// </remarks>
public enum SourceTrust
{
    /// <summary>No trust decision has been made about this source.</summary>
    Unverified,

    /// <summary>The source belongs to the user and is trusted without conditions.</summary>
    Trusted
}
