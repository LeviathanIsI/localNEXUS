namespace LocalNEXUS.App.Nodes;

/// <summary>Where a model node sends its requests.</summary>
/// <remarks>
/// Local means "a GGUF this application serves itself", which resolution may satisfy on this
/// machine alone or split across sources when the model does not fit; the graph does not care
/// which. Network means "a model the network serves", picked by identity from what the index
/// reports rather than by file. SelfHosted means "a server the user already runs somewhere",
/// nothing is spawned. Local and SelfHosted used to share one value, distinguished only by
/// whether a base URL was typed; they are separate meanings and are now separate values,
/// though the old behaviour of a Local node with an explicit base URL still works so existing
/// graphs keep running.
/// </remarks>
public enum ModelProvider
{
    /// <summary>A GGUF file served by processes this application starts and manages.</summary>
    Local,

    /// <summary>A model the network serves, assembled across whatever sources cover it.</summary>
    Network,

    /// <summary>An OpenAI compatible server the user runs themselves. Never spawned.</summary>
    SelfHosted,

    /// <summary>A hosted model reached through OpenRouter.</summary>
    OpenRouter
}
